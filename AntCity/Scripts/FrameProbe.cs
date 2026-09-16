using Godot;

// Reports real frame times from a real window, with subsystems removable one at a time.
//
// Wall clock rather than the delta handed to _Process, because that one is multiplied by
// Engine.TimeScale and this game drives that from its speed buttons - measuring it would report the
// simulation speed as though it were the frame rate.
public partial class FrameProbe : Node
{
    [Export] public string Drop { get; set; } = "";

    private const int WarmUpFrames = 120;
    private const int MeasuredFrames = 600;

    private ulong lastTick;
    private double worst;
    private double total;
    private int frames;

    public override void _Ready()
    {
        Node main = GetNode("Main");

        // Freed outright rather than hidden: hiding stops a node drawing but leaves its _Process
        // running, which is exactly the half that turned out to matter.
        foreach (string name in Drop.Split(',', System.StringSplitOptions.RemoveEmptyEntries))
        {
            Node target = main.GetNodeOrNull(name.Trim());

            if (target != null)
            {
                target.QueueFree();
            }
        }
    }

    public override void _Process(double delta)
    {
        frames++;

        ulong now = Time.GetTicksUsec();
        double realDelta = lastTick == 0 ? 0 : (now - lastTick) / 1_000_000.0;
        lastTick = now;

        if (frames < WarmUpFrames)
        {
            return;
        }

        worst = Mathf.Max(worst, realDelta);
        total += realDelta;

        if (frames == WarmUpFrames + MeasuredFrames)
        {
            double average = total / MeasuredFrames;

            GD.Print($"FRAME [{(Drop == "" ? "everything" : "minus " + Drop)}] " +
                     $"avg {average * 1000:F1}ms ({1.0 / average:F0} fps)  worst {worst * 1000:F1}ms  " +
                     $"process {Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000:F1}ms  " +
                     $"draws {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)}");

            GetTree().Quit();
        }
    }
}
