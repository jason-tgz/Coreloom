# Coreloom

A native Windows 11 hardware monitor with a calm, modern dashboard: live GPU, CPU, memory, storage and
network telemetry, animated charts with up to an hour of history, threshold alerts, a tray icon and
verified in-app updates.

Coreloom only **reads** sensors. It never changes clocks, voltages, fan curves or firmware settings, never
installs drivers or services, and never sends your data anywhere.

> **Project status:** 1.0.0 is the first release. Every build is compiled and tested by GitHub Actions, and
> the app is run on Windows 11 with an NVIDIA GeForce RTX 5090 and an AMD Ryzen 9 9950X3D. It hasn't been
> tried on a wide range of other hardware yet, so if a sensor reads wrong, please
> [open an issue](../../issues) with a [CoreloomDiag report](#diagnostics-coreloomdiag) attached.
> [docs/VERIFICATION.md](docs/VERIFICATION.md) lists what has and hasn't been verified.

---

## Features

- **Overview dashboard.** A header for your PC (motherboard, graphics card and key specs next to a drawing of the
  build in your accent color, or a render of your own build). Six summary cards (GPU utilization, GPU
  temperature, CPU utilization, CPU temperature, RAM, GPU power), each with a radial gauge, a sparkline,
  threshold colors and min/max/avg.
  Below them are large charts and memory, storage and network panels. Cards can be hidden and reordered,
  and the layout is saved.
- **Detail pages** for the GPU, CPU, memory, storage and network:
  - per-thread CPU graphs and a per-core table;
  - top processes;
  - SMBIOS memory modules;
  - per-disk activity, temperature and volumes;
  - per-adapter traffic.
- **Vendor marks.** The CPU and GPU names carry a vendor mark. Coreloom shows the official NVIDIA, AMD or Intel
  logo if you add the files (see [assets/vendors/README.md](assets/vendors/README.md)) and a plain-text badge
  until then.
- **Performance History.** Compare up to six metrics on stacked, time-aligned charts with synchronized hover.
  Choose a 60 s, 5 min, 15 min or 1 h window and export to CSV.
- **Honest data.** Every value is labelled *measured* or *derived*, along with its source (NVML, Windows
  performance counters, LibreHardwareMonitor…). Missing sensors say why they're missing. Nothing is
  estimated or faked.
- **Charts.**
  - Smooth GPU-composited rendering.
  - M4 min/max downsampling, so peaks never disappear.
  - Monotone curves that don't overshoot.
  - Pause/freeze and a reduced-motion mode.
- **Desktop integration.**
  - A branded startup splash that fades into the dashboard as soon as the window has painted (skipped when
    Coreloom starts in the tray; follows reduced-motion settings).
  - Dark and light themes with a custom accent color.
  - Mica, Snap Layouts and a collapsible sidebar.
  - A tray icon with a live tooltip, start with Windows (elevated through Task Scheduler, so no prompt at
    sign-in), minimize/close to tray, and single instance.
- **Alerts.** Per-metric rules with a sustain time, hysteresis and a cooldown, delivered as Windows
  notifications.
- **Automatic updates.** New versions come from GitHub Releases with no token needed.
  - Installed and portable copies alike download updates in the background and install them the next time
    Coreloom starts, while the PC is idle, or right away with *Install update*, which closes Coreloom, installs
    the new version over the old one and opens it again. *Settings → Updates* has *Check for updates*, the
    on/off switch and a short *What's new* summary of each release.
  - A badge in the sidebar and a notification say when a new version is available or ready.
  - Downloads can resume and are SHA-256 verified before anything runs. Files are replaced only after Coreloom
    has exited, by the installer or (portable copies) by the new version, and a failed update is rolled back.
- **Low overhead.** Coreloom polls on a dedicated thread and does no UI work while minimized. It measures its
  own CPU and memory use and shows them in *Settings › Diagnostics*.

## What Coreloom reads, and from where

| Area | Source | Administrator needed? |
|---|---|---|
| NVIDIA GPU: utilization, temperature, clocks, VRAM, power and limit, fan %, P-state, clock-limit reasons, encoder/decoder, PCIe | **NVML** (ships with the NVIDIA driver) | No |
| GPU hot spot, memory junction, fan RPM, voltage (not in NVML) | LibreHardwareMonitor (NvAPI) | No |
| AMD / Intel GPUs | LibreHardwareMonitor | Usually no |
| CPU total and per-thread utilization, effective clock | Windows performance counters (PDH) | No |
| CPU temperatures (Tctl/Tdie, CCDs), package power, per-core clocks | LibreHardwareMonitor via the **PawnIO** driver | **Yes**, plus PawnIO installed |
| Motherboard fans, voltages, temperatures (Super I/O) | LibreHardwareMonitor via PawnIO | **Yes**, plus PawnIO installed |
| Memory usage, commit, modules | Windows APIs + SMBIOS | No |
| Disk activity, capacity, volumes, NVMe temperature | PDH + storage IOCTLs (query-only handles) | No; some drives report temperature only to administrators |
| Network throughput | Windows network statistics | No |

**About PawnIO.** Reading CPU temperature and motherboard sensors on modern Windows needs a kernel driver.
LibreHardwareMonitor uses [PawnIO](https://pawnio.eu), a signed, open-source driver. Coreloom **never installs
it**. If you want those sensors:

1. Install PawnIO yourself from its website.
2. Restart Coreloom. It always runs as administrator, so nothing else is needed.

Without it, everything else still works and the affected cards explain what's missing.

## Requirements

- Windows 11 x64, version 23H2 or later (the versions .NET 10 supports). Windows 10 may work, but it isn't a
  supported or tested target. Mica needs Windows 11 22H2 or later.
- Nothing else for the installer or portable build: they are self-contained and include the .NET runtime.
- To build from source: the [.NET 10 SDK](https://dotnet.microsoft.com/download). Inno Setup 6 is
  optional and only needed to build the installer.

## Install

Download from the [Releases](../../releases) page:

- **`Coreloom-x.y.z-win-x64-Setup.exe`** (recommended).
  - Installs per-user to `%LOCALAPPDATA%\Programs\Coreloom` with no administrator prompt (Coreloom itself
    asks for administrator approval each time you start it by hand; see below).
  - Adds a Start menu entry.
  - Updates in place from inside Coreloom.
- **`Coreloom-x.y.z-win-x64-Portable.zip`**: unzip anywhere and run `Coreloom.exe`. Settings and logs stay in a
  `Data` folder next to it. Updates in place from inside Coreloom too (the `Data` folder is kept).

To verify a download, run the following and compare the result with `SHA256SUMS.txt` from the same
release:

```powershell
Get-FileHash .\Coreloom-x.y.z-win-x64-Setup.exe -Algorithm SHA256
```

**Uninstall.** Use *Settings → Apps → Installed apps → Coreloom → Uninstall*. This removes the program, the
logon entry, downloaded updates and logs. Your settings in `%APPDATA%\Coreloom` are kept; delete that folder
too for a clean removal. If you enabled *Start with Windows*, the uninstaller also removes the
*Coreloom startup* scheduled task (this asks for administrator approval once).

**Administrator rights.** Coreloom always runs as administrator: Windows shows its normal approval prompt each
time you start it by hand. *Settings → Desktop → Start with Windows* registers a Task Scheduler task with the
highest privileges instead of a Run entry, so it starts at sign-in **without** a prompt. Running as
administrator is what lets the PawnIO driver expose CPU temperature, package power and motherboard sensors;
Coreloom still only reads sensors and never writes to hardware.

**Upgrading from Vigil.** Coreloom is Vigil under a new name. The first start copies your `settings.json` from
`%APPDATA%\Vigil` (the old folder is left alone). Uninstall Vigil separately if you installed it, and turn on
*Start with Windows* again in Coreloom if you had it on.

## Build from source

```powershell
git clone https://github.com/jerryubah/Coreloom.git
cd Coreloom
./build/build.ps1                     # restore, build (Debug), run unit tests
dotnet run --project src/Coreloom.App    # start the app
```

More build commands:

```powershell
./build/build.ps1 -Configuration Release -Publish        # self-contained build in artifacts/publish/win-x64
./build/build.ps1 -Version 1.0.0 -Package                # + installer, portable zip, SHA256SUMS.txt in artifacts/release
./build/build.ps1 -NoLhm                                 # build without LibreHardwareMonitor (NVML + Windows sensors only)
```

You can also open `Coreloom.sln` in Visual Studio 2026 (version 18.0 or later; earlier versions don't support
the .NET 10 SDK) or a current JetBrains Rider, then run **Coreloom.App**.

To enable update checks in your own builds, pass `-Repository owner/name` (CI does this automatically from
the GitHub repository). Without it, update checks are disabled.

## Diagnostics: CoreloomDiag

`CoreloomDiag.exe` ships next to `Coreloom.exe`. It runs the same sensor engine headless and writes a Markdown
report covering:

- detected hardware and capabilities;
- every metric with its source and provenance;
- unavailable sensors and the reason for each;
- per-provider timing;
- Coreloom's own CPU and memory overhead.

```powershell
CoreloomDiag.exe --samples 30 --interval 1000 --compare --out coreloom-report.md
```

`--compare` samples `nvidia-smi` side by side with NVML and reports the differences. `--lhm-report` appends
LibreHardwareMonitor's own report. See [docs/TESTING-ON-YOUR-PC.md](docs/TESTING-ON-YOUR-PC.md).

## Privacy and safety

- **Read-only.** No writes to hardware, the BIOS or drivers, and no overclocking or fan-control features.
- **No telemetry.** The only network requests Coreloom makes are the update check to `api.github.com` and the
  update download from GitHub (both can be turned off in *Settings → Updates*). No hardware data is included.
- **Always elevated, still read-only.** Coreloom runs as administrator so the sensor driver can be read, but it
  never writes to hardware, never installs drivers or services, and has no remote access of any kind.
- **Alert thresholds are reminders, not safety limits.** Check your hardware vendor's specifications;
  modern CPUs and GPUs protect themselves by throttling.

## Repository layout

```
src/
  Coreloom.Core/          platform-neutral engine: metrics, history, downsampling, alerts, settings, formatting, updater
  Coreloom.Hardware/      Windows providers: NVML, PDH, storage, network, SMBIOS, processes, LibreHardwareMonitor adapter
  Coreloom.ViewModels/    MVVM presentation layer (no WPF types; unit-tested)
  Coreloom.App/           WPF app: theme, chart controls, pages, tray, startup, single instance
  Coreloom.Diagnostics/   CoreloomDiag command-line diagnostic tool
tests/                 xUnit v3 tests for Core, Hardware (mapping + Windows integration) and ViewModels
installer/Coreloom.iss    Inno Setup script
build/                 build.ps1, release-notes.ps1, check-xaml.py
docs/                  architecture, testing, releasing, troubleshooting, verification report
```

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the design.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Bug reports with a CoreloomDiag report attached are very welcome.

## License

[MIT](LICENSE) © 2026 Jerry Ubah. Third-party components and their licenses are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

NVIDIA, GeForce and RTX are trademarks of NVIDIA Corporation. AMD, Ryzen and Radeon are trademarks of
Advanced Micro Devices, Inc. Intel is a trademark of Intel Corporation. Coreloom is not affiliated with or
endorsed by these companies, and it does not bundle their logos.
