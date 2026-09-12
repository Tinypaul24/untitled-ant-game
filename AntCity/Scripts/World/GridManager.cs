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
        Tree,
        FoodStorage
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

    [Export]
    public int FoodPerTree { get; set; } = 3;

    [Export]
    public int FoodStorageCapacityBonus { get; set; } = 20;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float WaterNoiseFrequency { get; set; } = 0.1f;

    [Export(PropertyHint.Range, "-1,1,0.01")]
    public float WaterThreshold { get; set; } = 0.55f;

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
        { TileType.Dirt, new Vector2I(0, 1) },
        { TileType.Tunnel, new Vector2I(0, 0) },
        { TileType.Rock, new Vector2I(8, 0) },
        { TileType.FoodDeposit, new Vector2I(4, 1) },
        { TileType.Grass, new Vector2I(0, 0) },
        { TileType.Water, new Vector2I(4, 10) },
        { TileType.Tree, new Vector2I(7, 6) },
        { TileType.FoodStorage, new Vector2I(6, 5) },
    };

    private readonly Dictionary<Vector2I, TileType> grid = new();
    private readonly HashSet<Vector2I> generatedChunks = new();

    private readonly Dictionary<Vector2I, int> foodRemaining = new();

    private FastNoiseLite rockNoise;
    private FastNoiseLite foodNoise;
    private FastNoiseLite waterNoise;
    private int treeSalt;

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
        return IsInBounds(cell) && IsWalkable(GetTile(cell));
    }

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
            foodRemaining.Remove(cell);
            GD.Print($"Tunneled through a food deposit at {cell}, destroying it. Forage it instead to collect its food.");
        }
    }

    public bool IsFoodSource(Vector2I cell)
    {
        if (!IsInBounds(cell))
        {
            return false;
        }

        TileType type = GetTile(cell);
        return (type == TileType.FoodDeposit || type == TileType.Tree) && GetFoodAmount(cell) > 0;
    }

    public int GetFoodAmount(Vector2I cell)
    {
        return foodRemaining.TryGetValue(cell, out int amount) ? amount : 0;
    }

    public int Harvest(Vector2I cell, int amount)
    {
        if (!IsFoodSource(cell))
        {
            return 0;
        }

        int available = GetFoodAmount(cell);
        int harvested = Mathf.Min(amount, available);
        int remaining = available - harvested;

        if (remaining <= 0)
        {
            foodRemaining.Remove(cell);
            TileType depletedTo = GetTile(cell) == TileType.Tree ? TileType.Grass : TileType.Tunnel;
            SetTile(cell, depletedTo);
        }
        else
        {
            foodRemaining[cell] = remaining;
        }

        return harvested;
    }

    public bool BuildFoodStorage(Vector2I cell)
    {
        if (!IsInBounds(cell) || GetTile(cell) != TileType.Tunnel)
        {
            return false;
        }

        SetTile(cell, TileType.FoodStorage);
        ColonyManager?.IncreaseFoodCapacity(FoodStorageCapacityBonus);
        GD.Print($"Built a food storage room at {cell}! +{FoodStorageCapacityBonus} food capacity.");

        return true;
    }

    public Vector2I GetNearestStorageCell(Vector2I from)
    {
        const int MaxVisited = 4000;

        if (GetTile(from) == TileType.FoodStorage)
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

                if (visited.Contains(next) || !IsInBounds(next) || !IsWalkable(GetTile(next)))
                {
                    continue;
                }

                visited.Add(next);

                if (GetTile(next) == TileType.FoodStorage)
                {
                    return next;
                }

                frontier.Enqueue(next);
            }
        }

        return NestCenterCell;
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

        if (IsWalkable(GetTile(from)))
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

                if (IsWalkable(GetTile(next)))
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

                if (visited.Contains(next) || !IsInBounds(next) || !IsWalkable(GetTile(next)))
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
        treeSalt = actualSeed + 3;
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
            else if (RollChance(x, y, treeSalt) < TreeChance)
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

        if (type == TileType.FoodDeposit)
        {
            foodRemaining[cell] = FoodPerDeposit;
        }
        else if (type == TileType.Tree)
        {
            foodRemaining[cell] = FoodPerTree;
        }

        SetTile(cell, type);
    }

    private static float RollChance(int x, int y, int salt)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + salt * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h % 1_000_000u) / 1_000_000f;
        }
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

    private static bool IsWalkable(TileType type)
    {
        return type == TileType.Tunnel || type == TileType.FoodStorage || type == TileType.Grass;
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

        ClearSurfaceEntrance();

        GD.Print("Starting nest created!");
    }

    private void ClearSurfaceEntrance()
    {
        int surfaceRow = SurfaceHeight - 1;

        if (surfaceRow < 0)
        {
            return;
        }

        for (int x = NestCenterCell.X - 1; x <= NestCenterCell.X + 1; x++)
        {
            Vector2I cell = new Vector2I(x, surfaceRow);
            foodRemaining.Remove(cell);
            SetTile(cell, TileType.Grass);
        }
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
