using Coreloom.Core.Branding;

namespace Coreloom.Core.Tests;

public class ShortcutIconTests
{
    private const string Exe = @"C:\Users\Jerry\AppData\Local\Programs\Coreloom\Coreloom.exe";
    private const string Dark = @"C:\Users\Jerry\AppData\Local\Programs\Coreloom\coreloom-dark.ico";

    [Theory]
    [InlineData(@"C:\Users\Jerry\Desktop\Coreloom.lnk", true)]
    [InlineData(@"C:\Users\Jerry\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\coreloom (2).lnk", true)]
    [InlineData(@"C:\Users\Jerry\Desktop\Steam.lnk", false)]
    [InlineData(@"C:\Users\Jerry\Desktop\Coreloom.exe", false)]
    [InlineData("Coreloom.LNK", true)]
    public void OnlyShortcutsNamedForCoreloomAreOpened(string path, bool candidate) =>
        Assert.Equal(candidate, ShortcutIcons.IsCandidate(path));

    [Fact]
    public void AShortcutIsCoreloomsWhenItStartsThisExe()
    {
        Assert.True(ShortcutIcons.StartsApp(@"c:\users\jerry\appdata\local\programs\coreloom\CORELOOM.EXE", Exe));
        Assert.True(ShortcutIcons.StartsApp("\"" + Exe + "\"", Exe));
        Assert.False(ShortcutIcons.StartsApp(@"D:\Portable\Coreloom\Coreloom.exe", Exe));
        Assert.False(ShortcutIcons.StartsApp(null, Exe));
        Assert.False(ShortcutIcons.StartsApp("", Exe));
    }

    [Fact]
    public void DarkThemeGivesTheUsualIconTheDarkOne()
    {
        Assert.Equal(new IconLocation(Dark, 0), ShortcutIcons.Desired(true, IconLocation.None, Dark, Exe));
        Assert.Equal(new IconLocation(Dark, 0), ShortcutIcons.Desired(true, new IconLocation(Exe, 0), Dark, Exe));
        Assert.Equal(new IconLocation(Dark, 0), ShortcutIcons.Desired(true, new IconLocation(Dark, 3), Dark, Exe));
    }

    [Fact]
    public void NothingChangesWhenTheShortcutAlreadyMatchesTheTheme()
    {
        Assert.Null(ShortcutIcons.Desired(true, new IconLocation(Dark.ToUpperInvariant(), 0), Dark, Exe));
        Assert.Null(ShortcutIcons.Desired(false, IconLocation.None, Dark, Exe));
        Assert.Null(ShortcutIcons.Desired(false, new IconLocation(Exe, 0), Dark, Exe));
    }

    [Fact]
    public void LightThemePutsBackTheExesIcon()
    {
        Assert.Equal(new IconLocation(Exe, 0), ShortcutIcons.Desired(false, new IconLocation(Dark, 0), Dark, Exe));
    }

    [Fact]
    public void AnIconSomeoneChoseIsLeftAlone()
    {
        var custom = new IconLocation(@"C:\Icons\my-coreloom.ico", 0);
        Assert.Null(ShortcutIcons.Desired(true, custom, Dark, Exe));
        Assert.Null(ShortcutIcons.Desired(false, custom, Dark, Exe));
        Assert.Null(ShortcutIcons.Desired(true, new IconLocation(Exe, 2), Dark, Exe));
    }
}
