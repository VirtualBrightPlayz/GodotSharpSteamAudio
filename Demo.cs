using Godot;
using System;

public partial class Demo : Node3D
{
    [Export]
    public Label info;
    [Export]
    public AudioStreamPlayer3D player;

    public override void _Process(double delta)
    {
        info.Text = $"{GDSteamAudio.loaded}|{player.Bus}";
    }

    public void Toggle()
    {
        if (GDSteamAudio.loaded)
            Stop();
        else
            Start();
    }

    public void Start()
    {
        GDSteamAudio.Start();
    }

    public void Stop()
    {
        GDSteamAudio.Stop();
    }
}