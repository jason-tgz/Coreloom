# Changelog

All notable changes to Coreloom (formerly Vigil) are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/). The release workflow copies the section that matches the tag
into the GitHub release notes, so keep each version's heading in the form `## [x.y.z]`.

## [Unreleased]

Coreloom now keeps itself up to date and tells you what changed.

### Added
- **Automatic updates.** Coreloom downloads new versions in the background, checks them against the release's SHA-256 checksums and installs them the next time it starts, while your PC is idle, or right away with *Restart to update*. You can turn this off in *Settings › Updates*.
- **What's new.** Every update comes with a short summary of the release: in *Settings › Updates*, in the notification, and in a banner after Coreloom has updated.
- **Update badge.** The sidebar, the *Updates* section in Settings and the tray menu show when a new version is available, downloading or ready to install.
- **Redesigned Settings › Updates** with *Check for updates*, download progress, *Restart to update*, *Skip this version* and the update preferences in one place.

### Security
- **Safer installs.** A downloaded installer only runs if its SHA-256 matches a hash that only administrators can change, it stays locked from that check until it starts, and downloads never write through links placed in the updates folder.

### Changed
- After a background update, Coreloom returns to the notification area and says it was updated. If an update can't finish, the installer rolls back and starts the previous version again, and Coreloom stops retrying that version on its own.
- Portable copies are told about new versions and link to the release page; they're never changed in place.

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

Found a bug or a sensor that reads wrong? [Open an issue](https://github.com/jerryubah/Vigil/issues) and attach a CoreloomDiag report. It's the fastest way to get it fixed:

```powershell
CoreloomDiag.exe --samples 30 --out coreloom-report.md
```
