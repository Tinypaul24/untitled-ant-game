using Godot;

public partial class ColonyUI : CanvasLayer
{
    private Label colonyLabel;
    private Label foodLabel;
    private Label EggLabel;
    private Label capacityLabel;
    private Button layEggButton;
    private Label clockLabel;

    private ColonyManager colonyManager;
    private Queen queen;
    private GameClock gameClock;

    public override void _Ready()
    {
        colonyLabel = GetNode<Label>("TopBar/Stats/ColonyLabel");
        foodLabel = GetNode<Label>("TopBar/Stats/FoodLabel");
        EggLabel = GetNode<Label>("TopBar/Stats/EggLabel");
        capacityLabel = GetNode<Label>("TopBar/Stats/CapacityLabel");
        layEggButton = GetNode<Button>("TopBar/Stats/LayEggButton");
        clockLabel = GetNode<Label>("ClockLabel");

        colonyManager = GetNode<ColonyManager>("/root/Main/ColonyManager");
        queen = GetNode<Queen>("/root/Main/Queen");
        gameClock = GetNode<GameClock>("/root/Main/GameClock");

        // Listen for colony changes.
        colonyManager.ColonyChanged += UpdateUI;

        // Lay an egg when the button is pressed.
        layEggButton.Pressed += queen.LayEgg;

        // Set the initial values.
        UpdateUI();
    }

    public override void _Process(double delta)
    {
        clockLabel.Text = gameClock.GetFormattedTime();
    }

    private void UpdateUI()
    {
        colonyLabel.Text = $"🐜 Ants: {colonyManager.Ants}";
        foodLabel.Text = $"🍖 Food: {colonyManager.Food}";
        EggLabel.Text = $"🥚 Eggs: {colonyManager.Egg}";
        capacityLabel.Text = $"🏠 Capacity: {colonyManager.Capacity}";
    }
}