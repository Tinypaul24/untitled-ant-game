using System.Collections.Generic;
using Godot;
using System.Collections.Generic;

public partial class GridManager : Node2D
{
    public enum TileType
    {
        Dirt,
        Tunnel,
        Rock,
        FoodDeposit,
        Grass,
        Water,
        Tree
    }

    [ExportGroup("Grid")]
    [Export]
    public int Width { get; set; } = 40;

    [Export]
    public int Height { get; set; } = 25;

    [Export]
    public int CellSize { get; set; } = 16;

    [Export]
    public TileMapLayer Ground { get; set; }

    [Export]
    public ColonyManager ColonyManager { get; set; }

    [ExportGroup("World Generation")]
    // 0 picks a random seed every run. Any other value always reproduces the same world.
    [Export]
    public int Seed { get; set; } = 0;

    [Export]
    public int SurfaceHeight { get; set; } = 4;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float RockNoiseFrequency { get; set; } = 0.15f;

    [Export(PropertyHint.Range, "-1,1,0.01")]
    public float RockThreshold { get; set; } = 0.35f;

    [Export]
    public int RockFreeDepth { get; set; } = 2;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float FoodNoiseFrequency { get; set; } = 0.3f;

    [Export(PropertyHint.Range, "-1,1,0.01")]
    public float FoodThreshold { get; set; } = 0.62f;

    [Export]
    public int FoodPerDeposit { get; set; } = 5;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float WaterNoiseFrequency { get; set; } = 0.1f;
    
    [Export(PropertyHint.Range, "-1,1,0.01")]
    public float WaterThreshold { get; set; } = 0.55f;

    // Chance for a non-water surface cell to spawn a tree.
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float TreeChance { get; set; } = 0.12f;
    private static readonly Vector2I[] Directions =
    {
        new Vector2I(0, -1), // Up
        new Vector2I(0, 1),  // Down
        new Vector2I(-1, 0), // Left
        new Vector2I(1, 0)   // Right
    };

    // These are the coordinates from your tileset.
    private static readonly Dictionary<TileType, Vector2I> TileAtlasCoords = new()
    {
        { TileType.Dirt, new Vector2I(0, 1) },
        { TileType.Tunnel, new Vector2I(0, 0) },
        { TileType.Rock, new Vector2I(8, 0) },
        { TileType.FoodDeposit, new Vector2I(4, 1) },
        { TileType.Grass, new Vector2I(0, 0) },
        { TileType.Water, new Vector2I(4, 10) },
        { TileType.Tree, new Vector2I(1, 4) },
    };

    private TileType[,] grid;

    public override void _Ready()
    {
        grid = new TileType[Width, Height];

        GenerateTerrain();
        CreateStartingNest();

        GD.Print("Grid created!");
    }

    private void GenerateTerrain()
    {
        // 0 means "no seed was set" -> roll a random one so every run gets a different world.
        int actualSeed = Seed != 0 ? Seed : (int)GD.Randi();
        GD.Print($"World seed: {actualSeed}");

        var rockNoise = new FastNoiseLite { Seed = actualSeed, Frequency = RockNoiseFrequency };
        var foodNoise = new FastNoiseLite { Seed = actualSeed + 1, Frequency = FoodNoiseFrequency };
        var waterNoise = new FastNoiseLite { Seed = actualSeed + 2, Frequency = WaterNoiseFrequency };

        // A seeded RNG (instead of GD.Randf) keeps tree placement reproducible for a given seed.
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)actualSeed;

        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                TileType type;

                if (y < SurfaceHeight)
                {
                    // Above ground: open grass dotted with water pools and trees.
                    if (waterNoise.GetNoise2D(x, y) > WaterThreshold)
                    {
                        type = TileType.Water;
                    }
                    else if (rng.Randf() < TreeChance)
                    {
                        type = TileType.Tree;
                    }
                    else
                    {
                        type = TileType.Grass;
                    }
                }
                else
                {
                    // Underground: mostly dirt, with rock pockets and food deposits woven through it.
                    int depthBelowSurface = y - SurfaceHeight;
                    if (depthBelowSurface >= RockFreeDepth && rockNoise.GetNoise2D(x, y) > RockThreshold)
                    {
                        type = TileType.Rock;
                    }
                    else if (foodNoise.GetNoise2D(x, y) > FoodThreshold)
                    {
                        type = TileType.FoodDeposit;
                    }
                    else
                    {
                        type = TileType.Dirt;
                    }
                }

