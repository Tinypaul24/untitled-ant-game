using Godot;
using System.Collections.Generic;

public partial class SelectionManager : Node2D
{
    private const float DragThreshold = 6f;

    private static readonly Color BoxFillColor = new Color(1f, 1f, 1f, 0.15f);
    private static readonly Color BoxBorderColor = new Color(1f, 1f, 1f, 0.8f);

    [Export]
    public BuildManager BuildManager { get; set; }

    private GridManager gridManager;
    private readonly List<AntWorker> selectedAnts = new List<AntWorker>();

    private bool isPressed;
    private bool isDragging;
    private Vector2 dragStart;
    private Vector2 dragCurrent;

    public override void _Ready()
    {
        gridManager = GetNode<GridManager>("/root/Main/GridManager");

        // Area2D.InputEvent (used to click-select ants) only fires once this is on.
        GetViewport().PhysicsObjectPicking = true;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            HandleMouseButton(mouseButton);
        }
        else if (@event is InputEventMouseMotion && isPressed)
        {
            dragCurrent = GetGlobalMousePosition();

            if (!isDragging && (dragCurrent - dragStart).Length() > DragThreshold)
            {
                isDragging = true;
            }

            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (!isDragging)
        {
            return;
        }

        Vector2 topLeft = new Vector2(Mathf.Min(dragStart.X, dragCurrent.X), Mathf.Min(dragStart.Y, dragCurrent.Y));
        Vector2 size = new Vector2(Mathf.Abs(dragCurrent.X - dragStart.X), Mathf.Abs(dragCurrent.Y - dragStart.Y));
        Rect2 rect = new Rect2(topLeft, size);

        DrawRect(rect, BoxFillColor, filled: true);
        DrawRect(rect, BoxBorderColor, filled: false, width: 1f);
    }

    public void Select(AntWorker ant)
    {
        ClearSelection();
        AddToSelection(ant);
    }

    public void ToggleSelect(AntWorker ant)
    {
        if (selectedAnts.Contains(ant))
        {
            RemoveFromSelection(ant);
        }
        else
        {
            AddToSelection(ant);
        }
    }

    private void HandleMouseButton(InputEventMouseButton mouseButton)
    {
        // BuildManager owns input entirely while a building is being placed.
        if (BuildManager.IsPlacing)
        {
            return;
        }

        if (mouseButton.ButtonIndex == MouseButton.Left)
        {
            if (mouseButton.Pressed)
            {
                isPressed = true;
                dragStart = GetGlobalMousePosition();
                dragCurrent = dragStart;
                return;
            }

            isPressed = false;

            if (isDragging)
            {
                BoxSelect(dragStart, dragCurrent, mouseButton.ShiftPressed);
            }
            else
            {
                // A click that didn't land on an ant (those consume the event themselves) means empty ground.
                if (!mouseButton.ShiftPressed)
                {
                    ClearSelection();
                }
            }

            isDragging = false;
            QueueRedraw();
            return;
        }

        if (mouseButton.ButtonIndex == MouseButton.Right && mouseButton.Pressed)
        {
            IssueRightClickCommand(mouseButton);
        }
    }

    private void IssueRightClickCommand(InputEventMouseButton mouseButton)
    {
        if (selectedAnts.Count == 0)
        {
            return;
        }

        Vector2I cell = gridManager.WorldToCell(GetGlobalMousePosition());

        if (mouseButton.CtrlPressed && gridManager.IsTunnel(cell))
        {
            foreach (AntWorker ant in selectedAnts)
            {
                ant.CommandBuildStorage(cell);
            }
            return;
        }

        if (mouseButton.AltPressed && gridManager.IsTunnel(cell))
        {
            foreach (AntWorker ant in selectedAnts)
            {
                ant.CommandBuildNestChamber(cell);
            }
            return;
        }

        if (gridManager.IsTunnel(cell))
        {
            foreach (AntWorker ant in selectedAnts)
            {
                ant.CommandMove(cell);
            }
            return;
        }

        if (gridManager.IsFoodSource(cell))
        {
            foreach (AntWorker ant in selectedAnts)
            {
                ant.CommandForage(cell);
            }
            return;
        }

        if (!gridManager.CanDig(cell))
        {
            return;
        }

        foreach (AntWorker ant in selectedAnts)
        {
            if (mouseButton.DoubleClick)
            {
                ant.CommandDigDirect(cell);
            }
            else
            {
                ant.CommandDig(cell);
            }
        }
    }

    private void BoxSelect(Vector2 a, Vector2 b, bool additive)
    {
        if (!additive)
        {
            ClearSelection();
        }

        Vector2 topLeft = new Vector2(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y));
        Vector2 bottomRight = new Vector2(Mathf.Max(a.X, b.X), Mathf.Max(a.Y, b.Y));
        Rect2 rect = new Rect2(topLeft, bottomRight - topLeft);

        foreach (Node node in GetTree().GetNodesInGroup("ants"))
        {
            if (node is AntWorker ant && rect.HasPoint(ant.Position))
            {
                AddToSelection(ant);
            }
        }
    }

    private void AddToSelection(AntWorker ant)
    {
        if (selectedAnts.Contains(ant))
        {
            return;
        }

        selectedAnts.Add(ant);
        ant.SetSelected(true);
    }

    private void RemoveFromSelection(AntWorker ant)
    {
        selectedAnts.Remove(ant);
        ant.SetSelected(false);
    }

    private void ClearSelection()
    {
        foreach (AntWorker ant in selectedAnts)
        {
            ant.SetSelected(false);
        }

        selectedAnts.Clear();
    }
}
