using Godot;

public partial class ColonyUI : CanvasLayer
{
    private Label colonyLabel;
    private Label foodLabel;
    private Label EggLabel;
    private Label capacityLabel;

    private ColonyManager colonyManager;

    public override void _Ready()
    {
        colonyLabel = GetNode<Label>("TopBar/Stats/ColonyLabel");
        foodLabel = GetNode<Label>("TopBar/Stats/FoodLabel");
        EggLabel = GetNode<Label>("TopBar/Stats/EggLabel");
        capacityLabel = GetNode<Label>("TopBar/Stats/CapacityLabel");

        colonyManager = GetNode<ColonyManager>("/root/Main/ColonyManager");

        // Listen for colony changes.
        colonyManager.ColonyChanged += UpdateUI;

        // Set the initial values.
        UpdateUI();
    }

    private void UpdateUI()
    {
        colonyLabel.Text = $"🐜 Ants: {colonyManager.Ants}";
        foodLabel.Text = $"🍖 Food: {colonyManager.Food}";
        EggLabel.Text = $"🥚 Eggs: {colonyManager.Egg}";
        capacityLabel.Text = $"🏠 Capacity: {colonyManager.Capacity}";
    }
}