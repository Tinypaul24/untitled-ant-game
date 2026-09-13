using Godot;

public partial class Main : Node2D
{
    private const int StartingAntWorkers = 3;
    private const float StartingAntScatterRadius = 16f;

    private static readonly PackedScene AntWorkerScene = GD.Load<PackedScene>("res://AntCity/Scenes/Entities/AntWorker.tscn");

    public override void _Ready()
    {
        GD.Print("Ant City has started!");

        GridManager gridManager = GetNode<GridManager>("GridManager");
        Camera2D camera = GetNode<Camera2D>("Camera2D");
        Node2D queen = GetNode<Node2D>("Queen");
        ColonyManager colonyManager = GetNode<ColonyManager>("ColonyManager");

        Vector2 nestWorldPosition = gridManager.CellToWorld(gridManager.NestCenterCell);
        camera.Position = nestWorldPosition;
        camera.MakeCurrent();
        queen.Position = nestWorldPosition;

        for (int i = 0; i < StartingAntWorkers; i++)
        {
            if (!colonyManager.AddAnt())
            {
                break;
            }

            Vector2 offset = new Vector2(
                (GD.Randf() * 2f - 1f) * StartingAntScatterRadius,
                (GD.Randf() * 2f - 1f) * StartingAntScatterRadius
            );

            Node2D ant = AntWorkerScene.Instantiate<Node2D>();
            ant.Position = nestWorldPosition + offset;
            AddChild(ant);
        }
    }
}