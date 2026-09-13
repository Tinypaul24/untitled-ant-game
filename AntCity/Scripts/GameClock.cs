using Godot;

public partial class GameClock : Node
{
    // How many real seconds (scaled by Engine.TimeScale like everything else) make up one in-game hour.
    private const double SecondsPerGameHour = 60.0;

    public double ElapsedSeconds { get; private set; }
    public int ElapsedHours { get; private set; }

    // Fired each time the clock ticks over into a new in-game hour - ColonyManager hangs upkeep off this.
    [Signal]
    public delegate void HourElapsedEventHandler();

    public override void _Process(double delta)
    {
        ElapsedSeconds += delta;

        int hoursNow = (int)(ElapsedSeconds / SecondsPerGameHour);
        while (ElapsedHours < hoursNow)
        {
            ElapsedHours++;
            EmitSignal(SignalName.HourElapsed);
        }
    }

    public string GetFormattedTime()
    {
        double secondsIntoHour = ElapsedSeconds % SecondsPerGameHour;
        int minutesIntoHour = (int)(secondsIntoHour / SecondsPerGameHour * 60.0);
        return $"{ElapsedHours:00}:{minutesIntoHour:00}";
    }
}
