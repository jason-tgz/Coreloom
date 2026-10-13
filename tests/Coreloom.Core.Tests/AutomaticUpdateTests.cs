using System.Net;
using System.Security.Cryptography;
using Coreloom.Core.Settings;
using Coreloom.Core.Updates;

namespace Coreloom.Core.Tests;

public class ReleaseNotesSummaryTests
{
    // The 1.0.0 notes as the release workflow would publish them (CHANGELOG section + download footer).
    private const string HighlightsNotes = """
        **The first release of Coreloom**: a native Windows 11 hardware monitor with a calm, modern dashboard for your GPU, CPU, memory, storage and network.

        Coreloom only *reads* sensors. It never changes clocks.

        ### Highlights

        - **Overview dashboard.** Six live cards (GPU utilization, GPU temperature, CPU utilization, CPU temperature, RAM and GPU power), each with a radial gauge, a sparkline, threshold colors and min/max/avg. Below them sit large charts.
        - **Detail pages** for the GPU, CPU, memory, storage and network:
          - GPU clocks, VRAM, power and power limit;
          - per-thread CPU graphs.
        - **Performance History.** Compare up to six metrics on stacked, time-aligned charts.
        - **Honest data.** Every value is labelled *measured* or *derived* and names its source (NVML, Windows performance counters or [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor)).
        - **Startup splash** that fades into the dashboard the moment it's ready.
        - **Alerts.** Per-metric rules with a sustain time.
        - **Tray and startup.** A tray icon with a live tooltip.

        ### Requirements

        - Windows 11 x64, version 23H2 or later.

        ---
        **Verify your download:** compare the file's SHA-256 with `SHA256SUMS.txt`.
        """;

    [Fact]
    public void Highlights_AreSummarized_WithBoldLeadAsTitle()
    {
        var summary = ReleaseNotesSummary.Parse(HighlightsNotes);

        Assert.Equal("The first release of Coreloom: a native Windows 11 hardware monitor with a calm, modern dashboard for your GPU, CPU, memory, storage and network.", summary.Intro);
        Assert.Equal(5, summary.Items.Count);
        Assert.Equal(2, summary.MoreCount);

        var first = summary.Items[0];
        Assert.Null(first.Category);
        Assert.Equal("Overview dashboard", first.Title);
        Assert.StartsWith("Six live cards (GPU utilization", first.Text);
        Assert.True(first.Text.Length <= ReleaseNotesSummary.MaxTextLength + 1, first.Text);
        Assert.EndsWith("…", first.Text);

        // A bold lead that runs into the sentence stays one sentence; nested bullets are ignored.
        Assert.Null(summary.Items[1].Title);
        Assert.Equal("Detail pages for the GPU, CPU, memory, storage and network.", summary.Items[1].Text);

        Assert.Equal("Performance History", summary.Items[2].Title);
        Assert.Equal("Compare up to six metrics on stacked, time-aligned charts.", summary.Items[2].Text);

        // Inline Markdown is reduced to plain text.
        Assert.Equal("Every value is labelled measured or derived and names its source (NVML, Windows performance counters or LibreHardwareMonitor).", summary.Items[3].Text);

        // Requirements and the download footer never appear.
        Assert.DoesNotContain(summary.Items, i => i.Text.Contains("23H2") || i.Text.Contains("SHA256SUMS"));
    }

