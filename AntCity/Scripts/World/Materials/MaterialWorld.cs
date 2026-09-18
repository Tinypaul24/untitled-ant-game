using Godot;
using System.Collections.Generic;

// One chunk of material cells, plus the dirty rectangle that decides whether it gets simulated.
//
// A chunk only pays for itself while something inside it is moving. When a tick passes with nothing
// changed the rectangle comes back empty and the chunk sleeps until a write wakes it again.
public sealed class MaterialChunk
{
    public const int Size = 64;

    // Size as a power of two, so cell-to-chunk and cell-to-index maths can shift and mask instead of
    // dividing. GetCell is the hottest function in the project - the simulation calls it dozens of
    // times per cell per tick - and it was spending that time on float division.
    public const int SizeShift = 6;

    public readonly byte[] Cells = new byte[Size * Size];

    // Its own address, so the world can keep a set of awake chunks without having to search the
    // dictionary for the key a chunk belongs to.
    public readonly Vector2I Coord;

    public MaterialChunk(Vector2I coord)
    {
        Coord = coord;
    }

    // Cells that need looking at next tick, padded by one so a settled neighbour gets re-examined
    // when the thing beside it moves away.
    public int DirtyMinX { get; private set; } = Size;
    public int DirtyMinY { get; private set; } = Size;
    public int DirtyMaxX { get; private set; } = -1;
    public int DirtyMaxY { get; private set; } = -1;

    public bool Awake => DirtyMaxX >= DirtyMinX && DirtyMaxY >= DirtyMinY;

    // Set whenever a cell changes, so the renderer only re-uploads a chunk whose picture moved.
    public bool TextureDirty { get; set; } = true;

    // Returns whether this is what woke the chunk, so the world only has to touch its awake set on
    // the transition rather than on every one of the thousands of writes into an already-awake chunk.
    public bool MarkDirty(int localX, int localY)
    {
        bool wasAsleep = !Awake;

        DirtyMinX = Mathf.Max(0, Mathf.Min(DirtyMinX, localX - 1));
        DirtyMinY = Mathf.Max(0, Mathf.Min(DirtyMinY, localY - 1));
        DirtyMaxX = Mathf.Min(Size - 1, Mathf.Max(DirtyMaxX, localX + 1));
        DirtyMaxY = Mathf.Min(Size - 1, Mathf.Max(DirtyMaxY, localY + 1));

        return wasAsleep;
    }

    public void MarkAllDirty()
    {
        DirtyMinX = 0;
        DirtyMinY = 0;
        DirtyMaxX = Size - 1;
        DirtyMaxY = Size - 1;
    }

    // Called at the start of a tick: the simulation takes the current rectangle and the chunk starts
    // accumulating a fresh one from whatever actually moves.
    public void TakeDirtyRect(out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = DirtyMinX;
        minY = DirtyMinY;
        maxX = DirtyMaxX;
        maxY = DirtyMaxY;

        DirtyMinX = Size;
        DirtyMinY = Size;
        DirtyMaxX = -1;
        DirtyMaxY = -1;
    }
}

// The material grid: the source of truth for what matter sits where.
//
// Chunks are created only when something disturbs them - dug, spawned into, or flowed into - and are
// filled from the existing tile grid at that moment. Undisturbed terrain has no cells at all, which
// is what keeps an untouched world free to own.
public partial class MaterialWorld : Node2D
{
    // 2px cells: a 16px gameplay tile is 8x8 = 64 cells. Two of the game's own pixels per cell, which
    // at the 640x360 base resolution is the finest the simulation can be and still be something you
    // can see rather than something you have to be told about.
    //
    // A 1px version existed briefly and was taken back out: sixteen times the cells of the 4px
    // original, for a look that only showed up under per-cell painting nobody kept.
    //
    // Anything measured in cells has to be derived from these rather than written as a literal, or
    // it silently means something different at a different resolution. CellsPerStep below is the
    // pattern: distances are expressed in world pixels and converted.
    public const int CellSize = 2;
    public const int CellsPerTileAxis = 8;
    public const int CellsPerTile = CellsPerTileAxis * CellsPerTileAxis;

    // CellsPerTileAxis as a power of two, for the same reason MaterialChunk.SizeShift exists.
    private const int CellsPerTileShift = 3;

    // How many cells make up one world pixel step of movement, so falling and liquid spread keep the
    // same on-screen speed whatever the cell size is. Without it, halving the cell size would halve
    // the pace of everything that moves.
    public const int CellsPerStep = 4 / CellSize;

    [Export]
    public GridManager Grid { get; set; }

    private readonly Dictionary<Vector2I, MaterialChunk> chunks = new();

    // Per-cell countdown for materials with a Lifetime. Sparse on purpose - only the handful of
    // burning or transient cells ever appear here.
    private readonly Dictionary<Vector2I, int> lifetimes = new();

    private readonly List<Vector2I> awakeScratch = new();

    // Which chunks have something to simulate. Maintained on every wake and sleep rather than
    // recomputed, so the per-frame cost tracks how much is moving, not how much world exists.
    private readonly HashSet<Vector2I> awakeChunks = new();

    // Soil sits at roughly 1.0 toughness, so the blast falloff is expressed in units of "as hard
    // to shift as dirt".
    private const float ReferenceBlastDensity = 1400f;

    private readonly RandomNumberGenerator blastRandom = new();

    // Fixed timestep. A sand sim tied to frame rate looks different on different machines, and a
    // long frame must never let material tunnel through a floor by taking several steps at once.
    private const double TickSeconds = 1.0 / 30.0;
    private const int MaxStepsPerFrame = 3;

    [Export]
    public bool SimulationEnabled { get; set; } = true;

    private MaterialSimulation simulation;
    private double tickAccumulator;

    public MaterialSimulation Simulation => simulation;

    public override void _Ready()
    {
        // Three systems declare the tile size independently and none of them agree by construction:
        // GridManager.CellSize is an [Export], the TileSet in Main.tscn leans on Godot's default
        // 16x16 region, and CellSize * CellsPerTileAxis is this one. CellToTile shifts a material
        // cell straight into a tile index, so if they ever drift the simulation silently addresses
        // the wrong tiles - terrain derivation, digging and hazards all land one place over.
        if (Grid != null && Grid.CellSize != CellSize * CellsPerTileAxis)
        {
            GD.PushError(
                $"Tile size mismatch: GridManager.CellSize is {Grid.CellSize} but the material grid " +
                $"covers {CellSize * CellsPerTileAxis}px per tile. These must be equal.");
        }

        simulation = new MaterialSimulation(this);
        blastRandom.Randomize();

        Grid.CellDug += OnTileDug;
        Grid.TileChipped += OnTileChipped;

        // Navigation asks the materials what is lethal. Wired as a callback rather than a direct
        // reference so the dependency only points one way: the simulation knows about the tile grid,
        // and the tile grid stays usable on its own.
        Grid.SetHazardTest(IsTileHarmful);
    }

