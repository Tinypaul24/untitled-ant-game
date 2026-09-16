using Godot;

public partial class MaterialWorld : Node2D
{
    public MaterialSave CaptureState()
    {
        var save = new MaterialSave();

        foreach (var entry in chunks)
        {
            var chunk = new MaterialChunkSave { X = entry.Key.X, Y = entry.Key.Y };

            // Run-length encoded. A materialised chunk underground is overwhelmingly one material,
            // so this turns 4096 bytes into a handful of numbers; only the genuinely churned-up
            // chunks cost anything to store.
            byte[] cells = entry.Value.Cells;
            byte run = cells[0];
            int length = 1;

            for (int i = 1; i < cells.Length; i++)
            {
                if (cells[i] == run && length < int.MaxValue)
                {
                    length++;
                    continue;
                }

                chunk.Runs.Add(run);
                chunk.Runs.Add(length);

                run = cells[i];
                length = 1;
            }

            chunk.Runs.Add(run);
            chunk.Runs.Add(length);

            save.Chunks.Add(chunk);
        }

        foreach (var entry in lifetimes)
        {
            save.Lifetimes.AddCell(entry.Key, entry.Value);
        }

        // Rounded to whole degrees. Nothing in the simulation cares about fractions of one, and it
        // keeps the file readable.
        foreach (var entry in temperatures)
        {
            save.Temperatures.AddCell(entry.Key, Mathf.RoundToInt(entry.Value));
        }

        return save;
    }

    public void RestoreState(MaterialSave save)
    {
        chunks.Clear();
        awakeChunks.Clear();
        InvalidateChunkCache();
        lifetimes.Clear();
        temperatures.Clear();

        foreach (MaterialChunkSave saved in save.Chunks)
        {
            Vector2I coord = new Vector2I(saved.X, saved.Y);
            var chunk = new MaterialChunk(coord);
            int index = 0;

            for (int i = 0; i + 1 < saved.Runs.Count; i += 2)
            {
                byte material = (byte)saved.Runs[i];
                int length = saved.Runs[i + 1];

                for (int n = 0; n < length && index < chunk.Cells.Length; n++)
                {
                    chunk.Cells[index++] = material;
                }
            }

            // Everything gets one pass on load: settled matter proves it is still settled and goes
            // back to sleep, and anything that was mid-flight carries on falling.
            MarkChunkAllDirty(chunk);

            chunks[coord] = chunk;
        }

        foreach ((Vector2I cell, int remaining) in save.Lifetimes.ReadCellValues())
        {
            lifetimes[cell] = remaining;
        }

        foreach ((Vector2I cell, int degrees) in save.Temperatures.ReadCellValues())
        {
            temperatures[cell] = degrees;
        }
    }
}
