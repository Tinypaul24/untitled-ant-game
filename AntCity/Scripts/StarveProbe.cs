using Godot;

// Starves a colony on purpose and prints what happens to it.
//
// Making starvation real is the one change in this batch that can make the game unwinnable, and the
// risk is not the crash surface - that is covered by tests - but the shape of the curve. A famine
// removes a forager, which reduces foraging, which deepens the famine. Whether that is a slope the
// colony can climb back up or a cliff is not something you can reason your way to; it has to be run.
//
// Run with:
//   godot --headless --path . res://AntCity/Scenes/StarveCheck.tscn
public partial class StarveProbe : Node
{
    [Export] public int RunSeconds { get; set; } = 620;
    [Export] public int ReportEverySeconds { get; set; } = 40;

    // How long nothing at all can be eaten. Long enough to be well past StarvingHoursBeforeLoss and
    // to kill several workers, short enough to leave time to see whether the colony recovers.
    [Export] public int FamineSeconds { get; set; } = 400;

    // Whether the colony eats its dead. The whole point of the harness is to be able to run it both
    // ways and see whether the difference matters.
    [Export] public bool EatTheDead { get; set; }

    private GridManager grid;
    private ColonyManager colony;
    private ColonyFounding founding;
    private Node main;

    private double elapsed;
    private double sinceReport;
    private bool started;

    public override void _Ready()
    {
        main = GetNode("Main");
        grid = main.GetNode<GridManager>("GridManager");
        colony = main.GetNode<ColonyManager>("ColonyManager");
        founding = main.GetNode<ColonyFounding>("ColonyFounding");

        colony.Alert += message => GD.Print($"  [alert] {message}");

        // Run fast. An in-game hour is the better part of a minute of real time and starvation only
        // bites after four of them, so at normal speed this harness would spend most of its life
        // waiting for a clock rather than watching a population.
        Engine.TimeScale = 4f;

        GD.Print($"--- starve probe (eat the dead: {EatTheDead}) ---");
    }

    public override void _Process(double delta)
    {
        elapsed += delta;
        sinceReport += delta;

        if (!founding.Finished)
        {
            return;
        }

        if (!started)
        {
            started = true;

            colony.SetEatTheDead(EatTheDead);
            colony.SetLaying(true);

            GD.Print($"famine begins: {colony.Ants} ants");
        }

        // Held empty for the first part of the run rather than drained once.
        //
        // Emptying the larder a single time proves nothing: the world is full of food and the colony
        // foraged back to the cap inside twenty seconds. A famine is a period during which nothing
        // can be eaten, and the question this harness exists to answer is what the population does
        // during one and whether it comes back afterwards.
        if (elapsed < FamineSeconds)
        {
            colony.RemoveFood(colony.Food);
        }

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

    private void Report()
    {
        int nodes = 0;
        int corpses = 0;

        foreach (Node child in main.GetChildren())
        {
            if (child is AntWorker)
            {
                nodes++;
            }
            else if (child is AntCorpse)
            {
                corpses++;
            }
        }

        GD.Print($"t={elapsed:F0}s  ants={nodes}(counted {colony.Ants})  food={colony.Food}  " +
                 $"eggs={colony.Egg} larvae={colony.LarvaCount}  bodies={corpses}  " +
                 $"upkeep/hr={colony.UpkeepPerHour}");
    }
}