    [Fact]
    public void KeepAChangelogSections_AreTaggedAndPrioritized()
    {
        const string notes = """
            ### Changed
            - **Renamed from Vigil to Coreloom**, with the approved icon used as supplied (kit kept in
              `assets/branding`). It is embedded in the exe.
            - Faster charts.

            ### Fixed
            - Tray icon no longer disappears after Explorer restarts.

            ### Added
            - **Automatic updates.** New versions download in the background.
            - Per-metric alerts.

            ### Security
            - Installers are verified before they run.
            """;

        var summary = ReleaseNotesSummary.Parse(notes, maxItems: 4);

        Assert.Null(summary.Intro);
        Assert.Equal(new string?[] { "Security", "New", "New", "Improved" }, summary.Items.Select(i => i.Category).ToArray());
        Assert.Equal("Installers are verified before they run.", summary.Items[0].Text);
        Assert.Equal("Automatic updates", summary.Items[1].Title);
        Assert.Equal("New versions download in the background.", summary.Items[1].Text);
        Assert.Equal("Renamed from Vigil to Coreloom, with the approved icon used as supplied (kit kept in assets/branding).", summary.Items[3].Text);
        Assert.Equal(2, summary.MoreCount);
    }

    [Fact]
    public void VersionHeading_IsSkipped_AndPlainBulletsAreUsedWithoutSections()
    {
        const string notes = """
            ## [1.2.0] - 2026-11-01

            A small update with two fixes.

            - Fixed the CPU temperature on some AMD boards
            - Smoother sparkline scrolling
            """;

        var summary = ReleaseNotesSummary.Parse(notes);

        Assert.Equal("A small update with two fixes.", summary.Intro);
        Assert.Equal(new[] { "Fixed the CPU temperature on some AMD boards.", "Smoother sparkline scrolling." }, summary.Items.Select(i => i.Text).ToArray());
        Assert.Equal(0, summary.MoreCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("```\ncode only\n```")]
    public void EmptyOrUnusableNotes_GiveAnEmptySummary(string? notes)
    {
        Assert.True(ReleaseNotesSummary.Parse(notes).IsEmpty);
    }

    [Fact]
    public void GenericFallbackNote_BecomesTheIntro()
    {
        var summary = ReleaseNotesSummary.Parse("Coreloom 1.0.1.\n\n---\n**Verify your download:** …");
        Assert.Equal("Coreloom 1.0.1.", summary.Intro);
        Assert.Empty(summary.Items);
    }

    [Fact]
    public void OneLine_NamesTheFirstItems()
    {
        var summary = ReleaseNotesSummary.Parse(HighlightsNotes);
        Assert.Equal("Overview dashboard, Detail pages for the GPU and 5 more.", summary.ToOneLine());

        var changes = ReleaseNotesSummary.Parse("### Fixed\n- **Tray icon.** It no longer disappears.");
        Assert.Equal("Tray icon.", changes.ToOneLine());
    }

    [Fact]
    public void SentenceSplitting_KeepsAbbreviationsAndVersions()
    {
        Assert.Equal("Works with e.g. NVIDIA and AMD cards.", ReleaseNotesSummary.FirstSentences("Works with e.g. NVIDIA and AMD cards. Second sentence.", 140, single: true));
        Assert.Equal("Requires v1.2.3 or later.", ReleaseNotesSummary.FirstSentences("Requires v1.2.3 or later. More text.", 140, single: true));
    }
}

public sealed class PendingUpdateStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coreloom-pending-" + Guid.NewGuid().ToString("N"));

    private static PendingUpdate Pending(string version, string sha = "", int attempts = 0, bool failed = false) => new()
    {
        Version = version,
        TagName = "v" + version,
        Title = "Coreloom " + version,
        Notes = "### Added\n- Something new",
        InstallerFileName = $"Coreloom-{version}-win-x64-Setup.exe",
        Sha256 = sha.Length == 64 ? sha : new string('a', 64),
        InstallAttempts = attempts,
        InstallFailed = failed,
    };

    [Theory]
    [InlineData("1.1.0", PendingUpdateState.Ready)]
    [InlineData("1.0.0", PendingUpdateState.Installed)]
    [InlineData("0.9.0", PendingUpdateState.Stale)]
    [InlineData("not-a-version", PendingUpdateState.None)]
    public void Evaluate_ComparesWithTheRunningVersion(string version, PendingUpdateState expected)
    {
        Assert.Equal(expected, PendingUpdateStore.Evaluate(Pending(version), SemanticVersion.Parse("1.0.0")));
        Assert.Equal(PendingUpdateState.None, PendingUpdateStore.Evaluate(null, SemanticVersion.Parse("1.0.0")));
    }

