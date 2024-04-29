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
    public delegate void OnSimulatorRunEventHandler();
    public event OnSimulatorRunEventHandler OnSimulatorRun;
    public static System.Threading.SpinLock mutex = new System.Threading.SpinLock();
    public static System.Threading.SpinLock simMutex = new System.Threading.SpinLock();
    [Export]
    public Camera3D camera;
    [Export]
    public bool loadOnStart = true;
    public bool threadsRunning = false;
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
        if (loadOnStart)
            Start();
        StartThreads();
    }

    public void StartThreads()
    {
        threadsRunning = true;
        reflectThread = new Thread(() =>
        {
            while (IsInstanceValid(this) && threadsRunning)
            {
                if (!loaded)
                {
                    Thread.Sleep(10);
                    continue;
                }
                bool hasLock = false;
                hasLock = true;
                // mutex.TryEnter(ref hasLock);
                if (hasLock)
                {
                    bool hasSimLock = false;
                    simMutex.Enter(ref hasSimLock);
                    if (hasSimLock)
                    {
                        // IPL.SimulatorCommit(SimulatorDefault);
                        // IPL.SimulatorRunDirect(SimulatorDefault);
                        IPL.SimulatorRunReflections(SimulatorDefault);
                        simMutex.Exit();
                    }
                    // mutex.Exit();
                }
                Thread.Sleep(3);
                Thread.Yield();
            }
        });
        reflectThread.Start();
        audioThread = new Thread(() =>
        {
            while (IsInstanceValid(this) && threadsRunning)
            {
                if (!loaded)
                {
                    Thread.Sleep(10);
                    continue;
                }
                bool hasLock = false;
                mutex.TryEnter(ref hasLock);
                if (hasLock)
                {
                    SetupInputs();
                    IPL.SimulatorRunDirect(SimulatorDefault);
                    RunSim();
                    mutex.Exit();
                }
                Thread.Sleep(1);
                Thread.Yield();
            }
        });
        audioThread.Start();
    }

    public override void _Process(double delta)
    {
        if (!loaded)
        {
            return;
        }
        if (IsInstanceValid(camera))
            cameraTransform = camera.GlobalTransform;
        // SetupInputs();
        // RunSim();
    }

    public void SetupInputs()
    {
        if (IsInstanceValid(camera))
        {
            var inputs = new IPL.SimulationSharedInputs()
            {
                Listener = GetIPLTransform(cameraTransform),
                NumRays = 4096,
                NumBounces = 16,
                Duration = 2f,
                Order = 2,
                IrradianceMinDistance = 1f,
            };
            IPL.SimulatorSetSharedInputs(SimulatorDefault, IPL.SimulationFlags.Direct | IPL.SimulationFlags.Reflections, in inputs);
        }
    }

    public void RunSim()
    {
        // IPL.SimulatorRunDirect(SimulatorDefault);
        if (IsInstanceValid(camera))
            OnSimulatorRun?.Invoke();
    }

    public override void _ExitTree()
    {
        StopThreads();
        Stop();
    }

    public void StopThreads()
    {
        threadsRunning = false;
        reflectThread.Join();
        audioThread.Join();
    }

    public static void WaitBlockingProcess(Action callback)
    {
        bool hasLock = false;
        while (!hasLock)
        {
            mutex.Enter(ref hasLock);
            if (hasLock)
            {
                try
                {
                    callback();
                }
                catch (Exception e)
                {
                    GD.PrintErr(LogPrefix, e);
                }
                mutex.Exit();
                break;
            }
            // Thread.Sleep(2);
            Thread.Yield();
        }
    }

    public static void WaitBlockingSimulation(Action callback)
    {
        bool hasLock = false;
        while (!hasLock)
        {
            simMutex.Enter(ref hasLock);
            if (hasLock)
            {
                try
                {
                    callback();
                }
                catch (Exception e)
                {
                    GD.PrintErr(LogPrefix, e);
                }
                simMutex.Exit();
                break;
            }
            // Thread.Sleep(2);
            Thread.Yield();
        }
    }

    public static void WaitSimulation(Action callback)
    {
        new Thread(() =>
        {
            // if (mutex.WaitOne())
            bool hasLock = false;
            while (!hasLock)
            {
                simMutex.TryEnter(ref hasLock);
                if (hasLock)
                {
                    try
                    {
                        callback();
                    }
                    catch (Exception e)
                    {
                        GD.PrintErr(LogPrefix, e);
                    }
                    simMutex.Exit();
                    break;
                }
                Thread.Sleep(10);
                // Thread.Yield();
            }
        }).Start();
    }

    public static void WaitProcess(Action callback)
    {
        new Thread(() =>
        {
            bool hasLock = false;
            while (!hasLock)
            {
                mutex.TryEnter(ref hasLock);
                if (hasLock)
                {
                    try
                    {
                        callback();
                    }
                    catch (Exception e)
                    {
                        GD.PrintErr(LogPrefix, e);
                    }
                    mutex.Exit();
                    break;
                }
                Thread.Sleep(2);
                Thread.Yield();
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
        loaded = false;
        IPL.HrtfRelease(ref HrtfDefault);
        IPL.SceneRelease(ref SceneDefault);
        IPL.SimulatorRelease(ref SimulatorDefault);
        HrtfDefault = default;
        SceneDefault = default;
        SimulatorDefault = default;
        sources.Clear();
        simulators.Clear();
        staticMeshes.Clear();
        scenes.Clear();
        audioBuffers.Clear();
        directEffects.Clear();
        reflectionMixers.Clear();
        reflectionEffects.Clear();
        binauralEffects.Clear();
        hrtfs.Clear();
        /*
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
        */
        IPL.ContextRelease(ref iplCtx);
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

    public static IPL.Vector3 ConvertToIPLRaw(Vector3 v)
    {
        return new IPL.Vector3(v.X, v.Y, v.Z);
    }

    public static IPL.CoordinateSpace3 GetIPLTransform(Transform3D v)
    {
        return new IPL.CoordinateSpace3()
        {
            Ahead = ConvertToIPLRaw(-v.Basis.Z.Normalized()),
            Origin = ConvertToIPL(v.Origin),
            Right = ConvertToIPLRaw(v.Basis.X.Normalized()),
            Up = ConvertToIPLRaw(v.Basis.Y.Normalized()),
        };
    }

    public static IPL.CoordinateSpace3 GetIPLTransformOnlyOrigin(Transform3D v)
    {
        return new IPL.CoordinateSpace3()
        {
            Ahead = ConvertToIPLRaw(Vector3.Forward),
            Origin = ConvertToIPL(v.Origin),
            Right = ConvertToIPLRaw(Vector3.Right),
            Up = ConvertToIPLRaw(Vector3.Up),
        };
    }

    public static IPL.CoordinateSpace3 GetIPLTransformNoOrigin(Transform3D v)
    {
        return new IPL.CoordinateSpace3()
        {
            Ahead = ConvertToIPLRaw(-v.Basis.Z.Normalized()),
            Origin = ConvertToIPL(Vector3.Zero),
            Right = ConvertToIPLRaw(v.Basis.X.Normalized()),
            Up = ConvertToIPLRaw(v.Basis.Y.Normalized()),
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
        IPL.SourceAdd(source, simulator);
        WaitSimulation(() =>
        {
            IPL.SimulatorCommit(simulator);
        });
        sources.Add(source);
        return source;
    }

    public static void DelSource(ref IPL.Source source, IPL.Simulator simulator)
    {
        if (!loaded)
            throw new Exception();
        sources.Remove(source);
        IPL.SourceRemove(source, simulator);
        WaitSimulation(() =>
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
            MaxNumSources = 32,
            NumThreads = 4,
            SamplingRate = iplAudioSettings.SamplingRate,
            FrameSize = iplAudioSettings.FrameSize,
            MaxNumOcclusionSamples = 1024,
            NumVisSamples = 16,
            RayBatchSize = 16,
        };
        CheckError(IPL.SimulatorCreate(iplCtx, in settings, out var sim));
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
        staticMeshes.Add(mesh);
        return mesh;
    }

    public static void DelStaticMesh(ref IPL.StaticMesh mesh)
    {
        if (!loaded)
            throw new Exception();
        staticMeshes.Remove(mesh);
        IPL.StaticMeshRelease(ref mesh);
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
        directEffects.Add(effect);
        return effect;
    }

    public static void DelDirectEffect(ref IPL.DirectEffect effect)
    {
        if (!loaded)
            throw new Exception();
        directEffects.Remove(effect);
        IPL.DirectEffectRelease(ref effect);
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
        reflectionEffects.Add(effect);
        return effect;
    }

    public static void DelReflectionEffect(ref IPL.ReflectionEffect effect)
    {
        if (!loaded)
            throw new Exception();
        reflectionEffects.Remove(effect);
        IPL.ReflectionEffectRelease(ref effect);
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
        reflectionMixers.Add(effect);
        return effect;
    }

    public static void DelReflectionMixer(ref IPL.ReflectionMixer effect)
    {
        if (!loaded)
            throw new Exception();
        reflectionMixers.Remove(effect);
        IPL.ReflectionMixerRelease(ref effect);
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
        binauralEffects.Add(effect);
        return effect;
    }

    public static void DelBinauralEffect(ref IPL.BinauralEffect effect)
    {
        if (!loaded)
            throw new Exception();
        binauralEffects.Remove(effect);
        IPL.BinauralEffectRelease(ref effect);
    }

    public static IPL.AmbisonicsDecodeEffect NewAmbisonicsDecodeEffect(IPL.Hrtf hrtf)
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
        // binauralEffects.Add(effect);
        return effect;
    }

    public static void DelAmbisonicsDecodeEffect(ref IPL.AmbisonicsDecodeEffect effect)
    {
        if (!loaded)
            throw new Exception();
        // binauralEffects.Remove(effect);
        IPL.AmbisonicsDecodeEffectRelease(ref effect);
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

    public static void DelAudioBuffer(ref IPL.AudioBuffer buffer)
    {
        if (!loaded)
            throw new Exception();
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