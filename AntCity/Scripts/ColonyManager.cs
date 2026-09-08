using Godot;

public partial class ColonyManager : Node
{
    public int Ants { get; private set; } = 1;
    public int Food { get; private set; } = 0;
    public int Egg  { get; private set; } = 0;
    public int Capacity { get; private set; } = 10;

    // Fired whenever any colony value changes.
    [Signal]
    public delegate void ColonyChangedEventHandler();

    public override void _Ready()
    {
        GD.Print("Colony Manager started!");
    }

    public void AddFood(int amount)
    {
        Food += amount;
        EmitSignal(SignalName.ColonyChanged);
    }

    public bool RemoveFood(int amount)
    {
        if (Food < amount)
        {
            return false;
        }

        Food -= amount;
        EmitSignal(SignalName.ColonyChanged);

        return true;
    }

    public void AddEgg()
    {
        Egg++;
        EmitSignal(SignalName.ColonyChanged);
    }

    public bool RemoveEgg()
    {
        if (Egg <= 0)
        {
            return false;
        }

        Egg--;
        EmitSignal(SignalName.ColonyChanged);

        return true;
    }

    public bool AddAnt()
    {
        if (Ants >= Capacity)
        {
            return false;
        }

        Ants++;
        EmitSignal(SignalName.ColonyChanged);

        return true;
    }

    public bool RemoveAnt()
    {
        if (Ants <= 0)
        {
            return false;
        }

        Ants--;
        EmitSignal(SignalName.ColonyChanged);

        return true;
    }

    public void IncreaseCapacity(int amount)
    {
        Capacity += amount;
        EmitSignal(SignalName.ColonyChanged);
    }
}