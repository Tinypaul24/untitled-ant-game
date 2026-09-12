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

    private const int ChunkSize = 16;

    [ExportGroup("Grid")]
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

    // How many cells below the surface the starting nest is carved.
    [Export]
    public int NestDepth { get; set; } = 10;

    // How many chunks around the nest are generated immediately, so there's ground to see at the start.
    [Export]
    public int InitialRadiusChunks { get; set; } = 4;

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

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float TreeNoiseFrequency { get; set; } = 0.4f;

    // Roughly the fraction of surface tiles (that aren't water) that end up as trees.
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
        { TileType.Dirt, new Vector2I(0, 0) },
        { TileType.Tunnel, new Vector2I(1, 0) },
        { TileType.Rock, new Vector2I(2, 0) },
        { TileType.FoodDeposit, new Vector2I(3, 0) },
        { TileType.Grass, new Vector2I(0, 1) },
        { TileType.Water, new Vector2I(1, 1) },
        { TileType.Tree, new Vector2I(2, 1) },
    };

    private readonly Dictionary<Vector2I, TileType> grid = new();
    private readonly HashSet<Vector2I> generatedChunks = new();

    private FastNoiseLite rockNoise;
    private FastNoiseLite foodNoise;
    private FastNoiseLite waterNoise;
    private FastNoiseLite treeNoise;

    // World cell the starting nest is centered on. Useful for e.g. pointing the camera at the colony.
    public Vector2I NestCenterCell { get; private set; }

    public override void _Ready()
    {
        InitializeNoise();

        NestCenterCell = new Vector2I(0, SurfaceHeight + NestDepth);

        // Pre-generate enough terrain around the nest to fill the initial view; everything further
        // out is generated on demand as ants path or dig toward it, so the map keeps expanding.
        Vector2I nestChunk = CellToChunk(NestCenterCell);
        for (int cx = -InitialRadiusChunks; cx <= InitialRadiusChunks; cx++)
        {
            for (int cy = -InitialRadiusChunks; cy <= InitialRadiusChunks; cy++)
            {
                EnsureChunkGenerated(nestChunk + new Vector2I(cx, cy));
            }
        }

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

    // The world has no floor or side walls; the only edge is the surface at y = 0.
    public bool IsInBounds(Vector2I cell)
    {
        return cell.Y >= 0;
    }

    // True for cells an ant is allowed to dig through (dirt and food deposits; rock is a hard obstacle).
    public bool CanDig(Vector2I cell)
    {
        return IsInBounds(cell) && IsDiggable(GetTile(cell));
    }

    // True for an already-dug cell an ant can be sent to walk to without digging.
    public bool IsTunnel(Vector2I cell)
    {
        return IsInBounds(cell) && GetTile(cell) == TileType.Tunnel;
    }

    // Fired whenever a cell actually transitions to Tunnel, regardless of what caused the dig.
    [Signal]
    public delegate void CellDugEventHandler(Vector2I cell);

    public void Dig(Vector2I cell)
    {
        if (!IsInBounds(cell))
        {
            return;
        }

        TileType previous = GetTile(cell);

        if (!IsDiggable(previous))
        {
            return;
        }

        SetTile(cell, TileType.Tunnel);

        if (previous == TileType.FoodDeposit)
        {
            ColonyManager?.AddFood(FoodPerDeposit);
            GD.Print($"Harvested a food deposit at {cell}! +{FoodPerDeposit} food.");
        }

        EmitSignal(SignalName.CellDug, cell);
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
    // Bounded so a click far into unexplored territory can't trigger unbounded chunk generation.
    public Vector2I FindNearestTunnelCell(Vector2I from)
    {
        const int MaxVisited = 4000;

        if (GetTile(from) == TileType.Tunnel)
        {
            return from;
        }

        var visited = new HashSet<Vector2I> { from };
        var frontier = new Queue<Vector2I>();
        frontier.Enqueue(from);

        while (frontier.Count > 0 && visited.Count < MaxVisited)
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

                if (GetTile(next) == TileType.Tunnel)
                {
                    return next;
                }

                frontier.Enqueue(next);
            }
        }

        // No tunnel is reachable nearby.
        return from;
    }

    // Shortest walkable route from `start` to `goal` through already-dug tunnel cells, or null if unreachable.
    public List<Vector2I> FindTunnelPath(Vector2I start, Vector2I goal)
    {
        if (start == goal)
        {
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

                if (visited.Contains(next) || !IsInBounds(next) || GetTile(next) != TileType.Tunnel)
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

    private void InitializeNoise()
    {
        // Reseed Godot's global RNG from the current time, otherwise GD.Randi() below can repeat
        // the same sequence across separate runs and the "random" world ends up identical every time.
        GD.Randomize();

        // 0 means "no seed was set" -> roll a random one so every run gets a different world.
        int actualSeed = Seed != 0 ? Seed : (int)GD.Randi();
        GD.Print($"World seed: {actualSeed}");

        rockNoise = new FastNoiseLite { Seed = actualSeed, Frequency = RockNoiseFrequency };
        foodNoise = new FastNoiseLite { Seed = actualSeed + 1, Frequency = FoodNoiseFrequency };
        waterNoise = new FastNoiseLite { Seed = actualSeed + 2, Frequency = WaterNoiseFrequency };
        treeNoise = new FastNoiseLite { Seed = actualSeed + 3, Frequency = TreeNoiseFrequency };
    }

    // Generates and caches the chunk containing `cell` if it hasn't been generated yet.
    private void EnsureGenerated(Vector2I cell)
    {
        if (cell.Y < 0)
        {
            return;
        }

        EnsureChunkGenerated(CellToChunk(cell));
    }

    private void EnsureChunkGenerated(Vector2I chunkCoord)
    {
        // Nothing above the surface strip is part of the world.
        if (chunkCoord.Y < 0 || !generatedChunks.Add(chunkCoord))
        {
            return;
        }

        int startX = chunkCoord.X * ChunkSize;
        int startY = chunkCoord.Y * ChunkSize;

        for (int x = startX; x < startX + ChunkSize; x++)
        {
            for (int y = Mathf.Max(startY, 0); y < startY + ChunkSize; y++)
            {
                GenerateCell(new Vector2I(x, y));
            }
        }
    }

    private static Vector2I CellToChunk(Vector2I cell)
    {
        return new Vector2I(
            Mathf.FloorToInt((float)cell.X / ChunkSize),
            Mathf.FloorToInt((float)cell.Y / ChunkSize)
        );
    }

    private void GenerateCell(Vector2I cell)
    {
        int x = cell.X;
        int y = cell.Y;
        TileType type;

        if (y < SurfaceHeight)
        {
            // Above ground: open grass dotted with water pools and trees.
            if (waterNoise.GetNoise2D(x, y) > WaterThreshold)
            {
                type = TileType.Water;
            }
            else if (NormalizedNoise(treeNoise, x, y) < TreeChance)
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

        SetTile(cell, type);
    }

    // Maps a noise value from roughly [-1, 1] to [0, 1].
    private static float NormalizedNoise(FastNoiseLite noise, int x, int y)
    {
        return (noise.GetNoise2D(x, y) + 1f) / 2f;
    }

    private TileType GetTile(Vector2I cell)
    {
        if (cell.Y < 0)
        {
            // Above the surface strip isn't part of the world; treat it as solid so nothing paths through it.
            return TileType.Rock;
        }

        EnsureGenerated(cell);
        return grid[cell];
    }

    private void SetTile(Vector2I cell, TileType type)
    {
        grid[cell] = type;
        Ground.SetCell(cell, 0, TileAtlasCoords[type]);
    }

    private bool IsDiggable(TileType type)
    {
        return type == TileType.Dirt || type == TileType.FoodDeposit;
    }

    private void CreateStartingNest()
    {
        // Carve a small 5x5 room, clearing straight through any rock or deposits generation placed there.
        for (int x = NestCenterCell.X - 2; x <= NestCenterCell.X + 2; x++)
        {
            for (int y = NestCenterCell.Y - 2; y <= NestCenterCell.Y + 2; y++)
            {
                ForceDig(new Vector2I(x, y));
            }
        }

        // Carve an entrance shaft connecting the nest up to the surface.
        for (int y = SurfaceHeight; y < NestCenterCell.Y - 2; y++)
        {
            ForceDig(new Vector2I(NestCenterCell.X, y));
        }

        GD.Print("Starting nest created!");
    }

    // Used by world generation to guarantee the nest and its entrance shaft are always clear.
    private void ForceDig(Vector2I cell)
    {
        if (!IsInBounds(cell))
        {
            return;
        }

        SetTile(cell, TileType.Tunnel);
    }
}
