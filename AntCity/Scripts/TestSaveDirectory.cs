using System;
using System.Collections.Generic;
using System.IO;

// Test-only storage ownership. Never discovers player saves, and never deletes an unowned path.
public sealed class TestSaveDirectory : IDisposable
{
    private readonly SaveManager manager;
    private readonly string previousDirectory;
    private readonly HashSet<string> created = new(StringComparer.OrdinalIgnoreCase);
    public string DirectoryPath { get; }
    public string LastSavedPath { get; private set; }

    public TestSaveDirectory(SaveManager manager)
    {
        this.manager = manager;
        previousDirectory = manager.SavesDirectory;
        DirectoryPath = Path.Combine(Path.GetTempPath(), "ant-save-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        manager.SavesDirectory = DirectoryPath.Replace('\\', '/');
    }

    public string Save()
    {
        if (!manager.Save())
        {
            throw new InvalidOperationException("Required test save failed: " + manager.LastMessage);
        }

        return RecordSavedFile();
    }

    // A pause-menu button uses the same real manager, so track its successful write explicitly.
    public string RecordSavedFile()
    {
        string path = manager.LastSavedPath;
        if (string.IsNullOrEmpty(path) || !IsOwnedDirectory(path) || !File.Exists(path))
        {
            throw new InvalidOperationException("Test save did not create a file in its isolated directory.");
        }

        created.Add(path);
        LastSavedPath = path;
        return path;
    }

    public void Load()
    {
        if (LastSavedPath == null || !created.Contains(LastSavedPath) || !manager.Load(LastSavedPath))
        {
            throw new InvalidOperationException("Required test load failed: " + manager.LastMessage);
        }
    }

    public void Delete(string path)
    {
        if (!created.Contains(path) || !IsOwnedDirectory(path))
        {
            throw new InvalidOperationException("Refusing to delete a save not created by this test.");
        }

        if (!manager.DeleteSave(path))
        {
            throw new InvalidOperationException("Test save could not be deleted: " + manager.LastMessage);
        }
        created.Remove(path);
    }

    public void DeleteCreatedFiles()
    {
        foreach (string path in new List<string>(created))
        {
            Delete(path);
        }
    }

    private bool IsOwnedDirectory(string path) => string.Equals(
        Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(DirectoryPath),
        StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        try
        {
            DeleteCreatedFiles();
            // Nonrecursive: unexpected files are retained for diagnosis, never swept away.
            if (Directory.GetFileSystemEntries(DirectoryPath).Length == 0)
            {
                Directory.Delete(DirectoryPath);
            }
        }
        finally
        {
            manager.SavesDirectory = previousDirectory;
        }
    }
}
