# Changelog

All notable changes to Coreloom (formerly Vigil) are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/). The release workflow copies the section that matches the tag
into the GitHub release notes, so keep each version's heading in the form `## [x.y.z]`.

## [Unreleased]

## [1.2.7] - 2026-10-13

Arrange the Overview your way: move and resize its cards right on the dashboard.

### Highlights

- **Drag cards into place.** Drag any card or panel on the Overview to move it. The others make room as you go, and it snaps into the nearest spot when you let go.
- **Resize cards.** Drag a card's bottom-right corner to make it wider or taller, one grid step at a time. Make a chart full width, a summary card twice as wide or the storage list taller.
- **A scroll bar you can grab.** The page scroll bar is wider and sits clear of the window's edge, so you no longer get the resize arrow. It widens further when you point at it and slims down again when you use the mouse wheel.
- **Hide the PC picture.** A new switch in *Customize* shows or hides the PC picture in the header. It's on by default.

### In detail

- Summary cards and panels now share one grid, so you can mix them: put the CPU temperature card next to the temperatures chart, for example. The header with your PC stays at the top.
- Right-click a card to make it wider, narrower, taller or shorter, reset its size or hide it.
- Esc cancels a move or resize. Hold a card near the top or bottom of the page to scroll while you drag.
- In a narrower window the grid has 4, 2 or 1 columns and wide cards fit the width; your sizes come back when the window is wide again.
- *Customize › Reset layout* now resets sizes too. With the picture hidden, its lighting colors are greyed out.

### Feedback

