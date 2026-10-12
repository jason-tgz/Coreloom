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
| Core unit tests: telemetry engine, history ring buffers, M4 downsampling, axis/curve math, alerts, settings persistence, formatting, semantic versions, release selection, checksum parsing, resumable download, SMBIOS parsing, diagnostic report incl. redaction | Real test code run through the shim runner | ✅ 117 passed, 0 failed (includes the settings-import tests) |
| `Coreloom.Hardware` compiles | `dotnet build` targeting net10.0-windows from Linux. LibreHardwareMonitor adapter compiled against an API stub whose signatures were checked against the v0.9.6 sources (`Computer`, `IHardware`, `ISensor`, `SensorType`, `HardwareType`). | ✅ |
| Hardware mapping tests: LibreHardwareMonitor sensor name → canonical metric, GPU directory, disk matching, NVML return codes | shim runner | ✅ 47 passed. The 8 Windows integration tests were skipped, as designed, on Linux. |
| `Coreloom.ViewModels` compiles | `dotnet build` (net10.0) with the MVVM shim | ✅ |
| View-model tests: metric formatting and unavailable reasons, °F conversion, dashboard layout, overview cards, CSV export, settings service, shell navigation, section disabling, tray text, polling interval, sign-in task sync, custom accent, alert rule editing, history selection | shim runner | ✅ 20 passed |
| `CoreloomDiag` compiles | `dotnet build` | ✅ |
| `Coreloom.App` C# is syntactically valid | Roslyn parse of all app files (90 files across `src`, 11 in `tests`), language version `preview` | ✅ 0 errors |
| `Coreloom.App` C# uses the Core/Hardware/ViewModels APIs correctly | Roslyn semantic compile **without** WPF references, keeping method-body errors. Every remaining error was checked to come only from missing WPF/WinForms types or members, XAML-generated members (`InitializeComponent`, `x:Name` fields) or source-generated P/Invokes. A probe file confirmed that this setup does catch wrong member names on Coreloom's own types. | ✅ No errors in Coreloom's own APIs |
| XAML consistency (14 files) | `python3 build/check-xaml.py`, which checks: well-formed XML; x:Class ↔ code-behind; StaticResource defined before use; DynamicResource keys exist (including the brushes ThemeService creates at runtime); event handlers exist; `ElementName` targets exist; 392 binding paths resolve to known properties; icon keys; pack URIs (including linked resources and every embedded font family); settings sections. A mutation test confirmed it catches typos. | ✅ |

## Reported by the owner (not independently verified here)

The first Windows build (as Vigil, before the rename) went through three rounds of fixes on GitHub Actions and
on the owner's PC: the CI build is green, and after the last fix the owner reported that the app starts and
runs without errors. That settles that the XAML compiles and the app launches. It does **not** settle sensor
accuracy, overhead, or the details below, and none of it was observed by the author.

## Changes in this version and what backs them

| Change | What was done here | Not verified here |
|---|---|---|
| Inter + JetBrains Mono, sharper text | Fonts embedded as `Resource`s; Display text formatting, fixed hinting, ClearType hints; `check-xaml.py` confirms every referenced family exists in the embedded files | How the text actually looks and renders at your DPI |
| Animation rate up to the display's refresh rate | Frame-rate override from `EnumDisplaySettings`; page views cached | Real smoothness on a 240 Hz panel (WPF treats the rate as a request, and the DWM decides what is shown) |
| Leak hygiene | Chart/donut/sparkline handlers detach on `Unloaded`; bound immutable items are weak-event safe; per-frame arrays reused; managed heap and handle count are shown in Settings › Diagnostics | That memory stays flat over hours: no leak profiler was run. Use the checklist in TESTING-ON-YOUR-PC.md |
| Always runs as administrator | `requireAdministrator` manifest; sign-in task replaces the Run key; old UI and commands removed; unit tests for the task sync | The UAC prompt, the elevated task actually starting at sign-in, the uninstaller's task removal |
| Renamed Coreloom + supplied icon | Whole-repo rename; the supplied `coreloom.ico` and PNGs are used byte-for-byte (`cmp` identical) for the exe, window, tray (PNG sizes), installer and shortcuts; settings import is unit-tested | That the compiled exe, installer and shortcuts show the icon (needs a Windows build); Windows may show a cached old icon until the icon cache refreshes |

## Not verified yet (needs Windows and the real hardware)

None of the following has been confirmed by the author. Don't treat any of it as working until the checklist in
[TESTING-ON-YOUR-PC.md](TESTING-ON-YOUR-PC.md) has been completed.

- **This version has not been compiled on Windows.** Its changes (fonts, `PageHost`, `DisplayInfo`, tray icon
  from PNG, startup task, linked icon resources) are checked only by the Roslyn and XAML checks above; GitHub
  Actions is the first real compile.
- **Visual details**: layout, theming, Mica, chart rendering and smooth scrolling, animations, tray,
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
  (version selection, checksums, resume) is unit-tested.
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
7. **Inno Setup script.** Preprocessor expressions and the `/UPDATE=1` relaunch.

## How to re-run the checks that can run anywhere

```bash
dotnet test tests/Coreloom.Core.Tests            # needs nuget.org access for the real xunit packages
dotnet test tests/Coreloom.ViewModels.Tests
python3 build/check-xaml.py
```

On Windows, `./build/build.ps1` builds everything and runs every test, including the Windows integration
tests against the real machine.
