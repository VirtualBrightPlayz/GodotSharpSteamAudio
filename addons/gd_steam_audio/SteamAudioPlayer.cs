using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Godot;
using SteamAudio;

[GlobalClass]
public partial class SteamAudioPlayer : Node
{
    public struct DataBuffer
    {
        public Vector3 dir;
        public Transform3D camTransform;
        public Transform3D parentTransform;
    }

    [Export]
    public bool LinearDistance = false;
    [Export]
    public float MinDistance = 5f;
    [Export]
    public float MaxDistance = 50f;
    [Export]
    public float Radius = 0f;
    [Export]
    public bool Reflections = false;
    [Export(PropertyHint.Range, "0,2")]
    public float VolumeMultiplier = 1f;

    private AudioStreamGeneratorPlayback playback;
    private ProcessBuffersDirect directBuffers;
    private ProcessBuffersReflection reflectionBuffers;
    private IPL.Source source;
    private AudioStreamPlayer3D player;
    private IPL.DirectEffectParams directEffectData;
    private IPL.ReflectionEffectParams reflectionEffectData;

    private AudioBus bus;
    private AudioEffectCapture capture;
    private AudioStreamPlayer3D parent;
    private DataBuffer dataBuffer;

    public bool Playing => player.Playing;

    public void Play()
    {
        player.Play();
        playback = (AudioStreamGeneratorPlayback)player.GetStreamPlayback();
    }

    public void Stop()
    {
        player.Stop();
    }

    public override void _EnterTree()
    {
        directBuffers = new ProcessBuffersDirect();
        reflectionBuffers = new ProcessBuffersReflection();

        player = new AudioStreamPlayer3D();
        player.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled;
        player.PanningStrength = 0f;
        capture = new AudioEffectCapture();
        capture.BufferLength = (float)GDSteamAudio.iplAudioSettings.FrameSize / GDSteamAudio.iplAudioSettings.SamplingRate;
        parent = GetParent<AudioStreamPlayer3D>();
        player.Bus = parent.Bus;
        parent.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled;
        parent.PanningStrength = 0f;
        AddChild(player);
        player.Stream = new AudioStreamGenerator()
        {
            MixRate = GDSteamAudio.iplAudioSettings.SamplingRate,
            BufferLength = (float)GDSteamAudio.iplAudioSettings.FrameSize / GDSteamAudio.iplAudioSettings.SamplingRate,
        };

        source = default;
        if (IsInstanceValid(GDSteamAudio.Instance?.camera))
        {
            dataBuffer.dir = parent.GlobalPosition * GDSteamAudio.Instance.camera.GlobalTransform;
            dataBuffer.camTransform = GDSteamAudio.Instance.camera.GlobalTransform;
            dataBuffer.parentTransform = parent.GlobalTransform;
        }
        GDSteamAudio.Instance.OnSimulatorRun += SimRun;
        Play();
    }

    public override void _Process(double delta)
    {
        bool found = false;
        if (parent != null && IsInstanceValid(GDSteamAudio.Instance.camera))
        {
            if (parent.GlobalPosition.DistanceSquaredTo(GDSteamAudio.Instance.camera.GlobalPosition) <= MaxDistance * MaxDistance)
            {
                if (bus == null)
                {
                    bus = new AudioBus();
                    bus.Mute = true;
                    bus.AddEffect(capture);
                    parent.StreamPaused = false;
                    player.Bus = parent.Bus;
                    parent.Bus = bus.Name;
                    source = GDSteamAudio.NewSource(GDSteamAudio.SimulatorDefault);
                }
                else if (parent.Bus != bus.Name)
                {
                    player.Bus = parent.Bus;
                    parent.Bus = bus.Name;
                }
            }
            else if (bus != null)
            {
                parent.Bus = player.Bus;
                parent.StreamPaused = true;
                bus.Dispose();
                bus = null;
                if (source.Handle != IntPtr.Zero)
                {
                    GDSteamAudio.DelSource(source, GDSteamAudio.SimulatorDefault);
                }
                source = default;
                found = true;
            }
            else
            {
                found = true;
            }
        }
        else
        {
            found = true;
        }

        if (!found)
        {
            dataBuffer.parentTransform = parent.GlobalTransform;
            dataBuffer.camTransform = GDSteamAudio.Instance.camera.GlobalTransform;
            dataBuffer.dir = dataBuffer.parentTransform.Origin - dataBuffer.camTransform.Origin;
            if (!player.Playing)
                Play();
        }
    }

    public override void _ExitTree()
    {
        GDSteamAudio.Instance.OnSimulatorRun -= SimRun;
        if (source.Handle != IntPtr.Zero)
            GDSteamAudio.DelSource(source, GDSteamAudio.SimulatorDefault);
        directBuffers.Dispose();
        directBuffers = null;
        reflectionBuffers.Dispose();
        reflectionBuffers = null;
        bus?.Dispose();
        capture = null;
    }

    private static float DistCallback(float distance, IntPtr userData)
    {
        if (!IsInstanceIdValid((ulong)userData))
            return 0.1f;
        var obj = InstanceFromId((ulong)userData);
        if (IsInstanceValid(obj) && obj is SteamAudioPlayer plr)
            return Mathf.Clamp(Mathf.InverseLerp(plr.MaxDistance, plr.MinDistance, distance), 0f, 1f);
        return 0.1f;
    }

