# Contributing to PadToMIDI

Read the [README](README.md) for prerequisites, SDK download, and build commands.
Use GitHub Issues for bugs and musical use cases. Include the application version,
Windows build, controller model, and MIDI destination when relevant.

Keep changes focused. Core must remain independent of Avalonia, SDL3, and Windows MIDI
Services. The input-to-MIDI path must not perform file I/O or synchronous UI work.
Record every sounding pitch for its eventual release, including chord voices and
overlapping notes. Preserve Panic and disconnect cleanup.

Run the three xUnit suites before submitting a pull request. Hardware tests are additional
checks; they are not required to build the solution or run unit tests. See
[architecture](docs/ARCHITECTURE.md) and [release verification](docs/MILESTONE-8.md).

For a dependency change, update package versions, development and win-x64 release
lockfiles, [dependency inventory](docs/dependency-inventory.csv), and applicable notices.
Keep Microsoft's preview API conditions in mind when changing MIDI packaging.
