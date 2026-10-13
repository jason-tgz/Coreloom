# Architecture

Coreloom is a layered .NET 10 application. Only the bottom-right box knows about WPF, and only the Hardware
layer calls Windows sensor APIs, so most of the logic is testable on any OS.

```
┌────────────────────────────── Coreloom.App (WPF, net10.0-windows) ──────────────────────────────┐
│ Views (XAML) · chart controls · ThemeService · tray · startup · single instance · App.xaml.cs │
└───────────────▲──────────────────────────────────────────────────────────────────────────────┘
                │ data binding (frames delivered on the UI thread, coalesced)
┌───────────────┴────────────── Coreloom.ViewModels (net10.0, no WPF types) ──────────────────────┐
│ ShellViewModel · page view models · TimelineViewModel · Metric/Chart VMs · Settings · Updates │
└───────────────▲──────────────────────────────────────────────────────────────────────────────┘
                │ TelemetryFrame (immutable) · HistoryStore · MetricCatalog · DeviceRegistry
┌───────────────┴────────────── Coreloom.Core (net10.0, platform-neutral) ────────────────────────┐
│ TelemetryEngine · ITelemetryProvider · History + M4 downsampling · AlertEngine · Settings     │
│ UnitFormatter · axis/curve math · GitHub release client · checksums · resumable downloads    │
└───────────────▲──────────────────────────────────────────────────────────────────────────────┘
                │ ITelemetryProvider implementations
┌───────────────┴────────────── Coreloom.Hardware (net10.0-windows) ──────────────────────────────┐
│ NVML (GPU) · PDH + NtQuerySystemInformation (CPU) · memory · storage IOCTLs + PDH (disks)     │
│ network statistics · SMBIOS · process sampler · LibreHardwareMonitor adapter (optional)      │
└──────────────────────────────────────────────────────────────────────────────────────────────┘
```

`Coreloom.Diagnostics` (CoreloomDiag.exe) reuses Core and Hardware without any UI.

## Telemetry pipeline

1. **Providers** implement `ITelemetryProvider`.
   - `Initialize` detects hardware and registers metrics, each with a canonical ID, a kind, a category and
     its provenance.
   - `Poll` writes values.
   - `Shutdown` releases native handles.
2. **Canonical IDs** (`MetricIds`) such as `cpu/load`, `gpu/0/temp` and `gpu/0/temp/hotspot` let several
   sources describe the same quantity. Each registration carries a **priority**: NVML 100, PDH CPU 50,
   LibreHardwareMonitor GPU 50, LibreHardwareMonitor CPU 40, LibreHardwareMonitor CPU load 10.
   - The frame takes the highest-priority source that produced a value in that cycle.
   - Every LibreHardwareMonitor sensor is also available unmerged under `lhm/...` as a "raw sensor".
3. **Provenance.** Values are `Measured` (read from a sensor or counter) or `Derived` (computed from
   measured values, for example the average effective clock or the Task Manager-style clock).
   - The UI labels derived values.
   - A metric with no source has an *unavailable reason*, such as "Requires administrator rights and the
     PawnIO driver", that the UI shows instead of a number.
4. **TelemetryEngine** runs on a dedicated background thread.
   - **Staged initialization.** Stage 0 brings up the fast providers (NVML, PDH, storage, network) first.
     LibreHardwareMonitor (stage 1) follows, so the dashboard shows data within about a second.
   - **One frame per interval.** Each cycle builds one immutable `TelemetryFrame`, appends tracked metrics to
     history and raises `FrameProduced`. The default interval is 1 s; the range is 0.25–10 s.
   - **Slow providers.** A provider with a `MinimumPollInterval` keeps its last values between polls, so
     they aren't treated as missing.
   - **Fault isolation.** A provider that throws three times in a row is marked *Faulted*, shut down and
     re-initialized with back-off (5, 10, 30, 60, then 120 s). This covers GPU driver updates and device
     resets. NVML's fatal codes (`GPU_IS_LOST`, `DRIVER_NOT_LOADED`, `RESET_REQUIRED`, …) raise errors, so
     a lost driver is detected within three polls and NVML is re-initialized. Unsupported queries are
     simply dropped. After sleep and resume, the app asks for a full re-initialization.
   - **Disabled sections** (*Settings → Monitoring*) are skipped entirely.
5. **Frame delivery.** `App` subscribes to `FrameProduced`.
   - Alert rules are evaluated right there, on the telemetry thread, so alerts work while the window is
     hidden.
   - The UI gets at most one queued dispatcher operation, which always reads the *latest* frame. A slow UI
     can't build a backlog.

## History and charts

