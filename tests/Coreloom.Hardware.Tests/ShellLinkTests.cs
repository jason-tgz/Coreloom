using Coreloom.Core.Branding;
using Coreloom.Hardware.Native;

namespace Coreloom.Hardware.Tests;

/// <summary>Coreloom's shortcut icon switch on a real shortcut, through the Windows Shell. Skipped on other platforms.</summary>
public class ShellLinkTests
{
    [Fact]
    public void ShortcutIconSwitchesToDarkAndBack()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        string folder = Directory.CreateTempSubdirectory("coreloom-shortcuts-").FullName;
        try
        {
            string exe = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            string shortcut = Path.Combine(folder, "Coreloom.lnk");
            string darkIcon = Path.Combine(folder, ShortcutIcons.DarkIconFileName);
            File.WriteAllBytes(darkIcon, [0, 0, 1, 0, 0, 0]);

            using (var link = ShellLink.Create(shortcut, exe))
            {
                link.Save();
            }

            Assert.True(ShortcutIcons.IsCandidate(shortcut));
            using (var link = ShellLink.Open(shortcut))
            {
                Assert.True(ShortcutIcons.StartsApp(link.TargetPath, exe), $"target was '{link.TargetPath}'");
                var dark = ShortcutIcons.Desired(true, link.Icon, darkIcon, exe);
                Assert.NotNull(dark);
                link.Icon = dark.Value;
                link.Save();
            }

            using (var link = ShellLink.Open(shortcut))
            {
                Assert.True(ShortcutIcons.SamePath(link.Icon.Path, darkIcon), $"icon was '{link.Icon.Path}'");
                Assert.Equal(0, link.Icon.Index);
                Assert.Null(ShortcutIcons.Desired(true, link.Icon, darkIcon, exe));
                var light = ShortcutIcons.Desired(false, link.Icon, darkIcon, exe);
                Assert.NotNull(light);
                link.Icon = light.Value;
                link.Save();
            }

            using (var link = ShellLink.Open(shortcut))
            {
                Assert.True(ShortcutIcons.SamePath(link.Icon.Path, exe), $"icon was '{link.Icon.Path}'");
                Assert.Null(ShortcutIcons.Desired(false, link.Icon, darkIcon, exe));
                Assert.True(ShortcutIcons.StartsApp(link.TargetPath, exe));
            }

            ShellLink.NotifyChanged([shortcut]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
