# Troubleshooting

Start with *Settings → Diagnostics*. It shows:

- whether Vigil is running as administrator;
- whether PawnIO is installed;
- every sensor source with its state and last error;
- Vigil's own overhead.

From there you can open the log folder or export a report.

## CPU temperature, CPU power or motherboard fans show "Unavailable"

On current Windows versions, these sensors need a kernel driver. Vigil reads them through
LibreHardwareMonitor and the **PawnIO** driver.

1. Install PawnIO from [pawnio.eu](https://pawnio.eu). Vigil never installs drivers itself.
2. Click *Restart as administrator* in Vigil, or turn on *Settings → Desktop → Start with administrator rights*.
3. If CPU temperature is still missing, go to *Settings → Monitoring* and make sure *Use LibreHardwareMonitor*
   is on.
4. Export a diagnostic report, or run `VigilDiag.exe --lhm-report` as administrator, and open an issue.
   Sensor names change between CPU generations and may need a new mapping.

## GPU values are missing (NVIDIA)

Vigil loads `nvml.dll`, which comes with the NVIDIA driver, from `C:\Windows\System32` or
`C:\Program Files\NVIDIA Corporation\NVSMI`.

1. Check that `nvidia-smi` works in a terminal. If it doesn't, reinstall the NVIDIA driver.
2. After a driver update, Vigil re-initializes NVML automatically within about a minute. You can also use
   *Settings → Diagnostics → Rescan hardware*.
3. Hot spot temperature, memory junction temperature, fan RPM and core voltage aren't part of NVML. They
   come from LibreHardwareMonitor, which needs to be enabled.

## Values differ from another tool

- Tools sample at different moments. Compare averages over a few seconds, not single readings.
- "GPU temperature" here is the core sensor; hot spot is shown separately.
- AMD's Tctl can include an offset on some CPUs. Vigil labels which sensor it uses (hover a value).
- Clock speeds can be "current", "effective" or "derived (Task Manager style)". Vigil labels each one.

Run `VigilDiag.exe --compare` for an automatic NVML vs `nvidia-smi` comparison.

## High CPU or memory use

- The sidebar shows Vigil's own usage; it's measured, not estimated.
- Try a 2 s polling interval (*Settings → Monitoring*). Turn off per-thread graphs or smooth scrolling.
- *Scan USB fan and pump controllers* can make LibreHardwareMonitor slower on some systems. It's off by
  default.
- If usage stays high, attach a VigilDiag report to an issue. It includes per-provider poll times.

## The window doesn't appear

- Vigil may be running in the notification area. Click its tray icon, which may be in the overflow (^)
  menu.
- Starting Vigil again brings the running copy to the front.
- If it still doesn't appear, check the latest log in `%LOCALAPPDATA%\Vigil\Logs` (or `Data\Logs` next
  to a portable copy).

## Updates

- *"Update checks are not configured"*: the build had no GitHub repository embedded. Official releases
  always have one.
- *"Checksum mismatch"*: the download was corrupted or altered. It's discarded and nothing is installed.
  Try again later.
- Portable copies don't update in place. Download the new zip and replace the folder.
- Corporate proxies are honored through the Windows system proxy settings.

## Reset everything

1. Exit Vigil from the tray.
2. Delete `%APPDATA%\Vigil` (settings) and `%LOCALAPPDATA%\Vigil` (logs and downloads).
3. If you used *Start with administrator rights*, also delete the *Vigil (elevated startup)* task in Task
   Scheduler.
