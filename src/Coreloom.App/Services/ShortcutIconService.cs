using System.IO;
using Coreloom.Core.Branding;
using Coreloom.Core.Diagnostics;
using Coreloom.Hardware.Native;

namespace Coreloom.App.Services;

/// <summary>
/// Gives Coreloom's own Windows shortcuts the icon for the theme (see <see cref="ShortcutIcons"/>): the Start menu entry,
/// the desktop shortcut and a pinned taskbar button show the dark-mode icon while the dark theme is on, and the usual
/// icon again in the light theme. Windows draws those from the shortcut, not from the running window, which is why the
/// window icon alone isn't enough. Runs in the background; a shortcut it can't open or save is simply left as it is.
/// </summary>
internal static class ShortcutIconService
{
    private static readonly object Gate = new();
    private static int _generation;

    /// <summary>Brings the shortcuts in line with the theme (the latest call wins).</summary>
    public static void Apply(bool dark)
    {
        int generation = Interlocked.Increment(ref _generation);
        _ = Task.Run(() =>
        {
            lock (Gate)
            {
                if (generation == Volatile.Read(ref _generation))
                {
                    ApplyNow(dark);
                }
            }
        });
    }

    private static void ApplyNow(bool dark)
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (exe is null || !OperatingSystem.IsWindows())
            {
                return;
            }

            string darkIcon = Path.Combine(AppContext.BaseDirectory, ShortcutIcons.DarkIconFileName);
            if (dark && !File.Exists(darkIcon))
            {
                return; // nothing to point them at (a developer build)
            }

            var changed = new List<string>();
            foreach (string shortcut in Candidates())
            {
                try
                {
                    using var link = ShellLink.Open(shortcut);
                    if (!ShortcutIcons.StartsApp(link.TargetPath, exe)
                        || ShortcutIcons.Desired(dark, link.Icon, darkIcon, exe) is not { } icon)
                    {
                        continue;
                    }

                    link.Icon = icon;
                    link.Save();
                    changed.Add(shortcut);
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException or InvalidCastException)
                {
                    Log.Warn("Shortcuts", $"Couldn't update the icon of {shortcut}.", ex);
                }
            }

            if (changed.Count > 0)
            {
                ShellLink.NotifyChanged(changed);
                Log.Info("Shortcuts", $"Shortcuts now show the {(dark ? "dark" : "light")} icon: {string.Join(", ", changed)}");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Shortcuts", "Updating the shortcut icons failed.", ex);
        }
    }

    /// <summary>Coreloom-named shortcuts in the places Windows shows them: Start menu, desktop, taskbar and Start pins.</summary>
    private static IEnumerable<string> Candidates()
    {
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string pinned = Path.Combine(roaming, "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned");
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var folders = new List<string>
        {
            programs,
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Path.Combine(pinned, "TaskBar"),
            Path.Combine(pinned, "StartMenu"),
        };

        // The Start menu may keep it in a folder of its own ("Coreloom\Coreloom.lnk").
        try
        {
            if (Directory.Exists(programs))
            {
                folders.AddRange(Directory.EnumerateDirectories(programs, $"*{Coreloom.Core.AppVersionInfo.ProductName}*"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the top level still gets looked at
        }

        foreach (string folder in folders.Where(f => !string.IsNullOrEmpty(f) && Directory.Exists(f)))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder, "*.lnk").Where(ShortcutIcons.IsCandidate).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string file in files)
            {
                yield return file;
            }
        }
    }
}
