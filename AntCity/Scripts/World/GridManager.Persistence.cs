using Godot;

public partial class GridManager : Node2D
{
    public WorldSave CaptureState()
    {
        var save = new WorldSave { Seed = ActiveSeed };

        foreach (Vector2I chunk in generatedChunks)
        {
            save.Chunks.AddCell(chunk);
        }

        foreach (Vector2I cell in modifiedCells)
        {
            save.ModifiedCells.AddCell(cell, (int)grid[cell]);
        }

        foreach (var entry in foodRemaining)
        {
            save.FoodRemaining.AddCell(entry.Key, entry.Value);
        }

        foreach (var entry in grainsRemoved)
        {
            save.GrainsRemoved.AddCell(entry.Key, entry.Value);
        }

        return save;
    }

    public void RestoreState(WorldSave save)
    {
        grid.Clear();
        generatedChunks.Clear();
        foodRemaining.Clear();
        grainsRemoved.Clear();
        modifiedCells.Clear();
        Ground.Clear();

        InitializeNoise(save.Seed);
        NestCenterCell = SurfaceNestCell;

        foreach (Vector2I chunk in save.Chunks.ReadCells())
        {
            EnsureChunkGenerated(chunk);
        }

        foreach ((Vector2I cell, int type) in save.ModifiedCells.ReadCellValues())
        {
            SetTile(cell, (TileType)type);
        }

        foodRemaining.Clear();

        foreach ((Vector2I cell, int amount) in save.FoodRemaining.ReadCellValues())
        {
            foodRemaining[cell] = amount;
        }

        foreach ((Vector2I cell, int removed) in save.GrainsRemoved.ReadCellValues())
        {
            grainsRemoved[cell] = removed;
        }

        EmitSignal(SignalName.TerrainChanged);
    }
}
