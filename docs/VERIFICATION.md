# Verification report

This file states plainly what has been verified about Coreloom and what hasn't. Update it as on-device testing
results come in.

## Build environment

Coreloom was developed in a Linux container with these properties:

- .NET SDK 10.0.112, runtime 10.0.12
- No Windows, no GPU
- **No access to nuget.org** (blocked by the environment's network policy)

That last point had consequences:

- The real NuGet packages couldn't be restored: CommunityToolkit.Mvvm, LibreHardwareMonitorLib, xunit.v3
  and the Windows Desktop (WPF) reference pack.
- To compile and test anyway, a local harness replaced them with stand-ins:
  - a minimal xUnit-compatible runner (Fact/Theory/InlineData/Assert subset);
  - a minimal MVVM shim (ObservableObject, RelayCommand, AsyncRelayCommand);
  - an API stub of LibreHardwareMonitorLib.
- The stand-ins are not part of the repository. The repository references the real packages, pinned in
  `Directory.Packages.props`. Their versions were checked to exist on nuget.org.

## Verified here

| What | How | Result |
|---|---|---|
| `Coreloom.Core` compiles | `dotnet build` (net10.0) | ✅ |
| Core unit tests: telemetry engine, history ring buffers, M4 downsampling, axis/curve math, alerts, settings persistence, formatting, semantic versions, release selection, checksum parsing, resumable download, SMBIOS parsing, diagnostic report incl. redaction | Real test code run through the shim runner | ✅ 160 passed, 0 failed (includes the settings-import, vendor-detection and splash-policy tests) |
| `Coreloom.Hardware` compiles | `dotnet build` targeting net10.0-windows from Linux. LibreHardwareMonitor adapter compiled against an API stub whose signatures were checked against the v0.9.6 sources (`Computer`, `IHardware`, `ISensor`, `SensorType`, `HardwareType`). | ✅ |
| Hardware mapping tests: LibreHardwareMonitor sensor name → canonical metric, GPU directory, disk matching, NVML return codes | shim runner | ✅ 47 passed. The 8 Windows integration tests were skipped, as designed, on Linux. |
| `Coreloom.ViewModels` compiles | `dotnet build` (net10.0) with the MVVM shim | ✅ |
| View-model tests: metric formatting and unavailable reasons, °F conversion, dashboard layout, overview cards, CSV export, settings service, shell navigation, section disabling, tray text, polling interval, sign-in task sync, custom accent, alert rule editing, history selection, vendor marks on the GPU and CPU pages | shim runner | ✅ 23 passed |
| `CoreloomDiag` compiles | `dotnet build` | ✅ |
| `Coreloom.App` C# is syntactically valid | Roslyn parse of all source files (97 files under `src`, 14 under `tests`), language version `preview` | ✅ 0 errors |
| `Coreloom.App` C# uses the Core/Hardware/ViewModels APIs correctly | Roslyn semantic compile **without** WPF references, keeping method-body errors. Every remaining error was checked to come only from missing WPF/WinForms types or members, XAML-generated members (`InitializeComponent`, `x:Name` fields) or source-generated P/Invokes. A probe file confirmed that this setup does catch wrong member names on Coreloom's own types. | ✅ No errors in Coreloom's own APIs |
| XAML consistency (15 files, 149 resource keys) | `python3 build/check-xaml.py`, which checks: well-formed XML; x:Class ↔ code-behind; StaticResource defined before use; DynamicResource keys exist (including the brushes ThemeService creates at runtime); event handlers exist; `ElementName` targets exist; 396 binding paths resolve to known properties; icon keys; pack URIs (including linked resources and every embedded font family); settings sections. A mutation test confirmed it catches typos. | ✅ |

## Reported by the owner (not independently verified here)

The first Windows build (as Vigil, before the rename) went through three rounds of fixes on GitHub Actions and
on the owner's PC: the CI build is green, and after the last fix the owner reported that the app starts and
runs without errors. That settles that the XAML compiles and the app launches. It does **not** settle sensor
accuracy, overhead, or the details below, and none of it was observed by the author.

The first CI run with the vendor-mark, tuning and splash changes (run #6) failed in the Windows job's XAML compile:
"Tags of type 'PropertyArrayStart' are not supported in template sections" at `Templates.xaml` line 522. The tuning
chart's `Points` property was array-typed, which WPF can't set inside a template. It is now `IReadOnlyList<T>`, and
`check-xaml.py` flags array-typed dependency properties. With that fix the Windows build went green and the owner
ran the app, including the GPU tuning panel. The owner then asked for the tuning feature (overclock indicator,
*Tuning* button and panel) to be removed; it was, and the vendor marks stayed.

## Changes in this version and what backs them

| Change | What was done here | Not verified here |
|---|---|---|
| Inter + JetBrains Mono, sharper text | Fonts embedded as `Resource`s; Display text formatting, fixed hinting, ClearType hints; `check-xaml.py` confirms every referenced family exists in the embedded files | How the text actually looks and renders at your DPI |
| Animation rate up to the display's refresh rate | Frame-rate override from `EnumDisplaySettings`; page views cached | Real smoothness on a 240 Hz panel (WPF treats the rate as a request, and the DWM decides what is shown) |
| Leak hygiene | Chart/donut/sparkline handlers detach on `Unloaded`; bound immutable items are weak-event safe; per-frame arrays reused; managed heap and handle count are shown in Settings › Diagnostics | That memory stays flat over hours: no leak profiler was run. Use the checklist in TESTING-ON-YOUR-PC.md |
| Always runs as administrator | `requireAdministrator` manifest; sign-in task replaces the Run key; old UI and commands removed; unit tests for the task sync | The UAC prompt, the elevated task actually starting at sign-in, the uninstaller's task removal |
| Renamed Coreloom + supplied icon | Whole-repo rename; the supplied `coreloom.ico` and PNGs are used byte-for-byte (`cmp` identical) for the exe, window, tray (PNG sizes), installer and shortcuts; settings import is unit-tested | That the compiled exe, installer and shortcuts show the icon (needs a Windows build); Windows may show a cached old icon until the icon cache refreshes |

### Vendor marks

| Change | What was done here | Not verified here |
|---|---|---|
| Vendor mark next to the CPU and GPU names | `HardwareVendors` (vendor string first, device name second) is unit-tested with GeForce/RTX/Quadro, Radeon/Ryzen/EPYC, Intel/Arc/Xeon names and ambiguous input; `BrandMark` shows a supplied file or a text badge; `check-xaml.py` confirms the resource pack URI and the wildcard `Resource` item | That a real logo file loads and looks right at your DPI and on both themes. No third-party logos are bundled, so the badge is what you see until you add files |

### Startup splash

| Change | What was done here | Not verified here |
|---|---|---|
| Splash window (native XAML port of the supplied kit) | Layout numbers taken from the kit's `splash.css` measured at 700 × 370 in Chromium with the embedded Inter (the measured layout matches the supplied preview PNG); icon is the approved `png/coreloom-512.png`, byte-identical to the kit's `coreloom-app-icon.png`; `check-xaml.py` confirms the pack URIs, the embedded font, and that the splash uses no App.xaml resources | How it actually renders on Windows (letter spacing, gradients, sharpness at 125–200 %), and that `CenterScreen` puts it on the monitor with the mouse |
| Splash on its own UI thread | Code review; Roslyn checks show only the expected "WPF types missing" errors; `InheritanceBehavior` isolation enforced by `check-xaml.py` | That the line keeps moving while the dashboard is built, and that nothing cross-thread throws at runtime |
| Readiness and handoff (cloak → first frame → minimum 850 ms → raise splash → uncloak → crossfade) | Timing rules unit-tested in `SplashPolicyTests` (start-hidden rule, minimum time, reduced motion, timing ranges from the brief) | That `DWMWA_CLOAK` hides the first frames as intended, the crossfade looks smooth, focus lands on the dashboard, and no extra taskbar/Alt+Tab entry appears |
| Failure and exit paths | Error state plus the existing dialogs (a splash not yet on screen is cancelled so it can't cover the dialog); every exit path closes the splash; background thread; 10 s watchdog for a window that never paints | Behaviour of a real failed start |
| Independent read-through | A separate review pass over the splash code looked for compile errors, cross-thread access and lifecycle bugs. It found no compile or threading problems; its lifecycle findings (late splash over the error dialog, minimum time counted before the splash appeared, a blocking cross-thread call, watchdog priority, double close, live-region announcements) were fixed | Still not a substitute for compiling and running on Windows |

### Automatic updates

How this change was checked in the Linux container (no Windows, no nuget.org):

- **Core tests: 199 passed** (160 before, plus 39 new for the release-notes summary, the pending-update store,
  installer verification and link refusal, clean-up of old installers, the install policy and installer arguments, launch tracking and the
  downloader's link hardening, and reading one release by tag), through the stand-in xUnit runner.
- **View-model tests: 44 passed** (23 before, plus 21 new for the update state machine against a fake GitHub:
  automatic download → verify → ready → install, automatic updates off, portable copies, checksum mismatch,
  restoring a download after a restart, startup install and its attempt cap, `--update-failed`, untrusted or
  changed installers, idle-time installs, the "Updated to" banner, skip, a newer release replacing a download,
  failed checks, the shell badge, and What's new after updating from 1.0.0, which left no notes behind).
- **`Coreloom.App` compiles against the real WPF API surface.** WPF's reference-assembly sources (dotnet/wpf
  `src/*/ref/*.cs`) were compiled into a reference assembly and the whole app (`App.xaml.cs`, services, views'
  code-behind, the new trust store and idle detection, the source-generated P/Invokes) was compiled against it,
  with the XAML compiler's generated members (`InitializeComponent`, `x:Name` fields) produced from the XAML.
  Only the WinForms `NotifyIcon`, a few `System.Drawing` members and `SystemEvents` were stubbed. 0 errors,
  0 warnings, and a probe with wrong member names on WPF and Coreloom types was rejected as expected. This is
  stronger than the earlier Roslyn-without-WPF check, but the XAML itself is still only checked by
  `check-xaml.py` (passed: 15 files, 151 resource keys, 435 bindings; a mutation test caught typos in the new
  bindings).

| Change | What was done here | Not verified here |
|---|---|---|
| Background download + verification | Unit tests against a fake GitHub (listing, `SHA256SUMS.txt`, installer bytes) | Real downloads from GitHub, proxies, a 70 MB installer |
| Install at next start / while idle / *Restart to update* | Policy and state machine unit-tested; idle detection uses `GetLastInputInfo` and the CPU/GPU load already measured | That the installer runs, closes Coreloom and relaunches it (`/UPDATE=1`, `/MINIMIZED=1`) |
| Installer rollback relaunch (`--update-failed`) | Pascal code reviewed; the arguments the app passes are tested against the script's parameter names | Inno Setup compiling the new `[Code]` and the rollback path on a real failure |
| HKLM trust store | Code review; the view model's trust logic is tested with an in-memory store | Writing/reading `HKLM\SOFTWARE\Coreloom\Updates` on Windows |
| Settings › Updates, badge, banner, tray item | XAML checks; bindings resolve; app compiles | How it all looks and behaves on screen |
| Release workflow: existing release, re-run by tag | YAML parses; PowerShell reviewed | A real run. (The likely reason `v1.0.0` was published without its files: its release already existed when the workflow tried to create it, and that step failed.) |

### Overview header and page layout (after 1.1.0)

- **Core tests: 241 passed** (199 before, plus 42 for `SystemDescriptions`: board vendor names and placeholder
  values, GPU badges and card sizes for NVIDIA/AMD/Intel names, memory, cores and Windows lines).
- **View-model tests: 50 passed** (44 before, plus 6: the Overview header from the fixture's summary and GPU list,
  the header following the selected GPU, and the two-column sensor split).
- **`Coreloom.App` compiles** against the WPF reference assembly as above, including `PcIllustration`, `PcImages`,
  `ScrollChain` and the new `AdaptiveGrid.RowSpacing`: 0 errors, 0 warnings.
- **XAML checks passed** (16 files, 153 resource keys, 456 bindings). A mutation test caught a misspelt header
  binding, a misspelt illustration binding and an undefined style. The checker now also resolves `Resource` items
  whose `Link` uses `%(Filename)%(Extension)`.
- `build/pc-illustration.py` regenerates `PcIllustration.xaml` byte-for-byte, and its SVG output was rendered in
  Chromium for two hardware variants and accents to check the drawing.

| Change | What was done here | Not verified here |
|---|---|---|
| Overview header and PC drawing | Unit tests for the text; bindings checked; SVG renders of the same shapes | How the XAML drawing renders in WPF (gradients, the accent mask, sharpness at your scaling) |
| Optional `pc.png` | Code review; loader decodes once, size-capped, errors fall back to the drawing | Loading a real file on Windows |
| GPU specifications beside VRAM, sensors in two columns | Split logic unit-tested; XAML checks | The final look at your window size, and the wheel hand-off from the inner list to the page |

### Install update for every copy (after 1.1.0)

- **Core tests: 269 passed** (241 before, plus 28: `FolderUpdate` staging, applying and cleaning up, keeping `Data`
  and `portable.txt`, zips with a top folder, unsafe paths and non-Coreloom zips refused with nothing left behind,
  a failed copy undone file by file, links planted in the work folder, the helper's location and arguments, and
  picking the installer or the zip per kind of copy).
- **View-model tests: 53 passed** (50 before, plus 3, and the update tests rewritten for *Install update*: one click
  downloads, verifies and installs; portable copies download the zip and install in place; a download for the
  other kind of copy is discarded; an update that can't be unpacked stays ready and explains; a release without
  the needed package explains).
- **`Coreloom.App` compiles** against the WPF reference assembly with the helper mode, the per-copy registry key and
  the new Settings card: 0 errors, 0 warnings. **XAML checks passed** (16 files, 153 keys, 453 bindings).

| Change | What was done here | Not verified here |
|---|---|---|
| *Install update* (download if needed → install → reopen) | View-model tests against a fake GitHub | The whole round trip on Windows |
| In-place update of portable copies | File operations unit-tested on Linux (staging, copy, rollback, cleanup) | Windows specifics: the elevated helper starting without a prompt, locked files and antivirus timing, the hidden `.coreloom-update` folder, the splash while copying, `Compress-Archive` zips from the real release |
| Per-copy trust key `HKLM\SOFTWARE\Coreloom\Updates\Copies\<id>` | Code review | Registry writes and clean-up on Windows |

## Not verified yet (needs Windows and the real hardware)

None of the following has been confirmed by the author. Don't treat any of it as working until the checklist in
[TESTING-ON-YOUR-PC.md](TESTING-ON-YOUR-PC.md) has been completed.

- **This version has not been compiled on Windows.** Its changes (fonts, `PageHost`, `DisplayInfo`, tray icon
  from PNG, startup task, linked icon resources) are checked only by the Roslyn and XAML checks above; GitHub
  Actions is the first real compile.
- **Visual details**: the startup splash and its handoff, the vendor badge/logo, layout, theming, Mica, chart rendering and smooth scrolling, animations, tray,
  notifications, single instance, startup registration and the elevated task.
- **Sensors on real hardware**, by any provider:
  - NVML calls, struct layouts and per-metric support on the RTX 5090;
  - PDH counters;
  - storage IOCTLs;
  - SMBIOS on the ASRock X870E;
  - LibreHardwareMonitor sensor names on the 9950X3D;
  - PawnIO detection.
- **Overhead is unmeasured.** The CPU and memory budgets in the docs are design targets, not measurements.
- **The installer script (including the new `shellexec` launch and uninstall task removal), `build.ps1` and the
  release workflow have never been run.**
- **The update flow has not been exercised end to end** against real GitHub releases. Its logic
  (version selection, checksums, resume, automatic download and install, the what's-new summary) is
  unit-tested; section 6 of TESTING-ON-YOUR-PC.md walks through it with two releases.
- **No comparison with established tools** (HWiNFO64, the NVIDIA app, Task Manager) has been made.

## Highest-risk areas

These are the places most likely to need fixes on the first Windows build, in rough order.

1. **XAML compile errors** the static check can't see: property value types, style `TargetType`
   mismatches, template details. These are usually quick to fix from the compiler's message.
2. **Window chrome.** This covers `WindowChrome` combined with DWM attributes:
   - the Mica backdrop (DWMWA 38) and dark mode (DWMWA 20);
   - `GlassFrameThickness` switching;
   - the maximized-window inset;
   - the `WM_NCHITTEST` hook that makes the custom maximize button show the Snap Layouts flyout.
3. **Custom chart controls** (`LiveLineChart`, `Sparkline`, `RadialGauge`, `DonutChart`). They are written
   against WPF's `DrawingVisual` APIs but have never rendered a frame.
4. **LibreHardwareMonitor mapping** on the 9950X3D. It expects "Core (Tctl/Tdie)" and "CCD1 (Tdie)" style
   names, with fallbacks. If the names differ, temperatures will show as unavailable rather than wrong.
   CoreloomDiag's `--lhm-report` shows the real names.
5. **P/Invoke marshalling.** NVML (function-pointer calls and structs), PDH, storage IOCTL structs, and
   `NtQuerySystemInformation` for processes.
6. **Tray context menu.** It is a WPF `ContextMenu` opened from WinForms' `NotifyIcon`, using the
   foreground-window workaround.
7. **Inno Setup script.** Preprocessor expressions, the `/UPDATE=1` relaunch with `--updated`/`--minimized`, and
   the new `[Code]` that restarts the old version after a failed update.

## How to re-run the checks that can run anywhere

```bash
dotnet test tests/Coreloom.Core.Tests            # needs nuget.org access for the real xunit packages
dotnet test tests/Coreloom.ViewModels.Tests
python3 build/check-xaml.py
```

On Windows, `./build/build.ps1` builds everything and runs every test, including the Windows integration
tests against the real machine.
