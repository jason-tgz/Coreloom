# Changelog

All notable changes to Coreloom (formerly Vigil) are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/). The release workflow copies the section that matches the tag
into the GitHub release notes, so keep each version's heading in the form `## [x.y.z]`.

## [Unreleased]

### Changed
- **Renamed from Vigil to Coreloom**, with the approved Coreloom icon used as supplied (kit kept in
  `assets/branding`). It is embedded in `Coreloom.exe` and `CoreloomDiag.exe` through `ApplicationIcon`, so
  local builds and CI/release builds both carry it, and the window, tray (supplied PNG sizes), installer,
  shortcuts and About page use it too. Project, namespace, assembly, installer and artifact names changed to
  match (`Coreloom.exe`, `CoreloomDiag.exe`, `Coreloom-<version>-win-x64-Setup.exe`). The installer has a new
  AppId, so it installs next to an existing Vigil rather than upgrading it. Settings are copied once from
  `%APPDATA%\Vigil` on first start; the old folder is left in place.
- **Always runs as administrator.** The manifest is now `requireAdministrator`. *Start with Windows* uses a
  Task Scheduler task (highest privileges, no prompt at sign-in) instead of a Run-key entry; *Start with
  administrator rights* and *Restart as administrator* are gone. The limited-mode banner now only appears when
  the PawnIO driver is missing. The uninstaller removes the sign-in task through
  `Coreloom.exe --remove-startup-task`.
- **Typography.** Inter is the UI font and JetBrains Mono is used for numbers and technical values. Both are
  embedded (SIL OFL 1.1, notices added). Text uses Display formatting with fixed hinting and ClearType hints for
  crisper small text, including in the custom-drawn charts.
- **Animation.** Animation frame rate follows the fastest attached display (60–240 Hz); chart and gauge
  animations no longer have a 30 fps cap. Pages are cached so switching is instant.

### Fixed
- Chart, donut and sparkline controls hold event subscriptions only while they are on screen; bound immutable
  items (option lists, spec rows, sensor groups) no longer rely on WPF's static property-descriptor table;
  per-frame allocations in the sparkline and gauge were removed.

### Added
- Settings › Diagnostics shows the managed heap and handle count next to the working set, so a memory or handle
  leak is visible.
- Tests for the sign-in task sync and the settings import.

## [1.0.0-beta.1]

The first public build, intended for on-device validation before 1.0.0.

### Added
- **Overview**:
  - six summary cards (GPU utilization, GPU temperature, CPU utilization, CPU temperature, RAM, GPU power)
    with gauges, sparklines, threshold colors and min/max/avg;
  - large utilization, temperature and power charts;
  - memory, storage and network panels;
  - cards you can hide and reorder, with the layout persisted.
- **GPU page** (NVIDIA through NVML, other vendors through LibreHardwareMonitor):
  - utilization, temperatures (hot spot and memory junction when available), clocks, VRAM, power and power
    limit, fans, voltage, memory controller and video engine load, P-state;
  - clock-limit reasons;
  - multi-GPU selection.
- **CPU page**:
  - total and per-thread utilization from Windows performance counters (no administrator rights);
  - temperatures, package power and per-core clocks through LibreHardwareMonitor and PawnIO;
  - per-core table, top processes and motherboard sensors.
- **Memory, Storage and Network pages**:
  - donut charts, per-disk activity and temperature, volumes;
  - per-adapter traffic with automatic active-adapter detection.
- **Performance History**:
  - up to six metrics on time-aligned charts with synchronized hover;
  - windows of 60 s, 5 min, 15 min and 1 h with M4 downsampling;
  - CSV export.
- **Provenance labels**: every value is marked measured or derived and names its source. Unavailable
  sensors explain why.
- **Themes**: dark and light themes, a custom accent color, Mica, a reduced-motion mode, smooth chart
  scrolling and animated values.
- **Tray and startup**: a tray icon with a live tooltip, pause/resume, start with Windows (optionally
  elevated through Task Scheduler), minimize/close to tray, and single instance.
- **Alerts**: threshold alerts with sustain time, hysteresis and cooldown, shown as Windows notifications.
- **Updates**: in-app updates from GitHub Releases.
  - Resumable downloads with SHA-256 verification.
  - The installer runs only after Vigil exits.
  - Updates can be skipped per version.
- **VigilDiag**: a command-line diagnostic report with an `nvidia-smi` comparison and an overhead
  measurement.
- **Packaging and CI**:
  - per-user Inno Setup installer, portable zip and SHA256SUMS.txt;
  - CI on Windows and Linux;
  - tag-triggered releases.
