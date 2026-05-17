using System.Security.Cryptography;

namespace CodexAccountSwitcher.Core;

public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    void CopyFile(string sourcePath, string destinationPath, bool overwrite);
    void DeleteFile(string path);
    void DeleteDirectory(string path, bool recursive);
    void WriteAllBytesAtomic(string path, byte[] bytes);
    byte[] ReadAllBytes(string path);
    void WriteAllTextAtomic(string path, string text);
    string ReadAllText(string path);
    string ComputeSha256(string path);
    IReadOnlyList<FileInventoryItem> EnumerateInventory(string rootPath);
    IReadOnlyList<string> EnumerateBackupDirectories(string backupsDirectory);
}

public sealed class RealFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite)
    {
        var parent = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.Copy(sourcePath, destinationPath, overwrite);
    }

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void DeleteDirectory(string path, bool recursive)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive);
        }
    }

    public void WriteAllBytesAtomic(string path, byte[] bytes)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllBytes(tempPath, bytes);
        ReplaceOrMove(tempPath, path);
    }

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public void WriteAllTextAtomic(string path, string text)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(tempPath, text);
        ReplaceOrMove(tempPath, path);
    }

    public string ReadAllText(string path) => File.ReadAllText(path);

    public string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public IReadOnlyList<FileInventoryItem> EnumerateInventory(string rootPath)
    {
        var root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root))
        {
            return [];
        }

        var items = new List<FileInventoryItem>();
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            if (ShouldSkip(directory))
            {
                continue;
            }

            var info = new DirectoryInfo(directory);
            items.Add(new FileInventoryItem(
                Path.GetRelativePath(root, directory),
                "directory",
                InventoryClassifier.Classify(Path.GetRelativePath(root, directory), true),
                null,
                info.LastWriteTimeUtc,
                null));
        }

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (ShouldSkip(file))
            {
                continue;
            }

            var info = new FileInfo(file);
            string? hash = null;
            try
            {
                hash = ComputeSha256(file);
            }
            catch (IOException)
            {
                hash = "locked";
            }

            items.Add(new FileInventoryItem(
                Path.GetRelativePath(root, file),
                "file",
                InventoryClassifier.Classify(Path.GetRelativePath(root, file), false),
                info.Length,
                info.LastWriteTimeUtc,
                hash));
        }

        return items.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<string> EnumerateBackupDirectories(string backupsDirectory)
    {
        if (!Directory.Exists(backupsDirectory))
        {
            return [];
        }

        return Directory.EnumerateDirectories(backupsDirectory)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void ReplaceOrMove(string tempPath, string targetPath)
    {
        if (File.Exists(targetPath))
        {
            File.Replace(tempPath, targetPath, null);
        }
        else
        {
            File.Move(tempPath, targetPath);
        }
    }

    private static bool ShouldSkip(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals("_account_profiles", StringComparison.OrdinalIgnoreCase)
            || name.Equals("_account_switcher_backups", StringComparison.OrdinalIgnoreCase);
    }
}
