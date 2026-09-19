using Godot;
using System.Collections.Generic;

// Watches the colony play itself and reports what it is actually doing.
//
// "Is the game working" is not answerable by reading the code: the loop is autonomous workers making
// their own decisions against terrain they generate as they go. This runs it and prints the numbers
// that say whether anything is happening - food going up or down, tiles being dug, ants sitting idle.
//
// Run with:
//   godot --headless --path . res://AntCity/Scenes/ColonyCheck.tscn
public partial class ColonyProbe : Node
{
    [Export] public int ReportEverySeconds { get; set; } = 15;
    [Export] public int RunSeconds { get; set; } = 240;

    private GridManager grid;
    private ColonyManager colony;
    private MaterialWorld materials;
    private Node main;

    private BuildManager build;
    private ColonyFounding founding;
    private PheromoneField pheromones;
    private double elapsed;
    private double sinceReport;
    private int startingTunnels;

    // Baselines. Generation leaves natural water pockets and turf underground, and ponds in the
    // surface row already split it into segments that cannot reach each other - so the absolute
    // counts are noise. What matters is whether the colony makes either number worse.
    private int startingSpoilUnderground;
    private int startingCutOff;
    private bool baselined;
    private int standableSurface;

    public override void _Ready()
    {
        main = GetNode("Main");
        grid = main.GetNode<GridManager>("GridManager");
        colony = main.GetNode<ColonyManager>("ColonyManager");
        colony.Alert += message => GD.Print($"  [alert] {message}");
        materials = main.GetNode<MaterialWorld>("MaterialWorld");
        build = main.GetNode<BuildManager>("BuildManager");
        founding = main.GetNode<ColonyFounding>("ColonyFounding");
        pheromones = main.GetNode<PheromoneField>("PheromoneField");

        startingTunnels = CountTunnels();

        GD.Print("--- colony probe ---");
        GD.Print($"start: nest {grid.NestCenterCell}  open tiles {startingTunnels}");
    }

    public override void _Process(double delta)
    {
        elapsed += delta;
        sinceReport += delta;

        // Nothing to drive until the queen is in the ground and the first workers are out.
        if (!founding.Finished)
        {
            return;
        }

        // Baselined here rather than in _Ready, because in _Ready the world has not been generated
        // yet - every count came back zero and the deltas then read as the whole world appearing.
        if (!baselined)
        {
            baselined = true;
            startingSpoilUnderground = SpoilUnderground();
            startingCutOff = SurfaceCellsCutOff();
            GD.Print($"baseline: spoil underground {startingSpoilUnderground}  surface reach {startingCutOff}");
        }

        PlayTheGame();

        if (sinceReport >= ReportEverySeconds)
        {
            sinceReport = 0;
            Report();
        }

        if (elapsed >= RunSeconds)
        {
            GD.Print("--- end ---");
            GetTree().Quit();
        }
    }

    // Does what a player would in the first couple of minutes: dig a chamber, put a nursery in it,
    // and start the queen laying. If the loop closes, population should climb after that.
    private void PlayTheGame()
    {
        if (step == 0 && elapsed > 2)
        {
            step = 1;
            OrderChamberDug();

            GD.Print($"  [player] ordered a chamber dug at {ChamberTopLeft()}");
        }

        if (step == 1 && elapsed > 60)
        {
            step = 2;

            Rect2I footprint = new Rect2I(ChamberTopLeft(), new Vector2I(ChamberWide, ChamberTall));

            build.TryCreateRoom(footprint, BuildingType.NestingChamber);

            GD.Print($"  [player] placed a nesting chamber over {footprint}  (food {colony.Food})");
        }

        if (step == 2 && elapsed > 90)
        {
            step = 3;
            colony.SetLaying(true);

            GD.Print("  [player] switched laying on");
        }

        // A second room, of a kind that feeds the colony rather than housing it. Worth driving
        // because a Fungus Farm is the only room whose effect arrives on the hour rather than at
        // the moment it is built, so nothing else would exercise that path.
        if (step == 3 && elapsed > 120)
        {
            step = 4;
            OrderFarmDug();

            GD.Print($"  [player] ordered a fungus farm dug at {FarmTopLeft()}");
        }

        if (step == 4 && elapsed > 180)
        {
            step = 5;

            var footprint = new Rect2I(FarmTopLeft(), new Vector2I(ChamberWide, ChamberTall));

            build.TryCreateRoom(footprint, BuildingType.FungusFarm);

            GD.Print($"  [player] placed a fungus farm over {footprint}  (food {colony.Food})");
        }
    }

