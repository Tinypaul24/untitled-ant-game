using Godot;
using System.Collections.Generic;

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
    private Button layingButton;
    private Button eatDeadButton;
    private Label clockLabel;

    private Button pauseButton;
    private Button speed1xButton;
    private Button speed2xButton;
    private Button speed3xButton;
    private Button speed4xButton;

    private float currentSpeed = 1f;

    private Button buildButton;
    private Control buildTray;
    private Button nestingChamberButton;
    private Button granaryButton;
    private Button nurseryButton;
    private Button fungusFarmButton;
    private Button royalChamberButton;

    private PanelContainer previewPanel;
    private Label previewLabel;

    private Button digButton;
    private Button colonyButton;
    private Control colonyPanel;
    private Label workDetail;
    private Label foragerLabel;
    private Label builderLabel;
    private Label larderDetail;
    private Label logDetail;

    // The last few alerts, newest first.
    //
    // ColonyManager.Alert is a colony-wide bus carrying fifteen kinds of message into a single
    // 2.5 second toast with no history, so "four hours before they start to die" was exactly as
    // forgettable as "stores are full" and both were gone before you looked up. A player who is
    // meant to plan needs to be able to read what just happened.
    private readonly List<string> alertLog = new();

    private const int AlertsKept = 6;

    private Control roomPanel;
    private Label roomTitle;
    private Label roomDetail;
    private Button demolishButton;

    private PanelContainer toastPanel;
    private Label toastLabel;
    private Tween toastTween;

    private ColonyManager colonyManager;
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
        layingButton = GetNode<Button>("ThemeRoot/BottomBar/Actions/LayingButton");
        eatDeadButton = GetNode<Button>("ThemeRoot/BottomBar/Actions/EatDeadButton");
        clockLabel = GetNode<Label>("ThemeRoot/ClockLabel");

        pauseButton = GetNode<Button>("ThemeRoot/SpeedPanel/SpeedButtons/PauseButton");
        speed1xButton = GetNode<Button>("ThemeRoot/SpeedPanel/SpeedButtons/Speed1xButton");
        speed2xButton = GetNode<Button>("ThemeRoot/SpeedPanel/SpeedButtons/Speed2xButton");
        speed3xButton = GetNode<Button>("ThemeRoot/SpeedPanel/SpeedButtons/Speed3xButton");
        speed4xButton = GetNode<Button>("ThemeRoot/SpeedPanel/SpeedButtons/Speed4xButton");

        buildButton = GetNode<Button>("ThemeRoot/BottomBar/Actions/BuildButton");
        buildTray = GetNode<Control>("ThemeRoot/BuildTray");
        nestingChamberButton = GetNode<Button>("ThemeRoot/BuildTray/NestingChamberButton");
        granaryButton = GetNode<Button>("ThemeRoot/BuildTray/GranaryButton");
        nurseryButton = GetNode<Button>("ThemeRoot/BuildTray/NurseryButton");
        fungusFarmButton = GetNode<Button>("ThemeRoot/BuildTray/FungusFarmButton");
        royalChamberButton = GetNode<Button>("ThemeRoot/BuildTray/RoyalChamberButton");

        previewPanel = GetNode<PanelContainer>("ThemeRoot/PreviewPanel");
        previewLabel = GetNode<Label>("ThemeRoot/PreviewPanel/PreviewLabel");

        digButton = GetNode<Button>("ThemeRoot/BottomBar/Actions/DigButton");
        colonyButton = GetNode<Button>("ThemeRoot/BottomBar/Actions/ColonyButton");
        colonyPanel = GetNode<Control>("ThemeRoot/ColonyPanel");
        workDetail = GetNode<Label>("ThemeRoot/ColonyPanel/ColonyBox/WorkDetail");
        foragerLabel = GetNode<Label>("ThemeRoot/ColonyPanel/ColonyBox/ForagerRow/ForagerLabel");
        builderLabel = GetNode<Label>("ThemeRoot/ColonyPanel/ColonyBox/BuilderRow/BuilderLabel");
        larderDetail = GetNode<Label>("ThemeRoot/ColonyPanel/ColonyBox/LarderDetail");
        logDetail = GetNode<Label>("ThemeRoot/ColonyPanel/ColonyBox/LogDetail");

        roomPanel = GetNode<Control>("ThemeRoot/RoomPanel");
        roomTitle = GetNode<Label>("ThemeRoot/RoomPanel/RoomBox/RoomTitle");
        roomDetail = GetNode<Label>("ThemeRoot/RoomPanel/RoomBox/RoomDetail");
        demolishButton = GetNode<Button>("ThemeRoot/RoomPanel/RoomBox/DemolishButton");

        toastPanel = GetNode<PanelContainer>("ThemeRoot/ToastPanel");
        toastLabel = GetNode<Label>("ThemeRoot/ToastPanel/ToastLabel");

        colonyManager = GetNode<ColonyManager>("../ColonyManager");
        gameClock = GetNode<GameClock>("../GameClock");
        buildManager = GetNode<BuildManager>("../BuildManager");

        // Listen for colony changes and noteworthy events.
        colonyManager.ColonyChanged += UpdateUI;
        colonyManager.Alert += ShowToast;

        // The player owns when the colony grows; the Queen just acts on the switch.
        layingButton.Toggled += colonyManager.SetLaying;

        // And what happens to the ones who do not make it. Real ants carry their dead out to a refuse
        // pile in normal times and eat them in famine, so which one this colony does is the
        // player's call rather than the game's.
        eatDeadButton.Toggled += colonyManager.SetEatTheDead;

        // Speed controls scale Engine.TimeScale directly, which every delta-based system (movement,
        // dig/forage/build timers, upkeep, the clock) already reads from - nothing else needs to know.
        speed1xButton.Pressed += () => SetSpeed(1f);
        speed2xButton.Pressed += () => SetSpeed(2f);
        speed3xButton.Pressed += () => SetSpeed(3f);
        speed4xButton.Pressed += () => SetSpeed(4f);

        // Pause freezes time at 0x and remembers whatever speed was active, so unpausing resumes there.
        pauseButton.Toggled += OnPauseToggled;

        // Toggle the build tray, and start placement when a building is chosen.
        // Marking and placing both swallow left clicks, so they cannot both be armed. Turning one
        // on turns the other off, and the button reflects it - the alternative is the player
        // wondering why the world has stopped responding, which this game has shipped once already.
        digButton.Toggled += pressed =>
        {
            if (pressed)
            {
                buildManager.BeginMarking();
                buildTray.Visible = false;
            }
            else
            {
                buildManager.CancelMarking();
            }
        };

        colonyButton.Toggled += pressed => colonyPanel.Visible = pressed;

        // The colony's one real allocation decision. Diggers are the remainder, so there is nothing
        // to set for them - and nothing the player can do to leave a worker with no trade at all.
        GetNode<Button>("ThemeRoot/ColonyPanel/ColonyBox/ForagerRow/ForagerFewer").Pressed +=
            () => colonyManager.SetForagerQuota(colonyManager.ForagerQuota - 1);
        GetNode<Button>("ThemeRoot/ColonyPanel/ColonyBox/ForagerRow/ForagerMore").Pressed +=
            () => colonyManager.SetForagerQuota(colonyManager.ForagerQuota + 1);
        GetNode<Button>("ThemeRoot/ColonyPanel/ColonyBox/BuilderRow/BuilderFewer").Pressed +=
            () => colonyManager.SetBuilderQuota(colonyManager.BuilderQuota - 1);
        GetNode<Button>("ThemeRoot/ColonyPanel/ColonyBox/BuilderRow/BuilderMore").Pressed +=
            () => colonyManager.SetBuilderQuota(colonyManager.BuilderQuota + 1);

        buildButton.Pressed += ToggleBuildTray;
        nestingChamberButton.Pressed += () => buildManager.BeginPlacement(BuildingType.NestingChamber);
        granaryButton.Pressed += () => buildManager.BeginPlacement(BuildingType.Granary);
        nurseryButton.Pressed += () => buildManager.BeginPlacement(BuildingType.Nursery);
        fungusFarmButton.Pressed += () => buildManager.BeginPlacement(BuildingType.FungusFarm);
        royalChamberButton.Pressed += () => buildManager.BeginPlacement(BuildingType.RoyalChamber);

        SetBuildingButtonLabel(nestingChamberButton, BuildingType.NestingChamber);
        SetBuildingButtonLabel(granaryButton, BuildingType.Granary);
        SetBuildingButtonLabel(nurseryButton, BuildingType.Nursery);
        SetBuildingButtonLabel(fungusFarmButton, BuildingType.FungusFarm);
        SetBuildingButtonLabel(royalChamberButton, BuildingType.RoyalChamber);

        buildManager.RoomSelected += ShowRoom;
        buildManager.PreviewChanged += ShowPreviewReason;
        demolishButton.Pressed += () => buildManager.Demolish(buildManager.Selected);

        // Set the initial values.
        UpdateUI();
    }

    public override void _Process(double delta)
    {
        clockLabel.Text = gameClock.GetFormattedTime();

        // The inspector used to be a snapshot taken the instant you clicked. Select a room mid-dig
        // and it would still read "40%, 6 cells to go" long after the ants had finished, furnished
        // and activated it - the only way to see the truth was to deselect and click again.
        if (roomPanel.Visible && GodotObject.IsInstanceValid(buildManager.Selected))
        {
            ShowRoom(buildManager.Selected);
        }

        if (colonyPanel.Visible)
        {
            RefreshColonyPanel();
        }
    }

    // What the colony is doing, and whether it can keep doing it.
    //
    // Every number here already existed and was public; the only thing reading them was the
    // headless probe. A game meant to be played by deciding things has to say what there is to
    // decide about.
    private void RefreshColonyPanel()
    {
        var doing = new Dictionary<string, int>();
        int ants = 0;

        foreach (Node node in GetTree().GetNodesInGroup("ants"))
        {
            if (node is not AntWorker worker)
            {
                continue;
            }

            ants++;
            doing.TryGetValue(worker.DebugState, out int count);
            doing[worker.DebugState] = count + 1;
        }

        var work = new List<string>();

        foreach (KeyValuePair<string, int> entry in doing)
        {
            work.Add($"{entry.Value} {entry.Key}");
        }

        work.Sort();
        workDetail.Text = ants == 0 ? "nobody yet" : string.Join("\n", work);

        foragerLabel.Text = $"Foragers {colonyManager.ForagerQuota}";
        builderLabel.Text = $"Builders {colonyManager.BuilderQuota}";

        // Income is the lifetime average, and an in-game hour is sixty seconds, so food per minute
        // and food per hour are the same number - which is the only reason this arithmetic is
        // honest without a second accumulator.
        double income = colonyManager.FoodPerMinute;
        double drain = colonyManager.UpkeepPerHour - colonyManager.FoodPerHourFarmed - income;

        string outlook = drain <= 0.05
            ? "feeding itself"
            : $"{colonyManager.Food / drain:0} hours of stores left";

        larderDetail.Text =
            $"in  {income:0.0}/hr\n" +
            $"out {colonyManager.UpkeepPerHour}/hr for {colonyManager.Ants} ants, {colonyManager.LarvaCount} larvae\n" +
            $"{outlook}";

        logDetail.Text = alertLog.Count == 0 ? "nothing yet" : string.Join("\n", alertLog);
    }


    // Hiding the tray has to disarm the placement it started.
    //
    // It used to only flip the tray's visibility, so pendingType survived - and BuildManager marks
    // every left click handled while it is placing. The player could no longer select an ant or a
    // room, with nothing on screen to say why, and the only way out was a right-click they had no
    // reason to try.
    private void ToggleBuildTray()
    {
        buildTray.Visible = !buildTray.Visible;

        if (buildTray.Visible)
        {
            // Opening the build tray disarms the dig tool, for the same reason the dig tool closes
            // the tray: two tools that both eat left clicks, one cursor.
            digButton.SetPressedNoSignal(false);
            buildManager.CancelMarking();
        }
        else
        {
            buildManager.CancelPlacement();
        }
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
            $"Upkeep: {colonyManager.UpkeepPerHour} food/hour ({colonyManager.Ants} ants, {colonyManager.LarvaCount} larvae)\n" +
            $"Farmed: {colonyManager.FoodPerHourFarmed} food/hour, net {colonyManager.NetFoodPerHour:+#;-#;0}/hour";

        eggLabel.Text = $"🥚 Eggs: {colonyManager.Egg}";

        // Reflect the switch, including after a load restores it.
        layingButton.SetPressedNoSignal(colonyManager.LayingEnabled);
        layingButton.Text = colonyManager.LayingEnabled ? "🥚 Laying: on" : "🥚 Laying: off";

        eatDeadButton.SetPressedNoSignal(colonyManager.EatTheDeadPolicy);
        eatDeadButton.Text = colonyManager.EatTheDeadPolicy ? "☠ Eat dead: on" : "☠ Eat dead: off";
    }

    public float CurrentSpeed => currentSpeed;

    public bool IsPaused => pauseButton.ButtonPressed;

    public void RestoreSpeed(float speed, bool paused)
    {
        currentSpeed = speed;
        pauseButton.SetPressedNoSignal(paused);
        pauseButton.Text = paused ? "▶" : "⏸";
        Engine.TimeScale = paused ? 0f : speed;

        // The buttons too. Loading a save taken at 3x ran the game at 3x with the HUD showing 1x lit.
        speed1xButton.SetPressedNoSignal(Mathf.IsEqualApprox(speed, 1f));
        speed2xButton.SetPressedNoSignal(Mathf.IsEqualApprox(speed, 2f));
        speed3xButton.SetPressedNoSignal(Mathf.IsEqualApprox(speed, 3f));
        speed4xButton.SetPressedNoSignal(Mathf.IsEqualApprox(speed, 4f));
    }

    private void SetSpeed(float scale)
    {
        currentSpeed = scale;

        // Clicking a speed while paused should also resume - avoid re-entering OnPauseToggled to do it.
        pauseButton.SetPressedNoSignal(false);
        pauseButton.Text = "⏸";

        Engine.TimeScale = scale;
    }

    private void OnPauseToggled(bool paused)
    {
        pauseButton.Text = paused ? "▶" : "⏸";
        Engine.TimeScale = paused ? 0f : currentSpeed;
    }

    private void ShowToast(string message)
    {
        alertLog.Insert(0, message);

        if (alertLog.Count > AlertsKept)
        {
            alertLog.RemoveAt(alertLog.Count - 1);
        }

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

        // The effect, not just the price. A tooltip that states what a room costs and not what it
        // does leaves the player choosing between rooms on price alone, which is the one piece of
        // information that cannot tell them which room they need.
        button.TooltipText =
            $"{def.Description}\n\n" +
            $"Effect: {def.EffectSummary}\n" +
            $"Cost: {def.FoodCostPerCell} food per cell";
    }

    // Why the placement under the cursor would be refused, while there is still time to move it.
    //
    // The reason already existed - IsFootprintValid has always produced one - but it was discarded
    // during the drag and only surfaced as a toast once the placement had already failed. A player
    // learned the rules by breaking them and being told afterwards.
    private void ShowPreviewReason(string reason, bool valid)
    {
        if (!buildManager.IsPlacing)
        {
            previewPanel.Visible = false;
            return;
        }

        previewPanel.Visible = true;
        previewLabel.Text = valid ? "Drag out a chamber" : reason;
        previewLabel.Modulate = valid ? Colors.White : new Color(1f, 0.6f, 0.5f);
    }

    // The room inspector. Shows what the selected room is, how far along it is, and what it is
    // doing for the colony - and lets the player take it back down again.
    private void ShowRoom(Room room)
    {
        roomPanel.Visible = room != null;

        if (room == null)
        {
            return;
        }

        BuildingDef def = BuildingDefs.All[room.Type];
        int cells = room.CellCount;

        roomTitle.Text = $"{def.Name}  {cells} cell{(cells == 1 ? "" : "s")}";

        string progress = room.State switch
        {
            Room.RoomState.Excavating => $"Being dug - {room.DugFraction * 100f:0}% ({room.PendingDigCells.Count} cells to go)",
            Room.RoomState.Furnishing => "Dug out, waiting on a worker to furnish it",
            _ => $"Working: {EffectOf(room.Type, cells)}",
        };

        roomDetail.Text = $"{progress}\n{def.EffectSummary}";
        roomDetail.TooltipText = def.Description;

        int refund = Mathf.FloorToInt(def.FoodCostPerCell * cells * 0.5f);
        demolishButton.Text = $"Pull down (+{refund})";
    }

    // What this particular room contributes, in whole numbers the player can check against the bars.
    private static string EffectOf(BuildingType type, int cells)
    {
        BuildingDef def = BuildingDefs.All[type];
        int whole = Mathf.RoundToInt(def.EffectPerCell * cells);

        return type switch
        {
            BuildingType.NestingChamber => $"+{whole} population capacity",
            BuildingType.Granary => $"+{whole} food storage",
            BuildingType.FungusFarm => $"+{whole} food per hour",
            BuildingType.Nursery => $"{(1f - Mathf.Pow(1f - def.EffectPerCell, cells)) * 100f:0}% faster hatching",
            BuildingType.RoyalChamber => $"{(Mathf.Pow(1f + def.EffectPerCell, cells) - 1f) * 100f:0}% faster laying",
            _ => def.EffectSummary,
        };
    }
}
