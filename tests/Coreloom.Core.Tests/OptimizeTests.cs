using Coreloom.Core.Optimize;

namespace Coreloom.Core.Tests;

public sealed class JunkCleanerTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "coreloom-junk-" + Guid.NewGuid().ToString("N"));
    private readonly string _outside = Path.Combine(Path.GetTempPath(), "coreloom-keep-" + Guid.NewGuid().ToString("N"));

    public JunkCleanerTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outside);
    }

    public void Dispose()
    {
        foreach (string path in new[] { _root, _outside })
        {
            if (Directory.Exists(path))
            {
                foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    file.Attributes = FileAttributes.Normal;
                }

                Directory.Delete(path, recursive: true);
            }
        }
    }

    private string File(string relative, int bytes, TimeSpan age, string? root = null)
    {
        string path = Path.Combine(root ?? _root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, new byte[bytes]);
        System.IO.File.SetLastWriteTimeUtc(path, Now - age);
        return path;
    }

    private JunkCategory Category(params JunkLocation[] locations) => new("test", "Test", "Test files", "Icon.Clock", locations, true);

    [Fact]
    public void OnlyOldEnoughFilesAreMeasuredAndCleaned()
    {
        string old = File("old.tmp", 1000, TimeSpan.FromDays(3));
        string nested = File(Path.Combine("setup", "part.bin"), 500, TimeSpan.FromDays(2));
        string fresh = File("fresh.tmp", 300, TimeSpan.FromMinutes(5));
        Directory.SetLastWriteTimeUtc(Path.Combine(_root, "setup"), Now - TimeSpan.FromDays(2));
        var category = Category(new JunkLocation(_root, MinAge: TimeSpan.FromHours(24)));

        Assert.Equal(new JunkSize(1500, 2), JunkCleaner.Measure(category, now: Now));
        var result = JunkCleaner.Clean(category, now: Now);

        Assert.Equal(new CleanResult(1500, 2, 0), result);
        Assert.False(System.IO.File.Exists(old));
        Assert.False(System.IO.File.Exists(nested));
        Assert.True(System.IO.File.Exists(fresh)); // the app that made it may still need it
        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public void LinksAreNeverFollowed()
    {
        string precious = File("document.txt", 100, TimeSpan.FromDays(400), _outside);
        File("cache.bin", 100, TimeSpan.FromDays(400));
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(_root, "link"), _outside);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return; // links need privileges on some systems; nothing to test then
        }

        var result = JunkCleaner.Clean(Category(new JunkLocation(_root)), now: Now);
        Assert.Equal(1, result.Deleted);
        Assert.True(System.IO.File.Exists(precious));
    }

    [Fact]
    public void ReadOnlyFilesAreSkipped_AndPatternsRespected()
    {
        string locked = File("thumbcache_256.db", 50, TimeSpan.FromDays(10));
        System.IO.File.SetAttributes(locked, FileAttributes.ReadOnly);
        string match = File("thumbcache_32.db", 70, TimeSpan.FromDays(10));
        string other = File("iconcache_32.db", 90, TimeSpan.FromDays(10));
        string deeper = File(Path.Combine("sub", "thumbcache_16.db"), 10, TimeSpan.FromDays(10));
        var category = Category(new JunkLocation(_root, "thumbcache_*.db", Recursive: false));

        Assert.Equal(new JunkSize(120, 2), JunkCleaner.Measure(category, now: Now));
        var result = JunkCleaner.Clean(category, now: Now);

        Assert.Equal(1, result.Deleted);
        Assert.Equal(1, result.Skipped);
        Assert.False(System.IO.File.Exists(match));
        Assert.True(System.IO.File.Exists(locked));
        Assert.True(System.IO.File.Exists(other));
        Assert.True(System.IO.File.Exists(deeper));
    }

    [Fact]
    public void MissingFoldersAreFine()
    {
        var category = Category(new JunkLocation(Path.Combine(_root, "does-not-exist")));
        Assert.Equal(default(JunkSize), JunkCleaner.Measure(category));
        Assert.Equal(default(CleanResult), JunkCleaner.Clean(category));
    }

    [Fact]
    public void WindowsCategories_CoverTheUsualJunk_AndKeepRiskyOnesOff()
    {
        var categories = WindowsJunk.Categories(new JunkFolders(_root, Path.Combine(_root, "Temp"), Path.Combine(_root, "Windows"), Path.Combine(_root, "ProgramData")));
        Assert.Equal(
            new[] { WindowsJunk.UserTemp, WindowsJunk.SystemTemp, WindowsJunk.Browsers, WindowsJunk.CrashReports, WindowsJunk.Thumbnails, WindowsJunk.Shaders, WindowsJunk.UpdateDownloads, WindowsJunk.RecycleBin },
            categories.Select(c => c.Id).ToArray());
        Assert.All(categories.Where(c => !c.SelectedByDefault), c => Assert.False(string.IsNullOrEmpty(c.Caution)));
        Assert.Equal(TimeSpan.FromHours(24), categories[0].Locations.Single().MinAge);
        Assert.Equal(JunkKind.RecycleBin, categories.Single(c => c.Id == WindowsJunk.RecycleBin).Kind);
    }
}

