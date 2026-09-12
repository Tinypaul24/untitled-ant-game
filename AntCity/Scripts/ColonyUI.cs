using Godot;

public partial class ColonyUI : CanvasLayer
{
    private Label colonyLabel;
    private Label foodLabel;
    private Label EggLabel;
    private Label capacityLabel;
    private Button layEggButton;
    private Label clockLabel;

    private Button buildButton;
    private Control buildTray;
    private Button nestingChamberButton;
    private Button granaryButton;
    private Button nurseryButton;

    private ColonyManager colonyManager;
    private Queen queen;
    private GameClock gameClock;
    private BuildManager buildManager;

    public override void _Ready()
    {
        colonyLabel = GetNode<Label>("TopBar/Stats/ColonyLabel");
        foodLabel = GetNode<Label>("TopBar/Stats/FoodLabel");
        EggLabel = GetNode<Label>("TopBar/Stats/EggLabel");
        capacityLabel = GetNode<Label>("TopBar/Stats/CapacityLabel");
        layEggButton = GetNode<Button>("BottomBar/Actions/LayEggButton");
        clockLabel = GetNode<Label>("ClockLabel");

        buildButton = GetNode<Button>("BottomBar/Actions/BuildButton");
        buildTray = GetNode<Control>("BuildTray");
        nestingChamberButton = GetNode<Button>("BuildTray/NestingChamberButton");
        granaryButton = GetNode<Button>("BuildTray/GranaryButton");
        nurseryButton = GetNode<Button>("BuildTray/NurseryButton");

        colonyManager = GetNode<ColonyManager>("/root/Main/ColonyManager");
        queen = GetNode<Queen>("/root/Main/Queen");
        gameClock = GetNode<GameClock>("/root/Main/GameClock");
        buildManager = GetNode<BuildManager>("/root/Main/BuildManager");

        // Listen for colony changes.
        colonyManager.ColonyChanged += UpdateUI;

        // Lay an egg when the button is pressed.
        layEggButton.Pressed += queen.LayEgg;

        // Toggle the build tray, and start placement when a building is chosen.
        buildButton.Pressed += () => buildTray.Visible = !buildTray.Visible;
        nestingChamberButton.Pressed += () => buildManager.BeginPlacement(BuildingType.NestingChamber);
        granaryButton.Pressed += () => buildManager.BeginPlacement(BuildingType.Granary);
        nurseryButton.Pressed += () => buildManager.BeginPlacement(BuildingType.Nursery);

        SetBuildingButtonLabel(nestingChamberButton, BuildingType.NestingChamber);
        SetBuildingButtonLabel(granaryButton, BuildingType.Granary);
        SetBuildingButtonLabel(nurseryButton, BuildingType.Nursery);

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
        foodLabel.Text = $"🍖 Food: {colonyManager.Food}/{colonyManager.FoodCapacity}";
        EggLabel.Text = $"🥚 Eggs: {colonyManager.Egg}";
        capacityLabel.Text = $"🏠 Capacity: {colonyManager.Capacity}";
    }

    private static void SetBuildingButtonLabel(Button button, BuildingType type)
    {
        BuildingDef def = BuildingDefs.All[type];
        button.Text = $"{def.Name} ({def.FoodCostPerCell}/cell)";
    }
}
