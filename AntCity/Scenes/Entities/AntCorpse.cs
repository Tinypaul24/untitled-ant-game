using Godot;

// A worker who starved, lying where she fell.
//
// Real ants do two different things with their dead, and which one depends on how the colony is
// doing: in normal times they perform necrophoresis - carrying corpses out of the nest to a refuse
// pile - and in famine they eat them. Both are here, and which happens is the player's decision
// rather than the game's.
//
// The body registers itself as carrion on the grid, and everything else about being eaten goes
// through the ordinary forage pipeline. A forager treats a corpse exactly as she treats a seed
// cache; none of her code had to learn what a corpse is.
public partial class AntCorpse : Node2D
{
    // What a dead worker is worth, eaten.
    //
    // Less than she cost. An egg is two food and she ate upkeep the whole time she was a larva, so
    // eating the dead slows a famine without ever making starvation profitable - the spiral still
    // has to be broken by foraging.
    public const int FoodValue = 12;

    // Long enough that a body is a real opportunity rather than a flicker, short enough that a
    // colony which ignores its dead is not eventually walking through a museum of them.
    private const double DecaySeconds = 180.0;

    private static readonly Texture2D Texture = GD.Load<Texture2D>("res://AntCity/Textures/Red Ant Down.svg");

    // Drained of colour and turned over. At twelve pixels there is no room for a distinct corpse
    // sprite, but a worker lying on her back in muted brown reads as dead at a glance and never
    // reads as a live ant facing away.
    private static readonly Color DeadTint = new Color(0.45f, 0.38f, 0.34f);

    private GridManager grid;
    private Vector2I cell;
    private double age;

    public override void _Ready()
    {
        grid = GetNode<GridManager>("../GridManager");

        var sprite = new Sprite2D
        {
            Texture = Texture,
            FlipV = true,
            Modulate = DeadTint,
        };

        AddChild(sprite);

        cell = grid.WorldToCell(Position);
        grid.AddCarrion(cell, FoodValue);
    }

    public override void _Process(double delta)
    {
        // Picked clean, or carried off. Either way the grid is the authority on whether this body
        // still exists, so the node follows it rather than keeping its own copy.
        if (!grid.IsCarrion(cell))
        {
            QueueFree();
            return;
        }

        age += delta;

        if (age >= DecaySeconds)
        {
            grid.RemoveCarrion(cell);
            QueueFree();
        }
    }

    // Taken away by a worker rather than eaten - the body stops being anything the colony can use.
    public void CarriedOff()
    {
        grid.RemoveCarrion(cell);
        QueueFree();
    }
}
