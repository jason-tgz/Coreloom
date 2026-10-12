# Contributing to Coreloom

Thanks for helping. This document explains how the project is put together and what we expect from
changes.

## Getting set up

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download). `global.json` pins the feature band.
2. Clone the repository, then run `./build/build.ps1` (Windows PowerShell 5.1 or PowerShell 7).
3. Run the app with `dotnet run --project src/Coreloom.App`, or open `Coreloom.sln` in Visual Studio 2026 or Rider.
4. Coreloom always runs as administrator (its manifest requires it), so start your IDE or terminal as
   administrator before `dotnet run`, or run the built `Coreloom.exe` and accept the prompt. For CPU temperature
   and motherboard sensors also install [PawnIO](https://pawnio.eu).

On Linux or macOS you can build and test the platform-neutral projects:

```bash
dotnet test tests/Coreloom.Core.Tests
dotnet test tests/Coreloom.ViewModels.Tests
python3 build/check-xaml.py      # static XAML consistency checks
```

## Ground rules

These rules matter more than any style preference. A pull request that breaks one of them will not be
merged.

1. **Read-only.** Coreloom never writes to hardware. That means no fan control, clock or voltage changes,
   BIOS/EC writes, or driver installation. Use LibreHardwareMonitor only for reading sensors.
2. **Never fabricate data.**
   - If a sensor isn't available, the value is missing, and the UI says why: call
     `IProviderContext.SetUnavailableReason`.
   - Don't add estimates, defaults or "typical" values that could be mistaken for measurements.
   - A computed value (for example an average clock) must be registered with `Provenance.Derived`.
3. **No telemetry.** No analytics or crash reporting, and no network calls other than the user-controlled
   update check.
4. **Read-only while elevated.** Coreloom runs as administrator so the sensor driver can be read. That makes
   the read-only rule stricter, not looser: never write to hardware, install drivers or services, or add remote
   access. Keep new background work off the UI thread and idle when the window is hidden.
5. **Never block the UI thread** with sensor I/O. Providers run on the telemetry thread. View models get
   frames on the UI thread and must stay cheap.

## Architecture in one minute

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for details.

- **`Coreloom.Core`** (net10.0, no Windows APIs) holds the telemetry engine, metric catalog, history,
  downsampling, alerts, settings, formatting and update logic. It is fully unit-tested.
- **`Coreloom.Hardware`** (Windows) contains the providers. Each one implements `ITelemetryProvider`,
  registers canonical metric IDs (`MetricIds`) with a priority, and writes values in `Poll`.
- **`Coreloom.ViewModels`** (net10.0, no WPF types) holds the pages, cards, charts and settings as plain view
  models, so they can be tested on any OS.
- **`Coreloom.App`** (WPF) contains views, chart controls, theming and desktop integration. Keep logic out of
  code-behind.

### Adding a sensor

1. If the metric is new, add a canonical ID to `MetricIds`. Reuse an existing ID when two sources measure
   the same thing; the higher-priority source wins automatically.
2. Register it in the provider's `Initialize` (or lazily when first seen) with the correct `MetricKind`,
   `MetricCategory`, provenance and a short source note.
3. Write values in `Poll`. Keep `Poll` fast: anything slow belongs behind `MinimumPollInterval`.
4. Add a mapping test (see `tests/Coreloom.Hardware.Tests/MappingTests.cs`) and, where possible, a
   Windows integration test that skips gracefully on machines without that hardware.

### Future: frame-rate (FPS) monitoring

FPS needs ETW (PresentMon) and usually administrator rights. Implement it as a separate
`ITelemetryProvider` in its own project (for example `Coreloom.PresentMon`), registered only when the user
enables it. The engine, history and charts don't need to change.

## Code style

- Follow `.editorconfig`; nullable reference types are on.
- Prefer clarity over cleverness, and comment the *why* (hardware quirks, API behavior), not the *what*.
- Use invariant culture for anything persisted or parsed, and the current culture for display.

## Tests

- `dotnet test` must pass.
- New logic in Core or ViewModels needs unit tests.
- Windows-only tests use `Assert.SkipUnless(OperatingSystem.IsWindows(), ...)` and must not assume
  particular hardware.

## Pull request checklist

- [ ] Builds and tests pass locally (`./build/build.ps1`).
- [ ] No new network call, driver or service install, or hardware write.
- [ ] Unavailable data is shown as unavailable, never as zero.
- [ ] `CHANGELOG.md` updated under *Unreleased*.
- [ ] For UI changes: screenshots in dark and light themes, and checked with *Reduce motion* on.

## Reporting bugs

Please attach a CoreloomDiag report (`CoreloomDiag.exe --samples 20 --compare --out report.md`) and mention your
Windows version and whether Coreloom was running as administrator. Reports replace your computer name and user
name with placeholders, and they don't include IP addresses or serial numbers. Still, please review a report
before posting it.
