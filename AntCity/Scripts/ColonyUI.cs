using Godot;

public partial class ColonyUI : CanvasLayer
{
    private const double ToastHoldSeconds = 2.5;
    private const double ToastFadeSeconds = 0.4;

    private Control populationBlock;
    private Label populationLabel;
    private ProgressBar populationBar;
    private Control foodBlock;
    private Label foodLabel;
    private ProgressBar foodBar;
    private Label eggLabel;
    private Button layEggButton;
    private Label clockLabel;

    private Button speed1xButton;
    private Button speed2xButton;
    private Button speed3xButton;
    private Button speed4xButton;

    private Button buildButton;
    private Control buildTray;
    private Button nestingChamberButton;
    private Button granaryButton;
    private Button nurseryButton;

    private PanelContainer toastPanel;
    private Label toastLabel;
    private Tween toastTween;

    private ColonyManager colonyManager;
    private Queen queen;
    private GameClock gameClock;
    private BuildManager buildManager;

    public override void _Ready()
    {
        populationBlock = GetNode<Control>("ThemeRoot/TopBar/Stats/PopulationBlock");
        populationLabel = GetNode<Label>("ThemeRoot/TopBar/Stats/PopulationBlock/ColonyLabel");
        populationBar = GetNode<ProgressBar>("ThemeRoot/TopBar/Stats/PopulationBlock/PopulationBar");
        foodBlock = GetNode<Control>("ThemeRoot/TopBar/Stats/FoodBlock");
        foodLabel = GetNode<Label>("ThemeRoot/TopBar/Stats/FoodBlock/FoodLabel");
        foodBar = GetNode<ProgressBar>("ThemeRoot/TopBar/Stats/FoodBlock/FoodBar");
        eggLabel = GetNode<Label>("ThemeRoot/TopBar/Stats/EggLabel");

        // Static explainer tooltips - the food block's is refreshed in UpdateUI() since it shows live rates.
        populationBlock.TooltipText = "Population capacity for ants, eggs, and larvae combined. Build Nesting Chambers to raise it.";
        eggLabel.TooltipText = "Eggs the Queen has laid. Each one hatches into a larva, which then matures into a worker ant.";
        layEggButton = GetNode<Button>("ThemeRoot/BottomBar/Actions/LayEggButton");
        clockLabel = GetNode<Label>("ThemeRoot/ClockLabel");

        speed1xButton = GetNode<Button>("ThemeRoot/SpeedPanel/SpeedButtons/Speed1xButton");
        speed2xButton = GetNode<Button>("ThemeRoot/SpeedPanel/SpeedButtons/Speed2xButton");
        speed3xButton = GetNode<Button>("ThemeRoot/SpeedPanel/SpeedButtons/Speed3xButton");
        speed4xButton = GetNode<Button>("ThemeRoot/SpeedPanel/SpeedButtons/Speed4xButton");

        buildButton = GetNode<Button>("ThemeRoot/BottomBar/Actions/BuildButton");
        buildTray = GetNode<Control>("ThemeRoot/BuildTray");
        nestingChamberButton = GetNode<Button>("ThemeRoot/BuildTray/NestingChamberButton");
        granaryButton = GetNode<Button>("ThemeRoot/BuildTray/GranaryButton");
        nurseryButton = GetNode<Button>("ThemeRoot/BuildTray/NurseryButton");

        toastPanel = GetNode<PanelContainer>("ThemeRoot/ToastPanel");
        toastLabel = GetNode<Label>("ThemeRoot/ToastPanel/ToastLabel");

        colonyManager = GetNode<ColonyManager>("/root/Main/ColonyManager");
        queen = GetNode<Queen>("/root/Main/Queen");
        gameClock = GetNode<GameClock>("/root/Main/GameClock");
        buildManager = GetNode<BuildManager>("/root/Main/BuildManager");

        // Listen for colony changes and noteworthy events.
        colonyManager.ColonyChanged += UpdateUI;
        colonyManager.Alert += ShowToast;

        // Lay an egg when the button is pressed.
        layEggButton.Pressed += queen.LayEgg;

        // Speed controls scale Engine.TimeScale directly, which every delta-based system (movement,
        // dig/forage/build timers, upkeep, the clock) already reads from - nothing else needs to know.
        speed1xButton.Pressed += () => Engine.TimeScale = 1f;
        speed2xButton.Pressed += () => Engine.TimeScale = 2f;
        speed3xButton.Pressed += () => Engine.TimeScale = 3f;
        speed4xButton.Pressed += () => Engine.TimeScale = 4f;

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
        populationLabel.Text = $"🐜 Population: {colonyManager.PopulationUsed}/{colonyManager.Capacity}";
        populationBar.MaxValue = colonyManager.Capacity;
        populationBar.Value = colonyManager.PopulationUsed;

        foodLabel.Text = $"🍖 Food: {colonyManager.Food}/{colonyManager.FoodCapacity}";
        foodBar.MaxValue = colonyManager.FoodCapacity;
        foodBar.Value = colonyManager.Food;
        foodBlock.TooltipText =
            "Food feeds every ant and larva each in-game hour, and pays the cost of laying eggs and building rooms.\n\n" +
            $"Generating: {colonyManager.FoodPerMinute:0.0} food/min (average)\n" +
            $"Upkeep: {colonyManager.UpkeepPerHour} food/hour ({colonyManager.Ants} ants, {colonyManager.LarvaCount} larvae)";

        eggLabel.Text = $"🥚 Eggs: {colonyManager.Egg}";
    }

    private void ShowToast(string message)
    {
        toastTween?.Kill();

        toastLabel.Text = message;
        toastPanel.Modulate = Colors.White;
        toastPanel.Visible = true;

        toastTween = CreateTween();
        toastTween.TweenInterval(ToastHoldSeconds);
        toastTween.TweenProperty(toastPanel, "modulate:a", 0f, ToastFadeSeconds);
        toastTween.TweenCallback(Callable.From(() => toastPanel.Visible = false));
    }

    private static void SetBuildingButtonLabel(Button button, BuildingType type)
    {
        BuildingDef def = BuildingDefs.All[type];
        button.Text = $"{def.Name} ({def.FoodCostPerCell}/cell)";
        button.TooltipText = $"{def.Description}\n\nCost: {def.FoodCostPerCell} food per cell.";
    }
}