public class StartupApprovedTests
{
    [Fact]
    public void EvenFirstBytesMeanEnabled()
    {
        Assert.True(StartupApproved.IsEnabled(null));
        Assert.True(StartupApproved.IsEnabled([2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        Assert.True(StartupApproved.IsEnabled([6, 0, 0, 0]));
        Assert.False(StartupApproved.IsEnabled([3, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8]));
        Assert.False(StartupApproved.IsEnabled([7]));
    }

    [Fact]
    public void Values_RoundTrip()
    {
        var at = new DateTime(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc);
        var off = StartupApproved.Value(false, at);
        Assert.Equal(12, off.Length);
        Assert.False(StartupApproved.IsEnabled(off));
        Assert.Equal(at.ToFileTimeUtc(), BitConverter.ToInt64(off, 4));
        Assert.True(StartupApproved.IsEnabled(StartupApproved.Value(true, at)));
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32", StartupApproved.KeyFor(StartupScope.Machine32));
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder", StartupApproved.KeyFor(StartupScope.UserFolder));
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\Steam\\steam.exe\" -silent", "C:\\Program Files\\Steam\\steam.exe")]
    [InlineData("C:\\Program Files\\App\\app.exe --minimized", "C:\\Program Files\\App\\app.exe")]
    [InlineData("rundll32.exe shell32.dll,Control_RunDLL", "rundll32.exe")]
    [InlineData("", null)]
    public void Executable_IsFoundInTheCommand(string command, string? expected)
    {
        Assert.Equal(expected, StartupApproved.Executable(command));
    }

    [Fact]
    public void Names_AreReadable()
    {
        Assert.Equal("Discord", StartupApproved.NameFrom("Discord", null));
        Assert.Equal("OneNote", StartupApproved.NameFrom("OneNote.lnk", null));
        Assert.Equal("helper", StartupApproved.NameFrom("{8D0E3E1E-0000}", "C:\\Apps\\helper.exe"));
    }
}

public class DefenderStatusTests
{
    [Fact]
    public void Status_IsRead()
    {
        const string json = """
            {"available":true,"realTime":true,"antivirus":true,"mode":"Normal","signaturesUpdated":"2026-10-13T06:00:00.0000000Z",
             "signatureVersion":"1.419.12.0","lastQuickScan":"2026-10-10T09:30:00.0000000Z","lastFullScan":null,
             "threats":{"name":"Trojan:Win32/Wacatac.B!ml","severity":5,"resource":"file:_C:\\Users\\me\\Downloads\\x.exe","active":true},
             "products":"Windows Defender"}
            """;
        var status = DefenderScripts.ParseStatus(json);

        Assert.True(status.DefenderAvailable);
        Assert.True(status.IsDefenderInCharge);
        Assert.True(status.IsProtected);
        Assert.Equal("1.419.12.0", status.SignatureVersion);
        Assert.Equal(new DateTime(2026, 10, 13, 6, 0, 0, DateTimeKind.Utc), status.SignaturesUpdated!.Value.ToUniversalTime());
        Assert.Null(status.LastFullScan);
        Assert.Equal(1, status.ActiveThreats);
        Assert.Equal("Severe", status.Threats[0].SeverityText);
        Assert.Null(status.OtherProduct);
        Assert.False(status.SignaturesOutdated(new DateTime(2026, 10, 14, 6, 0, 0, DateTimeKind.Utc).ToLocalTime()));
        Assert.True(status.ScanOverdue(new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc).ToLocalTime()));
    }

    [Fact]
    public void AnotherAntivirus_IsRecognised()
    {
        var status = DefenderScripts.ParseStatus("""{"available":true,"realTime":false,"antivirus":true,"mode":"Passive Mode","threats":[],"products":["Windows Defender","Norton 360"]}""");
        Assert.False(status.IsDefenderInCharge);
        Assert.Equal("Norton 360", status.OtherProduct);
        Assert.True(status.IsProtected);
        Assert.Empty(status.Threats);
    }

    [Fact]
    public void Problems_AreExplained()
    {
        var off = DefenderScripts.ParseStatus("""{"available":false,"error":"The service cannot be started","products":[]}""");
        Assert.False(off.DefenderAvailable);
        Assert.False(off.IsProtected);
        Assert.Contains("cannot be started", off.Problem);
        Assert.NotNull(DefenderScripts.ParseStatus("not json").Problem);
        Assert.NotNull(DefenderScripts.ParseStatus("").Problem);
    }
}
