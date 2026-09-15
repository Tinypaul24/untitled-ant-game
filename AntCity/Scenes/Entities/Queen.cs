using Godot;

public partial class Queen : Node2D
{
    private static readonly PackedScene EggScene = GD.Load<PackedScene>("res://AntCity/Scenes/Entities/Egg.tscn");
    private const float EggScatterRadius = 7f;
    private const int EggFoodCost = 2;

    // How often she lays while the player has laying switched on. Delta-based, so pause and the
    // speed controls govern it like everything else.
    private const double LayIntervalSeconds = 12.0;

    private ColonyManager colonyManager;
    private double layAccumulator;

    public override void _Ready()
    {
        GD.Print("The Queen has arrived!");

        colonyManager = GetNode<ColonyManager>("../ColonyManager");
    }


    public override void _Process(double delta)
    {
        if (!colonyManager.LayingEnabled)
        {
            return;
        }

        layAccumulator += delta;

        if (layAccumulator < LayIntervalSeconds)
        {
            return;
        }

        // Reset either way. A blocked attempt - no food, no room - should not bank up and then
        // burst out a clutch of eggs the moment the colony can afford one.
        layAccumulator = 0;
        LayEgg();
    }
    public void LayEgg()
    {
        if (!colonyManager.HasRoomForMorePopulation)
        {
            return;
        }

        if (!colonyManager.RemoveFood(EggFoodCost))
        {
            return;
        }

        colonyManager.AddEgg();

        Vector2 offset = new(
            (GD.Randf() * 2f - 1f) * EggScatterRadius,
            (GD.Randf() * 2f - 1f) * EggScatterRadius
        );

        Node2D egg = EggScene.Instantiate<Node2D>();
        egg.Position = Position + offset;
        GetParent().AddChild(egg);
    }
}
