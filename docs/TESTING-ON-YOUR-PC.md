# Testing Coreloom on your PC

Coreloom was written and unit-tested in a Linux build environment that had no Windows, no GPU and no
access to NuGet. The Windows app has **not been built or run yet** (see [VERIFICATION.md](VERIFICATION.md)).
This checklist takes you from a fresh clone to a verified build on the target machine:

- Windows 11 x64
- PNY GeForce RTX 5090
- AMD Ryzen 9 9950X3D
- 64 GB RAM
- ASRock X870E Nova WiFi

Plan for about an hour. Note anything that doesn't match the **Expect** lines; that's exactly what needs
fixing.

## 0. Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0): `dotnet --version` should print 10.0.1xx.
- Git.
- Optional, for comparison: [HWiNFO64](https://www.hwinfo.com/) (sensors-only mode) and the NVIDIA app
  overlay.
- Optional, for CPU temperature and motherboard sensors: [PawnIO](https://pawnio.eu). Install it only if
  you want those sensors; Coreloom works without it.
- Optional, for building the installer: [Inno Setup 6](https://jrsoftware.org/isinfo.php).

## 1. Build and run the tests

```powershell
git clone https://github.com/<you>/Coreloom.git
cd Coreloom
./build/build.ps1
```

**Expect:** the restore, build and all tests succeed. On Windows, the integration tests in
`Coreloom.Hardware.Tests` really run, against PDH, storage, SMBIOS, NVML and processes on your machine. On
Linux they are skipped.

If the build fails, copy the first error and stop here; everything else depends on it. The most likely
spots are listed in [VERIFICATION.md](VERIFICATION.md#highest-risk-areas).

## 2. CoreloomDiag as a standard user

Run this from a normal (non-administrator) terminal:

```powershell
dotnet run --project src/Coreloom.Diagnostics -c Release -- --samples 30 --compare --out diag-standard.md
```

**Expect:**

- **GPU**: utilization, temperature, core and memory clock, VRAM used/total, power, power limit, fan %,
  P-state and clock-limit reasons are all present from **NVML**. Hot spot, memory junction, fan RPM and
  voltage come from LibreHardwareMonitor, or show as unavailable with a reason.
- **`--compare` section**: NVML and `nvidia-smi` agree on temperature (±1 °C), clocks (±1 bin), power
  (±5 %) and memory.
- **CPU**: utilization and the derived clock are present. Temperature, package power and per-core clocks
  show as unavailable, with the reason "requires administrator rights and the PawnIO driver".
- **Memory**: 64 GB total. Modules are listed from SMBIOS.
- **Storage**: every disk is listed with read/write throughput, and NVMe temperature where Windows exposes
  it to standard users.
- **Network**: the active adapter is the one with your default gateway.
- **Overhead**: the "Overhead while sampling" line prints Coreloom's own CPU use. The design budget is well
  under 1 % of total CPU at a 1 s interval. Please report what you see.

## 3. CoreloomDiag as administrator, with PawnIO installed

Run this from an elevated terminal:

```powershell
dotnet run --project src/Coreloom.Diagnostics -c Release -- --samples 30 --lhm-report --out diag-admin.md
```

**Expect:**

- CPU temperature from LibreHardwareMonitor's "Core (Tctl/Tdie)" sensor.
- CCD1 and CCD2 temperatures.
- Package power and per-core clocks.
- Motherboard fans, voltages and temperatures.

Compare a few values with HWiNFO64. The **Manual comparison checklist** section of the report lists the
tolerances to expect. If CPU temperature is still missing, look in the `--lhm-report` section for the
sensor names LibreHardwareMonitor actually reports. The mapping lives in
`src/Coreloom.Hardware/SensorMapping.cs` and may need a name added.

> The `--lhm-report` section is LibreHardwareMonitor's raw output and can include drive or memory serial
> numbers. Review it before sharing.

## 4. The app

```powershell
dotnet run --project src/Coreloom.App -c Release
```

Work through each area and tick what works.

**Startup splash** (use the built `Coreloom.exe`, the installed copy and the portable zip at least once each)

- [ ] Double-click `Coreloom.exe` and accept the UAC prompt: the dark Coreloom splash (icon, "Coreloom",
      "SYSTEM PERFORMANCE", a thin moving blue line) appears centred on the monitor with your mouse pointer and
      fades in gently. No white or black flash before it.
- [ ] The splash fades into the dashboard; the dashboard never appears empty, white or half-drawn first.
- [ ] While the splash is up, the taskbar shows no extra Coreloom button and Alt+Tab doesn't list the splash.
      Afterwards there is exactly one Coreloom taskbar button, with the Coreloom icon.
- [ ] Cold start (first launch after a reboot) and warm start (launch again right after exiting): the splash shows
      in both, stays up at least a moment on the fast one, and doesn't hang around after the window is ready.
      The log has two `Startup` lines with the timings; note them.
- [ ] Display scaling 125 %, 150 % and 200 % (*Settings → System → Display → Scale*): the icon and text are sharp
      and nothing is cut off.
- [ ] Two monitors: start Coreloom with the mouse on each monitor; the splash follows the mouse. (If the dashboard
      was last open on the other monitor, the handoff happens across monitors; that is expected.)
- [ ] Windows *Settings → Accessibility → Visual effects → Animation effects* off, or Coreloom's *Reduce motion*
      on: the splash appears without the fade/rise, the line is still (not moving) and the hand-off has no fade.
- [ ] Windows in light mode and dark mode: the splash stays dark (brand design); the dashboard follows its theme.
- [ ] With Coreloom already running, start it again: no splash; the running window comes to the front.
- [ ] `Coreloom.exe --minimized`, and *Start with Windows* with *Start minimized*: no splash; Coreloom goes straight
      to the notification area. Opening it from the tray shows the window without a splash.
- [ ] After closing Coreloom (tray → Exit), Task Manager (Details) shows no Coreloom process left behind.
- [ ] Start Menu and desktop shortcuts from the installer launch with the splash as well.

**Window and appearance**

- [ ] The window opens with dark Mica (or the solid dark background if Mica is off).
- [ ] Minimize, maximize and close work. Hovering maximize shows the Windows 11 Snap Layouts flyout.
- [ ] A maximized window has no clipped edges.
- [ ] The sidebar collapses with its button, and on its own below about 980 px window width.
- [ ] *Settings → Appearance*: light/dark/system themes animate. Accent swatches and a custom hex like
      `#FF5C8A` apply everywhere. *Reduce motion* stops the animations.

**Overview**

- [ ] The six cards show live values with gauges and sparklines.
- [ ] The numbers match the NVIDIA overlay and Task Manager within the usual tolerances.
- [ ] *Customize* (or *Settings → Dashboard*): hiding and reordering cards persists after a restart.

**Detail pages**

- [ ] The GPU, CPU, Memory, Storage and Network pages are populated, and *Raw sensors* lists
      LibreHardwareMonitor's sensors.
- [ ] Hovering a chart shows the crosshair and tooltip, and the 60s/5m/15m/1h selector and pause work.
- [ ] *Performance History*: picking metrics stacks the charts, hover is synchronized, and *Export CSV*
      writes a file.

**Vendor marks**

- [ ] The *Processor* page shows an *AMD* mark next to the CPU name, and the *GPU* page shows an *NVIDIA* mark next
      to the GPU name. Without logo files these are plain-text badges; that is expected.
- [ ] (Optional) Put `nvidia.png` and `amd.png` (official logos you downloaded, transparent PNG) in
      `%AppData%\Coreloom\Vendors`, restart, and check they replace the badges and read well in dark and light
      themes. Add `-on-dark` / `-on-light` versions if one theme is hard to read.

**Tray**

- [ ] The tray tooltip shows CPU/GPU/RAM.
- [ ] Right-click shows Open, Pause, Settings, Check for updates and Exit.
- [ ] Minimizing and closing hide to the tray (by default). *Exit* quits.

**Single instance and startup**

- [ ] Starting Coreloom a second time brings the first window forward instead of opening a second copy.
- [ ] *Settings → Desktop → Start with Windows*: after signing out and back in, Coreloom starts (minimized if
      chosen).
- [ ] Starting Coreloom by hand shows the Windows administrator prompt once; *Settings → Diagnostics* says
      "Running as administrator".
- [ ] With *Start with Windows* on, a *Coreloom startup* task exists in Task Scheduler (Run level: highest).
      After signing out and back in, Coreloom starts **without** a UAC prompt.
- [ ] Turning *Start with Windows* off removes that task, and no *Vigil* entry remains under
      *Settings → Apps → Startup*.
- [ ] If you used Vigil before: the first start imports your settings from `%APPDATA%\Vigil`.

**Memory and speed (leave it running)**

- [ ] Open *Settings → Diagnostics* and note the working set, managed heap and handle count. Click through every
      page twice, then leave Coreloom open for an hour: the managed heap should settle and the handle count
      should stay flat. Steady growth is a leak: export a diagnostic report and send it.
- [ ] CPU use shown in the sidebar stays around 1 % or less at a 1 s interval; it should drop to about zero
      while the window is minimized to the tray.
- [ ] Page switches feel instant and charts keep scrolling smoothly. The log's first lines show the animation
      rate in use (for example "animation 240 fps").

**Icon**

- [ ] `Coreloom.exe` shows the Coreloom icon in File Explorer, the taskbar, Alt+Tab and the tray. If an old icon
      shows, run a freshly named copy or restart Explorer; Windows caches icons.

**Alerts**

- [ ] *Settings → Alerts*: add a rule, for example GPU temperature above 30 °C for 5 seconds.
- [ ] A Windows notification appears.
- [ ] The notification doesn't repeat until the value drops back and the cooldown passes.

**Sleep and drivers**

- [ ] Sleep and resume the PC. Values continue within about 10 seconds.
- [ ] (Optional) Update or reinstall the NVIDIA driver while Coreloom runs. GPU values come back by themselves.
      *Settings → Diagnostics* shows NVML recovering.

**Overhead**

- [ ] With the dashboard open, Coreloom's own usage at the bottom of the sidebar stays low (note the numbers).
- [ ] When minimized, Task Manager shows Coreloom's CPU close to zero.

## 5. Installer and portable build

```powershell
./build/build.ps1 -Version 1.0.0-beta.1 -Package
```

**Expect:** `artifacts/release` contains these files:

- `Coreloom-1.0.0-beta.1-win-x64-Setup.exe` (needs Inno Setup)
- `...-Portable.zip`
- `SHA256SUMS.txt`

Then check the following:

- [ ] The installer runs without an administrator prompt, installs to
      `%LOCALAPPDATA%\Programs\Coreloom` and starts Coreloom (Windows shows its administrator prompt for the app).
- [ ] The installer, the Start menu entry, the desktop shortcut and the uninstall entry show the Coreloom icon.
- [ ] Uninstalling from *Settings → Apps* removes the program (and, after one administrator prompt, the
      *Coreloom startup* task) and keeps your settings.
- [ ] Unzipping the portable build elsewhere and running it creates a `Data` folder next to `Coreloom.exe`.

## 6. Automatic updates (after publishing two releases)

Automatic updates need an **installed** copy (from `Coreloom-<version>-win-x64-Setup.exe`, not the CI artifact or the
portable zip) and a newer release for it to find. First check that each release has its files: the release page
should list `…-Setup.exe`, `…-Portable.zip` and `SHA256SUMS.txt`. If a release only shows "Source code", open
*Actions → Release → Run workflow*, enter its tag (e.g. `v1.0.0`) and run it; the files are added to the release.

**From 1.0.0 (the old updater) to the first version with automatic updates.** 1.0.0 only offers updates; it
doesn't install them by itself.

1. Install `Coreloom-1.0.0-win-x64-Setup.exe`.
2. Release the next version (e.g. tag `v1.1.0` with a `## [1.1.0]` section in `CHANGELOG.md`).
3. In 1.0.0: *Settings → Updates → Check now → Download and install*.

**Expect:**

- [ ] Coreloom closes, a small Setup window appears, and the new version opens by itself.
- [ ] The window shows *Coreloom was updated to 1.1.0*; *See what's new* opens a short summary of the release.

**Automatic updates in action.** Use pre-releases so nobody else sees the test versions (only copies with
*Include pre-release versions* on do), and delete them afterwards.

1. In the new version, open *Settings → Updates*. **Expect:** *You're up to date*, *Update automatically* on.
2. Turn on *Include pre-release versions*.
3. Add a `## [1.1.1-test.1]` section to `CHANGELOG.md` with a sentence and a few bullets (see RELEASING.md),
   commit, then tag and push `v1.1.1-test.1`. Wait for the Release workflow to finish.
4. Click *Check for updates*.

**Expect:**

- [ ] The card shows *Downloading Coreloom 1.1.1-test.1* with a progress bar, then *…is ready to install*.
- [ ] The sidebar shows the badge (*Restart to update*), the *Updates* section in Settings has a dot, and the
      tray menu has *Restart to update to 1.1.1-test.1*.
- [ ] *What's new in 1.1.1-test.1* shows your sentence and bullets, short and readable, with New/Improved/Fixed
      tags when you used those headings.
- [ ] *Restart to update*: Coreloom closes, a small Setup progress window appears, and Coreloom reopens by itself
      with the *Coreloom was updated to 1.1.1-test.1* banner. *Settings → About* shows the new version.

Then the automatic paths (publish another pre-release, e.g. `v1.1.1-test.2`, for each):

- [ ] **At the next start:** let it download, then exit Coreloom from the tray and start it again. It installs
      before the dashboard opens and comes back on its own.
- [ ] **While idle:** let it download, close the window to the tray and leave the PC alone for about 20 minutes
      (no games or renders running). Coreloom updates and returns to the tray; a notification says it was updated.
- [ ] With *Update automatically* off, a new version only shows the badge and a notification; nothing downloads
      until you click *Download update*.
- [ ] *Skip this version* hides the badge, and the skipped version isn't offered again by automatic checks.

When you're done, delete the test pre-releases (and their tags) on GitHub and turn *Include pre-release versions*
off again.

## 7. What to send back

Send the following:

- `diag-standard.md` and `diag-admin.md` (computer and user names are already redacted).
- The checklist above, with failures marked.
- Screenshots of anything that looks wrong.
- The log folder: *Settings → Diagnostics → Open log folder*.
