# PadToMIDI 0.1.0-preview.3

Turn a standard game controller into an expressive MIDI instrument for Windows.
This preview includes scale-based notes, Start-button chord mode, octave and accidental
modifiers, polyphonic aftertouch, pitch bend, timbre, editable settings, and JSON profiles.

Requirements: **Windows 11 25H2 or newer, x64**, plus Windows MIDI Services and a configured
MIDI destination. Follow Microsoft's [Preview 10 setup](https://github.com/microsoft/MIDI/releases/tag/inbox-preview-10)
for the service tools and transports. These installers are not included.

Downloads:

- `PadToMIDI-0.1.0-preview.3-win-x64-setup.exe`: per-user installer.
- `PadToMIDI-0.1.0-preview.3-win-x64.zip`: extract and run `PadToMIDI/PadToMIDI.exe`.
- `PadToMIDI-0.1.0-preview.3-source.zip`: MIT-licensed source, build scripts, docs, and notices.
- `SHA256SUMS.txt`: SHA-256 hashes for these three downloads.

The application packages include the .NET runtime. This is an **unsigned preview** using
preview Windows MIDI APIs and the pinned SDL 3.5.0 development binary. API updates may
require a rebuilt application. PadToMIDI is MIT-licensed; dependency licenses and
Microsoft's preview conditions remain independently applicable. Notices are included.

Default: C Major, octave 3, velocity 100, MIDI channel 1. Start toggles scale-derived triads.
Connect an output in PadToMIDI and enable its receiving MIDI input in your DAW/synth.
Use Panic to stop tracked notes and reset expression.

This version renames the product to PadToMIDI. Existing profile libraries are copied
into the new data folder without overwriting destination profiles or deleting originals.
Close previous versions before launching or installing this release.

Validation: 330 unit tests passed; Debug/Release builds and Avalonia settings bindings
passed; portable startup, isolated install/upgrade/uninstall, running-app guards, bundled
runtime, payload hashes, and user-data preservation passed. Normal registry/shortcut
integration and physical controller/DAW compatibility are additional manual checks.
