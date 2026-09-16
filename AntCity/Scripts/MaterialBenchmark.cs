using Godot;
using System.Collections.Generic;
using System.Diagnostics;

// Times the simulation so Stage 7 optimisation is aimed at whatever is actually slow.
//
// Every scenario drives Step() by hand with SimulationEnabled off, because a benchmark that runs off
// _Process measures the frame scheduler as much as the simulation.
//
// Run with:
//   godot --headless --path . res://AntCity/Scenes/Bench.tscn --quit-after 2
public partial class MaterialBenchmark : Node
{
    private const int IdleChunkAxis = 24;

    private GridManager grid;
    private MaterialWorld materials;

    public override void _Ready()
    {
        grid = GetNode<GridManager>("Main/GridManager");
        materials = GetNode<MaterialWorld>("Main/MaterialWorld");

        // Hands the tick to us rather than the frame loop.
        materials.SimulationEnabled = false;

        GD.Print("--- material benchmark ---");

        IdleWorld();
        Primitives();
        FallingColumn();
        FloodedCavern();
        TextureUploads();

        GD.Print("--- end ---");
    }

    // The calls the tick makes over and over. A settling pool was costing about 2.5us per scanned
    // cell, which is thousands of cycles for what should be a few array reads, and the only way to
    // find out which call is eating it is to time them one at a time.
    private void Primitives()
    {
        Vector2I origin = MaterialWorld.TileToCellOrigin(grid.NestCenterCell);
        var set = new HashSet<Vector2I>();

        // Inside one already-materialised chunk, so none of these measure chunk creation.
        materials.EnsureChunk(MaterialWorld.CellToChunk(origin));

        const int Ops = 200_000;

        // The instrument measuring itself. Every line below builds a cell address the same way, so
        // whatever this costs has to come off each of them before the numbers mean anything.
        Vector2I sink = Vector2I.Zero;

        ReportOps("control (address maths only)", Ops, () =>
        {
            for (int i = 0; i < Ops; i++)
            {
                sink += origin + new Vector2I(i & 31, (i >> 5) & 31);
            }
        });

        ReportOps("GetCell", Ops, () =>
        {
            for (int i = 0; i < Ops; i++)
            {
                _ = materials.GetCell(origin + new Vector2I(i & 31, (i >> 5) & 31));
            }
        });

        ReportOps("Wake", Ops, () =>
        {
            for (int i = 0; i < Ops; i++)
            {
                materials.Wake(origin + new Vector2I(i & 31, (i >> 5) & 31));
            }
        });

        ReportOps("GetLifetime", Ops, () =>
        {
            for (int i = 0; i < Ops; i++)
            {
                _ = materials.GetLifetime(origin + new Vector2I(i & 31, (i >> 5) & 31));
            }
        });

        ReportOps("HashSet<Vector2I> add+contains", Ops, () =>
        {
            set.Clear();

            for (int i = 0; i < Ops; i++)
            {
                Vector2I cell = origin + new Vector2I(i & 31, (i >> 5) & 31);

                set.Add(cell);
                _ = set.Contains(cell);
            }
        });

        ReportOps("ReactionTable.TryGet", Ops, () =>
        {
            for (int i = 0; i < Ops; i++)
            {
                _ = ReactionTable.TryGet(MaterialId.Water, MaterialId.Dirt, out _);
            }
        });
    }

    private static void ReportOps(string name, int ops, System.Action body)
    {
        body();

        var watch = Stopwatch.StartNew();
        body();
        watch.Stop();

        GD.Print($"  {name}: {watch.Elapsed.TotalMilliseconds / ops * 1_000_000:F1} ns/op");
    }

