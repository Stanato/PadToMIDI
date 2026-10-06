# Milestone 8: desktop polish and preview packaging

Version **0.1.0-preview.1** adds an original controller icon, versioned application header,
Help window, an always-visible Panic button, and keyboard shortcuts. Startup failures show
a readable message and write `%LOCALAPPDATA%\PadToMIDI\Logs\startup.log`.
A named mutex prevents two instances from competing for the same controller.

## Run the packaged application

The latest package is preview 3, renamed to PadToMIDI, with [chord mode](CHORD-MODE.md). Open `artifacts/releases/0.1.0-preview.3/PadToMIDI-0.1.0-preview.3-win-x64-setup.exe`.
It installs for the current user, defaults to `%LOCALAPPDATA%\Programs\PadToMIDI`, and
adds Start Menu and Installed Apps entries. Administrator privileges are not needed.
Alternatively extract the ZIP beside it and run `PadToMIDI/PadToMIDI.exe`.
Both include the .NET runtime; neither requires a .NET SDK or separate runtime installation.

Windows 11 25H2 or newer, x64, and Windows MIDI Services are required. Install the service
and desired loopback/synth transports separately using Microsoft's
[Preview 10 instructions](https://github.com/microsoft/MIDI/releases/tag/inbox-preview-10).
Select an output, choose Connect, then enable its receiving input in the DAW/synth.
Profiles remain in `%LOCALAPPDATA%\PadToMIDI\Profiles` when upgrading or uninstalling.
The first launch copies saved profiles and the active selection from the previous app's data
folder without overwriting existing profiles or removing the originals.
Close the app before upgrading or uninstalling. The installer refuses while it is running.

Shortcuts, while the app has focus:

| Shortcut | Action |
| --- | --- |
| F1 | Help |
| Ctrl+, | Settings |
| Ctrl+Shift+P | Profiles |
| Ctrl+Shift+Esc | Panic, including inside dialogs |

## Build in VS Code on Windows

Install the x64 .NET 10 SDK and [NSIS 3.11](https://sourceforge.net/projects/nsis/files/NSIS%203/3.11/).
Use the compiler at the NSIS root, beside `COPYING`, not a relocated executable.
This workspace has SDK 10.0.401 and NSIS under `.tools`; a fresh checkout must install them.
The tested NSIS 3.11 portable ZIP SHA-256 is
`C7D27F780DDB6CFFB4730138CD1591E841F4B7EDB155856901CDF5F214394FA1`.

```powershell
# System-installed tools:
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Release.ps1 -NsisCompiler 'C:\Program Files (x86)\NSIS\makensis.exe'
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-Release.ps1
```

For the tools already present in this workspace:

```powershell
$env:DOTNET_CLI_HOME = "$PWD\.tools\cli-home"
$env:NUGET_PACKAGES = "$PWD\.tools\packages"
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Release.ps1 -Dotnet "$PWD\.tools\dotnet\dotnet.exe"
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-Release.ps1
```

The build performs locked restore, Release compilation, all three xUnit suites,
self-contained win-x64 publishing, version/runtime checks, manifest generation, NSIS compilation,
ZIP packaging, and SHA-256 generation. `-SkipTests` is for packaging iterations after tests pass.
Normal development lockfiles remain separate from `packages.win-x64.lock.json` release graphs.
`release/version.json` specifies release version, supported OS, and expected runtime 10.0.12.
If a newer SDK chooses a newer runtime, review and update metadata and release lockfiles together;
the packaging check fails rather than silently changing runtime versions.

Output: installer, portable ZIP, `SHA256SUMS.txt`, and `build.json` under the versioned release folder.
The payload carries a file-level integrity manifest, quick start, dependency inventory, and original
licenses/notices, including the exact bundled runtime notices. Trimming and single-file publishing
are disabled to preserve native SDL and WinRT DLL/PRI discovery.

## Verification and limits

Verified in this workspace: Debug and Release builds with zero warnings/errors;
330 xUnit tests (277 Core, 35 App, 18 MIDI), including saved-profile migration;
the release verification script; and the real Avalonia settings/chord binding probe.
The profile/Help/Panic probe also provides normal and compact layout inspection.

`Test-Release.ps1` verifies archive and payload hashes, portable startup with an invalid global
runtime location, native startup from an unrelated working directory, install/upgrade, installed
startup, single-instance protection, running-app install/uninstall rejection, graceful shutdown,
uninstall, and preservation of foreign files and isolated profile data. All installation fixtures
stay under `artifacts`; `/TESTMODE` skips user registry and shortcut writes. Normal Start Menu and
Installed Apps integration should also be checked manually before distributing publicly.

The real Avalonia profiles probe additionally checks main-window and Help Panic commands and
renders Help, normal, and compact layouts:

```powershell
dotnet run --project tools/PadToMIDI.SettingsProbe -c Release -- --profiles-test
```

This is an unsigned preview package. The tested SDL binding ships a 3.5.0 development snapshot;
Windows MIDI Services uses preview APIs. Dependency notices and Microsoft's preview go-live/update
conditions remain applicable. The application owner's distribution license and signing certificate
were not chosen by this milestone. See [LICENSE.txt](../LICENSE.txt) for current project terms.
Packages remain unsigned previews. Before promoting a stable release, complete physical controller,
DAW, clean-machine prerequisite, and normal installer integration checks.
