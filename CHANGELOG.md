# Changelog

## 0.1.0-preview.3

- Rename the app, solution, projects, executable, and installer to PadToMIDI.
- Copy existing profile libraries into the new data folder without replacing existing files.
- Add the MIT license, GitHub CI/templates, public setup documentation, and source-release tooling.
- Fetch the official MIDI SDK with SHA-256 verification instead of committing the package.
- Preserve note/chord playing, scales, expression, editable mappings, and JSON profiles.
- Verify 330 unit tests, Debug/Release builds, real Avalonia settings bindings, and
  portable/install/upgrade/uninstall behavior in isolated fixtures.

## 0.1.0-preview.2

- Add Start-button chord mode, scale-derived triads, configurable harmony and chord shapes.
- Spell scale notes and chords with key-aware accidentals and correct octave names.
- Track all chord voices for releases, pressure, overlapping notes, and Panic.

## 0.1.0-preview.1

- Add desktop Help, keyboard shortcuts, original controller graphics, and Panic controls.
- Package a self-contained Windows x64 portable ZIP and per-user installer.
- Implement controller discovery, scale-based playing, octave and accidental modifiers,
  polyphonic aftertouch, pitch bend, timbre, settings, and JSON profiles.
