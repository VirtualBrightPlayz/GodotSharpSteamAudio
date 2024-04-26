using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Godot;
using Godot.NativeInterop;
using SteamAudio;

public partial class GDSteamAudio : Node
{
    public enum MaterialPreset : byte
    {
        Generic,
        Brick,
        Concrete,
        Ceramic,
        Gravel,
        Carpet,
        Glass,
        Plaster,
        Wood,
        Metal,
        Rock,
    }

    public static GDSteamAudio Instance;
    public const string LogPrefix = "SteamAudio: ";
    public const int MixRate = 48000;
    // public const int AudioFrameSize = 1024 * 4;
    public const int AudioFrameSize = 1024 * 2;
    public const int AudioFrameSizeInBytes = AudioFrameSize * sizeof(float);
    public const IPL.ReflectionEffectType ReflType = IPL.ReflectionEffectType.Convolution;
    public static Dictionary<MaterialPreset, float[]> materialPresets = new Dictionary<MaterialPreset, float[]>()
    {
        { MaterialPreset.Generic, new[] {0.10f,0.20f,0.30f,0.05f,0.100f,0.050f,0.030f} },
        { MaterialPreset.Brick, new[] {0.03f,0.04f,0.07f,0.05f,0.015f,0.015f,0.015f} },
        { MaterialPreset.Concrete, new[] {0.05f,0.07f,0.08f,0.05f,0.015f,0.002f,0.001f} },
        { MaterialPreset.Ceramic, new[] {0.01f,0.02f,0.02f,0.05f,0.060f,0.044f,0.011f} },
        { MaterialPreset.Gravel, new[] {0.60f,0.70f,0.80f,0.05f,0.031f,0.012f,0.008f} },
        { MaterialPreset.Carpet, new[] {0.24f,0.69f,0.73f,0.05f,0.020f,0.005f,0.003f} },
        { MaterialPreset.Glass, new[] {0.06f,0.03f,0.02f,0.05f,0.060f,0.044f,0.011f} },
        { MaterialPreset.Plaster, new[] {0.12f,0.06f,0.04f,0.05f,0.056f,0.056f,0.004f} },
        { MaterialPreset.Wood, new[] {0.11f,0.07f,0.06f,0.05f,0.070f,0.014f,0.005f} },
        { MaterialPreset.Metal, new[] {0.20f,0.07f,0.06f,0.05f,0.200f,0.025f,0.010f} },
        { MaterialPreset.Rock, new[] {0.13f,0.20f,0.24f,0.05f,0.015f,0.002f,0.001f} },
    };
    public static Thread audioThread;
    public static Thread reflectThread;
    public static IPL.Context iplCtx;
    public static IPL.AudioSettings iplAudioSettings;
    public static List<IPL.Hrtf> hrtfs = new List<IPL.Hrtf>();
    public static List<IPL.AudioBuffer> audioBuffers = new List<IPL.AudioBuffer>();
    public static List<IPL.DirectEffect> directEffects = new List<IPL.DirectEffect>();
    public static List<IPL.ReflectionEffect> reflectionEffects = new List<IPL.ReflectionEffect>();
    public static List<IPL.ReflectionMixer> reflectionMixers = new List<IPL.ReflectionMixer>();
    public static List<IPL.BinauralEffect> binauralEffects = new List<IPL.BinauralEffect>();
    public static List<IPL.Scene> scenes = new List<IPL.Scene>();
    public static List<IPL.StaticMesh> staticMeshes = new List<IPL.StaticMesh>();
    public static List<IPL.Simulator> simulators = new List<IPL.Simulator>();
    public static List<IPL.Source> sources = new List<IPL.Source>();
    public static bool loaded = false;
    public static IPL.Hrtf HrtfDefault;
    public static IPL.Scene SceneDefault;
    public static IPL.Simulator SimulatorDefault;
    public static IPL.DirectEffect DirectEffectDefault;
    public static IPL.ReflectionEffect ReflectionEffectDefault;
    public static IPL.ReflectionMixer ReflectionMixerDefault;
    public delegate void OnSimulatorRunEventHandler();
    public event OnSimulatorRunEventHandler OnSimulatorRun;
    public static System.Threading.Mutex mutex = new System.Threading.Mutex();
    [Export]
    public Camera3D camera;
    private Transform3D cameraTransform = Transform3D.Identity;

