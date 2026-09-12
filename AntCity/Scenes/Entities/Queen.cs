using Godot;

public partial class Queen : Node2D
{
	private static readonly PackedScene EggScene = GD.Load<PackedScene>("res://AntCity/Scenes/Entities/Egg.tscn");
	private const float EggScatterRadius = 16f;
	private const int EggFoodCost = 2;

	private ColonyManager colonyManager;

	public override void _Ready()
	{
		GD.Print("The Queen has arrived!");

		colonyManager = GetNode<ColonyManager>("/root/Main/ColonyManager");
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