                SetTile(new Vector2I(x, y), type);
            }
        }
    }

    private void SetTile(Vector2I cell, TileType type)
    {
        grid[cell.X, cell.Y] = type;
        Ground.SetCell(cell, 0, TileAtlasCoords[type]);
    }

    private bool IsDiggable(TileType type)
    {
        return type == TileType.Dirt || type == TileType.FoodDeposit;
    }

    private bool HasAdjacentTunnel(Vector2I cell)
    {
        // Check the four cardinal directions.
        Vector2I[] directions =
        {
            new Vector2I(0, -1), // Up
            new Vector2I(0, 1),  // Down
            new Vector2I(-1, 0), // Left
            new Vector2I(1, 0)   // Right
        };

        foreach (Vector2I direction in directions)
        {
            Vector2I neighbour = cell + direction;

            // Make sure neighbour is inside the map.
            if (neighbour.X < 0 || neighbour.X >= Width ||
                neighbour.Y < 0 || neighbour.Y >= Height)
            {
                continue;
            }

            // Is the neighbouring cell a tunnel?
            if (grid[neighbour.X, neighbour.Y] == TileType.Tunnel)
            {
                return true;
            }
        }

        return false;
    }

    private void CreateStartingNest()
    {
        // Put the nest in the middle of the underground area.
        int centerX = Width / 2;
        int centerY = SurfaceHeight + (Height - SurfaceHeight) / 2;

        // Carve a small 5x5 room, clearing straight through any rock or deposits generation placed there.
        for (int x = centerX - 2; x <= centerX + 2; x++)
        {
            for (int y = centerY - 2; y <= centerY + 2; y++)
            {
                ForceDig(new Vector2I(x, y));
            }
        }

        // Carve an entrance shaft connecting the nest up to the surface.
        for (int y = SurfaceHeight; y < centerY - 2; y++)
        {
            ForceDig(new Vector2I(centerX, y));
        }

        GD.Print("Starting nest created!");
    }

    // Used by world generation to guarantee the nest and its entrance shaft are always clear.
    private void ForceDig(Vector2I cell)
    {
        if (cell.X < 0 || cell.X >= Width || cell.Y < 0 || cell.Y >= Height)
        {
            return;
        }

        SetTile(cell, TileType.Tunnel);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left &&
                mouseButton.Pressed)
            {
                DigAtMousePosition(mouseButton.Position);

        CreateStartingNest();

        GD.Print("Grid created!");
    }

    public Vector2I WorldToCell(Vector2 worldPosition)
    {
        return new Vector2I(
            Mathf.FloorToInt(worldPosition.X / CellSize),
            Mathf.FloorToInt(worldPosition.Y / CellSize)
        );
    }

    public Vector2 CellToWorld(Vector2I cell)
    {
        return new Vector2(
            cell.X * CellSize + CellSize / 2f,
            cell.Y * CellSize + CellSize / 2f
        );
    }

    public bool IsInBounds(Vector2I cell)
    {
        return cell.X >= 0 && cell.X < Width && cell.Y >= 0 && cell.Y < Height;
    }

    public bool IsDirt(Vector2I cell)
    {
        return IsInBounds(cell) && grid[cell.X, cell.Y] == TileType.Dirt;
    }

    public void Dig(Vector2I cell)
    {
        if (!IsInBounds(cell) || grid[cell.X, cell.Y] != TileType.Dirt)
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

    // The next cell to step into when walking a straight line from `from` toward `to`.
    public Vector2I GetStepToward(Vector2I from, Vector2I to)
    {
        int dx = to.X - from.X;
        int dy = to.Y - from.Y;

        if (Mathf.Abs(dx) >= Mathf.Abs(dy))
        {
            return from + new Vector2I(Mathf.Sign(dx), 0);
        }

        return from + new Vector2I(0, Mathf.Sign(dy));
    }

    // The closest already-dug tunnel cell to `from`, reached by expanding outward regardless of tile type.
    public Vector2I FindNearestTunnelCell(Vector2I from)
    {
        if (IsInBounds(from) && grid[from.X, from.Y] == TileType.Tunnel)
        {
            return from;
        }

        var visited = new HashSet<Vector2I> { from };
        var frontier = new Queue<Vector2I>();
        frontier.Enqueue(from);

        while (frontier.Count > 0)
        {
            Vector2I current = frontier.Dequeue();

            foreach (Vector2I direction in Directions)
            {
                Vector2I next = current + direction;

                if (visited.Contains(next) || !IsInBounds(next))
                {
                    continue;
                }

                visited.Add(next);

                if (grid[next.X, next.Y] == TileType.Tunnel)
                {
                    return next;
                }

                frontier.Enqueue(next);
            }
        }
    }

    // Player-facing dig: only dirt and food deposits can be dug, and only next to an existing tunnel.
    // Rock is a hard obstacle. Returns what was dug through so callers can react (e.g. food reward).
    private bool TryDigCell(Vector2I cell, bool requireAdjacentTunnel, out TileType previousType)
    {
        previousType = TileType.Rock;
        // No tunnel exists anywhere yet.
        return from;
    }

    // Shortest walkable route from `start` to `goal` through already-dug tunnel cells, or null if unreachable.
    public List<Vector2I> FindTunnelPath(Vector2I start, Vector2I goal)
    {
        if (start == goal)
        {
            return false;
        }

        TileType current = grid[cell.X, cell.Y];
        if (!IsDiggable(current))
        {
            return false;
        }

        if (requireAdjacentTunnel && !HasAdjacentTunnel(cell))
        {
            return false;
        }

        previousType = current;
        SetTile(cell, TileType.Tunnel);

        if (current == TileType.FoodDeposit)
        {
            ColonyManager?.AddFood(FoodPerDeposit);
        }

        return true;
            return new List<Vector2I> { start };
        }

        var cameFrom = new Dictionary<Vector2I, Vector2I>();
        var visited = new HashSet<Vector2I> { start };
        var frontier = new Queue<Vector2I>();
        frontier.Enqueue(start);

        while (frontier.Count > 0)
        {
            Vector2I current = frontier.Dequeue();

            foreach (Vector2I direction in Directions)
            {
                Vector2I next = current + direction;

                if (visited.Contains(next) || !IsInBounds(next) || grid[next.X, next.Y] != TileType.Tunnel)
                {
                    continue;
                }

                visited.Add(next);
                cameFrom[next] = current;

                if (next == goal)
                {
                    return BuildPath(cameFrom, start, goal);
                }

                frontier.Enqueue(next);
            }
        }

        return null;
    }

    private static List<Vector2I> BuildPath(Dictionary<Vector2I, Vector2I> cameFrom, Vector2I start, Vector2I goal)
    {
        // Convert the mouse position into a grid coordinate.
        Vector2I cell = new Vector2I(
            Mathf.FloorToInt(mousePosition.X / CellSize),
            Mathf.FloorToInt(mousePosition.Y / CellSize)
        );

        if (TryDigCell(cell, requireAdjacentTunnel: true, out TileType previous))
        {
            if (previous == TileType.FoodDeposit)
            {
                GD.Print($"Harvested a food deposit at {cell}! +{FoodPerDeposit} food.");
            }
            else
            {
                GD.Print($"Dug tunnel at {cell}");
        List<Vector2I> path = new List<Vector2I> { goal };
        Vector2I current = goal;

        while (current != start)
        {
            current = cameFrom[current];
            path.Add(current);
        }

        path.Reverse();
        return path;
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
                Dig(new Vector2I(x, y));
            }
        }

        GD.Print("Starting nest created!");
    }
}
