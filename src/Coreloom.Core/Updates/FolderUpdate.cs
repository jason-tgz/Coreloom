using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Coreloom.Core.Diagnostics;

namespace Coreloom.Core.Updates;

/// <summary>Outcome of <see cref="FolderUpdate.Apply"/>.</summary>
public sealed record FolderUpdateResult(bool Succeeded, int FilesCopied, string? Error);

/// <summary>
/// Updates a copy of Coreloom that wasn't installed with the installer (a portable copy, or any unzipped copy) from the
/// release's portable zip. Running files are never overwritten, so it happens in three steps:
/// <list type="number">
/// <item><b>Stage</b> (the running version, just before it exits): the verified zip is unpacked into
/// <c>.coreloom-update\new</c> inside the app folder. That folder has the same permissions as the app itself.</item>
/// <item><b>Apply</b> (the new version's <c>Coreloom.exe</c>, started from that folder with <c>--apply-update</c> once
/// the old one has exited): each file in the app folder that the update replaces is moved to
/// <c>.coreloom-update\backup</c> and the new file is copied in. If anything fails, every change is undone and the old
/// version starts again with <c>--update-failed</c>. The new version starts with <c>--updated</c> otherwise.</item>
/// <item><b>Clean up</b> (the next normal start): <c>.coreloom-update</c> is deleted.</item>
/// </list>
/// What belongs to the copy rather than the version is never touched: the <c>Data</c> folder (settings and logs of a
/// portable copy) and <c>portable.txt</c> (so an unzipped non-portable copy doesn't turn portable, or the reverse).
/// Files of the old version that the new one no longer ships are left in place; they are not loaded.
/// </summary>
/// <remarks>
/// <c>--apply-update</c> is a contract between versions: the running version starts the <em>next</em> version's
/// executable with it, so every later version must keep accepting it (see <see cref="HelperArguments"/>).
/// </remarks>
public static class FolderUpdate
{
    public const string WorkFolderName = ".coreloom-update";
    public const string NewFolderName = "new";
    public const string BackupFolderName = "backup";
    public const string AppExecutable = "Coreloom.exe";
    public const string AppAssembly = "Coreloom.dll";
    public const string DataFolderName = "Data";

    /// <summary>Command-line switch that runs a staged executable as the update helper.</summary>
    public const string ApplySwitch = "--apply-update";

    /// <summary>Background updates: no splash while the files are copied.</summary>
    public const string QuietSwitch = "--quiet";

    /// <summary>A release is ~150 MB unpacked; anything far bigger is not a Coreloom release.</summary>
    public const long MaxUnpackedBytes = 2L * 1024 * 1024 * 1024;

    public const int MaxEntries = 20_000;

    public static string WorkDirectory(string appDirectory) => Path.Combine(appDirectory, WorkFolderName);

    public static string StagingDirectory(string appDirectory) => Path.Combine(WorkDirectory(appDirectory), NewFolderName);

    public static string BackupDirectory(string appDirectory) => Path.Combine(WorkDirectory(appDirectory), BackupFolderName);

    /// <summary>The staged executable that applies the update (<c>.coreloom-update\new\Coreloom.exe</c>).</summary>
    public static string HelperPath(string appDirectory) => Path.Combine(StagingDirectory(appDirectory), AppExecutable);

    /// <summary>
    /// Arguments for the staged executable: <c>--apply-update --wait-for &lt;pid&gt;</c>, plus <c>--minimized</c> to
    /// start the new version in the notification area and <c>--quiet</c> to copy without a splash.
    /// </summary>
    public static string HelperArguments(int processId, UpdateInstallOptions options) =>
        $"{ApplySwitch} --wait-for {processId.ToString(CultureInfo.InvariantCulture)}"
        + (options.RelaunchMinimized ? " --minimized" : string.Empty)
        + (options.ShowProgress ? string.Empty : " " + QuietSwitch);

    /// <summary>
    /// True when <paramref name="baseDirectory"/> is a staging folder (<c>&lt;app&gt;\.coreloom-update\new</c>) next to an
    /// existing Coreloom; <paramref name="appDirectory"/> is then the folder to update. The helper only ever updates the
    /// folder it was unpacked into, so it needs no path on its command line.
    /// </summary>
    public static bool TryGetAppDirectoryFromStagingLocation(string baseDirectory, out string appDirectory)
    {
        appDirectory = string.Empty;
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return false;
        }

