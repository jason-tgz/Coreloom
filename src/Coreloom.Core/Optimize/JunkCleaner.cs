namespace Coreloom.Core.Optimize;

public enum JunkKind
{
    /// <summary>Files in known junk folders.</summary>
    Files,

    /// <summary>The Recycle Bin (measured and emptied by Windows).</summary>
    RecycleBin,
}

/// <summary>
/// A folder holding junk. Only files under <see cref="Root"/> matching <see cref="Pattern"/> are touched, links and
/// junctions are never followed, and with <see cref="MinAge"/> only files nobody has written to for that long go.
/// </summary>
public sealed record JunkLocation(string Root, string Pattern = "*", bool Recursive = true, TimeSpan? MinAge = null)
{
    /// <summary>Empty folders left behind are removed too (only with <see cref="MinAge"/>, so folders apps just made stay).</summary>
    public bool RemoveEmptyFolders => MinAge is not null;
}

/// <summary>A kind of junk the user can choose to clean ("Temporary files", "Browser caches"…).</summary>
public sealed record JunkCategory(string Id, string Name, string Description, string IconKey, IReadOnlyList<JunkLocation> Locations, bool SelectedByDefault, JunkKind Kind = JunkKind.Files)
{
    /// <summary>Shown under the name when it isn't selected by default (why to think twice).</summary>
    public string? Caution { get; init; }
}

public readonly record struct JunkSize(long Bytes, int Files)
{
    public static JunkSize operator +(JunkSize a, JunkSize b) => new(a.Bytes + b.Bytes, a.Files + b.Files);
}

/// <summary>What cleaning did: bytes freed, files deleted, files skipped because they were in use or protected.</summary>
public readonly record struct CleanResult(long FreedBytes, int Deleted, int Skipped)
{
    public static CleanResult operator +(CleanResult a, CleanResult b) => new(a.FreedBytes + b.FreedBytes, a.Deleted + b.Deleted, a.Skipped + b.Skipped);
}

/// <summary>Measures and deletes junk files, safely: see <see cref="JunkLocation"/>.</summary>
public static class JunkCleaner
{
    private static readonly EnumerationOptions Shallow = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    public static JunkSize Measure(JunkCategory category, CancellationToken cancellationToken = default, DateTime? now = null)
    {
        var total = default(JunkSize);
        var at = now ?? DateTime.UtcNow;
        foreach (var location in category.Locations)
        {
            foreach (var file in Files(location, at, cancellationToken))
            {
                total += new JunkSize(file.Length, 1);
            }
        }

        return total;
    }

    public static CleanResult Clean(JunkCategory category, CancellationToken cancellationToken = default, DateTime? now = null)
    {
        var result = default(CleanResult);
        var at = now ?? DateTime.UtcNow;
        foreach (var location in category.Locations)
        {
            foreach (var file in Files(location, at, cancellationToken).ToList())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (file.Attributes.HasFlag(FileAttributes.ReadOnly))
                    {
                        result += new CleanResult(0, 0, 1); // someone protected it on purpose
                        continue;
                    }

                    long length = file.Length;
                    file.Delete();
                    result += new CleanResult(length, 1, 0);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    result += new CleanResult(0, 0, 1); // in use
                }
            }

            if (location.RemoveEmptyFolders)
            {
                RemoveEmptyFolders(location, at);
            }
        }

        return result;
    }

    /// <summary>The files a location would clean right now.</summary>
    public static IEnumerable<FileInfo> Files(JunkLocation location, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        DirectoryInfo root;
        try
        {
            root = new DirectoryInfo(location.Root);
            if (!root.Exists || root.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                yield break; // never clean through a link: it could lead anywhere
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            yield break;
        }

        string rootPath = Path.TrimEndingDirectorySeparator(root.FullName) + Path.DirectorySeparatorChar;
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = pending.Pop();
            FileSystemInfo[] entries;
            try
            {
                entries = folder.GetFileSystemInfos(location.Recursive ? "*" : location.Pattern, Shallow);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue; // links, junctions, cloud placeholders
                }

                if (entry is DirectoryInfo directory)
                {
                    if (location.Recursive)
                    {
                        pending.Push(directory);
                    }

                    continue;
                }

                if (entry is not FileInfo file
                    || !file.FullName.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)
                    || (location.Recursive && location.Pattern != "*" && !FileSystemName.MatchesSimpleExpression(location.Pattern, file.Name))
                    || (location.MinAge is { } age && utcNow - file.LastWriteTimeUtc < age))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static void RemoveEmptyFolders(JunkLocation location, DateTime utcNow)
    {
        var root = new DirectoryInfo(location.Root);
        if (!root.Exists || root.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return;
        }

        // Deepest first, so a folder whose subfolders are all empty goes too. The root itself always stays.
        var folders = new List<DirectoryInfo>();
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            DirectoryInfo[] children;
            try
            {
                children = folder.GetDirectories("*", Shallow);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children.Where(c => !c.Attributes.HasFlag(FileAttributes.ReparsePoint)))
            {
                folders.Add(child);
                pending.Push(child);
            }
        }

        foreach (var folder in folders.OrderByDescending(f => f.FullName.Length))
        {
            try
            {
                folder.Refresh();
                if (folder.Exists && utcNow - folder.LastWriteTimeUtc >= location.MinAge!.Value && !folder.EnumerateFileSystemInfos().Any())
                {
                    folder.Delete();
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // in use: stays
            }
        }
    }
}

/// <summary>Matches simple file name patterns ("*.tmp", "thumbcache_*.db") the way Windows does, ignoring case.</summary>
internal static class FileSystemName
{
    public static bool MatchesSimpleExpression(string pattern, string name) =>
        System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true);
}
