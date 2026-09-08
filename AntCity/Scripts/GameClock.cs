using Godot;

public partial class GameClock : Node
{
    public double ElapsedSeconds { get; private set; }

    public override void _Process(double delta)
    {
        ElapsedSeconds += delta;
    }

    public string GetFormattedTime()
    {
        int totalSeconds = (int)ElapsedSeconds;
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }
}