- **HistoryStore** keeps one ring buffer per tracked metric.
  - Each point is 12 bytes: a monotonic timestamp (`Environment.TickCount64`, unaffected by clock changes)
    and a float value.
  - Capacity is one hour at the current interval plus slack. At 1 s that's 3,632 points, about 43 KB per
    metric, so memory is bounded no matter how long Coreloom runs.
- **Downsampling.** Long windows are reduced with M4: the first, minimum, maximum and last point per pixel
  column. This keeps spikes that plain averaging would hide.
- **Curves** use monotone cubic interpolation (Fritsch–Carlson), so smoothed lines never overshoot the data:
  0 % stays 0 %. **Axes** snap to "nice" steps, with hysteresis so they don't jitter.
- **Rendering.** `LiveLineChart` and `Sparkline` draw with WPF's retained, GPU-composited `DrawingVisual`s
  and rebuild geometry only when a frame arrives.
  - Smooth scrolling is a composited translate animation at 30 fps, used for windows of 5 minutes or less.
    It is disabled with *Reduce motion*.
  - Hovering one chart moves a synchronized crosshair across all charts that share the `TimelineViewModel`.
  - Freezing charts stops the timeline while history keeps recording.
- **Minimized or hidden.** The shell deactivates the current page, so no bindings update and nothing
  renders. Polling, history and alerts continue.

## UI layer

- **MVVM.** Page view models (`OverviewViewModel`, `GpuViewModel`, …) expose plain properties and
  `ObservableCollection`s. Views are XAML with data templates and no logic in code-behind. View models
  never reference WPF, so they're unit-tested on Linux in CI.
- **Theming.** `ThemeService` creates every brush once, unfrozen, and animates its color on theme or accent
  changes. All XAML uses `DynamicResource` for brushes. Mica uses `DWMWA_SYSTEMBACKDROP_TYPE` on Windows 11
  22H2 and later. The custom caption buttons support Snap Layouts by answering `WM_NCHITTEST` with
  `HTMAXBUTTON`.
