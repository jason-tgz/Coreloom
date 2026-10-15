namespace Coreloom.Core.Branding;

/// <summary>An icon reference as a shortcut stores it: a file (an .ico, or an .exe/.dll with icons) and an index in it.</summary>
public readonly record struct IconLocation(string Path, int Index)
{
    public static IconLocation None { get; } = new(string.Empty, 0);

    public bool IsEmpty => string.IsNullOrWhiteSpace(Path);
}

/// <summary>
/// Which icon Coreloom's Windows shortcuts show. Windows draws a shortcut (Start menu, desktop, a pinned taskbar button)
/// with the shortcut's own icon, not the app's window icon, so while the dark theme is on Coreloom points its own
/// shortcuts at the dark-mode icon that ships beside the exe, and points them back at the exe's icon when the light theme
/// returns. Only shortcuts named for Coreloom that start this very exe are touched, and only while they show Coreloom's
/// own icon (a shortcut someone gave an icon of their own is left alone).
/// </summary>
public static class ShortcutIcons
{
    /// <summary>The dark-mode icon's file name, next to Coreloom.exe.</summary>
    public const string DarkIconFileName = "coreloom-dark.ico";

    /// <summary>Whether a shortcut file is worth opening: a .lnk named for Coreloom ("Coreloom.lnk", "Coreloom (2).lnk").</summary>
    public static bool IsCandidate(string shortcutPath)
    {
        string name = System.IO.Path.GetFileName(shortcutPath.Replace('\\', '/'));
        return name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
            && name.Contains(AppVersionInfo.ProductName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether a shortcut starts this exe.</summary>
    public static bool StartsApp(string? targetPath, string exePath) =>
        !string.IsNullOrWhiteSpace(targetPath) && SamePath(targetPath, exePath);

    /// <summary>
    /// The icon a shortcut of Coreloom's should have now, or null to leave it as it is: in the dark theme the dark icon
    /// (when it shows the usual one: none of its own, or the exe's); in the light theme the exe's own icon again (when it
    /// shows the dark one, which Coreloom set). Any other icon was chosen by someone and stays.
    /// </summary>
    public static IconLocation? Desired(bool dark, IconLocation current, string darkIconPath, string exePath)
    {
        bool showsDark = !current.IsEmpty && SamePath(current.Path, darkIconPath);
        bool showsUsual = current.IsEmpty || (SamePath(current.Path, exePath) && current.Index == 0);
        if (dark)
        {
            return showsUsual || (showsDark && current.Index != 0) ? new IconLocation(darkIconPath, 0) : null;
        }

        return showsDark ? new IconLocation(exePath, 0) : null;
    }

    /// <summary>Compares Windows paths: case-insensitively, ignoring quotes and the kind of slash.</summary>
    public static bool SamePath(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) => path.Trim().Trim('"').Replace('/', '\\').TrimEnd('\\');
}