Found a bug or a sensor that reads wrong? [Open an issue](https://github.com/jerryubah/Coreloom/issues) and attach a CoreloomDiag report.

## [1.2.6] - 2026-10-13

Customizing the Overview now happens right on the Overview.

### Highlights

- **Customize in place.** *Customize* opens a card over the Overview instead of taking you to Settings. Pick the PC's lighting, show, hide and reorder cards, and watch the dashboard change behind it.
- **Lighting moved into Customize.** The glow colors (blue, purple, red, green, rainbow or your accent) now sit in the *PC lighting* section of the Customize card, so the header is cleaner.

### In detail

- Close the card with its X, *Done*, Esc or a click outside it. Every change is saved as you make it.
- *Reset layout* brings back every card and panel in the default order.
- The *Dashboard* section in Settings is gone; everything it had is in the Customize card.

### Feedback

Found a bug or a sensor that reads wrong? [Open an issue](https://github.com/jerryubah/Coreloom/issues) and attach a CoreloomDiag report.

## [1.2.5] - 2026-10-13

The PC on the Overview gets a premium new look: pick its glow color and watch its fans turn with yours.

### Highlights

- **A new PC picture.** The Overview header now shows a rendered PC with glass, mesh and lit fans, with your graphics card's model on the card and your CPU temperature on the pump screen.
- **Pick the glow.** Use the dots next to the PC to light it blue, purple, red or green, cycle through a rainbow, or match your accent color.
- **Fans that follow yours.** The PC's fans turn with your real fan speeds: the top fans follow your CPU fan, the bottom ones your case fans.

### In detail

- The fans speed up and slow down with the readings, blur as they go faster and stop when your fans stop. Hover the PC to see which readings they follow.
- Fan speeds come from the motherboard and need PawnIO. Without them, the fans turn at an idle pace.
- The fans only turn while the Overview is on screen and stand still with *Reduce motion* on, so Coreloom stays light in the background.

### Good to know

- A picture of your own build (`pc.png` in `%AppData%\Coreloom`) still replaces the render. It's shown as it is, without the color picker or turning fans.

### Feedback

Found a bug or a sensor that reads wrong? [Open an issue](https://github.com/jerryubah/Coreloom/issues) and attach a CoreloomDiag report.

## [1.2.0] - 2026-10-13

Updating Coreloom now takes one click, and portable copies can update themselves too.

### Highlights

- **One-click updates.** When a new version is out, click *Install update* in *Settings › Updates* or the tray menu and Coreloom does the rest.
- **Portable copies update themselves.** Copies from the portable zip now update in place and keep their settings in the `Data` folder.

### In detail

- *Install update* downloads the new version, checks it against the release's SHA-256 checksums, closes Coreloom, installs the new version over the old one and opens it again. No more trips to GitHub.
- Automatic updates now work the same way for installed and portable copies: they download in the background and install the next time Coreloom starts or while your PC is idle.
- If an update can't replace a file, every change is undone and the previous version opens again.

### Good to know

- **Installed copies of 1.1.0** pick up this update on their own, or right away with *Check for updates*.
- **Portable copies of 1.1.0 or earlier need one last update by hand.** Replace the folder's files with the new portable zip below, keeping the `Data` folder. After that, *Install update* works for them too.
- Portable copies unpack updates into a hidden `.coreloom-update` folder next to `Coreloom.exe`, so keep yours in a folder you can write to.

### Feedback

Found a bug or a sensor that reads wrong? [Open an issue](https://github.com/jerryubah/Coreloom/issues) and attach a CoreloomDiag report.

## [1.1.0] - 2026-10-13

Coreloom now keeps itself up to date, and the Overview opens with your PC and its specs at a glance.

### Added
- **Automatic updates.** Coreloom downloads new versions in the background, checks them against the release's SHA-256 checksums and installs them the next time it starts, while your PC is idle, or right away with *Restart to update*. You can turn this off in *Settings › Updates*.
- **Your PC at a glance.** The Overview opens with a header for this PC: the motherboard name, the graphics card and the key specs (processor, cores, memory, storage, graphics memory, driver, Windows and BIOS), next to a drawing of the build that follows your hardware (memory sticks, graphics card size and model) and your accent color, with the CPU temperature on the pump screen. You can show a render or photo of your own build instead: save it as `pc.png` in `%AppData%\Coreloom`.
- **What's new.** Every update comes with a short summary of the release: in *Settings › Updates*, in the notification, and in a banner after Coreloom has updated.
- **Update badge.** The sidebar, the *Updates* section in Settings and the tray menu show when a new version is available, downloading or ready to install.
- **Redesigned Settings › Updates** with *Check for updates*, download progress, *Restart to update*, *Skip this version* and the update preferences in one place.

### Security
- **Safer installs.** A downloaded installer only runs if its SHA-256 matches a hash that only administrators can change, it stays locked from that check until it starts, and downloads never write through links placed in the updates folder.

### Changed
- **Tidier Graphics page.** Specifications now sit next to the VRAM chart at the same height as the charts and scroll inside the card when the list is long.
- **Sensors in two columns.** Long sensor lists on the Graphics and Processor pages are split into two balanced columns instead of one long card, and cards fit their content instead of stretching to their neighbour.
- After a background update, Coreloom returns to the notification area and says it was updated. If an update can't finish, the installer rolls back and starts the previous version again, and Coreloom stops retrying that version on its own.
- Portable copies are told about new versions and link to the release page; they're never changed in place.
- Coreloom's own CPU use is no longer shown under Settings in the sidebar; it's in *Settings › Diagnostics*.

### Fixed
- The release workflow adds its installer, portable zip and checksums to a release that already exists (for example one created on GitHub's website) instead of failing, and can be re-run for an existing tag from the Actions tab.

## [1.0.0] - 2026-10-13

**The first release of Coreloom**: a native Windows 11 hardware monitor with a calm, modern dashboard for your GPU, CPU, memory, storage and network.

Coreloom only *reads* sensors. It never changes clocks, voltages, fan curves or firmware, never installs drivers or services, and never sends your data anywhere.

### Highlights

- **Overview dashboard.** Six live cards (GPU utilization, GPU temperature, CPU utilization, CPU temperature, RAM and GPU power), each with a radial gauge, a sparkline, threshold colors and min/max/avg. Below them sit large charts and memory, storage and network panels. Hide and reorder the cards; your layout is saved.
- **Detail pages** for the GPU, CPU, memory, storage and network:
  - GPU clocks, VRAM, power and power limit, fans, hot spot and memory junction temperatures, clock-limit reasons and video engine load, with multi-GPU selection;
  - per-thread CPU graphs, a per-core table and top processes;
  - installed memory modules;
  - per-disk activity, temperature and volumes;
  - per-adapter network traffic.
- **Performance History.** Compare up to six metrics on stacked, time-aligned charts with synchronized hover. Pick a 60 s, 5 min, 15 min or 1 h window and export to CSV.
- **Honest data.** Every value is labelled *measured* or *derived* and names its source (NVML, Windows performance counters or LibreHardwareMonitor). Missing sensors tell you why they're missing. Nothing is estimated or faked.
- **Smooth, sharp interface.** Inter and JetBrains Mono typography, animation that keeps up with your display's refresh rate (up to 240 Hz), and charts that never hide a peak. Dark and light themes with a custom accent color, Mica, Snap Layouts, a collapsible sidebar and a reduced-motion mode.
- **Startup splash** that fades into the dashboard the moment it's ready.
- **Alerts.** Per-metric rules with a sustain time, hysteresis and a cooldown, delivered as Windows notifications.
- **Tray and startup.** A tray icon with a live tooltip, minimize or close to tray, single instance, and *Start with Windows*.
- **In-app updates** from GitHub Releases. Downloads resume if interrupted and are SHA-256 verified before anything runs. You can skip a version or opt in to pre-releases.
- **Light on your system.** Sensors are polled on their own thread (every second by default, adjustable), nothing is drawn while Coreloom is minimized, and its own CPU and memory use is shown in the sidebar.
- **CoreloomDiag**, a command-line companion that writes a Markdown report of your hardware, every sensor and its source, and Coreloom's own overhead.

### Hardware support

- **NVIDIA GPUs** through NVML, which comes with the NVIDIA driver.
- **AMD and Intel GPUs** through LibreHardwareMonitor.
- **CPU temperatures, package power and motherboard sensors** need the free, open-source [PawnIO](https://pawnio.eu) driver. Coreloom never installs it for you: install it yourself, then restart Coreloom. Everything else works without it, and the affected cards explain what's missing.

### Requirements

- Windows 11 x64, version 23H2 or later.
- Nothing else. The installer and the portable build include the .NET runtime.

### Good to know

- **Coreloom runs as administrator** so it can read the sensor driver. Windows asks for approval when you start it by hand; with *Start with Windows* on, it starts at sign-in without a prompt. It still only reads sensors.
- **Windows SmartScreen may warn you on first run** ("Windows protected your PC") because this release isn't code-signed yet. Choose *More info* → *Run anyway*, and compare the file with `SHA256SUMS.txt` if you'd like to confirm it's genuine.
- **No telemetry.** The only network request Coreloom makes is the optional update check to GitHub.
- **Vendor logos aren't bundled**, since they're trademarks. The CPU and GPU names show a text badge, or the official logo if you add the file yourself (see `assets/vendors/README.md`).
- Alert thresholds are reminders, not safety limits. Modern CPUs and GPUs protect themselves by throttling.

### Feedback

Found a bug or a sensor that reads wrong? [Open an issue](https://github.com/jerryubah/Coreloom/issues) and attach a CoreloomDiag report. It's the fastest way to get it fixed:

```powershell
CoreloomDiag.exe --samples 30 --out coreloom-report.md
```
