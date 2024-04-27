using Godot;
using SteamAudio;

[GlobalClass]
public partial class SteamAudioCollider : Node
{
    [Export]
    public GDSteamAudio.MaterialPreset Preset = GDSteamAudio.MaterialPreset.Generic;
    private GDSteamAudio.MaterialPreset lastPreset;
    public IPL.StaticMesh staticMesh;
    private bool loaded = false;

    public override void _EnterTree()
    {
        LoadMesh();
    }

    public override void _ExitTree()
    {
        UnloadMesh();
    }

    public void LoadMesh()
    {
        if (!GDSteamAudio.loaded)
            return;
        if (loaded)
            return;
        loaded = true;
        lastPreset = Preset;
        CollisionShape3D parent = GetParent<CollisionShape3D>();
        staticMesh = GDSteamAudio.NewStaticMesh(GDSteamAudio.SceneDefault, Preset, parent.GlobalTransform, (ConcavePolygonShape3D)parent.Shape);
        IPL.StaticMeshAdd(staticMesh, GDSteamAudio.SceneDefault);
        GDSteamAudio.WaitOne(() =>
        {
            IPL.SceneCommit(GDSteamAudio.SceneDefault);
            IPL.SimulatorCommit(GDSteamAudio.SimulatorDefault);
        });
    }

    public override void _Process(double delta)
    {
        if (lastPreset != Preset)
        {
            UnloadMesh();
            LoadMesh();
        }
        lastPreset = Preset;
    }

    public void UnloadMesh()
    {
        loaded = false;
        if (!GDSteamAudio.loaded)
            return;
        CollisionShape3D parent = GetParent<CollisionShape3D>();
        IPL.StaticMeshRemove(staticMesh, GDSteamAudio.SceneDefault);
        GDSteamAudio.WaitOne(() =>
        {
            IPL.SceneCommit(GDSteamAudio.SceneDefault);
            IPL.SimulatorCommit(GDSteamAudio.SimulatorDefault);
        });
        GDSteamAudio.DelStaticMesh(ref staticMesh);
    }
}