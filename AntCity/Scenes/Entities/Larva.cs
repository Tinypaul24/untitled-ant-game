using Godot;

public partial class Larva : Node2D
{
    private const double MinMatureSeconds = 13.0;
    private const double MaxMatureSeconds = 17.0;
    private const double RecheckSeconds = 2.0;

    private static readonly PackedScene AntWorkerScene = GD.Load<PackedScene>("res://AntCity/Scenes/Entities/AntWorker.tscn");

    private ColonyManager colonyManager;
    private Timer matureTimer;

    public double RestoreSecondsLeft { get; set; }

    public double SecondsLeft => matureTimer.TimeLeft;

    public override void _Ready()
    {
        colonyManager = GetNode<ColonyManager>("../ColonyManager");
        colonyManager.AddLarva();

        matureTimer = GetNode<Timer>("MatureTimer");
        matureTimer.OneShot = true;
        matureTimer.WaitTime = RestoreSecondsLeft > 0
            ? RestoreSecondsLeft
            : GD.RandRange(MinMatureSeconds, MaxMatureSeconds) * colonyManager.HatchSpeedMultiplier;
        matureTimer.Timeout += Mature;
        matureTimer.Start();
    }

    private void Mature()
    {
        colonyManager.AddAnt();
        colonyManager.RemoveLarva();

        Node2D ant = AntWorkerScene.Instantiate<Node2D>();
        ant.Position = Position;
        GetParent().AddChild(ant);

        QueueFree();
    }
}
