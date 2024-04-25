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
    }

    // [Export]
    public bool LinearDistance = false; // seems to crash stuff
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
    private IPL.BinauralEffect effect;
    private IPL.DirectEffect directEffect;
    private IPL.ReflectionEffect reflectionEffect;
    private IPL.AmbisonicsDecodeEffect decodeEffect;
    private IPL.AudioBuffer buffer;
    private IPL.AudioBuffer bufferEffect;
    private IPL.AudioBuffer bufferEffect2;
    private IPL.AudioBuffer bufferOut;
    private IPL.Source source;
    private float[] tempInterlacingBuffer;
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
        effect = GDSteamAudio.NewBinauralEffect(GDSteamAudio.HrtfDefault);
        directEffect = GDSteamAudio.NewDirectEffect(1);
        reflectionEffect = GDSteamAudio.NewReflectionEffect(9);
        decodeEffect = GDSteamAudio.NewAmbisonicDecodeEffect(GDSteamAudio.HrtfDefault);
        buffer = GDSteamAudio.NewAudioBuffer(1);
        bufferEffect = GDSteamAudio.NewAudioBuffer(1);
        bufferEffect2 = GDSteamAudio.NewAudioBuffer(9);
        bufferOut = GDSteamAudio.NewAudioBuffer(2);
        tempInterlacingBuffer = new float[bufferOut.NumSamples * bufferOut.NumChannels];

        player = new AudioStreamPlayer3D();
        player.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled;
        player.PanningStrength = 0f;
        // bus = new AudioBus();
        // bus.Mute = true;
        capture = new AudioEffectCapture();
        capture.BufferLength = (float)GDSteamAudio.iplAudioSettings.FrameSize / GDSteamAudio.iplAudioSettings.SamplingRate;
        // bus.AddEffect(capture);
        parent = GetParent<AudioStreamPlayer3D>();
        player.Bus = parent.Bus;
        // parent.Bus = bus.Name;
        parent.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled;
        parent.PanningStrength = 0f;
        AddChild(player);
        player.Stream = new AudioStreamGenerator()
        {
            MixRate = GDSteamAudio.iplAudioSettings.SamplingRate,
            BufferLength = (float)GDSteamAudio.iplAudioSettings.FrameSize / GDSteamAudio.iplAudioSettings.SamplingRate,
        };

        source = default;
        // source = GDSteamAudio.NewSource(GDSteamAudio.SimulatorDefault);
        dataBuffer.dir = parent.GlobalPosition * GDSteamAudio.Instance.camera.GlobalTransform;
        dataBuffer.camTransform = GDSteamAudio.Instance.camera.GlobalTransform;
        GDSteamAudio.Instance.OnSimulatorRun += SimRun;
        Play();
    }

    public override void _Process(double delta)
    {
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
            else
            {
                parent.Bus = player.Bus;
                parent.StreamPaused = true;
                bus?.Dispose();
                bus = null;
                if (source.Handle != IntPtr.Zero)
                    GDSteamAudio.DelSource(source, GDSteamAudio.SimulatorDefault);
                source = default;
                return;
            }
        }
        else
        {
            return;
        }

        dataBuffer.dir = parent.GlobalPosition * GDSteamAudio.Instance.camera.GlobalTransform;
        dataBuffer.camTransform = GDSteamAudio.Instance.camera.GlobalTransform;

        var inputs = new IPL.SimulationInputs()
        {
            Flags = IPL.SimulationFlags.Direct | IPL.SimulationFlags.Reflections,
            Source = GDSteamAudio.GetIPLTransform(parent.GlobalTransform),
            DirectFlags = IPL.DirectSimulationFlags.DistanceAttenuation | IPL.DirectSimulationFlags.Occlusion | IPL.DirectSimulationFlags.Transmission,
            DistanceAttenuationModel = new IPL.DistanceAttenuationModel()
            {
                Type = LinearDistance ? IPL.DistanceAttenuationModelType.Callback : IPL.DistanceAttenuationModelType.InverseDistance,
                Callback = DistCallback,
                MinDistance = MinDistance,
                Dirty = 1,
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
        // if (player.Playing)
            // FillBuffer();
        // else
        if (!player.Playing)
            Play();
    }

    public override void _ExitTree()
    {
        GDSteamAudio.Instance.OnSimulatorRun -= SimRun;
        if (source.Handle != IntPtr.Zero)
            GDSteamAudio.DelSource(source, GDSteamAudio.SimulatorDefault);
        GDSteamAudio.DelAudioBuffer(buffer);
        GDSteamAudio.DelAudioBuffer(bufferEffect);
        GDSteamAudio.DelAudioBuffer(bufferEffect2);
        GDSteamAudio.DelAudioBuffer(bufferOut);
        bus?.Dispose();
        capture.Dispose();
    }

    private float DistCallback(float distance, IntPtr userData)
    {
        return Mathf.Clamp(Mathf.InverseLerp(MaxDistance, MinDistance, distance), 0f, 1f);
    }

    private void SimRun()
    {
        if (source.Handle == IntPtr.Zero)
            return;
        IPL.SourceGetOutputs(source, IPL.SimulationFlags.Direct | IPL.SimulationFlags.Reflections, out var outputs);
        directEffectData = outputs.Direct;
        reflectionEffectData = outputs.Reflections;
        // if (player.Playing)
        FillBuffer();
    }

    private unsafe void FillBuffer()
    {
        var cam = GDSteamAudio.Instance.camera;
        if (cam == null)
        {
            return;
        }
        int amount = Mathf.Min(playback.GetFramesAvailable(), GDSteamAudio.iplAudioSettings.FrameSize);
        if (amount == 0)
        {
            return;
        }
        if (amount < GDSteamAudio.iplAudioSettings.FrameSize)
        {
            return;
        }
        if (capture.GetFramesAvailable() < amount)
            return;
        Vector2[] data = capture.GetBuffer(amount);
        float *dataPcm = ((float**)buffer.Data)[0];
        for (int i = 0; i < amount; i++)
        {
            dataPcm[i] = (data[i].X + data[i].Y) / 2f * VolumeMultiplier;
        }

        // var dir = parent.GlobalPosition * cam.GlobalTransform;
        var dir = dataBuffer.dir;

        var directEffectParams = new IPL.DirectEffectParams();
        directEffectParams = directEffectData;
        directEffectParams.Flags = IPL.DirectEffectFlags.ApplyDistanceAttenuation | IPL.DirectEffectFlags.ApplyOcclusion | IPL.DirectEffectFlags.ApplyTransmission;

        var reflectionEffectParams = new IPL.ReflectionEffectParams();
        reflectionEffectParams = reflectionEffectData;
        reflectionEffectParams.IrSize = GDSteamAudio.iplAudioSettings.SamplingRate * 2;
        reflectionEffectParams.NumChannels = bufferEffect2.NumChannels;

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
        if (Reflections && dir.LengthSquared() <= MaxDistance * MaxDistance)
        {
            IPL.ReflectionEffectApply(reflectionEffect, ref reflectionEffectParams, ref buffer, ref bufferEffect2, default);
            IPL.AmbisonicsDecodeEffectApply(decodeEffect, ref decodeParams, ref bufferEffect2, ref bufferOut);
        }
        else
        {
            IPL.DirectEffectApply(directEffect, ref directEffectParams, ref buffer, ref bufferEffect);
            IPL.BinauralEffectApply(effect, ref binauralEffectParams, ref bufferEffect, ref bufferOut);
        }
        IPL.AudioBufferInterleave(GDSteamAudio.iplCtx, in bufferOut, in Unsafe.AsRef(tempInterlacingBuffer[0]));
        Vector2[] frames = new Vector2[amount];
        for (int i = 0; i < frames.Length; i++)
        {
            frames[i] = new Vector2(tempInterlacingBuffer[i*bufferOut.NumChannels], tempInterlacingBuffer[i*bufferOut.NumChannels+1]);
        }
        playback.PushBuffer(frames);
    }
}