- **Settings** are JSON in `%APPDATA%\Coreloom\settings.json`, or `Data\` in portable mode.
  - Writes are atomic: write a temp file, then replace.
  - Writes are debounced by 600 ms.
  - A corrupt file is kept as `settings.json.bad` and defaults are used.

## Vendor marks

`HardwareVendors.Detect` (Core) identifies NVIDIA, AMD or Intel from the vendor string first, then the device name.
`BrandMark` shows a logo file from `%AppData%\Coreloom\Vendors` or the embedded `assets/vendors/` set, and a
plain-text badge otherwise. No logos are bundled.

## Privileges

Coreloom's manifest is `requireAdministrator`: Windows shows a UAC prompt each time the app is started by
hand, and the process always holds an elevated token. CoreloomDiag stays `asInvoker` (run it from an elevated
terminal to include the admin-only sensors).

- **Without PawnIO:** NVML, PDH, memory, network, SMBIOS, process lists and storage queries all work.
- **With PawnIO (the app is already elevated):** LibreHardwareMonitor's low-level CPU and Super I/O sensors
  need both. If the driver is missing Coreloom explains that; it never installs drivers.
- **Starting at sign-in:** *Start with Windows* creates a Task Scheduler logon task (`HighestAvailable`, named
  `Coreloom startup`). A Run-key entry would trigger a UAC prompt at every sign-in for an app that requires
  administrator rights, so Coreloom does not use one. The app is already elevated when it creates or removes the
  task, so there is no extra prompt, and it refreshes the task at launch if the executable moved. The
  uninstaller calls `Coreloom.exe --remove-startup-task` (one elevation prompt) because only an elevated process
  can delete that task.
- **Restart:** relaunching from the elevated process (settings changes, `--wait-for <pid>`) does not prompt
  again, because the child of an elevated parent inherits the elevation.
- **Installer:** per-user and not elevated, so `Coreloom.exe` is launched with `shellexec` and an in-app update
  (started by the elevated app) inherits elevation. A manual install over a running instance requires exiting
  Coreloom first, because a non-elevated installer cannot close an elevated app.

## Startup splash and handoff

A launch that opens a window shows the Coreloom splash first and fades into the real main window once that window
has painted. The design is the Coreloom splash opener kit (`assets/branding/splash/splash-screen-preview.png`),
recreated in native XAML; no browser or web view is involved.

- **When.** `App.Start` starts the splash right after the single-instance check and settings load. It is skipped
  for `--minimized`, for the sign-in task with *Start minimized*, for a second launch (which hands over to the
  running copy and exits) and for the uninstaller's `--remove-startup-task`. `SplashPolicy` (Core, unit-tested)
  holds these rules and the timings.
- **Own UI thread.** `SplashScreenHost` runs `SplashWindow` on a dedicated background STA thread with its own
  dispatcher. WPF animations tick on their owner thread, so the loading line keeps moving while the main thread
  builds the dashboard. The thread can't keep the process alive, and its window is destroyed with it. The window
  sets `InheritanceBehavior = SkipToThemeNext` and defines everything it uses locally, because App.xaml's resources
  belong to the main thread; `check-xaml.py` enforces both.
- **Window.** 700 × 370, borderless and layered (`AllowsTransparency`, so the whole window can fade), centred on
  the monitor with the mouse cursor, not topmost, no taskbar button, `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` so it
  never appears in Alt+Tab or takes focus. The wordmark uses the embedded Inter with CSS-style letter spacing
  (`TrackedText`); the icon is the approved `png/coreloom-512.png`.
- **Readiness.** `StartupHandoff` creates the main window's handle, cloaks it (`DWMWA_CLOAK`), and shows it: the
  window lays out and renders but isn't displayed. Its `ContentRendered` event, plus two composition frames, is
  the ready signal. The dashboard does not wait for sensor data or network calls.
- **Handoff.** The splash stays up at least 850 ms from its own first frame, so a fast start doesn't flash it.
  Then the splash is ordered just above the window (only if the window is in the foreground, so it never jumps
  over another app), the window is uncloaked with its frame and background already drawn, and the splash fades
  out over 360 ms while the window's content fades in. With reduced motion (Coreloom's setting or Windows'
  *Animation effects* off) there is no entrance, no moving line and no fade: the window appears fully painted.
  If the splash hasn't reached the screen by the time the window is ready, it is cancelled instead of shown late.
  While cloaked (normally well under a second) the main window already has keyboard focus.
- **Failure paths.** A fatal startup error switches the splash to its error look behind the existing error
  dialog, and every exit path closes it. A 10 s watchdog only covers a main window that never reports a first
  frame. If cloaking isn't available, the window is shown directly. Splash problems are logged and never stop
  the app.
- **Timings** are logged under `Startup`: when the splash reached the screen and when the window was revealed,
  both measured from process start.

## Rendering, fonts and lifetime

- **Text:** Inter (UI) and JetBrains Mono (numbers and technical values) ship embedded in the assembly as
  `Resource`s (SIL OFL 1.1; licence texts ship next to the app). `TextFormattingMode="Display"` with fixed
  hinting and ClearType hints on opaque cards keeps small text sharp; custom-drawn chart text uses
  `FormattedText` in Display mode.
- **Animation rate:** WPF animates at 60 fps unless told otherwise, so `App` raises
  `Timeline.DesiredFrameRate`'s default to the fastest attached display (60–240 Hz, read with
  `EnumDisplaySettings`). The rate is a request, not a guarantee: WPF composites in software and the DWM still
  decides what reaches the screen. Number tweens stay at 60 fps because each tick re-lays-out a `TextBlock`.
- **Page cache:** `PageHost` keeps one view per page view model, so navigating back is instant. A page that is not
  shown is unloaded, which detaches every chart's event handlers (`Loaded`/`Unloaded` symmetry); charts
  re-attach when the page returns.
- **Memory:** history is a bounded ring per metric (12 bytes per point, one hour). Bound immutable items derive
  from `StaticRecord`/`StaticItem`, so WPF uses its weak event path for them. Settings › Diagnostics shows the
  process working set, the managed heap and the handle count so growth is visible.

## Updates

`GitHubReleaseClient` lists releases from `api.github.com`. This is anonymous: the 60 requests per hour
limit is far above Coreloom's check frequency.

1. It picks the newest non-draft release, skipping pre-releases unless the user opted in.
2. It downloads the `-Setup.exe` asset with HTTP range resume.
3. It verifies the SHA-256 against `SHA256SUMS.txt` from the same release, using a constant-time
   comparison.
4. It starts the installer with `/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /UPDATE=1`, then
   exits. Inno Setup replaces the files once Coreloom has exited, then relaunches it.

Portable copies are never modified in place; they link to the release page instead. The repository is
embedded at build time as `AssemblyMetadata("UpdateRepository")`. A build without it simply has no
update checks.

## Extending

- **New sensor source:** add an `ITelemetryProvider` (see CONTRIBUTING.md).
- **FPS / frame times:** a future `Coreloom.PresentMon` provider based on ETW. It needs administrator rights
  or membership in *Performance Log Users*, so it belongs behind an explicit opt-in.
- **Other GPU vendors:** AMD and Intel GPUs already flow through LibreHardwareMonitor's mappings. A native
  ADLX or IGCL provider could be added with a higher priority, the same way NVML outranks
  LibreHardwareMonitor for NVIDIA.
