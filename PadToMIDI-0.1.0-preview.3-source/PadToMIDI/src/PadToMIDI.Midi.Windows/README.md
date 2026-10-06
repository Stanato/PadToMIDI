# Windows MIDI backend — Milestone 3

This project intentionally contains no MIDI backend implementation or SDK dependency through Milestone 2.
It will implement `PadToMIDI.Core.Midi.IMidiOutput` with Windows MIDI Services,
not the legacy WinMM API. Milestone 2 generates normalized note events internally;
the current application does not send them to a MIDI endpoint.

Before implementation, verify the installed Windows build/service, SDK runtime deployment,
current Microsoft NuGet package and C#/WinRT projection, endpoint enumeration, and a loopback
endpoint visible to the target DAW. Keep session, endpoint, UMP conversion, and error recovery here.
Output switching/shutdown must ask the engine to clean up on the old connection before closing it.

Official reference: https://microsoft.github.io/MIDI/sdk-reference/