    public override void _Process(double delta)
    {
        if (!SimulationEnabled)
        {
            return;
        }

        tickAccumulator += delta;

        for (int step = 0; step < MaxStepsPerFrame && tickAccumulator >= TickSeconds; step++)
        {
            tickAccumulator -= TickSeconds;
            simulation.Step();
        }

        // Once per frame rather than once per step: a tile flipping solid twice inside one frame is
        // work nobody sees, and pathfinding only reads the grid between frames anyway.
        DeriveDirtyTiles();

        CementWalls(delta);
    }

    // Ants cementing their tunnel walls with saliva, the way real ones do.
    //
    // A sweep rather than a reaction, for a specific reason: a reaction fires from a cell the tick
    // visits, and a wall that is merely sitting there is asleep. Making it stay awake to wait for a
    // one-in-a-thousand roll would keep the entire nest ticking forever. This walks a handful of
    // candidate cells per pass instead, so the cost is a fixed trickle no matter how big the colony
    // gets, and the walls harden at a pace you notice over minutes rather than seconds.
    // Driven from _Process in play; the tests call it directly so they can run the clock forward
    // without waiting on real frames.
    public void CementWallsForTest(double delta) => CementWalls(delta);

    // Where the colony is currently plastering.
    //
    // This used to be a fixed square around the nest, which is fine for a burrow and useless for a
    // chamber somebody dug forty tiles out. Sites are registered instead: the nest permanently, and
    // every room's perimeter once it has been excavated.
    private readonly List<Rect2I> cementSites = new();
    private int nextSite;

    public void AddCementSite(Rect2I site)
    {
        if (!cementSites.Contains(site))
        {
            cementSites.Add(site);
        }
    }

    public void RemoveCementSite(Rect2I site)
    {
        cementSites.Remove(site);
    }

    // Tiles this site will not plaster. A room's interior is the room; only its ring is the wall,
    // and without this most of a small site's attempts would land inside the cavity and be wasted.
    private readonly List<Rect2I> cementHoles = new();

    public void AddCementSite(Rect2I site, Rect2I hole)
    {
        AddCementSite(site);

        if (!cementHoles.Contains(hole))
        {
            cementHoles.Add(hole);
        }
    }

    public void RemoveCementSite(Rect2I site, Rect2I hole)
    {
        RemoveCementSite(site);
        cementHoles.Remove(hole);
    }

    // Ants cementing their tunnel walls with saliva, the way real ones do.
    //
    // A sweep rather than a reaction, for a specific reason: a reaction fires from a cell the tick
    // visits, and a wall that is merely sitting there is asleep. This walks a handful of candidate
    // cells per pass instead, so the cost is a fixed trickle no matter how big the colony gets.
    //
    // Sites are taken in turn against that same fixed budget rather than each getting its own, so
    // twenty chambers cost exactly what one does. A colony that plastered proportionally to its own
    // size would get slower the more it built, which is the wrong way round.
    private void CementWalls(double delta)
    {
        if (Grid == null)
        {
            return;
        }

        EnsureNestSite();

        hardenTimer += delta;

        if (hardenTimer < HardenIntervalSeconds)
        {
            return;
        }

        hardenTimer = 0;

        for (int attempt = 0; attempt < HardenAttemptsPerPass; attempt++)
        {
            Rect2I site = cementSites[nextSite % cementSites.Count];
            nextSite++;

            Vector2I tile = new Vector2I(
                site.Position.X + blastRandom.RandiRange(0, site.Size.X - 1),
                site.Position.Y + blastRandom.RandiRange(0, site.Size.Y - 1));

            if (IsInsideACementHole(tile))
            {
                continue;
            }

            // Only ground the simulation owns, and only where a chunk already exists - hardening
            // must never be the thing that materialises new terrain.
            if (!TileIsSimulationOwned(tile) || !TryGetChunkCached(CellToChunk(TileToCellOrigin(tile)), out _))
            {
                continue;
            }

            Vector2I cell = TileToCellOrigin(tile) + new Vector2I(
                blastRandom.RandiRange(0, CellsPerTileAxis - 1),
                blastRandom.RandiRange(0, CellsPerTileAxis - 1));

            MaterialId here = GetCell(cell);

            if (here != MaterialId.Dirt && here != MaterialId.LooseDirt)
            {
                continue;
            }

            // Only the face of a wall. Ants plaster the tunnel they walk through, not the rock a
            // metre behind it.
            if (!TouchesAir(cell))
            {
                continue;
            }

            SetCell(cell, MaterialId.HardenedDirt);
        }
    }

    private bool IsInsideACementHole(Vector2I tile)
    {
        foreach (Rect2I hole in cementHoles)
        {
            if (hole.HasPoint(tile))
            {
                return true;
            }
        }

        return false;
    }

    // The burrow itself, added once and never removed. Byte-for-byte the old behaviour: the same
    // square, the same radius. The nest is where the colony lives whether or not a room has been
    // placed in it, and it has to keep cementing before there are any rooms at all.
    private void EnsureNestSite()
    {
        if (nestSite.HasValue)
        {
            return;
        }

        Vector2I nest = Grid.NestCenterCell;
        int span = HardenRadiusTiles * 2 + 1;

        nestSite = new Rect2I(nest - new Vector2I(HardenRadiusTiles, HardenRadiusTiles), new Vector2I(span, span));

        AddCementSite(nestSite.Value);
    }

    private Rect2I? nestSite;

    // Four orthogonal neighbours, and a cell with no chunk behind it counts as earth rather than as
    // air.
    //
    // That last part matters: GetCell falls through to the tile grid for a cell whose chunk does not
    // exist, and the tile grid generates on demand - so a candidate sitting on a chunk boundary
    // could make the plastering sweep the thing that materialises new terrain, which the guard
    // above exists to prevent.
    private bool TouchesAir(Vector2I cell)
    {
        return IsAirAndMaterialised(cell + Vector2I.Up)
            || IsAirAndMaterialised(cell + Vector2I.Down)
            || IsAirAndMaterialised(cell + Vector2I.Left)
            || IsAirAndMaterialised(cell + Vector2I.Right);
    }

