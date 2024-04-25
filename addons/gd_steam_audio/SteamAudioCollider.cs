using Godot;
using SteamAudio;

[GlobalClass]
public partial class SteamAudioCollider : Node
{
    [Export]
    public GDSteamAudio.MaterialPreset Preset = GDSteamAudio.MaterialPreset.Generic;
    private GDSteamAudio.MaterialPreset lastPreset;
    public IPL.StaticMesh staticMesh;

    public override void _EnterTree()
    {
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
            _ExitTree();
            _EnterTree();
        }
        lastPreset = Preset;
    }

    public override void _ExitTree()
    {
        CollisionShape3D parent = GetParent<CollisionShape3D>();
        IPL.StaticMeshRemove(staticMesh, GDSteamAudio.SceneDefault);
        GDSteamAudio.WaitOne(() =>
        {
            IPL.SceneCommit(GDSteamAudio.SceneDefault);
            IPL.SimulatorCommit(GDSteamAudio.SimulatorDefault);
        });
    }
}