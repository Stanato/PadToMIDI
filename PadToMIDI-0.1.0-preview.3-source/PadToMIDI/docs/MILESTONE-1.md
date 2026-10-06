# Milestone 1

This records the initial milestone's implementation and validation. Current behavior and test
results are documented in [Milestone 2](MILESTONE-2.md).

Implemented: all five requested solution projects, Avalonia MVVM desktop host, native SDL3
initialization, controller discovery/selection, hot-plugging, normalized button/axis events,
live generic controller drawing, trigger percentages/bars, stick coordinates, button indicators,
connection/error messages, and safe native resource shutdown. Package versions are pinned and
lock files are checked in. Build/run/debug instructions are in the root README.

## Verification on this workspace

- .NET SDK 10.0.401 installed locally under the ignored `.tools` directory.
- Release solution build: zero warnings, zero errors.
- Core xUnit suite: 21 passing tests, zero skipped. Covers normalized value bounds/independence,
  immutable snapshots, selection filtering, simultaneous adjacent D-pad input, release,
  switching, stale events, disconnect/fatal/shutdown resets, and concurrent observation.
- Native SDL probe: two virtual gamepads passed discovery, selection, generic button mapping,
  exact full-range stick/trigger normalization, button release, switching, unplug cleanup,
  lifecycle notifications, addition/selection of a newly hot-plugged virtual controller, and shutdown.
- Application smoke check: Avalonia and bundled SDL3 initialized successfully; no physical
  controller was present. Generated monitor preview was visually inspected.

## Physical hardware acceptance check

1. Launch the app with no controller; verify the waiting status and neutral display.
2. Connect a controller after launch; verify device discovery and live selection.
3. Verify face directions, every D-pad direction, bumpers, stick clicks, and optional buttons.
4. Move each stick to both extremes and center; check the sign convention and visible drift.
5. Fully press/release each trigger; check 0–100% and independent left/right values.
6. Check adjacent D-pad combinations that the device reports; no opposite combinations are required.
7. Connect a second controller, switch selection, and verify only selected-device input appears.
8. Disconnect the selected controller while buttons/triggers are held; verify neutral reset.
9. Reconnect and select it; check USB and Bluetooth where available, also with another window focused.
10. Close the app and relaunch; verify discovery and native resources work again.

Hardware acceptance remains unverified. Windows MIDI Services and DAW/synth checks belong to
Milestone 3 and have not been performed. No scales, playable notes, expression processing,
editable musical mappings, profiles, panic MIDI messages, or installer are implemented here.

## Next milestone

Add configurable scale definitions and the C Major degree mapping in Core, with held-note records
and tests proving exact corresponding Note Offs across configuration changes. Keep it hardware-free
and route the engine directly from normalized input. Add real MIDI output only after these tests pass.