    [Fact]
    public void SaveLoad_RoundTrips_AndMarkInstalledKeepsNotesButRemovesDownloads()
    {
        var store = new PendingUpdateStore(_dir);
        var pending = Pending("1.1.0", attempts: 1);
        store.SavePending(pending);
        File.WriteAllText(Path.Combine(_dir, pending.InstallerFileName), "installer");
        File.WriteAllText(Path.Combine(_dir, "Coreloom-1.2.0-win-x64-Setup.exe.partial"), "part");

        var loaded = store.LoadPending();
        Assert.Equal(pending, loaded);

        store.MarkInstalled(loaded!);

        Assert.Null(store.LoadPending());
        Assert.Equal("1.1.0", store.LoadInstalled()!.Version);
        Assert.Equal(0, store.LoadInstalled()!.InstallAttempts);
        Assert.Empty(Directory.GetFiles(_dir, "*Setup*"));
    }

    [Fact]
    public void DeletePackagesUpTo_RemovesOnlyFinishedVersions()
    {
        Directory.CreateDirectory(_dir);
        foreach (string name in new[] { "Coreloom-1.0.0-win-x64-Setup.exe", "Coreloom-1.1.0-win-x64-Setup.exe", "Coreloom-1.2.0-win-x64-Setup.exe",
                                        "Coreloom-1.2.0-win-x64-Setup.exe.partial", "Coreloom-1.1.1-test.1-win-x64-Setup.exe", "notes.txt",
                                        "Coreloom-1.1.0-win-x64-Portable.zip", "Coreloom-1.2.0-win-x64-Portable.zip" })
        {
            File.WriteAllText(Path.Combine(_dir, name), "x");
        }

        new PendingUpdateStore(_dir).DeletePackagesUpTo(SemanticVersion.Parse("1.1.0"));

        Assert.Equal(
            new[] { "Coreloom-1.1.1-test.1-win-x64-Setup.exe", "Coreloom-1.2.0-win-x64-Portable.zip", "Coreloom-1.2.0-win-x64-Setup.exe", "Coreloom-1.2.0-win-x64-Setup.exe.partial", "notes.txt" },
            Directory.GetFiles(_dir).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void CorruptOrIncompleteFiles_AreIgnored()
    {
        Directory.CreateDirectory(_dir);
        var store = new PendingUpdateStore(_dir);
        File.WriteAllText(store.PendingFile, "{ not json");
        Assert.Null(store.LoadPending());

        File.WriteAllText(store.PendingFile, """{ "version": "1.1.0", "installerFileName": "x-Setup.exe", "sha256": "short" }""");
        Assert.Null(store.LoadPending());
    }

    [Theory]
    [InlineData("Coreloom-1.1.0-win-x64-Setup.exe", UpdatePackageKind.Installer, true)]
    [InlineData("..\\evil.exe", UpdatePackageKind.Installer, false)]
    [InlineData("../evil.exe", UpdatePackageKind.Installer, false)]
    [InlineData("sub/Coreloom-Setup.exe", UpdatePackageKind.Installer, false)]
    [InlineData("C:\\Windows\\evil.exe", UpdatePackageKind.Installer, false)]
    [InlineData("Coreloom-1.1.0-win-x64-Portable.zip", UpdatePackageKind.Installer, false)]
    [InlineData("Coreloom-1.1.0-win-x64-Portable.zip", UpdatePackageKind.PortableZip, true)]
    [InlineData("Coreloom-1.1.0-win-x64-Setup.exe", UpdatePackageKind.PortableZip, false)]
    [InlineData("../Coreloom-1.1.0-win-x64-Portable.zip", UpdatePackageKind.PortableZip, false)]
    public void PackagePath_OnlyAcceptsAPlainNameOfTheRightKindInTheFolder(string name, UpdatePackageKind kind, bool accepted)
    {
        var store = new PendingUpdateStore(_dir);
        var path = store.PackagePath(Pending("1.1.0") with { InstallerFileName = name }, kind);
        Assert.Equal(accepted, path is not null);
        if (path is not null)
        {
            Assert.Equal(Path.Combine(_dir, name), path);
        }
    }

    [Fact]
    public void VerifiedPackage_OpensOnlyAMatchingFile()
    {
        Directory.CreateDirectory(_dir);
        string file = Path.Combine(_dir, "Coreloom-1.1.0-win-x64-Setup.exe");
        byte[] content = [1, 2, 3, 4];
        File.WriteAllBytes(file, content);
        string hash = Convert.ToHexStringLower(SHA256.HashData(content));

        using (var verified = VerifiedPackage.Open(file, hash.ToUpperInvariant()))
        {
            Assert.NotNull(verified);
            Assert.Equal(file, verified!.Path);
            using (var read = verified.OpenRead())
            {
                var copy = new MemoryStream();
                read.CopyTo(copy);
                Assert.Equal(content, copy.ToArray()); // a second handle reads the checked bytes
            }

            verified.Release();
            Assert.Throws<ObjectDisposedException>(() => verified.OpenRead());
        }

        Assert.Null(VerifiedPackage.Open(file, new string('0', 64)));
        Assert.Null(VerifiedPackage.Open(Path.Combine(_dir, "missing.exe"), hash));
        Assert.Null(VerifiedPackage.Open(file, ""));
    }

    [Fact]
    public void VerifiedPackage_RefusesALink()
    {
        Directory.CreateDirectory(_dir);
        string target = Path.Combine(_dir, "real.bin");
        byte[] content = [9, 9, 9];
        File.WriteAllBytes(target, content);
        string link = Path.Combine(_dir, "Coreloom-1.1.0-win-x64-Setup.exe");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.SkipUnless(false, "Symbolic links can't be created here.");
        }

        Assert.Null(VerifiedPackage.Open(link, Convert.ToHexStringLower(SHA256.HashData(content))));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // ignore
        }
    }
}