    private bool IsAirAndMaterialised(Vector2I cell)
    {
        return TryGetChunkCached(CellToChunk(cell), out _) && MaterialDatabase.Get(GetCell(cell)).IsAir;
    }

    // How far from the nest the colony bothers to plaster, how often it works, and how many cells it
    // tries per pass. Tuned for a trickle: the core of a burrow cements over a few minutes, and a
    // room's ring - sixteen tiles against the nest's eight hundred - in seconds.
    private const int HardenRadiusTiles = 14;
    private const double HardenIntervalSeconds = 0.25;
    private const int HardenAttemptsPerPass = 24;

    private double hardenTimer;

    public int ChunkCount => chunks.Count;

    public int AwakeChunkCount => awakeChunks.Count;

    // ---- coordinate conversions -------------------------------------------------------------

    // Arithmetic right shift, which floors rather than truncating, so cells west or north of the
    // origin land in the right chunk instead of being pulled toward zero.
    public static Vector2I CellToChunk(Vector2I cell)
    {
        return new Vector2I(cell.X >> MaterialChunk.SizeShift, cell.Y >> MaterialChunk.SizeShift);
    }

    public static Vector2I WorldToCell(Vector2 worldPosition)
    {
        return new Vector2I(
            Mathf.FloorToInt(worldPosition.X / CellSize),
            Mathf.FloorToInt(worldPosition.Y / CellSize)
        );
    }

    public static Vector2 CellToWorld(Vector2I cell)
    {
        return new Vector2(cell.X * CellSize, cell.Y * CellSize);
    }

    public static Vector2I TileToCellOrigin(Vector2I tile)
    {
        return tile * CellsPerTileAxis;
    }

    public static Vector2I CellToTile(Vector2I cell)
    {
        return new Vector2I(cell.X >> CellsPerTileShift, cell.Y >> CellsPerTileShift);
    }

    // ---- cell access ------------------------------------------------------------------------

    public MaterialId GetCell(Vector2I cell)
    {
        Vector2I chunkCoord = CellToChunk(cell);

        // No chunk means nothing has disturbed this area, so it still is whatever the tile grid says.
        if (!TryGetChunkCached(chunkCoord, out MaterialChunk chunk))
        {
            return MaterialFromTile(CellToTile(cell));
        }

        return (MaterialId)chunk.Cells[LocalIndex(cell)];
    }

    public void SetCell(Vector2I cell, MaterialId material)
    {
        Vector2I chunkCoord = CellToChunk(cell);
        MaterialChunk chunk = EnsureChunk(chunkCoord);

        int index = LocalIndex(cell);

        if (chunk.Cells[index] == (byte)material)
        {
            return;
        }

        chunk.Cells[index] = (byte)material;
        chunk.TextureDirty = true;
        dirtyTiles.Add(CellToTile(cell));

        // A cell that has become something else no longer carries the old material temperature.
        //
        // Both of these stores are sparse, and in a world with no fire or lava in it they are empty.
        // Removing from an empty dictionary is a no-op, but it is a no-op that still hashes the key,
        // and SetCell runs twice for every cell that moves - so the emptiness is worth checking.
        if (temperatures.Count > 0)
        {
            temperatures.Remove(cell);
        }

        int lifetime = MaterialDatabase.Get(material).Lifetime;

        if (lifetime > 0)
        {
            lifetimes[cell] = lifetime;
        }
        else if (lifetimes.Count > 0)
        {
            lifetimes.Remove(cell);
        }

        Wake(cell);
    }

