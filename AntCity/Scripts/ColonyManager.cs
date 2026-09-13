using Godot;

public partial class ColonyManager : Node
{
    private const double ConsumptionIntervalSeconds = 5.0;
    private const int FoodPerAntPerInterval = 1;
    private const int FoodPerLarvaPerInterval = 1;
    private const int StarvingIntervalsBeforeLoss = 4;

    public int Ants { get; private set; } = 1;
    public int Food { get; private set; } = 50;
    public int FoodCapacity { get; private set; } = 75;
    public int Egg { get; private set; } = 0;
    public int LarvaCount { get; private set; } = 0;
    public int Capacity { get; private set; } = 10;
    public float HatchSpeedMultiplier { get; private set; } = 1f;

    public int PopulationUsed => Ants + Egg + LarvaCount;
    public bool HasRoomForMorePopulation => PopulationUsed < Capacity;

    private int nurseryCellTotal;
    private double consumptionTimer;
    private int starvingIntervalStreak;

    // Fired whenever any colony value changes.
    [Signal]
    public delegate void ColonyChangedEventHandler();

    // Fired for events worth surfacing to the player as a toast (starvation, room completions, ...).
    [Signal]
    public delegate void AlertEventHandler(string message);

    public void RaiseAlert(string message)
    {
        EmitSignal(SignalName.Alert, message);
    }

    public override void _Ready()
    {
        GD.Print("Colony Manager started!");
    }

    public override void _Process(double delta)
    {
        consumptionTimer += delta;

        if (consumptionTimer < ConsumptionIntervalSeconds)
        {
            return;
        }

        consumptionTimer -= ConsumptionIntervalSeconds;
        ConsumeUpkeep();
    }

    public int AddFood(int amount)
    {
        int newFood = Mathf.Min(Food + amount, FoodCapacity);
        int actuallyAdded = newFood - Food;
        Food = newFood;
        EmitSignal(SignalName.ColonyChanged);

        return actuallyAdded;
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

    public void IncreaseFoodCapacity(int amount)
    {
        FoodCapacity += amount;
        EmitSignal(SignalName.ColonyChanged);
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

    public void AddLarva()
    {
        LarvaCount++;
        EmitSignal(SignalName.ColonyChanged);
    }

    public bool RemoveLarva()
    {
        if (LarvaCount <= 0)
        {
            return false;
        }

        LarvaCount--;
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

    // Every nursery cell (across any number of nursery rooms) chips away at hatch/maturity time, with diminishing returns.
    public void AddNursery(int cellCount)
    {
        nurseryCellTotal += cellCount;
        HatchSpeedMultiplier = Mathf.Pow(0.95f, nurseryCellTotal);
        EmitSignal(SignalName.ColonyChanged);
    }

    private void ConsumeUpkeep()
    {
        int upkeep = Ants * FoodPerAntPerInterval + LarvaCount * FoodPerLarvaPerInterval;

        if (upkeep <= 0)
        {
            starvingIntervalStreak = 0;
            return;
        }

        if (Food >= upkeep)
        {
            Food -= upkeep;
            starvingIntervalStreak = 0;
        }
        else
        {
            Food = 0;
            starvingIntervalStreak++;
            GD.Print($"The colony is starving! ({starvingIntervalStreak} interval(s) with no food)");
            RaiseAlert("The colony is starving!");

            if (starvingIntervalStreak >= StarvingIntervalsBeforeLoss && RemoveAnt())
            {
                GD.Print("An ant has starved to death.");
                RaiseAlert("An ant has starved to death.");
                starvingIntervalStreak = 0;
            }
        }

        EmitSignal(SignalName.ColonyChanged);
    }
}
