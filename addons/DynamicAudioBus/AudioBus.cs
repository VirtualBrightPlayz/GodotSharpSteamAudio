using System;
using System.Collections.Generic;
using Godot;

public class AudioBus : IDisposable
{
    public readonly string Name;

    public int Index
    {
        get
        {
            for (int i = 0; i < AudioServer.BusCount; i++)
            {
                if (AudioServer.GetBusName(i) == Name)
                {
                    return i;
                }
            }
            return -1;
        }
    }

    public bool Mute
    {
        get => AudioServer.IsBusMute(Index);
        set => AudioServer.SetBusMute(Index, value);
    }

    public AudioBus()
    {
        AudioServer.AddBus();
        Name = Guid.NewGuid().ToString();
        AudioServer.SetBusName(AudioServer.BusCount - 1, Name);
    }

    public void Dispose()
    {
        AudioServer.RemoveBus(Index);
    }

    public void SetParent(string name)
    {
        AudioServer.SetBusSend(Index, name);
    }

    public void AddEffect(AudioEffect effect, int at = -1)
    {
        AudioServer.AddBusEffect(Index, effect, at);
    }

    public void RemoveEffectAt(int effectIdx)
    {
        AudioServer.RemoveBusEffect(Index, effectIdx);
    }

    public AudioEffectInstance GetEffectInstance(int effectIdx)
    {
        return AudioServer.GetBusEffectInstance(Index, effectIdx);
    }
}
