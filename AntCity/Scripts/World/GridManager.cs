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
        FoodStorage,
        NestChamber,
        SeedCache,
        MushroomPatch,
        BerryBush,
        Air
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
    public int SurfaceHeight { get; set; } = 12;

    // How many rows of actual topsoil sit at the surface. Everything above it is open sky, which is
    // what gives spoil mounds somewhere to grow and trees somewhere to grow into.
    [Export]
    public int GrassDepth { get; set; } = 1;

    // How many cells below the surface the starting nest is carved.
    [Export]
    public int NestDepth { get; set; } = 6;

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

    // Rare, big payoff - roughly 1 in 60 underground tiles once past the rock-free layer.
    [Export(PropertyHint.Range, "0,1,0.001")]
    public float SeedCacheChance { get; set; } = 0.016f;

    [Export]
    public int FoodPerSeedCache { get; set; } = 20;

    // Common, small payoff - the frequent little pickups.
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float MushroomPatchChance { get; set; } = 0.06f;

    [Export]
    public int FoodPerMushroomPatch { get; set; } = 2;

    // Surface alternative to trees.
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float BerryBushChance { get; set; } = 0.05f;

    [Export]
    public int FoodPerBerryBush { get; set; } = 8;

    [Export]
    public int FoodStorageCapacityBonus { get; set; } = 20;

    [Export]
    public int NestChamberCapacityBonus { get; set; } = 5;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float WaterNoiseFrequency { get; set; } = 0.1f;

    [Export(PropertyHint.Range, "-1,1,0.01")]
    public float WaterThreshold { get; set; } = 0.55f;

    // Trees are switched off for now. They block the one-tile-thick surface walkway outright, and an
    // ant can stand on a treetop, which made spoil haulers pile their loads up in the canopy. Set
    // this above zero to bring them back once they are proper multi-tile plants.
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float TreeChance { get; set; } = 0f;

    private static readonly Vector2I[] Directions =
    {
        new Vector2I(0, -1), // Up
        new Vector2I(0, 1),  // Down
        new Vector2I(-1, 0), // Left
        new Vector2I(1, 0)   // Right
    };

    // Ants walk; they do not climb. Elevation only changes on a diagonal, which makes every route
    // up or down a ramp that has to be dug as one.
    private static readonly Vector2I[] MoveDirections =
    {
        new Vector2I(-1, 0),  // Left
        new Vector2I(1, 0),   // Right
        new Vector2I(-1, -1), // Ramp up-left
        new Vector2I(1, -1),  // Ramp up-right
        new Vector2I(-1, 1),  // Ramp down-left
        new Vector2I(1, 1)    // Ramp down-right
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
        { TileType.FoodStorage, new Vector2I(3, 1) },
        { TileType.NestChamber, new Vector2I(0, 2) },
        { TileType.SeedCache, new Vector2I(1, 2) },
        { TileType.MushroomPatch, new Vector2I(2, 2) },
        { TileType.BerryBush, new Vector2I(3, 2) },
    };

    private readonly Dictionary<Vector2I, TileType> grid = new();
    private readonly HashSet<Vector2I> generatedChunks = new();

    private readonly Dictionary<Vector2I, int> foodRemaining = new();

    // Solid cells are made of GrainsPerCell small pieces that ants chip out one at a time,
    // so a wall visibly crumbles instead of flipping to open tunnel in one step.
    public const int GrainsPerCell = 4;

    private readonly Dictionary<Vector2I, int> grainsRemoved = new();


    private FastNoiseLite rockNoise;
    private FastNoiseLite foodNoise;
    private FastNoiseLite waterNoise;
    private int treeSalt;
    private int seedCacheSalt;
    private int mushroomPatchSalt;
    private int berryBushSalt;

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

    // An ant can only occupy an open cell that has solid ground directly beneath it. Open space with
    // nothing under it is a drop, not a floor - that is what forces tunnels to be dug as ramps.
    public bool IsStandable(Vector2I cell)
    {
        if (!IsInBounds(cell) || !IsWalkable(GetTile(cell)))
        {
            return false;
        }

        return !IsWalkable(GetTile(cell + new Vector2I(0, 1)));
    }

    // Fired when terrain changes at runtime, so overlays know to redraw.
    [Signal]
    public delegate void TerrainChangedEventHandler();

    // Standability limited to cells that already exist, so simply looking at the world cannot force
    // new chunks into being generated.
    public bool IsKnownStandable(Vector2I cell)
    {
        if (!IsInBounds(cell) || !grid.TryGetValue(cell, out TileType tile) || !IsWalkable(tile))
        {
            return false;
        }

        return grid.TryGetValue(cell + new Vector2I(0, 1), out TileType below) && !IsWalkable(below);
    }

    // First cell with a floor at or below this one, for an ant left standing over open air.
    public Vector2I FindFloorBelow(Vector2I cell)
    {
        const int MaxDrop = 64;

        for (int depth = 0; depth < MaxDrop; depth++)
        {
            Vector2I candidate = cell + new Vector2I(0, depth);

            if (IsStandable(candidate))
            {
                return candidate;
            }

            if (!IsInBounds(candidate) || !IsWalkable(GetTile(candidate)))
            {
                break;
            }
        }

        return cell;
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

        grainsRemoved.Remove(cell);

        SetTile(cell, TileType.Tunnel);

        if (IsFoodTileType(previous))
        {
            foodRemaining.Remove(cell);
            GD.Print($"Tunneled through a food source at {cell}, destroying it. Forage it instead to collect its food.");
        }

        EmitSignal(SignalName.CellDug, cell);
        EmitSignal(SignalName.TerrainChanged);
    }

    // Chips a single grain out of a solid cell. Returns true once there's nothing left to dig here -
    // either the cell gave up its last grain and became open tunnel, or it was never diggable.
    public bool DigGrain(Vector2I cell)
    {
        if (!CanDig(cell))
        {
            return true;
        }

        int removed = GetGrainsRemoved(cell) + 1;

        if (removed >= GrainsPerCell)
        {
            Dig(cell);
            return true;
        }

        grainsRemoved[cell] = removed;
        return false;
    }

    public int GetGrainsRemoved(Vector2I cell)
    {
        return grainsRemoved.TryGetValue(cell, out int removed) ? removed : 0;
    }

    // Cells part-way through excavation, for the crumble overlay to draw.
    public IReadOnlyDictionary<Vector2I, int> PartiallyDugCells => grainsRemoved;

    public bool IsFoodSource(Vector2I cell)
    {
        if (!IsInBounds(cell))
        {
            return false;
        }

        TileType type = GetTile(cell);
        return IsFoodTileType(type) && GetFoodAmount(cell) > 0;
    }

    public int GetFoodAmount(Vector2I cell)
    {
        return foodRemaining.TryGetValue(cell, out int amount) ? amount : 0;
    }

    // Snapshot of every cell currently tracked as a food source, for the minimap to plot.
    public List<Vector2I> GetFoodSourceCells()
    {
        return new List<Vector2I>(foodRemaining.Keys);
    }

    // Where a hauler should take her load.
    //
    // Spoil goes up and out, the way a real colony works it - never to one fixed spot. A fixed dump
    // cell is unreachable from most dig faces once ants cannot climb, and a hauler who cannot route
    // there just tips her load in the tunnel she is standing in. So instead she searches the burrow
    // she can actually walk and picks the highest point in it, preferring one a few cells clear of
    // the entrance so the heap never grows across her own way out.
    public Vector2I FindSpoilDropOff(Vector2I from)
    {
        const int MaxVisited = 4000;
        const int PreferredSpread = 6;

        Vector2I best = from;
        int bestScore = SpoilDropScore(from, PreferredSpread);

        var visited = new HashSet<Vector2I> { from };
        var frontier = new Queue<Vector2I>();
        frontier.Enqueue(from);

        while (frontier.Count > 0 && visited.Count < MaxVisited)
        {
            Vector2I current = frontier.Dequeue();

            foreach (Vector2I direction in MoveDirections)
            {
                Vector2I next = current + direction;

                if (visited.Contains(next) || !IsStandable(next))
                {
                    continue;
                }

                visited.Add(next);
                frontier.Enqueue(next);

                int score = SpoilDropScore(next, PreferredSpread);

                if (score < bestScore)
                {
                    bestScore = score;
                    best = next;
                }
            }
        }

        return best;
    }

    // Lower is better. Getting out of the burrow is what matters, so every cell at or above the
    // surface counts as equally good height-wise and the tie is broken by walking a few cells clear
    // of the nest column - otherwise a hauler would trek across the map to reach one perch that
    // happens to sit a single row higher.
    private int SpoilDropScore(Vector2I cell, int preferredSpread)
    {
        int height = Mathf.Max(cell.Y, SurfaceHeight - 1);
        int lateral = Mathf.Min(Mathf.Abs(cell.X - NestCenterCell.X), preferredSpread);

        return height * 100 - lateral;
    }


    public TileType GetTileAt(Vector2I cell)
    {
        return GetTile(cell);
    }

    // Loose grains have filled this cell right up, so it becomes ordinary solid ground again.
    public void PackCellToDirt(Vector2I cell)
    {
        if (!IsInBounds(cell))
        {
            return;
        }

        grainsRemoved.Remove(cell);
        SetTile(cell, TileType.Dirt);
        EmitSignal(SignalName.TerrainChanged);
    }

    private static bool IsFoodTileType(TileType type)
    {
        return type == TileType.FoodDeposit
            || type == TileType.Tree
            || type == TileType.SeedCache
            || type == TileType.MushroomPatch
            || type == TileType.BerryBush;
    }

    // Underground food sources revert to open tunnel once emptied; surface ones revert to grass.
    private static bool IsUndergroundFoodTileType(TileType type)
    {
        return type == TileType.FoodDeposit || type == TileType.SeedCache || type == TileType.MushroomPatch;
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
            grainsRemoved.Remove(cell);
            foodRemaining.Remove(cell);
            TileType depletedTo = IsUndergroundFoodTileType(GetTile(cell)) ? TileType.Tunnel : TileType.Grass;
            SetTile(cell, depletedTo);
            EmitSignal(SignalName.TerrainChanged);
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

    public bool BuildNestChamber(Vector2I cell)
    {
        if (!IsInBounds(cell) || GetTile(cell) != TileType.Tunnel)
        {
            return false;
        }

        SetTile(cell, TileType.NestChamber);
        ColonyManager?.IncreaseCapacity(NestChamberCapacityBonus);
        GD.Print($"Built a nest chamber at {cell}! +{NestChamberCapacityBonus} population capacity.");

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

            foreach (Vector2I direction in MoveDirections)
            {
                Vector2I next = current + direction;

                if (visited.Contains(next) || !IsStandable(next))
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

    // The next cell to carve when tunnelling from `from` toward `to`.
    //
    // Height is only ever gained or lost diagonally, so a corridor that has to descend comes out as a
    // staircase an ant can walk rather than a shaft nothing can climb. The router also avoids cutting
    // the floor out from under a cell it already opened, which would strand anything standing there.
    public Vector2I GetStepToward(Vector2I from, Vector2I to)
    {
        const float UnderminePenalty = 10000f;
        const float UnsupportedPenalty = 5000f;

        // Within reach, break straight in - unless the target is directly above or below, which would
        // undercut the cell she is standing in and leave a one-way drop. Then sidestep first so the
        // last move onto it is a diagonal she can also walk back up.
        if (Mathf.Max(Mathf.Abs(to.X - from.X), Mathf.Abs(to.Y - from.Y)) <= 1)
        {
            return to.X != from.X ? to : SidestepFor(from, to);
        }

        Vector2I best = from;
        float bestScore = float.MaxValue;

        foreach (Vector2I direction in MoveDirections)
        {
            Vector2I candidate = from + direction;

            if (!IsInBounds(candidate))
            {
                continue;
            }

            TileType tile = GetTile(candidate);

            if (!IsWalkable(tile) && !IsDiggable(tile))
            {
                continue;
            }

            int offX = candidate.X - to.X;
            int offY = candidate.Y - to.Y;
            float score = offX * offX + offY * offY;

            if (IsWalkable(GetTile(candidate + new Vector2I(0, -1))))
            {
                score += UnderminePenalty;
            }

            if (IsWalkable(GetTile(candidate + new Vector2I(0, 1))))
            {
                score += UnsupportedPenalty;
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        // Boxed in on every side by rock: fall back to a plain step so the caller still makes progress.
        if (best == from)
        {
            int stepX = Mathf.Sign(to.X - from.X);
            best = from + new Vector2I(stepX == 0 ? 1 : stepX, Mathf.Sign(to.Y - from.Y));
        }

        return best;
    }

    // A step to one side, so a target sitting directly above or below can be reached on a diagonal.
    private Vector2I SidestepFor(Vector2I from, Vector2I to)
    {
        Vector2I right = from + new Vector2I(1, 0);
        Vector2I left = from + new Vector2I(-1, 0);

        bool rightOpen = IsWalkable(GetTile(right)) || IsDiggable(GetTile(right));
        bool leftOpen = IsWalkable(GetTile(left)) || IsDiggable(GetTile(left));

        if (rightOpen && !leftOpen)
        {
            return right;
        }

        if (leftOpen && !rightOpen)
        {
            return left;
        }

        if (!rightOpen && !leftOpen)
        {
            // Solid rock either side - nothing to do but break straight in and accept the drop.
            return to;
        }

        // Both usable: pick the side that keeps a floor under the cell she is leaving.
        return IsWalkable(GetTile(right + new Vector2I(0, 1))) ? left : right;
    }

    // The closest cell an ant could actually stand in, expanding outward regardless of tile type.
    // Bounded so a click far into unexplored territory can't trigger unbounded chunk generation.
    public Vector2I FindNearestTunnelCell(Vector2I from)
    {
        const int MaxVisited = 4000;

        if (IsStandable(from))
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

                if (IsStandable(next))
                {
                    return next;
                }

                frontier.Enqueue(next);
            }
        }

        // No tunnel is reachable nearby.
        return from;
    }


    // Plans a corridor from `start` toward `goal` that is still walkable once it has been carved.
    //
    // A greedy per-step router cannot do this: it makes locally sensible moves and then saws off its
    // own approach when a switchback doubles back underneath itself. Planning the whole run up front
    // lets three rules hold along the entire route - only horizontal and diagonal moves, every cell
    // with solid ground under it, and no cell tucked directly beneath one the route already opened.
    //
    // Ends at the first cell from which `goal` is within reach, since an ant digs a cell by standing
    // next to it, not by standing in it. Null if no walkable corridor exists.
    public List<Vector2I> PlanDigRoute(Vector2I start, Vector2I goal)
    {
        const int MaxVisited = 6000;
        const int AncestorsChecked = 4;

        if (IsWithinReach(start, goal))
        {
            return new List<Vector2I> { start };
        }

        var cameFrom = new Dictionary<Vector2I, Vector2I>();
        var visited = new HashSet<Vector2I> { start };
        var frontier = new Queue<Vector2I>();
        frontier.Enqueue(start);

        while (frontier.Count > 0 && visited.Count < MaxVisited)
        {
            Vector2I current = frontier.Dequeue();

            foreach (Vector2I direction in MoveDirections)
            {
                Vector2I next = current + direction;

                if (visited.Contains(next) || !IsInBounds(next))
                {
                    continue;
                }

                TileType tile = GetTile(next);

                if (!IsWalkable(tile) && !IsDiggable(tile))
                {
                    continue;
                }

                // Must not destroy a floor that something is already standing on up there.
                if (IsStandable(next + new Vector2I(0, -1)))
                {
                    continue;
                }

                // Needs something solid to stand on once it has been carved out.
                if (IsWalkable(GetTile(next + new Vector2I(0, 1))))
                {
                    continue;
                }

                if (UnderminesRoute(cameFrom, start, current, next, AncestorsChecked))
                {
                    continue;
                }

                visited.Add(next);
                cameFrom[next] = current;

                if (IsWithinReach(next, goal))
                {
                    return BuildPath(cameFrom, start, next);
                }

                frontier.Enqueue(next);
            }
        }

        return null;
    }

    // True if carving `next` would pull the floor out from under a cell the route just opened.
    private static bool UnderminesRoute(Dictionary<Vector2I, Vector2I> cameFrom, Vector2I start, Vector2I current, Vector2I next, int depth)
    {
        Vector2I above = next + new Vector2I(0, -1);
        Vector2I node = current;

        for (int i = 0; i < depth; i++)
        {
            if (node == above)
            {
                return true;
            }

            if (node == start || !cameFrom.TryGetValue(node, out node))
            {
                break;
            }
        }

        return false;
    }

    public static bool IsWithinReach(Vector2I a, Vector2I b)
    {
        return Mathf.Max(Mathf.Abs(a.X - b.X), Mathf.Abs(a.Y - b.Y)) <= 1;
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

            foreach (Vector2I direction in MoveDirections)
            {
                Vector2I next = current + direction;

                if (visited.Contains(next) || !IsStandable(next))
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
        seedCacheSalt = actualSeed + 4;
        mushroomPatchSalt = actualSeed + 5;
        berryBushSalt = actualSeed + 6;
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

        if (y < SurfaceHeight - GrassDepth)
        {
            type = TileType.Air;
        }
        else if (y < SurfaceHeight)
        {
            // The topsoil band: grass dotted with water pools, trees and bushes.
            if (waterNoise.GetNoise2D(x, y) > WaterThreshold)
            {
                type = TileType.Water;
            }
            else if (RollChance(x, y, treeSalt) < TreeChance)
            {
                type = TileType.Tree;
            }
            else if (RollChance(x, y, berryBushSalt) < BerryBushChance)
            {
                type = TileType.BerryBush;
            }
            else
            {
                type = TileType.Grass;
            }
        }
        else
        {
            // Underground: mostly dirt, with rock pockets and food sources of varying rarity woven through it.
            int depthBelowSurface = y - SurfaceHeight;
            if (depthBelowSurface >= RockFreeDepth && rockNoise.GetNoise2D(x, y) > RockThreshold)
            {
                type = TileType.Rock;
            }
            else if (foodNoise.GetNoise2D(x, y) > FoodThreshold)
            {
                type = TileType.FoodDeposit;
            }
            else if (RollChance(x, y, seedCacheSalt) < SeedCacheChance)
            {
                type = TileType.SeedCache;
            }
            else if (RollChance(x, y, mushroomPatchSalt) < MushroomPatchChance)
            {
                type = TileType.MushroomPatch;
            }
            else
            {
                type = TileType.Dirt;
            }
        }

        switch (type)
        {
            case TileType.FoodDeposit:
                foodRemaining[cell] = FoodPerDeposit;
                break;
            case TileType.Tree:
                foodRemaining[cell] = FoodPerTree;
                break;
            case TileType.SeedCache:
                foodRemaining[cell] = FoodPerSeedCache;
                break;
            case TileType.MushroomPatch:
                foodRemaining[cell] = FoodPerMushroomPatch;
                break;
            case TileType.BerryBush:
                foodRemaining[cell] = FoodPerBerryBush;
                break;
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

        if (type == TileType.Air)
        {
            // Open sky has no artwork - erasing leaves the background showing through, which also
            // means it costs no slot in an atlas that is already full.
            Ground.EraseCell(cell);
            return;
        }

        Ground.SetCell(cell, 0, TileAtlasCoords[type]);
    }

    private bool IsDiggable(TileType type)
    {
        return type == TileType.Dirt || IsUndergroundFoodTileType(type);
    }

    private static bool IsWalkable(TileType type)
    {
        return type == TileType.Tunnel
            || type == TileType.FoodStorage
            || type == TileType.NestChamber
            || type == TileType.Grass
            || type == TileType.Air;
    }

    private void CreateStartingNest()
    {
        int floorY = NestCenterCell.Y;

        // A tight starting chamber - two rows tall, so ants walk its floor rather than swimming
        // around inside a big hollow box.
        for (int x = NestCenterCell.X - 1; x <= NestCenterCell.X + 1; x++)
        {
            for (int y = floorY - 1; y <= floorY; y++)
            {
                ForceDig(new Vector2I(x, y));
            }
        }

        CarveEntranceRamp(floorY);
        ClearSurfaceEntrance();

        GD.Print("Starting nest created!");
    }

    // A zigzag staircase from the chamber up to daylight. Each row steps one cell sideways, so every
    // move along it is a diagonal an ant can walk, and the whole thing stays two cells wide.
    private void CarveEntranceRamp(int floorY)
    {
        int rampX = NestCenterCell.X + 2;

        for (int y = floorY; y >= SurfaceHeight - 1; y--)
        {
            ForceDig(new Vector2I(rampX + ((floorY - y) % 2), y));
        }
    }

    private void ClearSurfaceEntrance()
    {
        int surfaceRow = SurfaceHeight - 1;

        if (surfaceRow < 0)
        {
            return;
        }

        // Entrance, plus the dump cell and pile column beside it, so haulers always have clear ground.
        for (int x = NestCenterCell.X - 1; x <= NestCenterCell.X + 6; x++)
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