    private Vector2I FarmTopLeft() => grid.NestCenterCell + new Vector2I(4, 8);

    private void OrderFarmDug()
    {
        Vector2I topLeft = FarmTopLeft();
        int issued = 0;

        foreach (Node child in main.GetChildren())
        {
            if (child is AntWorker worker)
            {
                worker.CommandDig(topLeft + new Vector2I(issued++ % ChamberWide, ChamberTall - 1));
            }
        }
    }

    private const int ChamberWide = 4;
    private const int ChamberTall = 2;

    private Vector2I ChamberTopLeft() => grid.NestCenterCell + new Vector2I(-2, 6);

    private void OrderChamberDug()
    {
        Vector2I topLeft = ChamberTopLeft();
        int issued = 0;

        foreach (Node child in main.GetChildren())
        {
            if (child is not AntWorker worker)
            {
                continue;
            }

            // Spread the workers across the chamber so they are not all queued on one cell.
            worker.CommandDig(topLeft + new Vector2I(issued % ChamberWide, ChamberTall - 1));
            issued++;
        }
    }

    private int step;

    private void Report()
    {
        var states = new Dictionary<string, int>();
        int ants = 0;
        int stalled = 0;

        foreach (Node child in main.GetChildren())
        {
            if (child is not AntWorker worker)
            {
                continue;
            }

            ants++;

            if (worker.IsStalled)
            {
                stalled++;
            }

            string state = worker.DebugState;
            states.TryGetValue(state, out int count);
            states[state] = count + 1;
        }

        string breakdown = "";

        foreach (KeyValuePair<string, int> entry in states)
        {
            breakdown += $"{entry.Key}:{entry.Value} ";
        }

        string rooms = "";

        foreach (Node child in main.GetChildren())
        {
            if (child is Room room)
            {
                rooms += $"{room.Type}:{room.State}({room.PendingDigCells.Count} left) ";
            }
        }

        GD.Print($"t={elapsed:F0}s  ants={ants}(counted {colony.Ants})/{colony.Capacity}  food={colony.Food}/{colony.FoodCapacity}  " +
                 $"rooms=[{rooms.Trim()}]  " +
                 $"eggs={colony.Egg} larvae={colony.LarvaCount}  upkeep/hr={colony.UpkeepPerHour}  " +
                 $"dug={CountTunnels() - startingTunnels}  stalls={AntWorker.StallRescues}  stuck={stalled}/{AntWorker.IdleStallRescues}  claims={build.ClaimedDigCellCount}/{build.ObstructionCount}  trail={pheromones.MarkedCells}  farmed={colony.FoodPerHourFarmed}/hr  mound={MoundTiles()}  spoilUnder={SpoilUnderground() - startingSpoilUnderground}  spoilLeft={AntWorker.SpoilLeftovers}  hauls={AntWorker.HaulTrips}  reach={SurfaceCellsCutOff()}of{standableSurface}/{startingCutOff}  surf=[{SurfaceProfile()}]  nest={grid.GetTileAt(grid.NestCenterCell)}/{(grid.IsStandable(grid.NestCenterCell) ? "stand" : "BLOCKED")}  [{breakdown.Trim()}]");
    }

