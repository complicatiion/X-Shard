# X-Shard 0.1.9 — Features

X-Shard is an Xbox 360 and Xbox Classic ISO tool for the community.

## Consoles and operations

- Xbox Classic and Xbox 360 in one queue, each with the matching bundled backend (extract-xiso, XGDTool)
- Console selection first, then the operation
- Extract, Create (XISO from folder), List, Rewrite (working copy) and Convert
- Output formats: XISO, GoD, CCI, CSO, ZAR and extracted files
- Scrub None / Partial / Full, split XISO, attach XBE, allowed-media XBE patch, XBE title rename
- Skip $SystemUpdate (on by default) and XBE media-patch control for Xbox Classic
- Device profiles: Xbox Classic, Xbox 360, Xbox Classic · Xemu Emulator, Xbox 360 · Xenia Emulator (editable presets)

## Title information

- Read offline from the image or folder, without extracting
- Xbox Classic: Title ID (hex and publisher code, e.g. MS-004), name, region, version, disc and title icon from `default.xbe`
- Xbox 360: Title ID, Media ID, region, version and disc from `default.xex`; name and icon from GoD content headers
- Icon and title in the queue row, full details in the Title tab

## Sources and queue

- Single ISO files, ISO folders (optionally recursive), source folders, extracted game and GoD folders
- Drag and drop, Explorer context menu, single running instance that receives new files
- Split image parts are not added twice
- Search (Ctrl+F), status filter and sorting (order added, name, status)
- Start, cancel current job, cancel waiting jobs, stop after current job, retry, remove, clear finished, clear all
- Per job: title icon, source path, operation, console, status, duration, progress and output-folder shortcut
- Total progress, taskbar progress and timed preparation feedback while a backend analyzes quietly
- Queue is saved and restored; interrupted jobs can be retried

## Before and after a run

- Target preview: destination, expected folder structure, estimated size and free space (local drives and network shares)
- Free-space warning before the queue starts
- Run report (TXT or self-contained HTML): succeeded/failed/cancelled, durations, sizes and space saved; automatic or on demand
- Windows notification when the queue finishes and taskbar flash when in the background
- Optional hibernate or shutdown after the queue, with a 60-second countdown and Cancel

## Emulators and libraries

- Output targets: custom folder, Xemu library, Xenia library, RetroBat (`roms\xbox`, `roms\xbox360`), Batocera share
- Single-file results placed directly in library folders
- Start the result or source in Xemu (`-dvd_path`) or Xenia

## Safety and privacy

- Sources are never modified; every writing job gets its own new destination
- Private staging folder per job; output is published only after success
- Existing destinations are never overwritten; collisions are renamed automatically
- No telemetry, no account, no cloud; offline mode on by default and every session starts offline
- Online backend lookups and external links only after an explicit Yes/No confirmation
- Local crash logs (newest ten), nothing is uploaded

## Interface

- Modern interface with icon buttons, Dark (Windows 11 Mica), Xbox, Light or System theme; green default accent, five more colors or the Windows accent
- Auto UI scaling for compact 11–14" displays (70–100 %), presets and Custom 100–200 %
- Inline job details that never squeeze the window; modal details on small displays
- English, German, Spanish, French, Polish and Danish with flat flag buttons; more languages via XML files

## Command line

- `xshard-cli.exe` with extract, create, list, rewrite, convert and info
- Same planning, backends, settings and offline default as the app; reports with `--report`
- Exit codes for scripts

## Distribution

- Self-contained portable ZIP and Windows installer (Program Files x64), no .NET installation needed
- Installer option for the Explorer context menu (all users)
- Integrated legal reader with first-start acceptance; GPL source accompaniment and all third-party licenses included
