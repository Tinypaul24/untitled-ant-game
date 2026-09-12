using Godot;

public partial class ColonyManager : Node
{
    private const double ConsumptionIntervalSeconds = 5.0;
    private const int FoodPerAntPerInterval = 1;
    private const int FoodPerLarvaPerInterval = 1;
    private const int StarvingIntervalsBeforeLoss = 4;

    public int Ants { get; private set; } = 1;
    public int Food { get; private set; } = 50;
    public int Egg  { get; private set; } = 0;
    public int LarvaCount { get; private set; } = 0;
    public int Capacity { get; private set; } = 10;

    public int FoodCapacity { get; private set; } = 50;

    private double consumptionTimer;
    private int starvingIntervalStreak;

    // Fired whenever any colony value changes.
    [Signal]
    public delegate void ColonyChangedEventHandler();

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

            if (starvingIntervalStreak >= StarvingIntervalsBeforeLoss && RemoveAnt())
            {
                GD.Print("An ant has starved to death.");
                starvingIntervalStreak = 0;
            }
        }

        EmitSignal(SignalName.ColonyChanged);
    }
}
