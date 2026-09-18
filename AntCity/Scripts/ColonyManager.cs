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

    // Only so the corpse policy can reach the forage search. Null-tolerant: the scenes that have no
    // grid still have a colony.
    [Export]
    public GridManager Grid { get; set; }

    public int Ants { get; private set; } = 0;
    public int Food { get; private set; } = 50;
    public int FoodCapacity { get; private set; } = 75;
    public int Egg { get; private set; } = 0;
    public int LarvaCount { get; private set; } = 0;
    public int Capacity { get; private set; } = 10;
    public float HatchSpeedMultiplier { get; private set; } = 1f;
    // Above one: the Queen lays that much faster. Raised by Royal Chamber cells.
    public float LaySpeedMultiplier { get; private set; } = 1f;
    // Food the colony grows for itself each in-game hour, from Fungus Farm cells.
    public int FoodPerHourFarmed => fungusCellTotal;

    // Whether the Queen is currently allowed to lay. The player owns this decision - growth costs
    // food and every new ant raises upkeep, so when the colony expands is theirs to choose.
    public bool LayingEnabled { get; private set; }

    public int PopulationUsed => Ants + Egg + LarvaCount;
    public bool HasRoomForMorePopulation => PopulationUsed < Capacity;

    // How much food upkeep currently costs the colony per in-game hour, for the food tooltip.
    public int UpkeepPerHour => Ants * FoodPerAntPerHour + LarvaCount * FoodPerLarvaPerHour;

    // What upkeep actually costs once the farms have paid their share. Negative means the colony
    // feeds itself and then some, which is the point of building farms.
    public int NetFoodPerHour => FoodPerHourFarmed - UpkeepPerHour;

    // Lifetime average food income, for the food tooltip's "generating per minute" figure.
    public double FoodPerMinute => GameClock.ElapsedSeconds > 0 ? totalFoodEarned / GameClock.ElapsedSeconds * 60.0 : 0.0;

    private int nurseryCellTotal;
    private int fungusCellTotal;
    private int royalCellTotal;
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

        GameClock.HourElapsed += HarvestFarms;
        GameClock.HourElapsed += ConsumeUpkeep;
    }

    public int AddFood(int amount)
    {
        int newFood = Mathf.Min(Food + amount, FoodCapacity);
        int actuallyAdded = newFood - Food;
        Food = newFood;
        totalFoodEarned += actuallyAdded;
        EmitSignal(SignalName.ColonyChanged);

        NoteCeilings();

        return actuallyAdded;
    }

    // Tells the player when the colony has run into a wall, and which wall.
    //
    // Both ceilings are silent by default: a full larder looks exactly like a healthy one, and a
    // colony at its population cap simply stops growing with no explanation. Watching the game play
    // itself, it reached both within two minutes and then did nothing whatsoever, which reads as the
    // game being broken rather than as the game waiting for you to dig.
    //
    // Latched, so each is said once and only said again after the colony has climbed off the ceiling.
    private void NoteCeilings()
    {
        if (Food >= FoodCapacity && !warnedStoresFull)
        {
            warnedStoresFull = true;
            RaiseAlert($"Food stores are full at {FoodCapacity}. Dig out a chamber and build a Granary to keep more.");
        }
        else if (Food < FoodCapacity * ReArmStoresFullFraction)
        {
            warnedStoresFull = false;
        }

        if (PopulationUsed >= Capacity && !warnedPopulationFull)
        {
            warnedPopulationFull = true;
            RaiseAlert($"The nest is full at {Capacity}. Build a Nesting Chamber to make room for more ants.");
        }
        else if (PopulationUsed < Capacity)
        {
            warnedPopulationFull = false;
        }
    }

    // How far food has to fall before the full-stores warning is worth saying again. Laying an egg
    // costs two food, so re-arming the moment the larder dips below the brim meant the message
    // repeated every time the queen laid - a warning that fires constantly is one nobody reads.
    private const float ReArmStoresFullFraction = 0.8f;

    private bool warnedStoresFull;
    private bool warnedPopulationFull;

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

        NoteCeilings();
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

    // A larva finishing is not a decision, it is an arrival.
    //
    // This used to refuse when Ants reached Capacity, which is a stricter rule than the one the
    // colony actually runs on: maturing leaves PopulationUsed unchanged, since a larva becomes an
    // ant. So a colony pushed over capacity - demolish a Nesting Chamber at full population - left
    // every larva retrying every two seconds forever, each still eating and each still holding a
    // population slot that kept the colony over the line. Nothing could ever bring it back under.
    //
    // The ceiling belongs at the egg, where DecreaseCapacity already says it does: laying stops
    // until the population comes back under on its own.
    public bool AddAnt()
    {
        Ants++;
        EmitSignal(SignalName.ColonyChanged);

        NoteCeilings();

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


    public void SetLaying(bool enabled)
    {
        if (LayingEnabled == enabled)
        {
            return;
        }

        LayingEnabled = enabled;
        EmitSignal(SignalName.ColonyChanged);
    }
    public void IncreaseCapacity(int amount)
    {
        Capacity += amount;
        EmitSignal(SignalName.ColonyChanged);

        NoteCeilings();
    }

    // Every nursery cell (across any number of nursery rooms) chips away at hatch/maturity time,
    // with diminishing returns. A negative count takes cells back off, which is what demolishing
    // one does.
    public void AddNursery(int cellCount)
    {
        nurseryCellTotal = Mathf.Max(0, nurseryCellTotal + cellCount);
        Recompute();
    }


    // Population capacity given back when a chamber is pulled down.
    //
    // The colony is allowed to end up over capacity. Clamping it would mean killing ants to balance
    // a building decision, which is not a trade the player asked for - instead laying simply stops
    // until the population comes back under the ceiling on its own.
    public void DecreaseCapacity(int amount)
    {
        Capacity = Mathf.Max(1, Capacity - amount);
        EmitSignal(SignalName.ColonyChanged);

        if (PopulationUsed > Capacity)
        {
            RaiseAlert($"Over capacity: {PopulationUsed} in a nest built for {Capacity}. No new eggs until that settles.");
        }
    }

    // Storage given back when a granary is pulled down. Anything the colony can no longer hold is
    // genuinely lost - it was in that granary.
    public void DecreaseFoodCapacity(int amount)
    {
        FoodCapacity = Mathf.Max(10, FoodCapacity - amount);

        if (Food > FoodCapacity)
        {
            int spilled = Food - FoodCapacity;
            Food = FoodCapacity;

            RaiseAlert($"{spilled} food spoiled with nowhere left to keep it.");
        }

        EmitSignal(SignalName.ColonyChanged);
    }

    public void AddFungusFarm(int cellCount)
    {
        fungusCellTotal = Mathf.Max(0, fungusCellTotal + cellCount);
        EmitSignal(SignalName.ColonyChanged);
    }

    public void AddRoyalChamber(int cellCount)
    {
        royalCellTotal = Mathf.Max(0, royalCellTotal + cellCount);
        Recompute();
    }

    // Both multipliers compound per cell rather than adding.
    //
    // Adding would let a big enough nursery reach zero hatch time and a big enough royal chamber
    // reach infinite laying. Compounding gives diminishing returns for free and can never reach
    // either wall, so a room is always worth something and never worth everything.
    private void Recompute()
    {
        HatchSpeedMultiplier = Mathf.Pow(1f - BuildingDefs.All[BuildingType.Nursery].EffectPerCell, nurseryCellTotal);
        LaySpeedMultiplier = Mathf.Pow(1f + BuildingDefs.All[BuildingType.RoyalChamber].EffectPerCell, royalCellTotal);

        EmitSignal(SignalName.ColonyChanged);
    }

    // The colony eats what it grew before it eats what it stored.
    private void HarvestFarms()
    {
        if (fungusCellTotal > 0)
        {
            AddFood(fungusCellTotal);
        }
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
            FungusCellTotal = fungusCellTotal,
            RoyalCellTotal = royalCellTotal,
            TotalFoodEarned = totalFoodEarned,
            StarvingIntervalStreak = starvingIntervalStreak,
            LayingEnabled = LayingEnabled,
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
        fungusCellTotal = save.FungusCellTotal;
        royalCellTotal = save.RoyalCellTotal;
        totalFoodEarned = save.TotalFoodEarned;
        starvingIntervalStreak = save.StarvingIntervalStreak;
        LayingEnabled = save.LayingEnabled;
        Recompute();

        EmitSignal(SignalName.ColonyChanged);
    }


    // One worker, once an hour, for as long as there is nothing to eat.
    //
    // This used to decrement a counter and free nobody, which made starvation a reward rather than a
    // punishment: upkeep fell while every ant kept digging and foraging, and PopulationUsed fell
    // too, so HasRoomForMorePopulation went true and the Queen started laying again in the middle of
    // the famine. A positive feedback loop into the disaster it exists to punish.
    private void StarveOne()
    {
        AntWorker victim = PickTheWeakest();

        if (victim == null)
        {
            return;
        }

        victim.Die();

        RaiseAlert(EatTheDeadPolicy
            ? "A worker has starved. The colony will eat her."
            : "A worker has starved. Her body is being carried out.");

        GD.Print("An ant has starved to death.");
    }

    // Whoever the colony can most afford to lose: somebody carrying nothing and holding no job,
    // rather than a forager on her way home with food in her jaws. Furthest from the nest breaks
    // the tie, which is both the most plausible victim and a deterministic rule, so a colony run
    // twice does the same thing twice.
    private AntWorker PickTheWeakest()
    {
        AntWorker best = null;
        int bestScore = int.MinValue;

        foreach (Node node in GetTree().GetNodesInGroup("ants"))
        {
            if (node is not AntWorker ant)
            {
                continue;
            }

            int score = ant.IsCarryingSomethingUseful ? 0 : 1000;

            score += Mathf.RoundToInt(ant.Position.DistanceTo(nestPosition));

            if (score > bestScore)
            {
                bestScore = score;
                best = ant;
            }
        }

        return best;
    }

    // Where the colony lives, for the victim tie-break. Set by whoever owns the grid, so this class
    // does not have to know the grid exists.
    public Vector2 NestPosition { get => nestPosition; set => nestPosition = value; }

    private Vector2 nestPosition;

    // Whether the colony eats its dead or carries them out. The player's decision, alongside laying:
    // real ants do both, and which one is right depends on how hungry they are.
    public bool EatTheDeadPolicy { get; private set; }

    public void SetEatTheDead(bool enabled)
    {
        if (EatTheDeadPolicy == enabled)
        {
            return;
        }

        EatTheDeadPolicy = enabled;

        // The grid is what foragers ask whether a body is food, so it has to hear about this.
        if (Grid != null)
        {
            Grid.EatTheDead = enabled;
        }

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

            int hoursLeft = StarvingHoursBeforeLoss - starvingIntervalStreak + 1;

            // Warned before anyone dies, and counted down, so a famine is something you are told
            // about while you can still act on it rather than something you learn about from the
            // obituary. The first one also says where the choice about the dead is made.
            if (hoursLeft > 0)
            {
                RaiseAlert(starvingIntervalStreak == 1
                    ? $"The colony is going hungry. {hoursLeft} hours before they start to die."
                    : $"Still no food. {hoursLeft} hour{(hoursLeft == 1 ? "" : "s")} before they start to die.");
            }

            GD.Print($"The colony is starving! ({starvingIntervalStreak} hour(s) with no food)");

            if (starvingIntervalStreak >= StarvingHoursBeforeLoss)
            {
                StarveOne();
            }
        }

        EmitSignal(SignalName.ColonyChanged);
    }
}
