using Godot;

public partial class Larva : Node2D
{
    private const double MinMatureSeconds = 13.0;
    private const double MaxMatureSeconds = 17.0;

    private static readonly PackedScene AntWorkerScene = GD.Load<PackedScene>("res://AntCity/Scenes/Entities/AntWorker.tscn");

    private ColonyManager colonyManager;

    public override void _Ready()
    {
        colonyManager = GetNode<ColonyManager>("/root/Main/ColonyManager");

        Timer matureTimer = GetNode<Timer>("MatureTimer");
        matureTimer.OneShot = true;
        matureTimer.WaitTime = GD.RandRange(MinMatureSeconds, MaxMatureSeconds);
        matureTimer.Timeout += Mature;
        matureTimer.Start();
    }

    private void Mature()
    {
        colonyManager.AddAnt();

        Node2D ant = AntWorkerScene.Instantiate<Node2D>();
        ant.Position = Position;
        GetParent().AddChild(ant);

        QueueFree();
    }
}
