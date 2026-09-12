using Godot;

public partial class Larva : Node2D
{
    private const double MinMatureSeconds = 13.0;
    private const double MaxMatureSeconds = 17.0;
    private const double RecheckSeconds = 2.0;

    private static readonly PackedScene AntWorkerScene = GD.Load<PackedScene>("res://AntCity/Scenes/Entities/AntWorker.tscn");

    private ColonyManager colonyManager;
    private Timer matureTimer;

    public override void _Ready()
    {
        colonyManager = GetNode<ColonyManager>("/root/Main/ColonyManager");
        colonyManager.AddLarva();

        matureTimer = GetNode<Timer>("MatureTimer");
        matureTimer.OneShot = true;
        matureTimer.WaitTime = GD.RandRange(MinMatureSeconds, MaxMatureSeconds);
        matureTimer.Timeout += Mature;
        matureTimer.Start();
    }

    private void Mature()
    {
        if (!colonyManager.AddAnt())
        {
            matureTimer.WaitTime = RecheckSeconds;
            matureTimer.Start();
            return;
        }

        colonyManager.RemoveLarva();

        Node2D ant = AntWorkerScene.Instantiate<Node2D>();
        ant.Position = Position;
        GetParent().AddChild(ant);

        QueueFree();
    }
}
