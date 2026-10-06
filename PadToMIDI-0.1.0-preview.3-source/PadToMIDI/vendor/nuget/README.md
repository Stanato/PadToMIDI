# Pinned Microsoft Windows MIDI Services SDK

`Windows.Devices.Midi2.0.99.88-preview.10.nupkg` is the unmodified official package from
[Microsoft's Preview 10 release](https://github.com/microsoft/MIDI/releases/tag/inbox-preview-10).
It is hosted on GitHub releases rather than nuget.org. `NuGet.Config` includes this local feed.
The 98 MiB package is downloaded for builds and ignored by Git. It is not included in
the source archive. Run this once after cloning:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Restore-MidiSdk.ps1
```

The script checks SHA-256 before installing or accepting an existing package.
The release build script and VS Code tasks run this step automatically.

Download source:
https://github.com/microsoft/MIDI/releases/download/inbox-preview-10/Windows.Devices.Midi2.0.99.88-preview.10.nupkg

SHA-256: `6DAF121A3F76A4A1E0D46521C5072AC576477448C9332048383BB58960A4CE69`
Machine-readable download metadata: [sdk.json](sdk.json).

Package metadata: MIT. Preview native API redistribution also has the release's go-live conditions;
see [dependency notes](../../docs/DEPENDENCIES.md). No transport or tools installer is bundled.
