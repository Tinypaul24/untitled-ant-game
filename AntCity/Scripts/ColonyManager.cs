using Godot;

public partial class ColonyManager : Node
{
    // Upkeep is charged once per in-game hour (see GameClock) rather than on a fast real-time tick,
    // so a few minutes of not foraging doesn't wipe out an early colony.
    private const int FoodPerAntPerHour = 2;
    private const int FoodPerLarvaPerHour = 1;
    private const int StarvingHoursBeforeLoss = 4;

    [Export]
    public GameClock GameClock { get; set; }

    public int Ants { get; private set; } = 0;
    public int Food { get; private set; } = 50;
    public int FoodCapacity { get; private set; } = 75;
    public int Egg { get; private set; } = 0;
    public int LarvaCount { get; private set; } = 0;
    public int Capacity { get; private set; } = 10;
    public float HatchSpeedMultiplier { get; private set; } = 1f;

    public int PopulationUsed => Ants + Egg + LarvaCount;
    public bool HasRoomForMorePopulation => PopulationUsed < Capacity;

    // How much food upkeep currently costs the colony per in-game hour, for the food tooltip.
    public int UpkeepPerHour => Ants * FoodPerAntPerHour + LarvaCount * FoodPerLarvaPerHour;

    // Lifetime average food income, for the food tooltip's "generating per minute" figure.
    public double FoodPerMinute => GameClock.ElapsedSeconds > 0 ? totalFoodEarned / GameClock.ElapsedSeconds * 60.0 : 0.0;

    private int nurseryCellTotal;
    private int totalFoodEarned;
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

        GameClock.HourElapsed += ConsumeUpkeep;
    }

    public int AddFood(int amount)
    {
        int newFood = Mathf.Min(Food + amount, FoodCapacity);
        int actuallyAdded = newFood - Food;
        Food = newFood;
        totalFoodEarned += actuallyAdded;
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

    public ColonySave CaptureState()
    {
        return new ColonySave
        {
            Ants = Ants,
            Food = Food,
            FoodCapacity = FoodCapacity,
            Eggs = Egg,
            Larvae = LarvaCount,
            Capacity = Capacity,
            NurseryCellTotal = nurseryCellTotal,
            TotalFoodEarned = totalFoodEarned,
            StarvingIntervalStreak = starvingIntervalStreak,
        };
    }

    public void RestoreState(ColonySave save)
    {
        Ants = save.Ants;
        Food = save.Food;
        FoodCapacity = save.FoodCapacity;
        Egg = save.Eggs;
        LarvaCount = save.Larvae;
        Capacity = save.Capacity;
        nurseryCellTotal = save.NurseryCellTotal;
        totalFoodEarned = save.TotalFoodEarned;
        starvingIntervalStreak = save.StarvingIntervalStreak;
        HatchSpeedMultiplier = Mathf.Pow(0.95f, nurseryCellTotal);

        EmitSignal(SignalName.ColonyChanged);
    }

    private void ConsumeUpkeep()
    {
        int upkeep = UpkeepPerHour;

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
            GD.Print($"The colony is starving! ({starvingIntervalStreak} hour(s) with no food)");
            RaiseAlert("The colony is starving!");

            if (starvingIntervalStreak >= StarvingHoursBeforeLoss && RemoveAnt())
            {
                GD.Print("An ant has starved to death.");
                RaiseAlert("An ant has starved to death.");
                starvingIntervalStreak = 0;
            }
        }

        EmitSignal(SignalName.ColonyChanged);
    }
}
