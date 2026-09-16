using Godot;
using System.Collections.Generic;

// Draws the material grid, one texture per chunk.
//
// Stage 1 drew a rectangle per cell, which is fine for a puddle and hopeless for a flooded cavern.
// A chunk is now a 64x64 image uploaded only when its contents actually changed, so a screen full of
// settled terrain costs a handful of textured quads and nothing else.
//
// The tilemap keeps drawing terrain; this only paints what the simulation adds on top of it - sand,
// liquids and fire. Painting solids here too was tried and taken back out: it replaces the textured
// tileset with flat colour, and hiding the seam it leaves at the edge of every materialised chunk
// means painting every chunk on screen whether anything ever disturbed it or not.
public partial class MaterialRenderer : Node2D
{
    [Export]
    public MaterialWorld World { get; set; }

    private readonly Dictionary<Vector2I, ImageTexture> textures = new();
    private readonly Dictionary<Vector2I, Image> images = new();

    private Camera2D camera;
    private int lastAwakeChunks = -1;

    public int UploadsLastFrame { get; private set; }

    public override void _Ready()
    {
        camera = GetNode<Camera2D>("../Camera2D");

        // Material cells are 4px blocks; smoothing them would turn crisp pixel terrain to mush.
        TextureFilter = TextureFilterEnum.Nearest;
    }

    public override void _Process(double delta)
    {
        int awake = World.AwakeChunkCount;

        // Redraw while anything is moving, plus the one frame after it all settles so the final
        // resting state gets painted. After that an idle screen costs nothing.
        if (awake > 0 || lastAwakeChunks != 0)
        {
            QueueRedraw();
        }

        lastAwakeChunks = awake;
    }

    public override void _Draw()
    {
        UploadsLastFrame = 0;

        Vector2 viewSize = GetViewportRect().Size / camera.Zoom;
        Vector2 topLeft = camera.GlobalPosition - viewSize / 2f;

        Vector2I firstChunk = MaterialWorld.CellToChunk(MaterialWorld.WorldToCell(topLeft));
        Vector2I lastChunk = MaterialWorld.CellToChunk(MaterialWorld.WorldToCell(topLeft + viewSize));

        float chunkPixels = MaterialChunk.Size * MaterialWorld.CellSize;

        for (int chunkY = firstChunk.Y; chunkY <= lastChunk.Y; chunkY++)
        {
            for (int chunkX = firstChunk.X; chunkX <= lastChunk.X; chunkX++)
            {
                Vector2I coord = new Vector2I(chunkX, chunkY);

                // Every chunk in view is painted, disturbed or not. Skipping the untouched ones is
                // what put a hard rectangular seam at the edge of every materialised chunk last
                // time: painted ground butting against tilemap ground. Materialising costs 4KB and
                // the chunk is born asleep, and at this resolution a 640x360 screen is about fifteen
                // chunks - so covering the whole view is cheap in a way it was not at 1px.
                MaterialChunk chunk = World.EnsureChunk(coord);

                ImageTexture texture = GetTexture(coord, chunk);
                Vector2 origin = MaterialWorld.CellToWorld(coord * MaterialChunk.Size);

                DrawTextureRect(texture, new Rect2(origin, new Vector2(chunkPixels, chunkPixels)), tile: false);
            }
        }
    }

    private ImageTexture GetTexture(Vector2I coord, MaterialChunk chunk)
    {
        if (!images.TryGetValue(coord, out Image image))
        {
            image = Image.CreateEmpty(MaterialChunk.Size, MaterialChunk.Size, false, Image.Format.Rgba8);
            images[coord] = image;

            chunk.TextureDirty = true;
        }

        if (chunk.TextureDirty || !textures.ContainsKey(coord))
        {
            Repaint(image, chunk, World);

            if (textures.TryGetValue(coord, out ImageTexture existing))
            {
                existing.Update(image);
            }
            else
            {
                textures[coord] = ImageTexture.CreateFromImage(image);
            }

            chunk.TextureDirty = false;
            UploadsLastFrame++;
        }

        return textures[coord];
    }