    static GDSteamAudio()
    {
        NativeLibrary.SetDllImportResolver(typeof(IPL).Assembly, Resolver);
    }

    private static IntPtr Resolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != "phonon.dll")
        {
            return IntPtr.Zero;
        }
        string dir = Directory.GetCurrentDirectory();
        if (OS.HasFeature("editor"))
        {
            dir = Path.Combine(dir, "addons");
        }
        switch (OS.GetName().ToLower())
        {
            case "windows":
                return NativeLibrary.Load(Path.Combine(dir, "phonon.dll"));
            case "linux":
                return NativeLibrary.Load(Path.Combine(dir, "libphonon.so"));
            default:
                return IntPtr.Zero;
        }
    }

    public override void _EnterTree()
    {
        Instance = this;
        Start();
        reflectThread = new Thread(() =>
        {
            while (IsInstanceValid(this))
            {
                if (mutex.WaitOne(0))
                {
                    IPL.SimulatorRunReflections(SimulatorDefault);
                    mutex.ReleaseMutex();
                }
                Thread.Yield();
            }
        });
        reflectThread.Start();
        audioThread = new Thread(() =>
        {
            while (IsInstanceValid(this))
            {
                // if (mutex.WaitOne(1))
                {
                    RunSim();
                    // mutex.ReleaseMutex();
                }
                Thread.Yield();
            }
        });
        audioThread.Start();
    }

    public override void _Process(double delta)
    {
        if (IsInstanceValid(camera))
            cameraTransform = camera.GlobalTransform;
        // RunSim();
    }

    public void RunSim()
    {
        if (IsInstanceValid(camera))
        {
            var inputs = new IPL.SimulationSharedInputs()
            {
                Listener = GetIPLTransform(cameraTransform),
                NumRays = 4096,
                NumBounces = 8,
                Duration = 2f,
                Order = 2,
                IrradianceMinDistance = 1f,
            };
            IPL.SimulatorSetSharedInputs(SimulatorDefault, IPL.SimulationFlags.Direct | IPL.SimulationFlags.Reflections, in inputs);
        }
        IPL.SimulatorRunDirect(SimulatorDefault);
        if (IsInstanceValid(camera))
            OnSimulatorRun?.Invoke();
    }

    public override void _ExitTree()
    {
        Stop();
    }

    public static void WaitOne(Action callback)
    {
        new Thread(() =>
        {
            if (mutex.WaitOne())
            {
                try
                {
                    callback();
                }
                catch (Exception e)
                {
                    GD.PrintErr(LogPrefix, e);
                }
                mutex.ReleaseMutex();
            }
        }).Start();
    }

    public static void Start()
    {
        GD.Print(LogPrefix, "Loading...");
        if (loaded)
            return;
        var ctxSettings = new IPL.ContextSettings()
        {
            Version = IPL.Version,
        };
        CheckError(IPL.ContextCreate(in ctxSettings, out iplCtx));
        // IPL.ContextRetain(iplCtx);
        iplAudioSettings = new IPL.AudioSettings()
        {
            SamplingRate = MixRate,
            FrameSize = AudioFrameSize,
        };
        loaded = true;
        HrtfDefault = NewHrtf();
        SceneDefault = NewScene();
        SimulatorDefault = NewSim(SceneDefault);
        // DirectEffectDefault = NewDirectEffect(1);
        // ReflectionEffectDefault = NewReflectionEffect(2);
        // ReflectionMixerDefault = NewReflectionMixer(1);
        GD.Print(LogPrefix, "Loaded.");
    }

    public static void Stop()
    {
        if (!loaded)
            return;
        HrtfDefault = default;
        SceneDefault = default;
        SimulatorDefault = default;
        DirectEffectDefault = default;
        ReflectionEffectDefault = default;
        ReflectionMixerDefault = default;
        for (int i = 0; i < sources.Count; i++)
        {
            var item = sources[i];
            IPL.SourceRelease(ref item);
        }
        sources.Clear();
        for (int i = 0; i < simulators.Count; i++)
        {
            var item = simulators[i];
            IPL.SimulatorRelease(ref item);
        }
        simulators.Clear();
        for (int i = 0; i < staticMeshes.Count; i++)
        {
            var item = staticMeshes[i];
            IPL.StaticMeshRelease(ref item);
        }
        staticMeshes.Clear();
        for (int i = 0; i < scenes.Count; i++)
        {
            var item = scenes[i];
            IPL.SceneRelease(ref item);
        }
        scenes.Clear();
        for (int i = 0; i < audioBuffers.Count; i++)
        {
            var hrtf = audioBuffers[i];
            IPL.AudioBufferFree(iplCtx, ref hrtf);
        }
        audioBuffers.Clear();
        for (int i = 0; i < directEffects.Count; i++)
        {
            var item = directEffects[i];
            IPL.DirectEffectRelease(ref item);
        }
        directEffects.Clear();
        for (int i = 0; i < reflectionMixers.Count; i++)
        {
            var item = reflectionMixers[i];
            IPL.ReflectionMixerRelease(ref item);
        }
        reflectionMixers.Clear();
        for (int i = 0; i < reflectionEffects.Count; i++)
        {
            var item = reflectionEffects[i];
            IPL.ReflectionEffectRelease(ref item);
        }
        reflectionEffects.Clear();
        for (int i = 0; i < binauralEffects.Count; i++)
        {
            var item = binauralEffects[i];
            IPL.BinauralEffectRelease(ref item);
        }
        binauralEffects.Clear();
        for (int i = 0; i < hrtfs.Count; i++)
        {
            var hrtf = hrtfs[i];
            IPL.HrtfRelease(ref hrtf);
        }
        hrtfs.Clear();
        IPL.ContextRelease(ref iplCtx);
        loaded = false;
        GD.Print(LogPrefix, "Unloaded.");
    }

    public static void CheckError(IPL.Error error)
    {
        if (error == IPL.Error.Success)
            return;
        throw new Exception(error.ToString());
    }

    public static IPL.Vector3 ConvertToIPL(Vector3 v)
    {
        return new IPL.Vector3(v.X, v.Y, v.Z);
    }

    public static IPL.CoordinateSpace3 GetIPLTransform(Transform3D v)
    {
        return new IPL.CoordinateSpace3()
        {
            Ahead = ConvertToIPL(-v.Basis.Z),
            Origin = ConvertToIPL(v.Origin),
            Right = ConvertToIPL(-v.Basis.X),
            Up = ConvertToIPL(v.Basis.Y),
        };
    }

    public static IPL.Source NewSource(IPL.Simulator simulator)
    {
        if (!loaded)
            throw new Exception();
        var settings = new IPL.SourceSettings()
        {
            Flags = IPL.SimulationFlags.Direct | IPL.SimulationFlags.Reflections,
        };
        CheckError(IPL.SourceCreate(simulator, in settings, out var source));
        // source = IPL.SourceRetain(source);
        IPL.SourceAdd(source, simulator);
        WaitOne(() =>
        {
            IPL.SimulatorCommit(simulator);
        });
        sources.Add(source);
        return source;
    }

    public static void DelSource(IPL.Source source, IPL.Simulator simulator)
    {
        sources.Remove(source);
        IPL.SourceRemove(source, simulator);
        WaitOne(() =>
        {
            IPL.SimulatorCommit(simulator);
        });
        IPL.SourceRelease(ref source);
    }

    public static IPL.Simulator NewSim(IPL.Scene scene)
    {
        if (!loaded)
            throw new Exception();
        var settings = new IPL.SimulationSettings()
        {
            Flags = IPL.SimulationFlags.Direct | IPL.SimulationFlags.Reflections,
            SceneType = IPL.SceneType.Default,
            ReflectionType = ReflType,
            MaxNumRays = 4096,
            NumDiffuseSamples = 1024,
            MaxDuration = 2f,
            MaxOrder = 2,
            MaxNumSources = 64000,
            NumThreads = 4,
            SamplingRate = iplAudioSettings.SamplingRate,
            FrameSize = iplAudioSettings.FrameSize,
            MaxNumOcclusionSamples = 1024,
            NumVisSamples = 16,
            RayBatchSize = 16,
        };
        CheckError(IPL.SimulatorCreate(iplCtx, in settings, out var sim));
        // sim = IPL.SimulatorRetain(sim);
        IPL.SimulatorSetScene(sim, scene);
        IPL.SimulatorCommit(sim);
        simulators.Add(sim);
        return sim;
    }

    public unsafe static void FillMaterialFromPreset(MaterialPreset preset, ref IPL.Material material)
    {
        var mat = materialPresets[preset];
        material.Absorption[0] = mat[0];
        material.Absorption[1] = mat[1];
        material.Absorption[2] = mat[2];
        material.Scattering = mat[3];
        material.Transmission[0] = mat[4];
        material.Transmission[1] = mat[5];
        material.Transmission[2] = mat[6];
    }

    public unsafe static IPL.StaticMesh NewStaticMesh(IPL.Scene scene, MaterialPreset materialPreset, Transform3D transform, ConcavePolygonShape3D shape)
    {
        if (!loaded)
            throw new Exception();
        var verts = shape.Data.Select(x => transform * x).Select(x => new IPL.Vector3(x.X, x.Y, x.Z)).ToArray();
        var tris = new IPL.Triangle[verts.Length / 3];
        var mats = new IPL.Material[1];
        var matInds = new int[tris.Length];
        for (int i = 0; i < tris.Length; i++)
        {
            fixed (int *data = tris[i].Indices)
            {
                data[0] = i * 3 + 0;
                data[1] = i * 3 + 1;
                data[2] = i * 3 + 2;
            }
            matInds[i] = 0;
        }
        {
            fixed (float *data = mats[0].Absorption)
            {
                data[0] = 0.1f;
                data[1] = 0.2f;
                data[2] = 0.3f;
            }
            mats[0].Scattering = 0.05f;
            fixed (float *data = mats[0].Transmission)
            {
                data[0] = 0.1f;
                data[1] = 0.05f;
                data[2] = 0.03f;
            }
        }
        FillMaterialFromPreset(materialPreset, ref mats[0]);
        var settings = new IPL.StaticMeshSettings();
        settings.NumVertices = verts.Length;
        settings.NumTriangles = tris.Length;
        settings.NumMaterials = mats.Length;
        IPL.StaticMesh mesh;
        fixed (void *v = verts)
        fixed (void *t = tris)
        fixed (void *m = mats)
        fixed (void *mi = matInds)
        {
            settings.Vertices = (IntPtr)v;
            settings.Triangles = (IntPtr)t;
            settings.Materials = (IntPtr)m;
            settings.MaterialIndices = (IntPtr)mi;
            CheckError(IPL.StaticMeshCreate(scene, in settings, out mesh));
        }
        // mesh = IPL.StaticMeshRetain(mesh);
        staticMeshes.Add(mesh);
        return mesh;
    }

    public static IPL.Scene NewScene()
    {
        if (!loaded)
            throw new Exception();
        var settings = new IPL.SceneSettings()
        {
            Type = IPL.SceneType.Default,
        };
        CheckError(IPL.SceneCreate(iplCtx, in settings, out var scene));
        // scene = IPL.SceneRetain(scene);
        scenes.Add(scene);
        return scene;
    }

    public static IPL.DirectEffect NewDirectEffect(int numChannels)
    {
        if (!loaded)
            throw new Exception();
        var settings = new IPL.DirectEffectSettings()
        {
            NumChannels = numChannels,
        };
        CheckError(IPL.DirectEffectCreate(iplCtx, in iplAudioSettings, in settings, out var effect));
        // effect = IPL.DirectEffectRetain(effect);
        directEffects.Add(effect);
        return effect;
    }

    public static IPL.ReflectionEffect NewReflectionEffect(int numChannels)
    {
        if (!loaded)
            throw new Exception();
        var settings = new IPL.ReflectionEffectSettings()
        {
            NumChannels = numChannels,
            Type = ReflType,
            IrSize = iplAudioSettings.SamplingRate * 2,
        };
        CheckError(IPL.ReflectionEffectCreate(iplCtx, in iplAudioSettings, in settings, out var effect));
        // effect = IPL.ReflectionEffectRetain(effect);
        reflectionEffects.Add(effect);
        return effect;
    }

    public static IPL.ReflectionMixer NewReflectionMixer(int numChannels)
    {
        if (!loaded)
            throw new Exception();
        var settings = new IPL.ReflectionEffectSettings()
        {
            NumChannels = numChannels,
            Type = ReflType,
            IrSize = iplAudioSettings.SamplingRate * 2,
        };
        CheckError(IPL.ReflectionMixerCreate(iplCtx, in iplAudioSettings, in settings, out var effect));
        // effect = IPL.ReflectionMixerRetain(effect);
        reflectionMixers.Add(effect);
        return effect;
    }

    public static IPL.BinauralEffect NewBinauralEffect(IPL.Hrtf hrtf)
    {
        if (!loaded)
            throw new Exception();
        var settings = new IPL.BinauralEffectSettings()
        {
            Hrtf = hrtf,
        };
        CheckError(IPL.BinauralEffectCreate(iplCtx, in iplAudioSettings, in settings, out var effect));
        // effect = IPL.BinauralEffectRetain(effect);
        binauralEffects.Add(effect);
        return effect;
    }

    public static IPL.AmbisonicsDecodeEffect NewAmbisonicDecodeEffect(IPL.Hrtf hrtf)
    {
        if (!loaded)
            throw new Exception();
        var settings = new IPL.AmbisonicsDecodeEffectSettings()
        {
            Hrtf = hrtf,
            MaxOrder = 2,
            SpeakerLayout = new IPL.SpeakerLayout()
            {
                Type = IPL.SpeakerLayoutType.Stereo,
            },
        };
        CheckError(IPL.AmbisonicsDecodeEffectCreate(iplCtx, in iplAudioSettings, in settings, out var effect));
        // effect = IPL.AmbisonicsDecodeEffectRetain(effect);
        // binauralEffects.Add(effect);
        return effect;
    }

    public static IPL.AudioBuffer NewAudioBuffer(int numChannels)
    {
        if (!loaded)
            throw new Exception();
        IPL.AudioBuffer buffer = new IPL.AudioBuffer();
        CheckError(IPL.AudioBufferAllocate(iplCtx, numChannels, iplAudioSettings.FrameSize, ref buffer));
        audioBuffers.Add(buffer);
        return buffer;
    }

    public static void DelAudioBuffer(IPL.AudioBuffer buffer)
    {
        audioBuffers.Remove(buffer);
        IPL.AudioBufferFree(iplCtx, ref buffer);
    }

    public static IPL.Hrtf NewHrtf()
    {
        if (!loaded)
            throw new Exception();
        var hrtfSettings = new IPL.HrtfSettings()
        {
            Type = IPL.HrtfType.Default,
        };
        CheckError(IPL.HrtfCreate(iplCtx, in iplAudioSettings, in hrtfSettings, out var hrtf));
        // hrtf = IPL.HrtfRetain(hrtf);
        hrtfs.Add(hrtf);
        return hrtf;
    }
}