        var staging = new DirectoryInfo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory)));
        if (!staging.Name.Equals(NewFolderName, StringComparison.OrdinalIgnoreCase)
            || staging.Parent is not { } work || !work.Name.Equals(WorkFolderName, StringComparison.OrdinalIgnoreCase)
            || work.Parent is not { } app)
        {
            return false;
        }

        appDirectory = app.FullName;
        return File.Exists(Path.Combine(appDirectory, AppExecutable));
    }

    /// <summary>
    /// A short, stable id for a copy's folder (used to keep each copy's trusted hash apart in the registry).
    /// </summary>
    public static string CopyId(string appDirectory)
    {
        string normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDirectory)).ToUpperInvariant();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..16];
    }

    // ---- Stage ------------------------------------------------------------------------------------------

    /// <summary>
    /// Unpacks the (already verified) portable zip into the staging folder, replacing anything a previous attempt left.
    /// The zip may hold the app at its root or inside one top-level folder. Throws <see cref="UpdateException"/> for a
    /// zip that isn't a Coreloom release or tries to write outside the staging folder; nothing is left behind then.
    /// Returns the staged files as paths relative to the app folder.
    /// </summary>
    public static IReadOnlyList<string> Stage(Stream zip, string appDirectory)
    {
        ArgumentNullException.ThrowIfNull(zip);
        ArgumentException.ThrowIfNullOrWhiteSpace(appDirectory);
        if (!Cleanup(appDirectory))
        {
            throw new UpdateException("An earlier update's files are still in use, so this update can't be prepared yet. Try again in a moment.");
        }

        string staging = StagingDirectory(appDirectory);
        try
        {
            CreateRealDirectory(WorkDirectory(appDirectory));
            TryHide(WorkDirectory(appDirectory));
            CreateRealDirectory(staging);
            var files = Unpack(zip, staging);
            Log.Info("Updates", $"Unpacked {files.Count} files for the update.");
            return files;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or UpdateException)
        {
            Cleanup(appDirectory);
            if (ex is UpdateException)
            {
                throw;
            }

            throw new UpdateException(ex is InvalidDataException
                ? "The downloaded update isn't a valid zip file."
                : $"The update couldn't be unpacked next to Coreloom: {ex.Message}", ex);
        }
    }

    private static List<string> Unpack(Stream zip, string staging)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > MaxEntries)
        {
            throw new UpdateException("The downloaded update has far more files than a Coreloom release.");
        }

        string root = FindRoot(archive.Entries);
        string stagingRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(staging)) + Path.DirectorySeparatorChar;
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            string name = entry.FullName.Replace('\\', '/');
            if (!name.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                continue; // outside the folder that holds the app (e.g. a readme next to it)
            }

            string relative = name[root.Length..];
            if (relative.Length == 0 || relative.EndsWith('/'))
            {
                continue; // a folder entry
            }

            if (!IsSafeRelativePath(relative))
            {
                throw new UpdateException($"The downloaded update contains an unexpected path ({entry.FullName}).");
            }

            string local = relative.Replace('/', Path.DirectorySeparatorChar);
            string destination = Path.GetFullPath(Path.Combine(staging, local));
            if (!destination.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase) || !seen.Add(local))
            {
                throw new UpdateException($"The downloaded update contains an unexpected path ({entry.FullName}).");
            }

            CreateRealDirectories(staging, Path.GetDirectoryName(local));
            using (var source = entry.Open())
            using (var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                total = CopyWithLimit(source, target, total);
            }

            files.Add(local);
        }

        if (!seen.Contains(AppExecutable) || !seen.Contains(AppAssembly))
        {
            throw new UpdateException("The downloaded zip doesn't contain Coreloom.exe, so it isn't a Coreloom release.");
        }

        return files;
    }

    /// <summary>"" when Coreloom.exe is at the zip's root; "Folder/" when everything sits in one folder that holds it.</summary>
    private static string FindRoot(IReadOnlyCollection<ZipArchiveEntry> entries)
    {
        var names = entries.Select(e => e.FullName.Replace('\\', '/')).ToArray();
        if (names.Any(n => n.Equals(AppExecutable, StringComparison.OrdinalIgnoreCase)))
        {
            return string.Empty;
        }

        var tops = names.Where(n => n.Length > 0).Select(n => n.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (tops.Length == 1 && names.Any(n => n.Equals($"{tops[0]}/{AppExecutable}", StringComparison.OrdinalIgnoreCase)))
        {
            return tops[0] + "/";
        }

        return string.Empty; // validation below rejects it
    }

    private static bool IsSafeRelativePath(string relative)
    {
        if (relative.StartsWith('/') || relative.Contains(':', StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            return false;
        }

        var invalid = Path.GetInvalidFileNameChars();
        foreach (string segment in relative.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.IndexOfAny(invalid) >= 0 || segment.EndsWith('.') || segment.EndsWith(' '))
            {
                return false;
            }
        }

        return true;
    }

    private static long CopyWithLimit(Stream source, Stream target, long total)
    {
        byte[] buffer = new byte[1 << 16];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MaxUnpackedBytes)
            {
                throw new UpdateException("The downloaded update unpacks to far more data than a Coreloom release.");
            }

            target.Write(buffer, 0, read);
        }

        return total;
    }

    // ---- Apply ------------------------------------------------------------------------------------------

    /// <summary>
    /// Copies the staged files over the app folder (run by the staged executable once the old version has exited).
    /// Files it replaces are moved aside first; on any failure every change is undone, so the old version still runs.
    /// Locked files (an antivirus scan, a CoreloomDiag window) are retried for a few seconds.
    /// </summary>
    /// <param name="appDirectory">The folder being updated.</param>
    /// <param name="attempts">Tries per file operation.</param>
    /// <param name="retryDelay">Pause between tries (default 250 ms).</param>
    /// <param name="sleep">Waits between tries (tests pass a no-op).</param>
    public static FolderUpdateResult Apply(string appDirectory, int attempts = 20, TimeSpan? retryDelay = null, Action<TimeSpan>? sleep = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDirectory);
        var delay = retryDelay ?? TimeSpan.FromMilliseconds(250);
        sleep ??= Thread.Sleep;
        string staging = StagingDirectory(appDirectory);
        string backup = BackupDirectory(appDirectory);
        if (!File.Exists(Path.Combine(staging, AppExecutable)) || !File.Exists(Path.Combine(appDirectory, AppExecutable)))
        {
            return new FolderUpdateResult(false, 0, "The staged update or the app folder is incomplete.");
        }

        var journal = new List<(string Relative, bool Replaced)>();
        var createdDirectories = new List<string>();
        string? current = null;
        try
        {
            if (Directory.Exists(backup) || ResumableDownloader.IsReparsePoint(backup, isDirectory: true))
            {
                throw new IOException("A backup from an earlier update is still there.");
            }

            CreateRealDirectory(backup);
            foreach (string relative in FilesToCopy(staging))
            {
                current = relative;
                string target = Path.Combine(appDirectory, relative);
                CreateRealDirectories(appDirectory, Path.GetDirectoryName(relative), createdDirectories);
                bool replaced = File.Exists(target);
                if (replaced)
                {
                    CreateRealDirectories(backup, Path.GetDirectoryName(relative));
                    string saved = Path.Combine(backup, relative);
                    Retry(() => File.Move(target, saved), attempts, delay, sleep);
                }

                journal.Add((relative, replaced));
                Retry(() => File.Copy(Path.Combine(staging, relative), target, overwrite: false), attempts, delay, sleep);
            }

            Log.Info("Updates", $"Copied {journal.Count} files into {appDirectory}.");
            return new FolderUpdateResult(true, journal.Count, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UpdateException)
        {
            Log.Error("Updates", $"Updating the files failed at {current ?? "the start"}; undoing the {journal.Count} changes made so far.", ex);
            RollBack(appDirectory, backup, journal, createdDirectories, attempts, delay, sleep);
            return new FolderUpdateResult(false, 0, ex.Message);
        }
    }

    /// <summary>Staged files, relative, without what belongs to the copy; Coreloom.exe last (so it is the last to change).</summary>
    private static List<string> FilesToCopy(string staging)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(staging)) + Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(staging, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Select(f => Path.GetFullPath(f)[root.Length..])
            .Where(f => !BelongsToTheCopy(f))
            .OrderBy(f => f.Equals(AppExecutable, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>portable.txt, the Data folder and the update's own folder stay as they are.</summary>
    public static bool BelongsToTheCopy(string relativePath)
    {
        string first = relativePath.Split('/', '\\')[0];
        return relativePath.Equals(AppPaths.PortableMarkerFile, StringComparison.OrdinalIgnoreCase)
            || (first.Equals(DataFolderName, StringComparison.OrdinalIgnoreCase) && relativePath.Length > first.Length)
            || first.Equals(WorkFolderName, StringComparison.OrdinalIgnoreCase);
    }

    private static void RollBack(string appDirectory, string backup, List<(string Relative, bool Replaced)> journal, List<string> createdDirectories,
        int attempts, TimeSpan delay, Action<TimeSpan> sleep)
    {
        for (int i = journal.Count - 1; i >= 0; i--)
        {
            var (relative, replaced) = journal[i];
            string target = Path.Combine(appDirectory, relative);
            try
            {
                if (File.Exists(target))
                {
                    Retry(() => File.Delete(target), attempts, delay, sleep);
                }

                if (replaced)
                {
                    Retry(() => File.Move(Path.Combine(backup, relative), target), attempts, delay, sleep);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error("Updates", $"Couldn't restore {relative}; the old copy is in {backup}.", ex);
            }
        }

        for (int i = createdDirectories.Count - 1; i >= 0; i--)
        {
            try
            {
                if (Directory.Exists(createdDirectories[i]) && !Directory.EnumerateFileSystemEntries(createdDirectories[i]).Any())
                {
                    Directory.Delete(createdDirectories[i]);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn("Updates", $"Couldn't remove {createdDirectories[i]}.", ex);
            }
        }
    }

    private static void Retry(Action action, int attempts, TimeSpan delay, Action<TimeSpan> sleep)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < attempts)
            {
                sleep(delay); // a file briefly held by an antivirus scan or an exiting process
            }
        }
    }

    // ---- Clean up ---------------------------------------------------------------------------------------

    /// <summary>
    /// Deletes <c>.coreloom-update</c> (what a finished or abandoned update left). A link planted in its place is removed
    /// without touching what it points to, and links inside it are never followed. Returns false when it couldn't.
    /// </summary>
    public static bool Cleanup(string appDirectory)
    {
        string work = WorkDirectory(appDirectory);
        try
        {
            if (ResumableDownloader.IsReparsePoint(work, isDirectory: true))
            {
                Directory.Delete(work, recursive: false); // removes the link itself
                return true;
            }

            if (Directory.Exists(work))
            {
                Directory.Delete(work, recursive: true);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Updates", "Removing the files of an earlier update failed; trying again next time.", ex);
            return false;
        }
    }

    // ---- Helpers ----------------------------------------------------------------------------------------

    /// <summary>Creates <paramref name="relativeDirectory"/> under <paramref name="root"/> one level at a time, refusing links.</summary>
    private static void CreateRealDirectories(string root, string? relativeDirectory, List<string>? created = null)
    {
        if (string.IsNullOrEmpty(relativeDirectory))
        {
            return;
        }

        string path = root;
        foreach (string segment in relativeDirectory.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            path = Path.Combine(path, segment);
            bool existed = Directory.Exists(path);
            CreateRealDirectory(path);
            if (!existed)
            {
                created?.Add(path);
            }
        }
    }

    private static void CreateRealDirectory(string path)
    {
        if (ResumableDownloader.IsReparsePoint(path, isDirectory: true))
        {
            throw new UpdateException($"{path} is a link to another location, so the update won't write there.");
        }

        Directory.CreateDirectory(path);
    }

    private static void TryHide(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var info = new DirectoryInfo(path);
            info.Attributes |= FileAttributes.Hidden;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // cosmetic only
        }
    }
}