public class UpdateInstallPolicyTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    private static PendingUpdate Pending(int attempts = 0, bool failed = false) => new()
    {
        Version = "1.1.0",
        InstallerFileName = "Coreloom-1.1.0-win-x64-Setup.exe",
        Sha256 = new string('a', 64),
        InstallAttempts = attempts,
        InstallFailed = failed,
    };

    private static readonly BackgroundInstallSignals Idle = new()
    {
        WindowInUse = false,
        UserIdle = TimeSpan.FromMinutes(20),
        SystemQuiet = TimeSpan.FromMinutes(12),
        MonitoringPaused = false,
    };

    private static bool Background(BackgroundInstallSignals signals, PendingUpdate? pending = null, bool auto = true, bool canInstall = true, double readyMinutes = 30, double uptimeMinutes = 60) =>
        UpdateInstallPolicy.ShouldInstallInBackground(auto, canInstall, pending ?? Pending(), Now.AddMinutes(-readyMinutes), Now.AddMinutes(-uptimeMinutes), Now, signals);

    [Fact]
    public void Background_InstallsOnlyWhenNobodyIsUsingThePc()
    {
        Assert.True(Background(Idle));
        Assert.False(Background(Idle with { WindowInUse = true }));
        Assert.False(Background(Idle with { UserIdle = TimeSpan.FromMinutes(14) }));
        Assert.False(Background(Idle with { SystemQuiet = TimeSpan.FromMinutes(9) }));
        Assert.False(Background(Idle with { MonitoringPaused = true }));
        Assert.False(Background(Idle, readyMinutes: 2));
        Assert.False(Background(Idle, uptimeMinutes: 5));
    }

    [Fact]
    public void Background_RespectsSettingsAndAttempts()
    {
        Assert.False(Background(Idle, auto: false));
        Assert.False(Background(Idle, canInstall: false));
        Assert.False(Background(Idle, Pending(attempts: UpdateInstallPolicy.MaxAutomaticAttempts)));
        Assert.False(Background(Idle, Pending(failed: true)));
        Assert.True(Background(Idle, Pending(attempts: 1)));
    }

    [Fact]
    public void Startup_InstallsReadyUpdatesUnlessTheyKeepFailing()
    {
        Assert.True(UpdateInstallPolicy.ShouldInstallAtStartup(true, true, Pending()));
        Assert.False(UpdateInstallPolicy.ShouldInstallAtStartup(true, true, null));
        Assert.False(UpdateInstallPolicy.ShouldInstallAtStartup(false, true, Pending()));
        Assert.False(UpdateInstallPolicy.ShouldInstallAtStartup(true, false, Pending()));
        Assert.False(UpdateInstallPolicy.ShouldInstallAtStartup(true, true, Pending(attempts: 2)));
        Assert.False(UpdateInstallPolicy.ShouldInstallAtStartup(true, true, Pending(failed: true)));
    }

    [Theory]
    [InlineData(10.0, 5.0, false)]
    [InlineData(25.0, 0.0, true)]
    [InlineData(3.0, 20.0, true)]
    [InlineData(null, null, false)]
    public void SystemBusy_UsesCpuAndGpuLoad(double? cpu, double? gpu, bool busy)
    {
        Assert.Equal(busy, UpdateInstallPolicy.IsSystemBusy(cpu, gpu));
    }

    [Fact]
    public void InstallerArguments_MatchTheInstallerScript()
    {
        Assert.Equal("/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /UPDATE=1", UpdateInstallOptions.Interactive.ToInstallerArguments());
        Assert.Equal("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /UPDATE=1 /MINIMIZED=1", UpdateInstallOptions.Background.ToInstallerArguments());

        string script = File.ReadAllText(Path.Combine(RepoRoot(), "installer", "Coreloom.iss"));
        Assert.Contains("{param:UPDATE|0}", script);
        Assert.Contains("{param:MINIMIZED|0}", script);
        Assert.Contains("--update-failed", script);
    }

    /// <summary>The repository root, found from this source file's location (works wherever the tests run).</summary>
    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        var dir = new FileInfo(sourceFile).Directory;
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Coreloom.sln")))
        {
            dir = dir.Parent;
        }

        Assert.SkipUnless(dir is not null, "Repository root not found.");
        return dir!.FullName;
    }
}

