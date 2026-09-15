using Godot;
using System.Collections.Generic;

public partial class PauseMenu : CanvasLayer
{
    private const int MenuLayer = 10;
    private const int PanelWidth = 380;
    private const int ButtonSpacing = 8;
    private const int SaveListHeight = 260;

    private static readonly Theme GameTheme = GD.Load<Theme>("res://AntCity/UI/GameTheme.tres");
    private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.55f);
    private static readonly Color StatusColor = new Color(0.85f, 0.643f, 0.255f);
    private static readonly Color EmptyListColor = new Color(0.6f, 0.55f, 0.48f);

    private Control root;
    private Label statusLabel;
    private Button loadButton;

    private Control mainPage;
    private Control loadPage;
    private VBoxContainer saveList;
    private Label emptyListLabel;

    private SaveManager saveManager;
    private ColonyUI colonyUI;
    private BuildManager buildManager;

    private float resumeSpeed = 1f;
    private bool resumePaused;

    public bool IsOpen => root.Visible;

    public override void _Ready()
    {
        Node2D main = GetParent<Node2D>();

        saveManager = main.GetNode<SaveManager>("SaveManager");
        colonyUI = main.GetNode<ColonyUI>("UI");
        buildManager = main.GetNode<BuildManager>("BuildManager");

        Layer = MenuLayer;

        BuildUI();

        root.Visible = false;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo || key.Keycode != Key.Escape)
        {
            return;
        }

        if (!IsOpen)
        {
            Open();
        }
        else if (loadPage.Visible)
        {
            ShowMainPage();
        }
        else
        {
            Resume();
        }

        GetViewport().SetInputAsHandled();
    }

    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        buildManager.CancelPlacement();

        resumeSpeed = colonyUI.CurrentSpeed;
        resumePaused = colonyUI.IsPaused;
        Engine.TimeScale = 0f;

        statusLabel.Text = string.Empty;
        ShowMainPage();

        root.Visible = true;
    }

    public void Resume()
    {
        if (!IsOpen)
        {
            return;
        }

        root.Visible = false;
        colonyUI.RestoreSpeed(resumeSpeed, resumePaused);
    }

    private void ShowMainPage()
    {
        mainPage.Visible = true;
        loadPage.Visible = false;
        loadButton.Disabled = !saveManager.HasSave;
    }

    private void ShowLoadPage()
    {
        mainPage.Visible = false;
        loadPage.Visible = true;
        statusLabel.Text = string.Empty;

        RefreshSaveList();
    }

    private void RefreshSaveList()
    {
        foreach (Node child in saveList.GetChildren())
        {
            saveList.RemoveChild(child);
            child.QueueFree();
        }

        List<SaveSlot> slots = saveManager.ListSaves();

        emptyListLabel.Visible = slots.Count == 0;

        foreach (SaveSlot slot in slots)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 4);

            var pick = new Button
            {
                Text = slot.Describe(),
                Disabled = !slot.IsReadable,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = slot.Path,
            };
            pick.Pressed += () => OnSlotChosen(slot);
            row.AddChild(pick);

            var remove = new Button { Text = "✕", TooltipText = "Delete this save" };
            remove.Pressed += () => OnSlotDeleted(slot);
            row.AddChild(remove);

            saveList.AddChild(row);
        }
    }

    private void OnSlotChosen(SaveSlot slot)
    {
        bool loaded = saveManager.Load(slot.Path);

        statusLabel.Text = saveManager.LastMessage;

        if (!loaded)
        {
            RefreshSaveList();
            return;
        }

        resumeSpeed = colonyUI.CurrentSpeed;
        resumePaused = colonyUI.IsPaused;
        Engine.TimeScale = 0f;

        ShowMainPage();
    }

    private void OnSlotDeleted(SaveSlot slot)
    {
        saveManager.DeleteSave(slot.Path);

        statusLabel.Text = saveManager.LastMessage;

        RefreshSaveList();
    }

    private void OnSavePressed()
    {
        saveManager.Save();

        statusLabel.Text = saveManager.LastMessage;
        loadButton.Disabled = !saveManager.HasSave;
    }

    private void OnExitPressed()
    {
        Engine.TimeScale = 1f;
        GetTree().Quit();
    }

    private void BuildUI()
    {
        root = new Control
        {
            Theme = GameTheme,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        var dim = new ColorRect { Color = DimColor, MouseFilter = Control.MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(dim);

        var centered = new CenterContainer();
        centered.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(centered);

        var panel = new PanelContainer();
        centered.AddChild(panel);

        var column = new VBoxContainer { CustomMinimumSize = new Vector2(PanelWidth, 0) };
        column.AddThemeConstantOverride("separation", ButtonSpacing);
        panel.AddChild(column);

        var title = new Label
        {
            Text = "Paused",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        column.AddChild(title);

        column.AddChild(new HSeparator());

        column.AddChild(BuildMainPage());
        column.AddChild(BuildLoadPage());

        statusLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        statusLabel.AddThemeColorOverride("font_color", StatusColor);
        column.AddChild(statusLabel);
    }

    private Control BuildMainPage()
    {
        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", ButtonSpacing);

        AddButton(page, "Resume", Resume);
        AddButton(page, "Save Colony", OnSavePressed);
        loadButton = AddButton(page, "Load Colony", ShowLoadPage);
        AddButton(page, "Exit Game", OnExitPressed);

        mainPage = page;

        return page;
    }

    private Control BuildLoadPage()
    {
        var page = new VBoxContainer { Visible = false };
        page.AddThemeConstantOverride("separation", ButtonSpacing);

        var heading = new Label
        {
            Text = "Choose a save",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        page.AddChild(heading);

        emptyListLabel = new Label
        {
            Text = "No saved colonies yet.",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        emptyListLabel.AddThemeColorOverride("font_color", EmptyListColor);
        page.AddChild(emptyListLabel);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, SaveListHeight),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        page.AddChild(scroll);

        saveList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        saveList.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(saveList);

        page.AddChild(new HSeparator());
        AddButton(page, "Back", ShowMainPage);

        loadPage = page;

        return page;
    }

    private static Button AddButton(Node parent, string text, System.Action onPressed)
    {
        var button = new Button { Text = text };
        button.Pressed += onPressed;
        parent.AddChild(button);

        return button;
    }
}
