# Dependencies — X-Shard 0.1.9

## End users

None. Portable and installer releases are self-contained (.NET runtime included). No Visual C++ redistributable, no internet connection and no administrator rights are required to run X-Shard (the installer itself needs admin rights for Program Files).

- Windows 10 (1809 or newer) or Windows 11, x64.

## Runtime and framework (bundled)

| Component | Version | License |
| --- | --- | --- |
| .NET runtime (self-contained, in both EXEs) | 10.0 | MIT (`licenses/dotnet-LICENSE.txt`, `dotnet-ThirdPartyNotices.txt`) |
| Windows SDK projection (CsWinRT, toast notifications; GUI only) | 10.0.19041 | MIT (part of the .NET Windows targeting pack) |
| Avalonia / Avalonia.Desktop / Avalonia.Themes.Fluent | 11.3.22 | MIT |
| Markdown.Avalonia.Tight (legal reader) | 11.0.3 | MIT |
| SkiaSharp + native assets | 2.88.9 | MIT |
| HarfBuzzSharp + native assets | 8.3.1.1 | MIT |
| Avalonia.Angle.Windows.Natives | 2.1.25547.20250602 | see `licenses/nuget` |
| MicroCom.Runtime | 0.11.0 | MIT |
| ColorDocument.Avalonia / ColorTextBlock.Avalonia | 11.0.3 | MIT |
| Tmds.DBus.Protocol (transitive, unused on Windows) | 0.21.3 | MIT |

The package graph is pinned in `src/XShard/packages.lock.json`, which NuGet regenerates on the first restore after the 0.1.8 target-framework change (commit it afterwards). `Avalonia.BuildServices` 11.3.2 is referenced with all assets excluded (no build telemetry).

## Programs in the release folder

| File | Content |
| --- | --- |
| `X-Shard.exe` | Desktop app (self-contained single file, Avalonia, Windows SDK projection) |
| `xshard-cli.exe` | Command line (self-contained single file, trimmed; no UI libraries) |
| `XShard.Core` | Shared services, embedded in both EXEs |

## Backends (bundled, separate executables)

| Backend | Version | Purpose | License |
| --- | --- | --- | --- |
| extract-xiso (XboxDev) | 2.7.1, statically linked MinGW build | Original Xbox XISO extract/create/list/rewrite | Modified BSD-style (`licenses/extract-xiso.txt`) |
| XGDTool (wiredopposite) | 1.0.0 Windows x64 CLI | Xbox 360 and advanced conversion (XISO, GoD, CCI, CSO, ZAR, Extract) | GNU GPL v3 (`licenses/XGDTool-GPL-3.0.txt`, source in `sources/`) |

Both import only Windows system DLLs.

## Build machine (developers only)

| Tool | Version | Used for |
| --- | --- | --- |
| .NET SDK | 10.0 (with the Windows targeting pack, included in the SDK) | Build, publish, verification harnesses, legal RTF generator |
| Visual Studio | 2022 17.14+ (or SDK CLI) | IDE build; Release build triggers the release script |
| PowerShell | 7.x preferred, 5.1 supported | `installer/publish-portable.ps1`, `installer/build-backend.ps1` |
| Inno Setup | 6.3 or newer | Installer (`installer/setup.iss`), including the optional context-menu task |
| Avalonia.Headless | 11.3.22 | UI verification harness only |
| Visual Studio C++ tools or MinGW-w64 GCC | optional | Rebuilding extract-xiso (`installer/build-backend.ps1`) |
| NuGet access | — | Package restore on the build machine |