public class UpdateSettingsLaunchTests
{
    [Fact]
    public void RecordLaunch_DetectsTheFirstLaunchAfterAnUpdate()
    {
        var updates = new UpdateSettings();

        Assert.False(updates.RecordLaunch(SemanticVersion.Parse("1.0.0")));  // first launch ever
        Assert.Equal("1.0.0", updates.LastRunVersion);
        Assert.Null(updates.WhatsNewVersion);

        Assert.False(updates.RecordLaunch(SemanticVersion.Parse("1.0.0")));  // same version again

        Assert.True(updates.RecordLaunch(SemanticVersion.Parse("1.1.0")));   // updated
        Assert.Equal("1.1.0", updates.WhatsNewVersion);
        Assert.Equal("1.1.0", updates.LastRunVersion);

        Assert.False(updates.RecordLaunch(SemanticVersion.Parse("1.1.0")));  // banner stays until dismissed
        Assert.Equal("1.1.0", updates.WhatsNewVersion);

        Assert.False(updates.RecordLaunch(SemanticVersion.Parse("1.0.5")));  // an older build clears it
        Assert.Null(updates.WhatsNewVersion);
    }

    [Fact]
    public void RecordLaunch_TreatsAnUnrecordedEarlierRunAsAnUpdate()
    {
        var updatedFromOneZero = new UpdateSettings();
        Assert.True(updatedFromOneZero.RecordLaunch(SemanticVersion.Parse("1.1.0"), ranBefore: true));
        Assert.Equal("1.1.0", updatedFromOneZero.WhatsNewVersion);

        var freshInstall = new UpdateSettings();
        Assert.False(freshInstall.RecordLaunch(SemanticVersion.Parse("1.1.0"), ranBefore: false));
        Assert.Null(freshInstall.WhatsNewVersion);
    }