    private void SimRun()
    {
        if (!IsInstanceValid(this))
            return;
        if (source.Handle == IntPtr.Zero)
            return;

        var inputs = new IPL.SimulationInputs()
        {
            Flags = IPL.SimulationFlags.Direct | IPL.SimulationFlags.Reflections,
            Source = GDSteamAudio.GetIPLTransform(dataBuffer.parentTransform),
            DirectFlags = IPL.DirectSimulationFlags.DistanceAttenuation | IPL.DirectSimulationFlags.Occlusion | IPL.DirectSimulationFlags.Transmission,
            DistanceAttenuationModel = new IPL.DistanceAttenuationModel()
            {
                Type = LinearDistance ? IPL.DistanceAttenuationModelType.Callback : IPL.DistanceAttenuationModelType.InverseDistance,
                Callback = DistCallback,
                UserData = (nint)GetInstanceId(),
                MinDistance = MinDistance,
            },
            AirAbsorptionModel = new IPL.AirAbsorptionModel()
            {
                Type = IPL.AirAbsorptionModelType.Default,
            },
            NumOcclusionSamples = 16,
            OcclusionRadius = Radius,
            OcclusionType = IPL.OcclusionType.Volumetric,
        };
        IPL.SourceSetInputs(source, IPL.SimulationFlags.Direct | IPL.SimulationFlags.Reflections, in inputs);

        IPL.SourceGetOutputs(source, IPL.SimulationFlags.Direct | IPL.SimulationFlags.Reflections, out var outputs);
        directEffectData = outputs.Direct;
        reflectionEffectData = outputs.Reflections;
        FillBuffer();
    }

    private unsafe void FillBuffer()
    {
        var cam = GDSteamAudio.Instance.camera;
        if (cam == null)
            return;
        if (!IsInstanceValid(playback) || !IsInstanceValid(capture))
            return;
        int amount = Mathf.Min(playback.GetFramesAvailable(), GDSteamAudio.iplAudioSettings.FrameSize);
        if (amount == 0)
            return;
        if (amount < GDSteamAudio.iplAudioSettings.FrameSize)
            return;
        if (capture.GetFramesAvailable() < amount)
            return;
        if (directBuffers == null || reflectionBuffers == null)
            return;

        Vector2[] data = capture.GetBuffer(amount);
        float *dataPcmDirect = ((float**)directBuffers.InputBuffer.Data)[0];
        float *dataPcmReflect = ((float**)reflectionBuffers.InputBuffer.Data)[0];
        for (int i = 0; i < amount; i++)
        {
            dataPcmDirect[i] = dataPcmReflect[i] = (data[i].X + data[i].Y) / 2f * VolumeMultiplier;
        }

        var dir = dataBuffer.dir;

        var directEffectParams = new IPL.DirectEffectParams();
        directEffectParams = directEffectData;
        directEffectParams.Flags = IPL.DirectEffectFlags.ApplyDistanceAttenuation | IPL.DirectEffectFlags.ApplyOcclusion | IPL.DirectEffectFlags.ApplyTransmission;

        var reflectionEffectParams = new IPL.ReflectionEffectParams();
        reflectionEffectParams = reflectionEffectData;
        reflectionEffectParams.IrSize = GDSteamAudio.iplAudioSettings.SamplingRate * 2;
        reflectionEffectParams.NumChannels = reflectionBuffers.EffectBuffer.NumChannels;

        var binauralEffectParams = new IPL.BinauralEffectParams()
        {
            Hrtf = GDSteamAudio.HrtfDefault,
            Direction = GDSteamAudio.ConvertToIPL(dir.Normalized()),
            Interpolation = IPL.HrtfInterpolation.Nearest,
            SpatialBlend = 1f,
        };
        var decodeParams = new IPL.AmbisonicsDecodeEffectParams()
        {
            Hrtf = GDSteamAudio.HrtfDefault,
            Order = 2,
            Orientation = GDSteamAudio.GetIPLTransform(dataBuffer.camTransform),
            Binaural = 1,
        };
        Vector2[] frames = new Vector2[amount];
        if (Reflections && dir.LengthSquared() <= MaxDistance * MaxDistance)
        {
            var InterlacingBuffer = reflectionBuffers.Process(ref reflectionEffectParams, ref decodeParams);
            for (int i = 0; i < frames.Length; i++)
            {
                frames[i] += new Vector2(InterlacingBuffer[i*reflectionBuffers.OutputBuffer.NumChannels], InterlacingBuffer[i*reflectionBuffers.OutputBuffer.NumChannels+1]);
            }
        }
        // TODO: optional and variable direct and reflection effects
        {
            var InterlacingBuffer = directBuffers.Process(ref directEffectParams, ref binauralEffectParams);
            for (int i = 0; i < frames.Length; i++)
            {
                frames[i] += new Vector2(InterlacingBuffer[i*directBuffers.OutputBuffer.NumChannels], InterlacingBuffer[i*directBuffers.OutputBuffer.NumChannels+1]);
            }
        }
        playback.PushBuffer(frames);
    }
}