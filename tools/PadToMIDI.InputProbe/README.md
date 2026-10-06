# Native input probe

This optional console check is separate from the hardware-free Core test suite.
Run `dotnet run --project tools/PadToMIDI.InputProbe/PadToMIDI.InputProbe.csproj -c Release`
from the repository root on Windows x64.

It initializes SDL on the process main thread and creates temporary in-process SDL virtual
gamepads. A driver task feeds input using SDL APIs documented as thread-safe. The real
`SdlGamepadInput` backend handles discovery, generic input, selection, disconnect, hot-plug,
and shutdown. No device drivers, profiles, MIDI endpoints, or system settings are modified.
The Core mapping engine is subscribed directly to normalized input. The probe checks all eight
C Major Note On/Off pairs and stops held notes when switching/disconnecting controllers.
SDL virtual devices are removed when the process exits. A timeout/failure returns a nonzero code.

The probe requires native SDL3 and is not a physical controller compatibility test.
