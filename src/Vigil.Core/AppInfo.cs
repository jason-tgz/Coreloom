using System.Reflection;
using Vigil.Core.Updates;

namespace Vigil.Core;

/// <summary>Version and build metadata of the running application.</summary>
public sealed record AppVersionInfo(SemanticVersion Version, string InformationalVersion, string? Repository, string? CommitHash)
{
    public const string ProductName = "Vigil";

    public string DisplayVersion => Version.ToString();

    public static AppVersionInfo FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        string informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";

        // The SDK appends "+<commit sha>" to the informational version when SourceLink data is available.
        string? commit = null;
        string versionText = informational;
        int plus = informational.IndexOf('+');
        if (plus >= 0)
        {
            commit = informational[(plus + 1)..];
            versionText = informational[..plus];
        }

        if (!SemanticVersion.TryParse(versionText, out var version))
        {
            version = new SemanticVersion(0, 0, 0);
        }

        string? repository = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "UpdateRepository")?.Value;

        return new AppVersionInfo(version, informational, repository, commit);
    }
}

/// <summary>
/// File-system locations used by the app. A file named <c>portable.txt</c> next to the executable switches
/// to portable mode, where settings and logs are stored in a <c>Data</c> folder beside the app.
/// </summary>
public sealed class AppPaths
{
    public const string PortableMarkerFile = "portable.txt";

    private AppPaths(bool isPortable, string baseDirectory, string settingsDirectory, string localDirectory)
    {
        IsPortable = isPortable;
        BaseDirectory = baseDirectory;
        SettingsDirectory = settingsDirectory;
        LocalDirectory = localDirectory;
    }

    public bool IsPortable { get; }

    /// <summary>Folder containing the executable.</summary>
    public string BaseDirectory { get; }

    /// <summary>Roaming settings folder (%AppData%\Vigil, or Data\ in portable mode).</summary>
    public string SettingsDirectory { get; }

    /// <summary>Machine-local folder for logs and downloads (%LocalAppData%\Vigil, or Data\ in portable mode).</summary>
    public string LocalDirectory { get; }

    public string SettingsFile => Path.Combine(SettingsDirectory, "settings.json");

    public string LogDirectory => Path.Combine(LocalDirectory, "Logs");

    public string DownloadDirectory => Path.Combine(LocalDirectory, "Updates");

    /// <summary>True when the app was installed by the Inno Setup installer (an uninstaller sits next to it).</summary>
    public bool IsInstalled => File.Exists(Path.Combine(BaseDirectory, "unins000.exe"));

    public static AppPaths Resolve(string baseDirectory, string? appDataRoot = null, string? localAppDataRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        if (File.Exists(Path.Combine(baseDirectory, PortableMarkerFile)))
        {
            string data = Path.Combine(baseDirectory, "Data");
            return new AppPaths(true, baseDirectory, data, data);
        }

        string roaming = appDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string local = localAppDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new AppPaths(false, baseDirectory, Path.Combine(roaming, AppVersionInfo.ProductName), Path.Combine(local, AppVersionInfo.ProductName));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(SettingsDirectory);
        Directory.CreateDirectory(LocalDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
