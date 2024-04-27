using System;
using System.Runtime.CompilerServices;
using SteamAudio;

public partial class ProcessBuffersDirect : IDisposable
{
    public IPL.DirectEffect DirectEffect;
    public IPL.BinauralEffect BinauralEffect;
    public IPL.AudioBuffer InputBuffer;
    public IPL.AudioBuffer EffectBuffer;
    public IPL.AudioBuffer OutputBuffer;

    public IPL.ReflectionEffect ReflectionEffect;
    public IPL.ReflectionMixer ReflectionMixer;
    public IPL.AmbisonicsDecodeEffect AmbisonicsDecodeEffect;

    public bool IsDisposed = false;

    public ProcessBuffersDirect()
    {
        DirectEffect = GDSteamAudio.NewDirectEffect(1);
        BinauralEffect = GDSteamAudio.NewBinauralEffect(GDSteamAudio.HrtfDefault);
        InputBuffer = GDSteamAudio.NewAudioBuffer(1);
        EffectBuffer = GDSteamAudio.NewAudioBuffer(1);
        OutputBuffer = GDSteamAudio.NewAudioBuffer(2);
    }

    public float[] Process(ref IPL.DirectEffectParams directArgs, ref IPL.BinauralEffectParams binauralArgs)
    {
        if (IsDisposed)
            return Array.Empty<float>();
        var InterlacingBuffer = new float[OutputBuffer.NumSamples * OutputBuffer.NumChannels];
        IPL.DirectEffectApply(DirectEffect, ref directArgs, ref InputBuffer, ref EffectBuffer);
        IPL.BinauralEffectApply(BinauralEffect, ref binauralArgs, ref EffectBuffer, ref OutputBuffer);
        IPL.AudioBufferInterleave(GDSteamAudio.iplCtx, in OutputBuffer, in Unsafe.AsRef(InterlacingBuffer[0]));
        return InterlacingBuffer;
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;
        IsDisposed = true;
        GDSteamAudio.DelDirectEffect(ref DirectEffect);
        GDSteamAudio.DelBinauralEffect(ref BinauralEffect);
        GDSteamAudio.DelAudioBuffer(ref InputBuffer);
        GDSteamAudio.DelAudioBuffer(ref EffectBuffer);
        GDSteamAudio.DelAudioBuffer(ref OutputBuffer);
    }
}