    // Public so the benchmark can time a repaint without needing a viewport to draw into.
    //
    // Writes straight into a byte buffer and hands the whole thing over in one call. The obvious
    // version of this used Image.SetPixel per cell, which is a marshalled engine call each time and
    // measured 0.71ms for one chunk - a screen of dirty chunks mid-flood cost more than the whole
    // frame budget. Filling the buffer ourselves is the same picture for about a hundredth of that.
    public static void Repaint(Image image, MaterialChunk chunk, MaterialWorld world)
    {
        byte[] pixels = Scratch;

        // Air stays transparent, and so does anything sitting on a tile the tilemap owns, so start
        // from all-zero and only write what we are actually responsible for.
        System.Array.Clear(pixels, 0, pixels.Length);

        byte[] cells = chunk.Cells;

        // Which tiles of this chunk the simulation may draw. Worked out per tile rather than per
        // cell - the answer is the same for all of a tile's cells - and it is what keeps a food
        // deposit or a finished nursery looking like itself instead of being painted over as soil.
        for (int tileY = 0; tileY < TilesPerChunkAxis; tileY++)
        {
            for (int tileX = 0; tileX < TilesPerChunkAxis; tileX++)
            {
                Vector2I tile = chunk.Coord * TilesPerChunkAxis + new Vector2I(tileX, tileY);
                OwnedTiles[tileY * TilesPerChunkAxis + tileX] = world.TileIsSimulationOwned(tile);
            }
        }

        // Where this chunk sits in the world. The noise below has to be keyed on world position:
        // hashing the chunk-local one gives every chunk in the world an identical speckle pattern,
        // which is invisible on a scattering of sand and obvious the moment a body of it is large.
        int originX = chunk.Coord.X * MaterialChunk.Size;
        int originY = chunk.Coord.Y * MaterialChunk.Size;

        for (int i = 0; i < cells.Length; i++)
        {
            byte id = cells[i];

            if (!Painted[id])
            {
                continue;
            }

            int localX = i & (MaterialChunk.Size - 1);
            int localY = i >> MaterialChunk.SizeShift;

            if (!OwnedTiles[(localY / MaterialWorld.CellsPerTileAxis) * TilesPerChunkAxis
                + localX / MaterialWorld.CellsPerTileAxis])
            {
                continue;
            }

            int worldX = originX + localX;
            int worldY = originY + localY;

            // Two octaves: a per-cell grain, plus a coarser blotch over four-cell blocks. One octave
            // alone reads as uniform static. Precomputed per material and shade combination, since
            // the colour only ever depends on those two things.
            int fine = (int)(Hash(worldX, worldY) % FineShades);
            int coarse = (int)(Hash(worldX >> 2, (worldY >> 2) + CoarseSalt) % CoarseShades);

            int source = ((id * CoarseShades + coarse) * FineShades + fine) * 4;
            int target = i * 4;

            pixels[target] = ShadeTable[source];
            pixels[target + 1] = ShadeTable[source + 1];
            pixels[target + 2] = ShadeTable[source + 2];
            pixels[target + 3] = ShadeTable[source + 3];
        }

        image.SetData(MaterialChunk.Size, MaterialChunk.Size, false, Image.Format.Rgba8, pixels);
    }

    // Grain levels per cell, and blotch levels per four-cell block.
    private const int FineShades = 7;
    private const int CoarseShades = 5;

    private const float FineShadeStep = 0.020f;
    private const float CoarseShadeStep = 0.028f;

    // Keeps the coarse noise from lining up with the fine noise it is multiplied against.
    private const int CoarseSalt = 7919;

    private const int TilesPerChunkAxis = MaterialChunk.Size / MaterialWorld.CellsPerTileAxis;

    private static readonly bool[] OwnedTiles = new bool[TilesPerChunkAxis * TilesPerChunkAxis];

    // One buffer reused by every repaint. Repainting is single-threaded and finishes before the next
    // one starts, so there is nothing to be gained from allocating 16KB per chunk per frame.
    private static readonly byte[] Scratch = new byte[MaterialChunk.Size * MaterialChunk.Size * 4];

    // Every material crossed with every shade combination, resolved to bytes once at startup.
    private static readonly byte[] ShadeTable = BuildShadeTable();

    // RenderedOverTerrain flattened to one array read, since the repaint loop asks it 4096 times a
    // chunk and the answer only ever depends on the material id.
    private static readonly bool[] Painted = BuildPaintedTable();

    private static bool[] BuildPaintedTable()
    {
        var table = new bool[byte.MaxValue + 1];

        for (int id = 0; id <= byte.MaxValue; id++)
        {
            table[id] = MaterialDatabase.Get((MaterialId)id).RenderedOverTerrain;
        }

        return table;
    }

    private static byte[] BuildShadeTable()
    {
        var table = new byte[(byte.MaxValue + 1) * CoarseShades * FineShades * 4];

        for (int id = 0; id <= byte.MaxValue; id++)
        {
            Color colour = MaterialDatabase.Get((MaterialId)id).Colour;

            for (int coarse = 0; coarse < CoarseShades; coarse++)
            {
                for (int fine = 0; fine < FineShades; fine++)
                {
                    float shade = 1f
                        + (fine - FineShades / 2) * FineShadeStep
                        + (coarse - CoarseShades / 2) * CoarseShadeStep;

                    int at = ((id * CoarseShades + coarse) * FineShades + fine) * 4;

                    table[at] = ToByte(colour.R * shade);
                    table[at + 1] = ToByte(colour.G * shade);
                    table[at + 2] = ToByte(colour.B * shade);
                    table[at + 3] = ToByte(colour.A);
                }
            }
        }

        return table;
    }

    private static byte ToByte(float component)
    {
        return (byte)Mathf.Clamp(component * 255f, 0f, 255f);
    }

    private static uint Hash(int x, int y)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;

            return h ^ (h >> 16);
        }
    }
}
