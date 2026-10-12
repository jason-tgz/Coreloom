# Vigil

A native Windows 11 hardware monitor with a calm, modern dashboard: live GPU, CPU, memory, storage and
network telemetry, animated charts with up to an hour of history, threshold alerts, a tray icon and
verified in-app updates.

Vigil only **reads** sensors. It never changes clocks, voltages, fan curves or firmware settings, never
installs drivers or services, and never sends your data anywhere.

> **Project status:** the code is complete and the platform-neutral parts are unit-tested, but the
> Windows app has **not yet been built or run on real hardware**. See [docs/VERIFICATION.md](docs/VERIFICATION.md)
> for exactly what has and hasn't been verified, and [docs/TESTING-ON-YOUR-PC.md](docs/TESTING-ON-YOUR-PC.md)
> for the on-device checklist.

---

## Features

- **Overview dashboard.** Six summary cards (GPU utilization, GPU temperature, CPU utilization, CPU
  temperature, RAM, GPU power), each with a radial gauge, a sparkline, threshold colors and min/max/avg.
  Below them are large charts and memory, storage and network panels. Cards can be hidden and reordered,
  and the layout is saved.
- **Detail pages** for the GPU, CPU, memory, storage and network:
  - per-thread CPU graphs and a per-core table;
  - top processes;
  - SMBIOS memory modules;
  - per-disk activity, temperature and volumes;
  - per-adapter traffic.
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
  - Dark and light themes with a custom accent color.
  - Mica, Snap Layouts and a collapsible sidebar.
  - A tray icon with a live tooltip, start with Windows (optionally elevated), minimize/close to tray,
    and single instance.
- **Alerts.** Per-metric rules with a sustain time, hysteresis and a cooldown, delivered as Windows
  notifications.
- **Updates.** Updates come from GitHub Releases with no token needed.
  - Downloads can resume and are SHA-256 verified before anything runs.
  - The installer replaces files only after Vigil has exited.
- **Low overhead.** Vigil polls on a dedicated thread and does no UI work while minimized. It measures its
  own CPU and memory use and shows them in the sidebar.

## What Vigil reads, and from where

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
LibreHardwareMonitor uses [PawnIO](https://pawnio.eu), a signed, open-source driver. Vigil **never installs
it**. If you want those sensors:

1. Install PawnIO yourself from its website.
2. Run Vigil as administrator: use *Settings → Diagnostics → Restart as administrator*, or turn on
   *Start with administrator rights*.

Without it, everything else still works and the affected cards explain what's missing.

## Requirements

- Windows 11 x64, version 23H2 or later (the versions .NET 10 supports). Windows 10 may work, but it isn't a
  supported or tested target. Mica needs Windows 11 22H2 or later.
- Nothing else for the installer or portable build: they are self-contained and include the .NET runtime.
- To build from source: the [.NET 10 SDK](https://dotnet.microsoft.com/download). Inno Setup 6 is
  optional and only needed to build the installer.

## Install

Download from the [Releases](../../releases) page:

- **`Vigil-x.y.z-win-x64-Setup.exe`** (recommended).
  - Installs per-user to `%LOCALAPPDATA%\Programs\Vigil` with no administrator prompt.
  - Adds a Start menu entry.
  - Updates in place from inside Vigil.
- **`Vigil-x.y.z-win-x64-Portable.zip`**: unzip anywhere and run `Vigil.exe`. Settings and logs stay in a
  `Data` folder next to it.

To verify a download, run the following and compare the result with `SHA256SUMS.txt` from the same
release:

```powershell
Get-FileHash .\Vigil-x.y.z-win-x64-Setup.exe -Algorithm SHA256
```

**Uninstall.** Use *Settings → Apps → Installed apps → Vigil → Uninstall*. This removes the program, the
logon entry, downloaded updates and logs. Your settings in `%APPDATA%\Vigil` are kept; delete that folder
too for a clean removal. If you enabled *Start with administrator rights*, the uninstaller also removes
the *Vigil (elevated startup)* scheduled task (this may ask for administrator approval).

## Build from source

```powershell
git clone https://github.com/<owner>/Vigil.git
cd Vigil
./build/build.ps1                     # restore, build (Debug), run unit tests
dotnet run --project src/Vigil.App    # start the app
```

More build commands:

```powershell
./build/build.ps1 -Configuration Release -Publish        # self-contained build in artifacts/publish/win-x64
./build/build.ps1 -Version 1.0.0 -Package                # + installer, portable zip, SHA256SUMS.txt in artifacts/release
./build/build.ps1 -NoLhm                                 # build without LibreHardwareMonitor (NVML + Windows sensors only)
```

You can also open `Vigil.sln` in Visual Studio 2026 (version 18.0 or later; earlier versions don't support
the .NET 10 SDK) or a current JetBrains Rider, then run **Vigil.App**.

To enable update checks in your own builds, pass `-Repository owner/name` (CI does this automatically from
the GitHub repository). Without it, update checks are disabled.

## Diagnostics: VigilDiag

`VigilDiag.exe` ships next to `Vigil.exe`. It runs the same sensor engine headless and writes a Markdown
report covering:

- detected hardware and capabilities;
- every metric with its source and provenance;
- unavailable sensors and the reason for each;
- per-provider timing;
- Vigil's own CPU and memory overhead.

```powershell
VigilDiag.exe --samples 30 --interval 1000 --compare --out vigil-report.md
```

`--compare` samples `nvidia-smi` side by side with NVML and reports the differences. `--lhm-report` appends
LibreHardwareMonitor's own report. See [docs/TESTING-ON-YOUR-PC.md](docs/TESTING-ON-YOUR-PC.md).

## Privacy and safety

- **Read-only.** No writes to hardware, the BIOS or drivers, and no overclocking or fan-control features.
- **No telemetry.** The only network request Vigil makes is the optional update check to `api.github.com`
  and the download from GitHub when you choose to install an update. No hardware data is included.
- **Administrator rights only when you ask for them.** Vigil runs as a standard user. Elevation is offered
  only for the sensors that need it.
- **Alert thresholds are reminders, not safety limits.** Check your hardware vendor's specifications;
  modern CPUs and GPUs protect themselves by throttling.

## Repository layout

```
src/
  Vigil.Core/          platform-neutral engine: metrics, history, downsampling, alerts, settings, formatting, updater
  Vigil.Hardware/      Windows providers: NVML, PDH, storage, network, SMBIOS, processes, LibreHardwareMonitor adapter
  Vigil.ViewModels/    MVVM presentation layer (no WPF types; unit-tested)
  Vigil.App/           WPF app: theme, chart controls, pages, tray, startup, single instance
  Vigil.Diagnostics/   VigilDiag command-line diagnostic tool
tests/                 xUnit v3 tests for Core, Hardware (mapping + Windows integration) and ViewModels
installer/Vigil.iss    Inno Setup script
build/                 build.ps1, release-notes.ps1, check-xaml.py
docs/                  architecture, testing, releasing, troubleshooting, verification report
```

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the design.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Bug reports with a VigilDiag report attached are very welcome.

## License

[MIT](LICENSE) © 2026 Jerry Ubah. Third-party components and their licenses are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

NVIDIA, GeForce and RTX are trademarks of NVIDIA Corporation. AMD and Ryzen are trademarks of Advanced
Micro Devices, Inc. Vigil is not affiliated with or endorsed by either company.
