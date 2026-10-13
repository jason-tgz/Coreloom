using System.IO.Compression;
using System.Text;
using Coreloom.Core.Updates;

namespace Coreloom.Core.Tests;

public sealed class FolderUpdateTests : IDisposable
{
    private static readonly string Sep = Path.DirectorySeparatorChar.ToString();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "coreloom-folder-" + Guid.NewGuid().ToString("N"));

    private string App => Path.Combine(_root, "Coreloom");

    /// <summary>An installed-by-unzipping 1.1.0: app files, a portable marker and the user's Data folder.</summary>
    private void CreateOldCopy(bool portable = true)
    {
        Write(App, "Coreloom.exe", "old exe");
        Write(App, "Coreloom.dll", "old dll");
        Write(App, "Coreloom.Core.dll", "old core");
        Write(App, $"lib{Sep}gone-in-new-version.dll", "old only");
        Write(App, $"Data{Sep}settings.json", "{ \"mine\": true }");
        Write(App, $"Data{Sep}Logs{Sep}coreloom.log", "log");
        if (portable)
        {
            Write(App, "portable.txt", "my marker");
        }
    }

    /// <summary>A portable zip as build.ps1 makes it: everything at the root, with portable.txt.</summary>
    private static MemoryStream ReleaseZip(string prefix = "", params (string Name, string Content)[] extra)
    {
        var files = new List<(string, string)>
        {
            ("Coreloom.exe", "new exe"),
            ("Coreloom.dll", "new dll"),
            ("Coreloom.Core.dll", "new core"),
            ("runtimes/win-x64/native/new.dll", "new native"),
            ("portable.txt", "marker from the zip"),
        };
        files.AddRange(extra);
        return Zip(files.Select(f => (prefix + f.Item1, f.Item2)).ToArray());
    }

    private static MemoryStream Zip(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static void Write(string root, string relative, string content)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string Read(string relative) => File.ReadAllText(Path.Combine(App, relative));

    private static FolderUpdateResult ApplyFast(string app) => FolderUpdate.Apply(app, attempts: 2, retryDelay: TimeSpan.Zero, sleep: _ => { });

    [Fact]
    public void StageApplyCleanup_ReplacesTheAppFiles_AndKeepsDataAndTheMarker()
    {
        CreateOldCopy();

        var staged = FolderUpdate.Stage(ReleaseZip(), App);
        Assert.Contains("Coreloom.exe", staged);
        Assert.True(File.Exists(FolderUpdate.HelperPath(App)));
        Assert.Equal("old exe", Read("Coreloom.exe")); // nothing changes until the old version has exited

        Assert.True(FolderUpdate.TryGetAppDirectoryFromStagingLocation(FolderUpdate.StagingDirectory(App), out string target));
        Assert.Equal(Path.GetFullPath(App), target);

        var result = ApplyFast(target);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(4, result.FilesCopied); // portable.txt is not copied
        Assert.Equal("new exe", Read("Coreloom.exe"));
        Assert.Equal("new dll", Read("Coreloom.dll"));
        Assert.Equal("new core", Read("Coreloom.Core.dll"));
        Assert.Equal("new native", Read($"runtimes{Sep}win-x64{Sep}native{Sep}new.dll"));
        Assert.Equal("my marker", Read("portable.txt"));
        Assert.Equal("{ \"mine\": true }", Read($"Data{Sep}settings.json"));
        Assert.Equal("old only", Read($"lib{Sep}gone-in-new-version.dll"));
        Assert.Equal("old exe", File.ReadAllText(Path.Combine(FolderUpdate.BackupDirectory(App), "Coreloom.exe")));

        Assert.True(FolderUpdate.Cleanup(App));
        Assert.False(Directory.Exists(FolderUpdate.WorkDirectory(App)));
        Assert.Equal("new exe", Read("Coreloom.exe"));
    }

    [Fact]
    public void ANonPortableCopy_StaysNonPortable()
    {
        CreateOldCopy(portable: false);

        FolderUpdate.Stage(ReleaseZip(), App);
        Assert.True(ApplyFast(App).Succeeded);

        Assert.False(File.Exists(Path.Combine(App, "portable.txt")));
        Assert.Equal("new exe", Read("Coreloom.exe"));
    }

    [Fact]
    public void AZipWithOneTopFolder_IsAccepted()
    {
        CreateOldCopy();

        var staged = FolderUpdate.Stage(ReleaseZip("Coreloom-1.2.0-win-x64/"), App);

        Assert.Contains("Coreloom.exe", staged);
        Assert.Contains($"runtimes{Sep}win-x64{Sep}native{Sep}new.dll", staged);
        Assert.True(ApplyFast(App).Succeeded);
        Assert.Equal("new exe", Read("Coreloom.exe"));
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("lib/../../evil.txt")]
    [InlineData("/rooted.txt")]
    [InlineData("C:/Windows/evil.dll")]
    [InlineData("Coreloom.exe:stream")]
    [InlineData("trailing./x.dll")]
    public void UnsafePaths_AreRefused_AndNothingIsLeftBehind(string name)
    {
        CreateOldCopy();

        var error = Assert.Throws<UpdateException>(() => FolderUpdate.Stage(ReleaseZip(extra: (name, "evil")), App));

        Assert.Contains("unexpected path", error.Message);
        Assert.False(Directory.Exists(FolderUpdate.WorkDirectory(App)));
        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));
        Assert.Equal("old exe", Read("Coreloom.exe"));
    }

    [Fact]
    public void AZipThatIsntCoreloom_IsRefused()
    {
        CreateOldCopy();

        Assert.Throws<UpdateException>(() => FolderUpdate.Stage(Zip(("readme.txt", "hello"), ("other.exe", "x")), App));
        Assert.False(Directory.Exists(FolderUpdate.WorkDirectory(App)));

        var garbage = new MemoryStream(Encoding.ASCII.GetBytes("this is not a zip file at all"));
        var error = Assert.Throws<UpdateException>(() => FolderUpdate.Stage(garbage, App));
        Assert.Contains("isn't a valid zip", error.Message);
    }

    [Fact]
    public void AFailedCopy_UndoesEverything()
    {
        CreateOldCopy();
        // The new version ships lib/new.dll, but the copy has a folder with that name: copying it fails halfway.
        FolderUpdate.Stage(ReleaseZip(extra: ("lib/new.dll", "new lib")), App);
        Directory.CreateDirectory(Path.Combine(App, "lib", "new.dll"));

        var result = ApplyFast(App);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
        Assert.Equal("old exe", Read("Coreloom.exe"));
        Assert.Equal("old dll", Read("Coreloom.dll"));         // replaced before the failure, restored
        Assert.Equal("old core", Read("Coreloom.Core.dll"));
        Assert.False(Directory.Exists(Path.Combine(App, "runtimes"))); // nothing new is left
        Assert.Equal("old only", Read($"lib{Sep}gone-in-new-version.dll"));
        Assert.Equal("my marker", Read("portable.txt"));
    }

    [Fact]
    public void StagingAgain_ReplacesAnEarlierAttempt()
    {
        CreateOldCopy();
        Write(FolderUpdate.StagingDirectory(App), "stale.dll", "left by an earlier attempt");
        Write(FolderUpdate.BackupDirectory(App), "Coreloom.exe", "left by an earlier attempt");

        FolderUpdate.Stage(ReleaseZip(), App);

        Assert.False(File.Exists(Path.Combine(FolderUpdate.StagingDirectory(App), "stale.dll")));
        Assert.False(Directory.Exists(FolderUpdate.BackupDirectory(App)));
        Assert.True(ApplyFast(App).Succeeded);
    }

    [Fact]
    public void Cleanup_RemovesALinkWithoutTouchingWhatItPointsTo()
    {
        CreateOldCopy();
        string elsewhere = Path.Combine(_root, "elsewhere");
        Write(elsewhere, "precious.txt", "keep me");
        Directory.CreateSymbolicLink(FolderUpdate.WorkDirectory(App), elsewhere);

        Assert.True(FolderUpdate.Cleanup(App));

        Assert.False(Directory.Exists(FolderUpdate.WorkDirectory(App)));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(elsewhere, "precious.txt")));
    }

    [Fact]
    public void Staging_NeverWritesThroughALinkPlantedInTheWorkFolder()
    {
        CreateOldCopy();
        string elsewhere = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        Directory.CreateDirectory(FolderUpdate.WorkDirectory(App));

        // Stage clears the work folder first, so plant the link where the zip's subfolder will be created.
        var zip = ReleaseZip();
        Directory.CreateSymbolicLink(Path.Combine(FolderUpdate.WorkDirectory(App), "new"), elsewhere);

        FolderUpdate.Stage(zip, App); // the link is removed with the old work folder, then a real folder is used

        Assert.Empty(Directory.GetFileSystemEntries(elsewhere));
        Assert.False(new DirectoryInfo(FolderUpdate.StagingDirectory(App)).Attributes.HasFlag(FileAttributes.ReparsePoint));
    }

    [Theory]
    [InlineData("portable.txt", true)]
    [InlineData("PORTABLE.TXT", true)]
    [InlineData("Data/settings.json", true)]
    [InlineData("Data\\Logs\\x.log", true)]
    [InlineData(".coreloom-update/new/Coreloom.exe", true)]
    [InlineData("Coreloom.exe", false)]
    [InlineData("runtimes/win-x64/native/x.dll", false)]
    [InlineData("Database.dll", false)]
    public void BelongsToTheCopy(string path, bool expected) => Assert.Equal(expected, FolderUpdate.BelongsToTheCopy(path));

    [Fact]
    public void HelperLocation_IsOnlyTheStagingFolderOfACoreloomFolder()
    {
        CreateOldCopy();
        Directory.CreateDirectory(FolderUpdate.StagingDirectory(App));

        Assert.True(FolderUpdate.TryGetAppDirectoryFromStagingLocation(FolderUpdate.StagingDirectory(App) + Sep, out _));
        Assert.False(FolderUpdate.TryGetAppDirectoryFromStagingLocation(App, out _));
        Assert.False(FolderUpdate.TryGetAppDirectoryFromStagingLocation(Path.Combine(_root, "x", ".coreloom-update", "new"), out _)); // no Coreloom.exe there
    }

    [Fact]
    public void HelperArguments_AndCopyId()
    {
        Assert.Equal("--apply-update --wait-for 42", FolderUpdate.HelperArguments(42, UpdateInstallOptions.Interactive));
        Assert.Equal("--apply-update --wait-for 42 --minimized --quiet", FolderUpdate.HelperArguments(42, UpdateInstallOptions.Background));

        string id = FolderUpdate.CopyId(App);
        Assert.Equal(16, id.Length);
        Assert.Equal(id, FolderUpdate.CopyId(App + Sep));
        Assert.NotEqual(id, FolderUpdate.CopyId(Path.Combine(_root, "Other")));
    }

    [Fact]
    public void Releases_PickThePackageForTheKindOfCopy()
    {
        var release = new ReleaseInfo(SemanticVersion.Parse("1.2.0"), "v1.2.0", "Coreloom 1.2.0", "", new Uri("https://example.test/r"), null, false,
        [
            new ReleaseAsset("Coreloom-1.2.0-win-x64-Setup.exe", new Uri("https://example.test/s"), 1),
            new ReleaseAsset("Coreloom-1.2.0-win-x64-Portable.zip", new Uri("https://example.test/p"), 1),
        ]);

        Assert.Equal("Coreloom-1.2.0-win-x64-Setup.exe", release.PackageFor(UpdatePackageKind.Installer)!.Name);
        Assert.Equal("Coreloom-1.2.0-win-x64-Portable.zip", release.PackageFor(UpdatePackageKind.PortableZip)!.Name);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
