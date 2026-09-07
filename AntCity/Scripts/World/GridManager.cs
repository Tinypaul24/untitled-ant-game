using Godot;

public partial class GridManager : Node2D
{
    public enum TileType
    {
        Dirt,
        Tunnel
    }

    [Export]
    public int Width { get; set; } = 40;

    [Export]
    public int Height { get; set; } = 25;

    [Export]
    public int CellSize { get; set; } = 16;

    [Export]
    public TileMapLayer Ground { get; set; }

    // These are the coordinates from your tileset.
    private Vector2I dirtTile = new Vector2I(0, 1);
    private Vector2I tunnelTile = new Vector2I(0, 0);

    private TileType[,] grid;

    public override void _Ready()
    {
        grid = new TileType[Width, Height];

        // Start the entire map as dirt.
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                grid[x, y] = TileType.Dirt;

                Ground.SetCell(
                    new Vector2I(x, y),
                    0,
                    dirtTile
                );
            }
        }
        
        CreateStartingNest();

        GD.Print("Grid created!");
    }

    private void CreateStartingNest()
    {
        // Put the nest in the middle of the map.
        int centerX = Width / 2;
        int centerY = Height / 2;

        // Create a small 5x5 room.
        for (int x = centerX - 2; x <= centerX + 2; x++)
        {
            for (int y = centerY - 2; y <= centerY + 2; y++)
            {
                DigCell(new Vector2I(x, y));
            }
        }

        GD.Print("Starting nest created!");
    }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton mouseButton)
            {
                if (mouseButton.ButtonIndex == MouseButton.Left &&
                    mouseButton.Pressed)
                {
                    DigAtMousePosition(mouseButton.Position);
                }
            }
        }

        private void DigCell(Vector2I cell)
    {
        // Safety check.
        if (cell.X < 0 || cell.X >= Width ||
            cell.Y < 0 || cell.Y >= Height)
        {
            return;
        }

        // Don't dig an already dug tile.
        if (grid[cell.X, cell.Y] != TileType.Dirt)
        {
            return;
        }
        // Change logical tile.
        grid[cell.X, cell.Y] = TileType.Tunnel;

        // Change visual tile.
        Ground.SetCell(
            cell,
            0,
            tunnelTile
        );

    
    }

    private void DigAtMousePosition(Vector2 mousePosition)
    {
        // Convert the mouse position into a grid coordinate.
        Vector2I cell = new Vector2I(
            Mathf.FloorToInt(mousePosition.X / CellSize),
            Mathf.FloorToInt(mousePosition.Y / CellSize)
        );

        // Make sure we're inside the map.
        if (cell.X < 0 || cell.X >= Width ||
            cell.Y < 0 || cell.Y >= Height)
        {
            return;
        }

        // Only dig dirt.
        if (grid[cell.X, cell.Y] != TileType.Dirt)
        {
            return;
        }

        // Change the logical tile.
        grid[cell.X, cell.Y] = TileType.Tunnel;

        // Change the visual tile.
        Ground.SetCell(
            cell,
            0,
            tunnelTile
        );

        GD.Print($"Dug tunnel at {cell}");
    }
}