using Godot;

public partial class Egg : Node2D
{
    private const double MinHatchSeconds = 5.0;
    private const double MaxHatchSeconds = 10.0;

    private static readonly PackedScene LarvaScene = GD.Load<PackedScene>("res://AntCity/Scenes/Entities/Larva.tscn");

    private ColonyManager colonyManager;

    public override void _Ready()
    {
        colonyManager = GetNode<ColonyManager>("/root/Main/ColonyManager");

        Timer hatchTimer = GetNode<Timer>("HatchTimer");
        hatchTimer.OneShot = true;
        hatchTimer.WaitTime = GD.RandRange(MinHatchSeconds, MaxHatchSeconds) * colonyManager.HatchSpeedMultiplier;
        hatchTimer.Timeout += Hatch;
        hatchTimer.Start();
    }

    private void Hatch()
    {
        colonyManager.RemoveEgg();

        Node2D larva = LarvaScene.Instantiate<Node2D>();
        larva.Position = Position;
        GetParent().AddChild(larva);

        QueueFree();
    }
}
