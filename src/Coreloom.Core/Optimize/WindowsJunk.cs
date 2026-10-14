namespace Coreloom.Core.Optimize;

/// <summary>The folders junk is looked for in (from Windows' known folders, or test folders).</summary>
public sealed record JunkFolders(string LocalAppData, string Temp, string Windows, string ProgramData);

/// <summary>
/// The junk Coreloom knows how to clean on Windows. Each category only lists folders whose contents Windows and apps
/// treat as disposable and rebuild on their own: temporary files, caches, crash reports. Documents, downloads and
/// settings are never touched.
/// </summary>
public static class WindowsJunk
{
    public const string UserTemp = "temp";
    public const string SystemTemp = "windows-temp";
    public const string Browsers = "browsers";
    public const string CrashReports = "crash-reports";
    public const string Thumbnails = "thumbnails";
    public const string Shaders = "shaders";
    public const string UpdateDownloads = "update-downloads";
    public const string RecycleBin = "recycle-bin";

    /// <summary>Temporary files younger than this stay: the app that made them may still need them.</summary>
    public static readonly TimeSpan TempAge = TimeSpan.FromHours(24);

    /// <summary>Chromium browsers: folder under LocalAppData that holds "User Data".</summary>
    private static readonly string[] ChromiumBrowsers =
    [
        @"Google\Chrome",
        @"Microsoft\Edge",
        @"BraveSoftware\Brave-Browser",
        @"Vivaldi",
        @"Chromium",
    ];

    private static readonly string[] ChromiumCaches = [@"Cache\Cache_Data", "Code Cache", "GPUCache", @"Service Worker\CacheStorage"];

    public static IReadOnlyList<JunkCategory> Categories(JunkFolders folders)
    {
        string local = folders.LocalAppData;
        return
        [
            new(UserTemp, "Temporary files", "Files apps left in your temp folder", "Icon.Clock",
                [new(folders.Temp, MinAge: TempAge)], SelectedByDefault: true),
            new(SystemTemp, "Windows temporary files", "Leftovers from installers and Windows services", "Icon.Window",
                [new(Path.Combine(folders.Windows, "Temp"), MinAge: TempAge)], SelectedByDefault: true),
            new(Browsers, "Browser caches", "Cached web pages and scripts in Chrome, Edge, Brave, Vivaldi, Opera and Firefox", "Icon.Globe",
                BrowserCaches(local), SelectedByDefault: true),
            new(CrashReports, "Crash dumps and error reports", "Reports Windows already sent or no longer needs", "Icon.Warning",
                [
                    new(Path.Combine(local, "CrashDumps")),
                    new(Path.Combine(local, @"Microsoft\Windows\WER\ReportArchive")),
                    new(Path.Combine(local, @"Microsoft\Windows\WER\ReportQueue")),
                    new(Path.Combine(folders.ProgramData, @"Microsoft\Windows\WER\ReportArchive")),
                    new(Path.Combine(folders.ProgramData, @"Microsoft\Windows\WER\ReportQueue")),
                ], SelectedByDefault: true),
            new(Thumbnails, "Thumbnail cache", "Picture previews File Explorer rebuilds when needed", "Icon.Image",
                [new(Path.Combine(local, @"Microsoft\Windows\Explorer"), "thumbcache_*.db", Recursive: false)], SelectedByDefault: false)
            {
                Caution = "Folders with many pictures show previews more slowly the first time",
            },
            new(Shaders, "Shader caches", "Graphics driver and DirectX caches", "Icon.Gpu",
                [
                    new(Path.Combine(local, "D3DSCache")),
                    new(Path.Combine(local, @"NVIDIA\DXCache")),
                    new(Path.Combine(local, @"NVIDIA\GLCache")),
                    new(Path.Combine(folders.ProgramData, @"NVIDIA Corporation\NV_Cache")),
                    new(Path.Combine(local, @"AMD\DxCache")),
                    new(Path.Combine(local, @"AMD\DxcCache")),
                    new(Path.Combine(local, @"AMD\VkCache")),
                ], SelectedByDefault: false)
            {
                Caution = "Games rebuild these on their next start, which can stutter for a while",
            },
            new(UpdateDownloads, "Windows Update downloads", "Update files Windows has already installed", "Icon.Download",
                [new(Path.Combine(folders.Windows, @"SoftwareDistribution\Download"), MinAge: TimeSpan.FromDays(10))], SelectedByDefault: false)
            {
                Caution = "Only files older than 10 days, so updates in progress aren't disturbed",
            },
            new(RecycleBin, "Recycle Bin", "Files you deleted", "Icon.Trash", [], SelectedByDefault: false, JunkKind.RecycleBin)
            {
                Caution = "Emptying it can't be undone",
            },
        ];
    }

    /// <summary>Cache folders of every browser profile found (Chromium "User Data\*", Firefox profiles' cache2).</summary>
    private static List<JunkLocation> BrowserCaches(string localAppData)
    {
        var locations = new List<JunkLocation>();
        foreach (string browser in ChromiumBrowsers)
        {
            foreach (string profile in Subfolders(Path.Combine(localAppData, browser, "User Data")))
            {
                locations.AddRange(ChromiumCaches.Select(cache => new JunkLocation(Path.Combine(profile, cache))));
            }
        }

        // Opera keeps its cache straight under LocalAppData\Opera Software\<edition>.
        foreach (string edition in Subfolders(Path.Combine(localAppData, "Opera Software")))
        {
            locations.AddRange(ChromiumCaches.Select(cache => new JunkLocation(Path.Combine(edition, cache))));
        }

        foreach (string profile in Subfolders(Path.Combine(localAppData, @"Mozilla\Firefox\Profiles")))
        {
            locations.Add(new JunkLocation(Path.Combine(profile, "cache2")));
        }

        return locations;
    }

    private static IEnumerable<string> Subfolders(string path)
    {
        try
        {
            return Directory.Exists(path)
                ? new DirectoryInfo(path).EnumerateDirectories().Where(d => !d.Attributes.HasFlag(FileAttributes.ReparsePoint)).Select(d => d.FullName).ToList()
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
