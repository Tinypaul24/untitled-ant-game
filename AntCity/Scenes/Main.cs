using Godot;

public partial class Main : Node2D
{
    private const int StartingAntWorkers = 3;

    private static readonly PackedScene AntWorkerScene = GD.Load<PackedScene>("res://AntCity/Scenes/Entities/AntWorker.tscn");

    public override void _Ready()
    {
        GD.Print("Ant City has started!");

        AddChild(new SaveManager { Name = "SaveManager" });
        AddChild(new PauseMenu { Name = "PauseMenu" });

        GridManager gridManager = GetNode<GridManager>("GridManager");
        Camera2D camera = GetNode<Camera2D>("Camera2D");
        Node2D queen = GetNode<Node2D>("Queen");
        ColonyManager colonyManager = GetNode<ColonyManager>("ColonyManager");

        Vector2 nestWorldPosition = gridManager.CellToWorld(gridManager.NestCenterCell);
        camera.Position = nestWorldPosition;
        camera.MakeCurrent();
        queen.Position = nestWorldPosition;

        // Spread the starting workers along the chamber floor. Ants cannot climb, so they have to
        // begin somewhere they can actually stand.
        for (int i = 0; i < StartingAntWorkers; i++)
        {
            if (!colonyManager.AddAnt())
            {
                break;
            }

            Vector2I spawnCell = gridManager.FindNearestTunnelCell(gridManager.NestCenterCell + new Vector2I(i - 1, 0));

            Node2D ant = AntWorkerScene.Instantiate<Node2D>();
            ant.Position = gridManager.CellToWorld(spawnCell);
            AddChild(ant);
        }
    }
}
