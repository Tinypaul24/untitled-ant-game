using Godot;

public partial class Main : Node2D
{
    public override void _Ready()
    {
        GD.Print("Ant City has started!");

        AddChild(new SaveManager { Name = "SaveManager" });
        AddChild(new PauseMenu { Name = "PauseMenu" });

        GridManager gridManager = GetNode<GridManager>("GridManager");
        Camera2D camera = GetNode<Camera2D>("Camera2D");

        // Pointed at the landing site before anything happens, so the queen flies in from off the
        // edge of the view rather than appearing in the middle of it.
        camera.Position = gridManager.CellToWorld(gridManager.NestCenterCell);
        camera.MakeCurrent();

        // The queen, the first shaft and the starting workers are all its business now. Spawning
        // them here meant the game opened on a chamber nobody dug and three ants nobody sent.
        AddChild(new ColonyFounding { Name = "ColonyFounding" });
    }
}
