using Godot;

public partial class Main : Node2D
{
    public override void _Ready()
    {
        GD.Print("Ant City has started!");

        GridManager gridManager = GetNode<GridManager>("GridManager");
        Camera2D camera = GetNode<Camera2D>("Camera2D");
        Node2D queen = GetNode<Node2D>("Queen");

        Vector2 nestWorldPosition = gridManager.CellToWorld(gridManager.NestCenterCell);
        camera.Position = nestWorldPosition;
        camera.MakeCurrent();
        queen.Position = nestWorldPosition;
    }
}