    // Wakes the cell's chunk, and any neighbouring chunk it sits against - otherwise material
    // arriving at a chunk edge would never stir the settled material on the other side.
    public void Wake(Vector2I cell)
    {
        Vector2I chunkCoord = CellToChunk(cell);
        int localX = cell.X & (MaterialChunk.Size - 1);
        int localY = cell.Y & (MaterialChunk.Size - 1);

        MarkChunkDirty(EnsureChunk(chunkCoord), localX, localY);

        // The sweep below exists only to stir the chunk on the far side of a border. A cell in the
        // interior has all eight of its neighbours in its own chunk, and MarkDirty already padded
        // the rectangle to cover them, so for the 94% of cells that are not on an edge the whole
        // thing was dead work - and Wake runs on almost every cell the simulation touches.
        if (localX > 0 && localY > 0 && localX < MaterialChunk.Size - 1 && localY < MaterialChunk.Size - 1)
        {
            return;
        }

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                Vector2I neighbour = cell + new Vector2I(dx, dy);
                Vector2I neighbourChunk = CellToChunk(neighbour);

                // Only stir chunks that already exist. Waking across a border must never be the
                // thing that materialises new terrain, or a single spill would cascade outward.
                if (neighbourChunk == chunkCoord || !chunks.TryGetValue(neighbourChunk, out MaterialChunk other))
                {
                    continue;
                }

                MarkChunkDirty(
                    other,
                    neighbour.X - neighbourChunk.X * MaterialChunk.Size,
                    neighbour.Y - neighbourChunk.Y * MaterialChunk.Size
                );
            }
        }
    }

    // The only three ways a chunk's wake state changes are this, MarkChunkAllDirty on a save restore,
    // and the simulation taking the rect at the start of a tick. Keeping all three here is what lets
    // the awake set be trusted instead of recomputed.
    private void MarkChunkDirty(MaterialChunk chunk, int localX, int localY)
    {
        if (chunk.MarkDirty(localX, localY))
        {
            awakeChunks.Add(chunk.Coord);
        }
    }

    private void MarkChunkAllDirty(MaterialChunk chunk)
    {
        chunk.MarkAllDirty();
        awakeChunks.Add(chunk.Coord);
    }

    // Called by the simulation once it has taken a chunk's dirty rectangle. Anything that moves
    // during the tick wakes it straight back up through Wake.
    public void MarkChunkAsleep(MaterialChunk chunk)
    {
        awakeChunks.Remove(chunk.Coord);
    }

    // Puts the rows a tick ran out of budget for back on the list, exactly as they were.
    public void RedirtyRows(MaterialChunk chunk, int minX, int minY, int maxX, int maxY)
    {
        MarkChunkDirty(chunk, minX, minY);
        MarkChunkDirty(chunk, maxX, maxY);
    }

    // Whether anything in the world currently has a countdown or an off-ambient temperature. When
    // nothing does - which is any world without fire, lava or steam in it - every read below is a
    // hash of a key that cannot be there, and the tick can skip the transient bookkeeping wholesale.
    public bool TracksAnyTransientState => lifetimes.Count > 0 || temperatures.Count > 0;

    public int GetLifetime(Vector2I cell)
    {
        if (lifetimes.Count == 0)
        {
            return 0;
        }

        return lifetimes.TryGetValue(cell, out int remaining) ? remaining : 0;
    }

    public void SetLifetime(Vector2I cell, int remaining)
    {
        if (remaining <= 0)
        {
            lifetimes.Remove(cell);
            return;
        }

        lifetimes[cell] = remaining;
    }


    // ---- temperature ------------------------------------------------------------------------
    //
    // Sparse, exactly like lifetimes: a cell only gets an entry once it deviates from its material
    // default, and loses it again when it settles back. A world of room-temperature dirt stores
    // nothing, and only the handful of cells near a fire or a lava flow cost anything.
    private const float AmbientEpsilon = 1.5f;

    private readonly Dictionary<Vector2I, float> temperatures = new();

    public int TrackedTemperatureCells => temperatures.Count;

    // Asked of every cell the tick scans, so the empty case has to be free rather than merely cheap.
    public bool HasTemperature(Vector2I cell)
    {
        return temperatures.Count > 0 && temperatures.ContainsKey(cell);
    }

    public float GetTemperature(Vector2I cell, MaterialDefinition definition)
    {
        if (temperatures.Count == 0)
        {
            return definition.DefaultTemperature;
        }

        return temperatures.TryGetValue(cell, out float value) ? value : definition.DefaultTemperature;
    }

    public void SetTemperature(Vector2I cell, float value, MaterialDefinition definition)
    {
        if (Mathf.Abs(value - definition.DefaultTemperature) < AmbientEpsilon)
        {
            temperatures.Remove(cell);
            return;
        }

        temperatures[cell] = value;
    }

    public void ClearTemperature(Vector2I cell)
    {
        temperatures.Remove(cell);
    }

    // ---- chunk management -------------------------------------------------------------------

    // One-entry lookup cache in front of the chunk dictionary.
    //
    // The simulation walks one chunk's cells in order and reads each cell's neighbours, so
    // consecutive lookups land in the same chunk nearly every time - only the cells on a chunk
    // border ever look elsewhere. Without this, every one of those dozens-per-cell reads re-hashed
    // a Vector2I and probed the dictionary.
    private Vector2I cachedChunkCoord;
    private MaterialChunk cachedChunk;

    // Loading a save swaps every chunk instance out from under the cache.
    private void InvalidateChunkCache()
    {
        cachedChunk = null;
    }

    private bool TryGetChunkCached(Vector2I chunkCoord, out MaterialChunk chunk)
    {
        if (cachedChunk != null && cachedChunkCoord == chunkCoord)
        {
            chunk = cachedChunk;
            return true;
        }

        if (!chunks.TryGetValue(chunkCoord, out chunk))
        {
            return false;
        }

        cachedChunkCoord = chunkCoord;
        cachedChunk = chunk;

        return true;
    }

    public MaterialChunk EnsureChunk(Vector2I chunkCoord)
    {
        if (TryGetChunkCached(chunkCoord, out MaterialChunk existing))
        {
            return existing;
        }

        MaterialChunk chunk = new MaterialChunk(chunkCoord);

        FillChunkFromTiles(chunk);

        chunks[chunkCoord] = chunk;

        cachedChunkCoord = chunkCoord;
        cachedChunk = chunk;

        // Brand new, so nothing has proved it settled yet. Awake is derived from the dirty rect, and
        // EnsureChunk fills from the tile grid without marking anything, so it starts asleep - which
        // is right: freshly materialised terrain is exactly as settled as the terrain it copied.
        return chunk;
    }

    public bool TryGetChunk(Vector2I chunkCoord, out MaterialChunk chunk)
    {
        return TryGetChunkCached(chunkCoord, out chunk);
    }

    // ---- terrain generation -----------------------------------------------------------------

    // Every cell of a tile gets the tile's material, so a 4x4 block of cells is one flat substance.
    // This is why painting terrain per cell still looked blocky: the cells were never given anything
    // to say that the tile had not already said.
    private void FillChunkFromTiles(MaterialChunk chunk)
    {
        Vector2I originCell = chunk.Coord * MaterialChunk.Size;

        for (int localY = 0; localY < MaterialChunk.Size; localY++)
        {
            for (int localX = 0; localX < MaterialChunk.Size; localX++)
            {
                Vector2I cell = originCell + new Vector2I(localX, localY);
                chunk.Cells[localY * MaterialChunk.Size + localX] = (byte)MaterialFromTile(CellToTile(cell));
            }
        }
    }

    public IReadOnlyDictionary<Vector2I, MaterialChunk> Chunks => chunks;

    // A stable snapshot of which chunks want simulating, since the tick itself can create chunks.
    //
    // Reads the maintained set rather than scanning every chunk. Scanning cost the frame a pass over
    // the whole dictionary whether or not anything was moving, which is the wrong shape entirely: an
    // idle world should cost nothing, and this way a world of ten thousand settled chunks is one
    // empty-set copy.
    public List<Vector2I> CollectAwakeChunks()
    {
        awakeScratch.Clear();

        foreach (Vector2I coord in awakeChunks)
        {
            awakeScratch.Add(coord);
        }

        return awakeScratch;
    }

    // Whether the maintained set still agrees with the chunks themselves. Only the tests call this:
    // a chunk that is dirty but missing from the set never gets stepped, which reads in game as
    // material frozen in mid-air, and that is not a failure anyone would connect back to here.
    public bool AwakeSetMatchesChunks()
    {
        int dirty = 0;

        foreach (MaterialChunk chunk in chunks.Values)
        {
            if (!chunk.Awake)
            {
                continue;
            }

            dirty++;

            if (!awakeChunks.Contains(chunk.Coord))
            {
                return false;
            }
        }

        return dirty == awakeChunks.Count;
    }

    // ---- gameplay-facing API ----------------------------------------------------------------
    //
    // What ants and tools ask the terrain. Deliberately phrased in world coordinates so callers do
    // not have to know the cell resolution.

    public MaterialId GetMaterialAt(Vector2 worldPosition)
    {
        return GetCell(WorldToCell(worldPosition));
    }

    public bool IsSolidAt(Vector2 worldPosition)
    {
        MaterialKind kind = MaterialDatabase.Get(GetMaterialAt(worldPosition)).Kind;

        return kind == MaterialKind.Solid || kind == MaterialKind.Powder;
    }

    public bool IsEmptyAt(Vector2 worldPosition)
    {
        return MaterialDatabase.Get(GetMaterialAt(worldPosition)).IsAir;
    }

    public bool IsLiquidAt(Vector2 worldPosition)
    {
        return MaterialDatabase.Get(GetMaterialAt(worldPosition)).Kind == MaterialKind.Liquid;
    }

    // Clears the 4x4 block of cells behind one gameplay tile. This is what digging calls.
    public void ClearTile(Vector2I tile)
    {
        Vector2I origin = TileToCellOrigin(tile);

        for (int y = 0; y < CellsPerTileAxis; y++)
        {
            for (int x = 0; x < CellsPerTileAxis; x++)
            {
                SetCell(origin + new Vector2I(x, y), MaterialId.Air);
            }
        }
    }

    public void FillTile(Vector2I tile, MaterialId material)
    {
        Vector2I origin = TileToCellOrigin(tile);

        for (int y = 0; y < CellsPerTileAxis; y++)
        {
            for (int x = 0; x < CellsPerTileAxis; x++)
            {
                SetCell(origin + new Vector2I(x, y), material);
            }
        }
    }

    // A circular daub of material. Doubles as the primitive an explosion will use later, by
    // painting Air over a radius.
    public void Paint(Vector2 worldPosition, float radius, MaterialId material, bool onlyReplaceAir = false)
    {
        Vector2I centre = WorldToCell(worldPosition);
        int cellRadius = Mathf.Max(1, Mathf.RoundToInt(radius / CellSize));

        for (int dy = -cellRadius; dy <= cellRadius; dy++)
        {
            for (int dx = -cellRadius; dx <= cellRadius; dx++)
            {
                if (dx * dx + dy * dy > cellRadius * cellRadius)
                {
                    continue;
                }

                Vector2I cell = centre + new Vector2I(dx, dy);

                if (onlyReplaceAir && !MaterialDatabase.Get(GetCell(cell)).IsAir)
                {
                    continue;
                }

                SetCell(cell, material);
            }
        }
    }



    // ---- terrain derivation -----------------------------------------------------------------
    //
    // The loop that turns the simulation into a gameplay system rather than a visual layer. Cells
    // are the truth about matter; this pushes that truth back into the tile grid so pathfinding,
    // standability and room building see it. Sand that buries a corridor really does block it.

    // Half the tile. Below this a few grains drifting through do not flip a corridor shut and then
    // open it again a tick later, which would thrash every path in the colony.
    private const int SolidCellsForSolidTile = CellsPerTile / 2;

    private readonly HashSet<Vector2I> dirtyTiles = new();

    public void DeriveDirtyTiles()
    {
        if (dirtyTiles.Count == 0)
        {
            return;
        }

        foreach (Vector2I tile in dirtyTiles)
        {
            DeriveTile(tile);
        }

        dirtyTiles.Clear();
    }

    private void DeriveTile(Vector2I tile)
    {
        GridManager.TileType current = Grid.GetTileAt(tile);

        // Only tiles the simulation owns. Food deposits, grass and the rest keep their identity no
        // matter what washes over them - losing a seed cache to a puddle would be a nasty surprise.
        if (!IsSimulationOwned(current))
        {
            return;
        }

        Vector2I origin = TileToCellOrigin(tile);

        int solid = 0;
        int stone = 0;
        int grass = 0;

        for (int y = 0; y < CellsPerTileAxis; y++)
        {
            for (int x = 0; x < CellsPerTileAxis; x++)
            {
                MaterialId id = GetCell(origin + new Vector2I(x, y));
                MaterialKind kind = MaterialDatabase.Get(id).Kind;

                if (kind != MaterialKind.Solid && kind != MaterialKind.Powder)
                {
                    continue;
                }

                solid++;

                if (id == MaterialId.Stone)
                {
                    stone++;
                }
                else if (id == MaterialId.Grass)
                {
                    grass++;
                }
            }
        }

        bool shouldBeSolid = solid >= SolidCellsForSolidTile;

        // Stone first, then turf, then plain soil. Turf only needs a third of the tile because it is
        // a skin over the dirt rather than the bulk of it - demanding a majority would mean a tile
        // never read as grass at all.
        GridManager.TileType wanted = shouldBeSolid
            ? (stone * 2 >= solid ? GridManager.TileType.Rock
                : grass * 3 >= solid ? GridManager.TileType.Grass
                : GridManager.TileType.Dirt)
            : PassableTileFor(tile);

        if (wanted != current)
        {
            Grid.SetTileFromSimulation(tile, wanted);
        }
    }

    // Open ground above the surface is sky, below it is tunnel. Getting this wrong would turn the
    // whole sky solid or punch holes of sky through the earth.
    private GridManager.TileType PassableTileFor(Vector2I tile)
    {
        return tile.Y < Grid.SurfaceHeight - Grid.GrassDepth
            ? GridManager.TileType.Air
            : GridManager.TileType.Tunnel;
    }

    private static bool IsSimulationOwned(GridManager.TileType type)
    {
        return type == GridManager.TileType.Tunnel
            || type == GridManager.TileType.Air
            || type == GridManager.TileType.Dirt
            || type == GridManager.TileType.Rock
            || type == GridManager.TileType.Grass;
    }

    // ---- digging integration ----------------------------------------------------------------
    //
    // The existing dig pipeline stays exactly as it was: AntWorker still calls DigGrain, tiles still
    // flip to Tunnel, CellDug still fires. This simply listens in, so opening ground also opens the
    // matter behind it - and because every write wakes its neighbours, sand above a fresh tunnel
    // collapses into it and liquids run in without anything having to ask them to.

    // An even scatter, so a tile chipped a quarter at a time erodes all over rather than from one
    // corner. Ordered-dither indices into the 4x4 block.
    // The order cells of a tile are chipped away in, so a half-dug tile has its material spread
    // evenly rather than cleared from one corner.
    //
    // Generated rather than written out. It used to be a hand-written list of sixteen slots, which
    // was correct while a tile was sixteen cells and read straight off the end of itself the moment
    // a tile became 256 - the exact failure the comment on CellsPerTile warns about, missed anyway.
    private static readonly int[] ChipOrder = BuildChipOrder();

    // Ordered-dither sequence. A Bayer matrix visits a grid in an order that stays evenly spread at
    // every prefix, which is precisely what "half dug" should look like.
    private static int[] BuildChipOrder()
    {
        var order = new int[CellsPerTile];

        for (int y = 0; y < CellsPerTileAxis; y++)
        {
            for (int x = 0; x < CellsPerTileAxis; x++)
            {
                order[BayerIndex(x, y)] = y * CellsPerTileAxis + x;
            }
        }

        return order;
    }

    // A bijection of the tile onto 0..CellsPerTile-1, so every slot is filled exactly once.
    private static int BayerIndex(int x, int y)
    {
        int value = 0;

        for (int bit = CellsPerTileShift - 1; bit >= 0; bit--)
        {
            int xi = (x >> bit) & 1;
            int yi = (y >> bit) & 1;

            value = (value << 2) | ((xi ^ yi) << 1) | yi;
        }

        return value;
    }


    // How much earth a bored tunnel keeps overhead, in cells. Two rows is four world pixels.
    //
    // A dug tile used to be emptied completely, so every corridor was a clean sixteen-pixel box.
    // The ant walking it is twelve pixels tall, which left her rattling around in a crate. An ant
    // tunnel in the ground is dug to the size of the ant - that is the whole reason it is a tunnel
    // and not a cave - so a bored tile now keeps its lip and the channel comes out twelve pixels,
    // the height of the animal that cut it.
    //
    // Two rows is also comfortably inside the derivation threshold: a bored tile keeps 16 of its
    // 64 cells against a SolidCellsForSolidTile of 32, so it always reads back as open ground.
    // The deepest a lip ever gets, in cells - six world pixels. Bounds the channel, so this is what
    // has to be cleared to take a ceiling off completely, and what a test has to measure below.
    public const int MaxCeilingCellRows = 3;

    // Digging simply removes the earth. Nothing is shaken loose around the hole.
    //
    // It used to jar the surrounding soil into loose grains that slumped into the new tunnel. With
    // spoil hauling off there was nowhere for any of that to go, so it settled on the floor and
    // stayed: every corridor came out speckled with blocks of soil nobody could clear, which reads
    // as the digging being broken rather than as physics.
    private void OnTileDug(Vector2I tile)
    {
        BoreTile(tile);

        // A lip is only ever the underside of the earth above it. Dig that earth out too and the
        // lip has nothing left to hang from - it becomes a slab floating in the middle of the
        // cavity, which is precisely the "small squares in the tunnel" this game has had before.
        // So opening a tile also takes the ceiling off anything already open beneath it.
        for (int dx = -1; dx <= 1; dx++)
        {
            OpenCeiling(tile + new Vector2I(dx, 1));
        }
    }

    // Cuts the channel an ant walks down: the full width of the tile, floor to ceiling.
    private void BoreTile(Vector2I tile)
    {
        Vector2I origin = TileToCellOrigin(tile);

        for (int x = 0; x < CellsPerTileAxis; x++)
        {
            int top = CeilingDepthAt(tile, x);

            LayCeiling(origin, x, top);

            for (int y = top; y < CellsPerTileAxis; y++)
            {
                SetCell(origin + new Vector2I(x, y), MaterialId.Air);
            }
        }
    }

    // How much earth this one column of the tile keeps overhead.
    //
    // Ragged on purpose. A lip of constant depth just lowers the lid: the corridor is still a
    // flat-topped box, only a shorter one, and it still repeats exactly every sixteen pixels. The
    // depth is hashed on the world column rather than the column within the tile, so the roughness
    // runs continuously along a corridor instead of restarting at every tile boundary - which is
    // the difference between a tunnel and a row of identical crates.
    private int CeilingDepthAt(Vector2I tile, int column)
    {
        if (!KeepsCeiling(tile))
        {
            return 0;
        }

        return 1 + (int)(ColumnNoise(tile.X * CellsPerTileAxis + column) % 3);
    }

    private static uint ColumnNoise(int worldColumn)
    {
        uint hash = (uint)worldColumn * 2654435761u;

        hash ^= hash >> 15;
        hash *= 2246822519u;
        hash ^= hash >> 13;

        return hash;
    }

    // The lip has to be written, not merely left alone.
    //
    // A chunk is materialised on first write and fills itself from the tile grid - and GridManager
    // flips the tile to Tunnel *before* it announces the dig, so on untouched ground "clear
    // everything except the top two rows" clears everything and then finds two rows of air. The
    // ceiling only survived on tiles whose chunk already existed, which is nearly none of them.
    //
    // Cells that already hold something are left as they are: a tile chipped grain by grain had its
    // chunk materialised while it was still solid, and whatever is up there - cemented wall, stone -
    // is the real ceiling and better than anything this could invent.
    private void LayCeiling(Vector2I origin, int column, int rows)
    {
        // Made of whatever it hangs from, so a roof under rock is rock and a roof under a wall the
        // ants have cemented keeps the cement. Turf is the one substitution: the underside of grass
        // is soil, and a green ceiling underground would be nonsense.
        MaterialId above = GetCell(origin + new Vector2I(column, -1));
        MaterialId roof = above == MaterialId.Stone || above == MaterialId.HardenedDirt
            ? above
            : MaterialId.Dirt;

        for (int y = 0; y < rows; y++)
        {
            Vector2I cell = origin + new Vector2I(column, y);

            if (MaterialDatabase.Get(GetCell(cell)).IsAir)
            {
                SetCell(cell, roof);
            }
        }
    }

    // Takes the lip off a tile that is already open.
    //
    // Only earth is removed. Sand that has slumped in and water that has run in belong to the
    // simulation now, and clearing them here would quietly destroy matter every time a neighbour
    // was dug - a corridor could be drained by excavating next to it.
    private void OpenCeiling(Vector2I tile)
    {
        if (!IsOpenGround(tile))
        {
            return;
        }

        Vector2I origin = TileToCellOrigin(tile);

        for (int y = 0; y < MaxCeilingCellRows; y++)
        {
            for (int x = 0; x < CellsPerTileAxis; x++)
            {
                Vector2I cell = origin + new Vector2I(x, y);

                if (MaterialDatabase.Get(GetCell(cell)).Kind == MaterialKind.Solid)
                {
                    SetCell(cell, MaterialId.Air);
                }
            }
        }
    }

    // No lip where there is nothing above to hang it from.
    //
    // Diagonals count, and that is the load-bearing part: a dig route is a staircase of tiles that
    // meet at a corner, so on a diagonal step the only join between two cavities is that single
    // corner. Leave the lip in and the corridor is stopped by a few pixels of earth at every step
    // of the staircase - open ground the ant is routed through and cannot be seen to pass.
    private bool KeepsCeiling(Vector2I tile)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            if (IsOpenGround(tile + new Vector2I(dx, -1)))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsOpenGround(Vector2I tile)
    {
        GridManager.TileType type = Grid.GetTileAt(tile);

        return type == GridManager.TileType.Tunnel || type == GridManager.TileType.Air;
    }

    private void OnTileChipped(Vector2I tile, int removed, int total)
    {
        // Confined to the channel the finished bore will cut, so a tile part-way through being dug
        // erodes towards the shape it is going to end up as. Spread over the whole tile instead and
        // the ceiling would be chewed away first and then laid back down by the bore - a roof that
        // visibly grows back as the digging finishes.
        System.Span<int> tops = stackalloc int[CellsPerTileAxis];
        int channel = 0;

        for (int x = 0; x < CellsPerTileAxis; x++)
        {
            tops[x] = CeilingDepthAt(tile, x);
            channel += CellsPerTileAxis - tops[x];
        }

        // Only materialise a chunk once a tile is genuinely being worked; a glancing first chip on
        // untouched ground is not worth paying for.
        int cellsToClear = Mathf.Min(channel, removed * channel / Mathf.Max(1, total));

        Vector2I origin = TileToCellOrigin(tile);
        int cleared = 0;

        for (int i = 0; i < ChipOrder.Length && cleared < cellsToClear; i++)
        {
            int slot = ChipOrder[i];
            int x = slot % CellsPerTileAxis;
            int y = slot / CellsPerTileAxis;

            if (y < tops[x])
            {
                continue;
            }

            SetCell(origin + new Vector2I(x, y), MaterialId.Air);
            cleared++;
        }
    }

    // True for anything an ant should not walk into, driven off the Harmful flag - so making a new
    // material dangerous is a line in the material table, not a change here or at any call site.
    public bool IsDangerousAt(Vector2 worldPosition)
    {
        MaterialDefinition definition = MaterialDatabase.Get(GetMaterialAt(worldPosition));

        return definition.Harmful;
    }

    // The same question asked about a whole gameplay tile, which is the resolution navigation works
    // at. Any harmful cell condemns the tile: half a tile of lava is not half safe to walk through,
    // and an ant that clips the corner of it is just as dead.
    //
    // Cheap for the ordinary case - terrain nothing has disturbed has no chunk, so this is one
    // dictionary miss and a tile-type lookup rather than sixteen cell reads.
    public bool IsTileHarmful(Vector2I tile)
    {
        if (!TryGetChunkCached(CellToChunk(TileToCellOrigin(tile)), out _))
        {
            return MaterialDatabase.Get(MaterialFromTile(tile)).Harmful;
        }

        Vector2I origin = TileToCellOrigin(tile);

        for (int y = 0; y < CellsPerTileAxis; y++)
        {
            for (int x = 0; x < CellsPerTileAxis; x++)
            {
                if (MaterialDatabase.Get(GetCell(origin + new Vector2I(x, y))).Harmful)
                {
                    return true;
                }
            }
        }

        return false;
    }



    // ---- spoil handling ---------------------------------------------------------------------
    //
    // What an ant does with excavated material. These work in tile coordinates because that is the
    // scale an ant thinks at; one tile is CellsPerTile cells of matter.
    //
    // Off by default: conserving spoil means a hauling round trip for every few tiles dug, which
    // slows the colony to a crawl. The machinery stays here so it can be switched back on.
    [Export]
    public bool HaulingEnabled { get; set; }

    // What a digger actually scrapes out of a tile.
    //
    // Read off the tile type, not off the matter. It used to sample the cell at the middle of the
    // tile - and chipping clears cells in Bayer-dither order, where the middle cell happens to be
    // the sixteenth of sixty-four visited, so it was gone by the first or second of the four chips
    // a tile takes. Every chip after that read back Air and the spoil silently evaporated. Sampling
    // any single cell has the same shape of bug; the tile type is the only thing that still says
    // what the tile was made of while it is being taken apart.
    //
    // Loose soil rather than packed earth, because spoil has to behave like spoil: LooseDirt is a
    // powder, so a tipped load slumps into a cone. Dirt is deliberately Solid - a heap of it would
    // stand up in mid-air as a stack of cubes.
    public MaterialId SpoilFor(Vector2I tile)
    {
        return Grid != null && Grid.CanDig(tile) ? MaterialId.LooseDirt : MaterialId.Air;
    }

    // Scrapes loose material into an open tile. Returns how many cells found room; any beyond that
    // had nowhere to go, and the caller keeps them.
    public int EmitInto(Vector2I tile, int count, MaterialId material)
    {
        // Air is not a material you can put somewhere. Without this the loop below sails past its
        // own IsAir guard, SetCell short-circuits because the cell is already air, and placed++
        // runs anyway - so it reported placing matter it had not placed, and the caller threw away
        // the load it was still holding.
        if (MaterialDatabase.Get(material).IsAir)
        {
            return 0;
        }

        Vector2I origin = TileToCellOrigin(tile);
        int placed = 0;

        for (int y = 0; y < CellsPerTileAxis && placed < count; y++)
        {
            for (int x = 0; x < CellsPerTileAxis && placed < count; x++)
            {
                Vector2I cell = origin + new Vector2I(x, y);

                if (!MaterialDatabase.Get(GetCell(cell)).IsAir)
                {
                    continue;
                }

                SetCell(cell, material);
                placed++;
            }
        }

        return placed;
    }

    // An ant scooping loose material up off the floor. Solids stay put - she is picking up spill,
    // not tearing the walls down.
    public int Collect(Vector2I tile, int max, List<MaterialId> into)
    {
        Vector2I origin = TileToCellOrigin(tile);
        int taken = 0;

        // Top down, so a scooped heap keeps a sensible surface instead of being hollowed out.
        for (int y = 0; y < CellsPerTileAxis && taken < max; y++)
        {
            for (int x = 0; x < CellsPerTileAxis && taken < max; x++)
            {
                Vector2I cell = origin + new Vector2I(x, y);
                MaterialId id = GetCell(cell);
                MaterialDefinition definition = MaterialDatabase.Get(id);

                // Powder only. It used to be "anything that is not solid", which included liquids -
                // so a digger who broke into a water pocket carried the water off in her jaws and
                // tipped it on the spoil heap.
                if (definition.Kind != MaterialKind.Powder)
                {
                    continue;
                }

                into.Add(id);
                SetCell(cell, MaterialId.Air);
                taken++;
            }
        }

        return taken;
    }

    // Tips a carried load out, working upward as the lower tiles fill in. Returns how many grains
    // had nowhere to go; those are still in the list, and still hers.
    //
    // It used to return void and clear the list unconditionally, so every grain it could not place -
    // because the column was full, or because it walked off the top of the world - was deleted with
    // no accounting anywhere. Matter conservation in this game is a settling invariant that the
    // tests check to the cell, and this was a hole straight through it.
    public int Release(Vector2I tile, List<MaterialId> materials)
    {
        // As far up as the sky goes. Six tiles was arbitrary and too few: a mature spoil heap is
        // taller than that, and every grain past the sixth tile was the leak above.
        int maxTilesSearched = Grid != null ? Grid.SurfaceHeight : 6;
        int kept = 0;

        // Indexed rather than foreach, because the list is compacted as it is walked and enumerating
        // a list you are writing to throws.
        for (int i = 0; i < materials.Count; i++)
        {
            MaterialId material = materials[i];
            bool placed = false;

            for (int up = 0; up < maxTilesSearched && !placed; up++)
            {
                Vector2I target = tile - new Vector2I(0, up);

                if (!Grid.IsInBounds(target))
                {
                    break;
                }

                // Sky only. This is the guard that makes spoil in a corridor impossible rather than
                // merely unlikely - every load in the game goes through here.
                if (!Grid.IsSpoilTile(target))
                {
                    continue;
                }

                placed = EmitInto(target, 1, material) > 0;
            }

            if (!placed)
            {
                // Compacted in place, so a partly-tipped load costs no allocation. kept never runs
                // ahead of i, so this only ever overwrites a grain already dealt with.
                materials[kept++] = material;
            }
        }

        materials.RemoveRange(kept, materials.Count - kept);

        return kept;
    }

    // ---- destruction ------------------------------------------------------------------------
    //
    // An explosion is deliberately not its own system. It clears a circle of cells, dumps heat in,
    // and then gets out of the way: because every write wakes its neighbours, the sand above caves
    // in, liquids pour into the new cavity and anything flammable catches by itself. None of that is
    // scripted here.
    public void Explode(Vector2 worldPosition, float radius, float heat = 850f)
    {
        Vector2I centre = WorldToCell(worldPosition);
        int cellRadius = Mathf.Max(1, Mathf.RoundToInt(radius / CellSize));

        for (int dy = -cellRadius; dy <= cellRadius; dy++)
        {
            for (int dx = -cellRadius; dx <= cellRadius; dx++)
            {
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                if (distance > cellRadius)
                {
                    continue;
                }

                Vector2I cell = centre + new Vector2I(dx, dy);
                MaterialDefinition definition = MaterialDatabase.Get(GetCell(cell));

                // Strongest at the centre, tapering to nothing at the edge, and rolled per cell so
                // the crater comes out ragged instead of a suspiciously perfect circle.
                float force = 1f - distance / cellRadius;

                if (!definition.IsAir && definition.Destructible)
                {
                    // Density doubles as blast resistance, so stone shrugs off what guts soil
                    // without needing yet another field to tune.
                    float toughness = Mathf.Max(1f, definition.Density / ReferenceBlastDensity);

                    if (blastRandom.Randf() < force / toughness)
                    {
                        SetCell(cell, MaterialId.Air);

                        // Embers in the heart of it, which is what lets a blast light a fuel seam.
                        if (force > 0.55f && blastRandom.Randf() < 0.18f)
                        {
                            SetCell(cell, MaterialId.Fire);
                        }

                        continue;
                    }
                }

                // Survived, so it just gets hot - and hot enough may still be enough to set it off.
                MaterialDefinition current = MaterialDatabase.Get(GetCell(cell));

                if (!current.IsAir)
                {
                    SetTemperature(cell, GetTemperature(cell, current) + heat * force, current);
                    Wake(cell);
                }
            }
        }
    }

    // Whether a cell is close enough to the surface for daylight to reach it. Used by reactions that
    // only make sense in the open, so that opening a deep tunnel to the air does not make it a place
    // things grow.
    public bool IsNearSurface(Vector2I cell)
    {
        return Grid != null && CellToTile(cell).Y <= Grid.SurfaceHeight + DaylightDepthTiles;
    }

    // Tiles below the surface line that still count as lit.
    private const int DaylightDepthTiles = 2;

    // Whether the simulation may draw over and rewrite this tile. Food deposits, built rooms and the
    // rest are the tilemap.s to draw and keep their own art no matter what has washed over the cells
    // beneath them.
    public bool TileIsSimulationOwned(Vector2I tile)
    {
        return Grid != null && IsSimulationOwned(Grid.GetTileAt(tile));
    }

    // ---- terrain baseline -------------------------------------------------------------------

    private MaterialId MaterialFromTile(Vector2I tile)
    {
        if (Grid == null)
        {
            return MaterialId.Air;
        }

        return Grid.GetTileAt(tile) switch
        {
            GridManager.TileType.Rock => MaterialId.Stone,
            GridManager.TileType.Water => MaterialId.Water,
            GridManager.TileType.Tunnel => MaterialId.Air,
            GridManager.TileType.Air => MaterialId.Air,
            GridManager.TileType.Grass => MaterialId.Grass,
            _ => MaterialId.Dirt,
        };
    }

    // Masking with Size - 1 is the positive remainder even for negative cells, which is exactly the
    // local offset inside the chunk CellToChunk picked.
    private static int LocalIndex(Vector2I cell)
    {
        int localX = cell.X & (MaterialChunk.Size - 1);
        int localY = cell.Y & (MaterialChunk.Size - 1);

        return (localY << MaterialChunk.SizeShift) + localX;
    }
}
