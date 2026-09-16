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
    private double elapsed;
    private double sinceReport;
    private int startingTunnels;

    public override void _Ready()
    {
        main = GetNode("Main");
        grid = main.GetNode<GridManager>("GridManager");
        colony = main.GetNode<ColonyManager>("ColonyManager");
        colony.Alert += message => GD.Print($"  [alert] {message}");
        materials = main.GetNode<MaterialWorld>("MaterialWorld");
        build = main.GetNode<BuildManager>("BuildManager");
        founding = main.GetNode<ColonyFounding>("ColonyFounding");

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

        foreach (Node child in main.GetChildren())
        {
            if (child is not AntWorker worker)
            {
                continue;
            }

            ants++;

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

        GD.Print($"t={elapsed:F0}s  ants={ants}/{colony.Capacity}  food={colony.Food}/{colony.FoodCapacity}  " +
                 $"rooms=[{rooms.Trim()}]  " +
                 $"eggs={colony.Egg} larvae={colony.LarvaCount}  upkeep/hr={colony.UpkeepPerHour}  " +
                 $"dug={CountTunnels() - startingTunnels}  [{breakdown.Trim()}]");
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
