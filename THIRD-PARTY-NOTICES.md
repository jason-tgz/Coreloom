# Third-party notices

Vigil is licensed under the MIT License (see `LICENSE`). Vigil's release packages also contain the
components listed below, which are distributed unmodified under their own licenses. The source code for
each one is available at the linked project.

This list covers the packages Vigil references directly and their notable dependencies. The exact set of
files in a build is determined by NuGet; run `dotnet list package --include-transitive` to see it.

## LibreHardwareMonitorLib

- License: Mozilla Public License 2.0 (MPL-2.0), https://mozilla.org/MPL/2.0/
- Source: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- Used for CPU, motherboard, non-NVIDIA GPU, SSD health and some NVIDIA sensors (through NvAPI). Vigil uses
  the library as published on NuGet, without modification.
- LibreHardwareMonitorLib includes **PawnIO modules**, licensed under the GNU Lesser General Public
  License v2.1. See LibreHardwareMonitor's `THIRD-PARTY-NOTICES.txt` for the full text. The PawnIO
  **driver** itself (GPL-2.0, https://github.com/namazso/PawnIO, downloads at https://pawnio.eu) is
  **not** included with Vigil. Users who want low-level sensors install it separately.

### Dependencies of LibreHardwareMonitorLib

| Package | License | Source |
|---|---|---|
| HidSharp | Apache License 2.0 (© James Bellinger) | https://software.seekye.com/hidsharp |
| DiskInfoToolkit | MPL-2.0 | https://github.com/Blacktempel/DiskInfoToolkit |
| RAMSPDToolkit-NDD | MPL-2.0 | https://github.com/Blacktempel/RAMSPDToolkit |
| Mono.Posix.NETStandard | MIT (Mono project) | https://github.com/mono/mono |
| System.Management, System.IO.Ports, System.IO.FileSystem.AccessControl | MIT (.NET Foundation) | https://github.com/dotnet/runtime |

## CommunityToolkit.Mvvm

- License: MIT, © .NET Foundation and Contributors
- Source: https://github.com/CommunityToolkit/dotnet

## .NET runtime and Windows Desktop runtime

The self-contained builds include the .NET runtime, WPF and Windows Forms.

- License: MIT, © .NET Foundation and Contributors
- Sources:
  - https://github.com/dotnet/runtime
  - https://github.com/dotnet/wpf
  - https://github.com/dotnet/winforms
- The runtime's own third-party notices are published in those repositories (`THIRD-PARTY-NOTICES.TXT`).

## NVIDIA Management Library (NVML)

Vigil does **not** redistribute NVML. At runtime it loads `nvml.dll` from the NVIDIA display driver already
installed on the PC (from `System32` or the NVSMI folder only). NVML is © NVIDIA Corporation and is subject
to NVIDIA's driver license.

## Inno Setup

The Windows installer is built with Inno Setup (© Jordan Russell and Martijn Laan,
https://jrsoftware.org/isinfo.php), under the Inno Setup License. Only the generated setup program is
distributed.

---

NVIDIA, GeForce and RTX are trademarks of NVIDIA Corporation. AMD and Ryzen are trademarks of Advanced
Micro Devices, Inc. Windows is a trademark of Microsoft Corporation. Vigil is an independent project, and
it is not affiliated with or endorsed by these companies.