    // The case that matters most: a world a player has been digging around in for a while, with
    // nothing moving. This should cost nothing, and anything it does cost is pure overhead.
    private void IdleWorld()
    {
        Vector2I start = MaterialWorld.CellToChunk(MaterialWorld.WorldToCell(grid.CellToWorld(grid.NestCenterCell)));

        for (int y = 0; y < IdleChunkAxis; y++)
        {
            for (int x = 0; x < IdleChunkAxis; x++)
            {
                materials.EnsureChunk(start + new Vector2I(x - IdleChunkAxis / 2, y - IdleChunkAxis / 2));
            }
        }

        // Let every freshly materialised chunk prove it is settled and go to sleep.
        for (int i = 0; i < 12; i++)
        {
            materials.Simulation.Step();
        }

        Report($"idle, {materials.ChunkCount} chunks, {materials.AwakeChunkCount} awake", 600, () =>
        {
            materials.Simulation.Step();
            materials.DeriveDirtyTiles();
        });

        Report("idle AwakeChunkCount only", 600, () => _ = materials.AwakeChunkCount);
    }

    // A single tall column of sand: the cheapest possible moving scenario, so it isolates per-tick
    // fixed cost from per-cell cost.
    private void FallingColumn()
    {
        Vector2I top = MaterialWorld.TileToCellOrigin(grid.NestCenterCell + new Vector2I(0, -14));

        for (int i = 0; i < 48; i++)
        {
            materials.SetCell(top + new Vector2I(0, i), MaterialId.Sand);
        }

        Report("one 48-cell sand column falling", 200, () =>
        {
            materials.Simulation.Step();
            materials.DeriveDirtyTiles();
        });
    }

    // The stress case. A wide body of liquid keeps a lot of chunks awake at once, because a pool
    // levelling out is the one thing that genuinely refuses to settle quickly.
    private void FloodedCavern()
    {
        Vector2I nest = grid.NestCenterCell;
        int cells = 0;

        for (int tileY = -12; tileY < 0; tileY++)
        {
            for (int tileX = -24; tileX < 24; tileX++)
            {
                Vector2I tile = nest + new Vector2I(tileX, tileY);

                if (grid.GetTileAt(tile) != GridManager.TileType.Tunnel && grid.GetTileAt(tile) != GridManager.TileType.Air)
                {
                    materials.ClearTile(tile);
                }

                materials.FillTile(tile, MaterialId.Water);
                cells += MaterialWorld.CellsPerTile;
            }
        }

        // Totalled as well as timed. The average alone cannot tell a slow per-cell path apart from a
        // tick that simply had a lot of cells in it, and those want completely different fixes.
        long scanned = 0;
        long moved = 0;

        Report($"{cells} water cells settling", 120, () =>
        {
            materials.Simulation.Step();
            materials.DeriveDirtyTiles();

            scanned += materials.Simulation.CellsProcessedLastTick;
            moved += materials.Simulation.CellsMovedLastTick;
        });

        GD.Print($"    {scanned} cells scanned over the run, {moved} moved, " +
                 $"awake {materials.AwakeChunkCount}/{materials.ChunkCount}");
        GD.Print($"    last tick: scanned {materials.Simulation.CellsProcessedLastTick}, " +
                 $"moved {materials.Simulation.CellsMovedLastTick}");
    }

    // Repainting is separate from ticking: a chunk with one changed cell still rewrites all 4096
    // pixels, so this says whether that matters next to the tick itself.
    private void TextureUploads()
    {
        var dirty = new List<MaterialChunk>();

        foreach (KeyValuePair<Vector2I, MaterialChunk> entry in materials.Chunks)
        {
            dirty.Add(entry.Value);
        }

        Image image = Image.CreateEmpty(MaterialChunk.Size, MaterialChunk.Size, false, Image.Format.Rgba8);

        Report($"repaint {dirty.Count} chunk textures", 20, () =>
        {
            foreach (MaterialChunk chunk in dirty)
            {
                MaterialRenderer.Repaint(image, chunk, materials);
            }
        });
    }

    private static void Report(string name, int iterations, System.Action body)
    {
        // One untimed pass so a first-call allocation does not land in the average.
        body();

        var watch = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            body();
        }

        watch.Stop();

        double perCall = watch.Elapsed.TotalMilliseconds / iterations;
        GD.Print($"  {name}: {perCall:F4} ms/call  ({perCall / (1000.0 / 60.0) * 100:F1}% of a 60fps frame)");
    }
}
