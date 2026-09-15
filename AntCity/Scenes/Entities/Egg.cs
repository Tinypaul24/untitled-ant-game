using Godot;

public partial class Egg : Node2D
{
    private const double MinHatchSeconds = 5.0;
    private const double MaxHatchSeconds = 10.0;

    private static readonly PackedScene LarvaScene = GD.Load<PackedScene>("res://AntCity/Scenes/Entities/Larva.tscn");

    private ColonyManager colonyManager;
    private Timer hatchTimer;

    public double RestoreSecondsLeft { get; set; }

    public double SecondsLeft => hatchTimer.TimeLeft;

    public override void _Ready()
    {
        colonyManager = GetNode<ColonyManager>("../ColonyManager");

        hatchTimer = GetNode<Timer>("HatchTimer");
        hatchTimer.OneShot = true;
        hatchTimer.WaitTime = RestoreSecondsLeft > 0
            ? RestoreSecondsLeft
            : GD.RandRange(MinHatchSeconds, MaxHatchSeconds) * colonyManager.HatchSpeedMultiplier;
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