    [Fact]
    public void AutoInstall_DefaultsOn_IncludingForOlderSettingsFiles()
    {
        Assert.True(new UpdateSettings().AutoInstall);
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "coreloom-upd-" + Guid.NewGuid().ToString("N"), "settings.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, """{ "updates": { "frequency": "Weekly" } }""");
        var loaded = store.Load();
        Assert.True(loaded.Updates.AutoInstall);
        Assert.Equal(UpdateCheckFrequency.Weekly, loaded.Updates.Frequency);
        Directory.Delete(Path.GetDirectoryName(store.FilePath)!, true);
    }
}

public class ReleaseByTagTests
{
    [Fact]
    public async Task GetByTagAsync_ReadsOneRelease_AndReturnsNullWhenMissing()
    {
        string? requested = null;
        var handler = new StubHandler(req =>
        {
            requested = req.RequestUri!.ToString();
            return requested.EndsWith("/v1.1.0", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        { "tag_name": "v1.1.0", "name": "Coreloom 1.1.0", "body": "### Added\n- **Automatic updates.** Yes.", "draft": false,
                          "prerelease": false, "html_url": "https://github.com/o/r/releases/tag/v1.1.0", "assets": [] }
                        """),
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var client = new GitHubReleaseClient(new HttpClient(handler), "o/r", "ua");

        var release = await client.GetByTagAsync("v1.1.0");

        Assert.Equal("https://api.github.com/repos/o/r/releases/tags/v1.1.0", requested);
        Assert.Equal("1.1.0", release!.Version.ToString());
        Assert.Equal("Automatic updates", ReleaseNotesSummary.Parse(release.Notes).Items[0].Title);
        Assert.Null(await client.GetByTagAsync("v9.9.9"));
    }
}

public sealed class DownloaderLinkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coreloom-link-" + Guid.NewGuid().ToString("N"));

    public DownloaderLinkTests()
    {
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public async Task Download_ReplacesALinkedPartialFile_WithoutTouchingItsTarget()
    {
        string victim = Path.Combine(_dir, "victim.txt");
        File.WriteAllText(victim, "keep me");
        string dest = Path.Combine(_dir, "updates", "setup.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        try
        {
            File.CreateSymbolicLink(dest + ".partial", victim);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.SkipUnless(false, "Symbolic links can't be created here.");
        }

        byte[] payload = [1, 2, 3];
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
        await new ResumableDownloader(new HttpClient(handler)).DownloadAsync(new Uri("https://example.com/x"), dest, payload.Length, null, CancellationToken.None);

        Assert.Equal("keep me", File.ReadAllText(victim));
        Assert.Equal(payload, File.ReadAllBytes(dest));
    }

    [Fact]
    public async Task Download_RefusesALinkedFolder()
    {
        string real = Path.Combine(_dir, "elsewhere");
        Directory.CreateDirectory(real);
        string linked = Path.Combine(_dir, "updates");
        try
        {
            Directory.CreateSymbolicLink(linked, real);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.SkipUnless(false, "Symbolic links can't be created here.");
        }

        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) });
        await Assert.ThrowsAsync<UpdateException>(() =>
            new ResumableDownloader(new HttpClient(handler)).DownloadAsync(new Uri("https://example.com/x"), Path.Combine(linked, "setup.exe"), 1, null, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(real));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // ignore
        }
    }
}
