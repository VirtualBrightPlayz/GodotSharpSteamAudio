using System;
using System.Runtime.CompilerServices;
using SteamAudio;

public partial class ProcessBuffersReflection : IDisposable
{
    public IPL.ReflectionEffect ReflectionEffect;
    public IPL.AmbisonicsDecodeEffect AmbisonicsDecodeEffect;
    public IPL.AudioBuffer InputBuffer;
    public IPL.AudioBuffer EffectBuffer;
    public IPL.AudioBuffer OutputBuffer;

    public bool IsDisposed = false;

    public ProcessBuffersReflection()
    {
        ReflectionEffect = GDSteamAudio.NewReflectionEffect(9);
        AmbisonicsDecodeEffect = GDSteamAudio.NewAmbisonicsDecodeEffect(GDSteamAudio.HrtfDefault);
        InputBuffer = GDSteamAudio.NewAudioBuffer(1);
        EffectBuffer = GDSteamAudio.NewAudioBuffer(9);
        OutputBuffer = GDSteamAudio.NewAudioBuffer(2);
    }

    public float[] Process(ref IPL.ReflectionEffectParams reflectionArgs, ref IPL.AmbisonicsDecodeEffectParams ambisonicsArgs)
    {
        // if (IsDisposed)
        //     return Array.Empty<float>();
        var InterlacingBuffer = new float[OutputBuffer.NumSamples * OutputBuffer.NumChannels];
        IPL.ReflectionEffectApply(ReflectionEffect, ref reflectionArgs, ref InputBuffer, ref EffectBuffer, default);
        IPL.AmbisonicsDecodeEffectApply(AmbisonicsDecodeEffect, ref ambisonicsArgs, ref EffectBuffer, ref OutputBuffer);
        IPL.AudioBufferInterleave(GDSteamAudio.iplCtx, in OutputBuffer, in Unsafe.AsRef(InterlacingBuffer[0]));
        return InterlacingBuffer;
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;
        IsDisposed = true;
        GDSteamAudio.WaitOne(() =>
        {
            GDSteamAudio.DelReflectionEffect(ref ReflectionEffect);
            GDSteamAudio.DelAmbisonicsDecodeEffect(ref AmbisonicsDecodeEffect);
            GDSteamAudio.DelAudioBuffer(ref InputBuffer);
            GDSteamAudio.DelAudioBuffer(ref EffectBuffer);
            GDSteamAudio.DelAudioBuffer(ref OutputBuffer);
        });
    }
}