    // Hauled spoil that has ended up underground, which must be zero.
    //
    // The whole point of the sky-only guard is that soil cannot get into a corridor, and this is what
    // checks it rather than trusting it. Loose soil only, not every powder: generation leaves natural
    // water pockets and buried turf down there in the hundreds, and counting those buried the signal
    // completely - the number moved around on its own and said nothing about hauling.
    private int SpoilUnderground()
    {
        int total = 0;
        Vector2I nest = grid.NestCenterCell;

        for (int y = grid.SurfaceHeight; y < grid.SurfaceHeight + 24; y++)
        {
            for (int x = nest.X - 20; x <= nest.X + 20; x++)
            {
                Vector2I origin = MaterialWorld.TileToCellOrigin(new Vector2I(x, y));

                for (int cy = 0; cy < MaterialWorld.CellsPerTileAxis; cy++)
                {
                    for (int cx = 0; cx < MaterialWorld.CellsPerTileAxis; cx++)
                    {
                        if (materials.GetCell(origin + new Vector2I(cx, cy)) == MaterialId.LooseDirt)
                        {
                            total++;
                        }
                    }
                }
            }
        }

        return total;
    }

    // How much hill the colony has built: solid tiles standing above the original surface line.
    private int MoundTiles()
    {
        int mound = 0;
        Vector2I nest = grid.NestCenterCell;

        for (int y = 0; y < grid.SurfaceHeight - grid.GrassDepth; y++)
        {
            for (int x = nest.X - 24; x <= nest.X + 24; x++)
            {
                if (!grid.IsTunnel(new Vector2I(x, y)))
                {
                    mound++;
                }
            }
        }

        return mound;
    }

    // The tripwire: how much of the foraging highway still reaches the nest.
    //
    // The turf row only, not every standable cell above the surface. A spoil heap has a standable
    // top that nothing can climb onto, and counting those as "cut off" made the number climb
    // steadily while the colony was in perfect health - it was measuring the hill, not the danger.
    // What actually kills a colony is its own mound sealing the row its foragers walk along, and
    // that is this number falling.
    private int SurfaceCellsCutOff()
    {
        int reaching = 0;
        standableSurface = 0;
        Vector2I nest = grid.NestCenterCell;
        int row = grid.SurfaceHeight - grid.GrassDepth;

        for (int x = nest.X - 20; x <= nest.X + 20; x++)
        {
            Vector2I cell = new Vector2I(x, row);

            if (!grid.IsStandable(cell))
            {
                continue;
            }

            standableSurface++;

            if (grid.FindTunnelPath(cell, nest) != null)
            {
                reaching++;
            }
        }

        return reaching;
    }

    // The surface walkway, one character per column, beside the count of it.
    //
    // reach= says how many surface cells can still reach the nest, and a falling count could mean
    // a hole, a hill, or the far lawn quietly detaching - three different problems that need three
    // different fixes. This says which. It turned an afternoon of theorising about the spoil mound
    // into one look: the hill grows two tiles proud at a choke point, which is steeper than the 45
    // degrees an ant can climb, and the lawn beyond it detaches until the pile slumps.
    //
    // '.' unstandable - buried, or undermined. 'o' standable and connected to the nest. 'X'
    // standable but cut off from it.
    private string SurfaceProfile()
    {
        var sb = new System.Text.StringBuilder();
        Vector2I nest = grid.NestCenterCell;
        int row = grid.SurfaceHeight - grid.GrassDepth;

        for (int x = nest.X - 20; x <= nest.X + 20; x++)
        {
            Vector2I cell = new Vector2I(x, row);

            sb.Append(!grid.IsStandable(cell) ? '.'
                : grid.FindTunnelPath(cell, nest) != null ? 'o' : 'X');
        }

        return sb.ToString();
    }

    // Open ground near the colony, as a proxy for "has anything been excavated".
    private int CountTunnels()
    {
        int open = 0;
        Vector2I nest = grid.NestCenterCell;

        for (int y = -4; y <= 40; y++)
        {
            for (int x = -40; x <= 40; x++)
            {
                if (grid.GetTileAt(nest + new Vector2I(x, y)) == GridManager.TileType.Tunnel)
                {
                    open++;
                }
            }
        }

        return open;
    }
}
