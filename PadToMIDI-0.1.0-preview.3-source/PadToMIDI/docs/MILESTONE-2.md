# Milestone 2

Implemented: configurable scale model, C Major default, the eight specified degree-based controls,
internal Note On/Off generation, immutable instrument configuration, extensible degree/fixed-note
actions, held-note records, last-note/group tracking, lifecycle cleanup, and an Avalonia note preview.
No additional NuGet dependencies were introduced. Core still has no platform/UI package references.

## Default notes and octave convention

The default is C Major at base octave 3, fixed velocity 100, and channel 1 (Core channel value 0).
C4 = MIDI 60, C3 = MIDI 48, and C-1 = MIDI 0. DPadDown/Up/Left/Right resolve to C3/D3/E3/F3;
FaceSouth/North/West/East resolve to G3/A3/B3/C4. Scale degrees are one-based and wrap according
to the scale's interval count. Root transposition may naturally cross into another octave.
Unplayable pitches are skipped instead of being clamped to a different note.

Only the Major preset ships in this milestone. `ScaleDefinition` accepts any valid ordered set of
semitone offsets starting at zero within 0..11. Additional named presets remain Milestone 6.

## Core API

Call the engine on one owner thread. The application uses the SDL input owner thread.

```csharp
var configuration = new InstrumentConfiguration();
var engine = new MappingEngine(configuration);
engine.MidiGenerated += message => { /* fast, nonblocking observer/output */ };

var device = new GamepadDeviceId(1);
engine.Process(new(GamepadEventKind.Selected, device));
engine.Process(new(GamepadEventKind.ControlChanged, device, PhysicalControl.FaceSouth, 1));

engine.UpdateConfiguration(configuration with { Root = PitchClass.D, BaseOctave = 4 });
engine.Process(new(GamepadEventKind.ControlChanged, device, PhysicalControl.FaceSouth, 0));
// Still Note Off G3 / MIDI 55 on channel 1: its original held record owns the release.

engine.Reset(); // Stops tracked pitches, clears musical/physical state, retains configuration.
```

`InstrumentConfiguration` validates root, scale, octave (-1..9), velocity (1..127), channel (0..15),
and note mappings. Degree and fixed-note actions are immutable. Degree mapping resolution lives in
`NoteResolver`; ViewModels format the results without implementing pitch/scale rules.
Configuration editing is currently a Core API; UI editing and profile JSON remain later milestones.

## Musical safety and state

- A press creates a `NoteRecord` with physical control, MIDI note, channel, velocity, group, and sequence.
- Release uses that record even after root/scale/octave/mapping/channel/velocity changes.
- Duplicate input edges do not retrigger; changing settings while a button is held does not retrigger.
- Currently held physical controls and note records are separate, including unmapped/out-of-range presses.
- Global and group-specific last-triggered notes persist after release. Last-active group selection falls
  back to the most recent remaining held note, ready for the later aftertouch milestone.
- Multiple physical controls targeting the same channel/pitch share one Note On until the last releases.
  MIDI 1.0 cannot distinguish independent voices sharing that identity. Different channels remain independent.
- Disconnect, replacement selection, fatal input, and stop generate matching Note Offs and clear runtime state.
- A callback exception propagates to the pipeline owner. Held identity is retained for cleanup/retry if a
  Note On/Off observer fails. A permanently failing real output will require backend error handling in Milestone 3.

The application subscribes `InstrumentSession` directly to `IGamepadInput.InputReceived` before SDL starts.
The session owns the engine and an eight-entry ring of normalized MIDI events. The UI independently samples
immutable musical/activity snapshots at 30 Hz. No strings, collection rebuilding, or Avalonia work occur in
the per-note callback. The preview shows the latest eight events newest first, held pitches, last note,
and the default mapping. The full physical button monitor remains available on the Buttons tab.

## Verification

- Release solution build: zero warnings, zero errors.
- Core xUnit suite: **70 passed**, zero failed/skipped (21 previous tests plus 49 musical tests/cases).
- Covers every default control, scale wrapping/transposition/custom scale length, exact MIDI boundaries,
  invalid settings, repeated edges, simultaneous notes, fixed mappings, last-active fallback, immutable
  snapshots, lifecycle cleanup, shared pitches, channel changes, and releases across configuration changes.
- A deterministic 2,000-step mixed-input/configuration test checks every emitted pitch against the held ledger.
- Callback-failure tests prove cleanup retains the original note/channel identity.
- Native SDL virtual-controller probe: all eight C Major notes and matching releases, held-note cleanup on
  device switch/disconnect, hot-plug, and empty musical state after shutdown passed.
- App startup with SDL passed with no physical controller present. A normalized-input UI fixture generated
  14 events with B3/C4 held; the activity/mapping display was rendered and visually inspected.
  Disposing that application session generated both matching Note Offs and cleared held state.

Build/test/run commands remain in the root [README](../README.md). Physical controller USB/Bluetooth
acceptance remains unverified. Temporary UI fixture artifacts live under ignored `.tools`/`artifacts`.

## Remaining milestones

Milestone 3 adds Windows MIDI Services and real DAW/synth validation. This build sends no events to an
endpoint and produces no audio. Temporary octave bumpers, persistent stick octave/accidentals, pressure,
pitch bend/timbre, editable configuration, extra presets, profiles, MIDI panic/reset expression, and packaging
remain in their specified later milestones. `Reset` currently cleans up note state; expression does not exist yet.
