# Milestone 4 — Octaves and accidentals

Core now implements momentary stick octaves and held bumper accidentals. Optional persistent
octave and last-note retrigger actions remain configurable. Avalonia observes octave/semitone shifts, resolved mapping previews,
exact held pitches, and last selection. No musical rules or MIDI sends were added to ViewModels.
No NuGet/native dependencies were added. Continuous expression is documented in [Milestone 5](MILESTONE-5.md).

## Default controls

| Control | Action |
| --- | --- |
| Left Bumper / L1 | One semitone lower for notes pressed while held |
| Right Bumper / R1 | One semitone higher for notes pressed while held |
| Both bumpers | Shifts cancel |
| Left stick Up | One octave higher until the stick returns to neutral |
| Left stick Left | One octave lower until the stick returns to neutral |
| Left stick Down / Right | No action |

Default trigger threshold is **0.65**, neutral dead zone **0.25**. Axes are normalized -1..1;
negative Y is Up. Each stick excursion produces at most one action. Both axes must return inside
the neutral square (absolute X/Y ≤ dead zone) before any direction rearms. Moving directly from Up
to Down/Right or holding beyond the threshold does not repeat. The first reported threshold crossing
wins for a diagonal; if both axes cross together, the dominant axis wins, with vertical priority on a tie.
This avoids multiple actions caused by independently arriving X/Y reports.

The base octave stays unchanged with these defaults. Centering the stick removes its temporary shift.
Panic, output/controller switches, and disconnect cleanup clear all held modifiers and direction latches.
Optional persistent octave actions remain limited to **-1..9**; that playing octave survives cleanup.
Changing the configured base octave resets the persistent offset. Profile loading remains a later milestone.

## Note identity and sharp/flat behavior

Note On resolves configured scale degree + playing octave + momentary octave/semitone shifts. The resulting
MIDI pitch/channel/velocity is stored in the physical control's held record. For example: Right Bumper
down → FaceSouth down → Right Bumper up → FaceSouth up emits Note On G#3 / Note Off G#3.
Holding left-stick Up at the same time produces G#4. Pressing FaceSouth without modifiers produces G3.
Pressing/releasing a bumper or centering the stick never changes notes already held.
Changing scale/root/octave/modifier assignments while holding a note never recalculates its release.
Fixed-note actions ignore the persistent base octave but respect both momentary modifiers.
Transposition happens before MIDI range checking; invalid pitches are ignored rather than clamped.

The optional `SharpenLastNote` / `FlattenLastNote` actions still target the global last note; these
are no longer assigned by default. For a currently held note, Core releases the old pitch,
starts the pitch ±1 on its **original channel and velocity**, and updates the held record and global/group
history. Releasing that physical button stops the adjusted pitch. Multiple adjustments require neutral
between gestures. Sequence/group identity is preserved so active group selection remains correct for
future aftertouch. Adjusting below MIDI 0 or above 127 does nothing to the current note or ledger.

A released selection changes only its remembered/displayed pitch and emits no MIDI. It does not
retune an older still-held note or create a new sounding voice. A fresh physical press resolves its
configured mapping again; per-note accidentals do not rewrite the profile or persist as button mappings.
With no last selection, a sharp/flat gesture is silent and still consumes its excursion.

MIDI 1 cannot distinguish overlapping owners of the same channel/pitch. Existing ownership coalescing
is retained: moving one owner away leaves the other sounding; moving into an occupied pitch does not
emit a duplicate Note On; the final owner releases it. If a callback fails during a retune, the held
record retains the exact old/new pitch that fatal cleanup must stop. Backend queue/error handling,
output switching, and Panic continue to release the updated ledger safely.

## Configuration and implementation

- `InstrumentConfiguration.TemporaryOctaveModifiers`: immutable abstract-button → octave-shift map.
  Empty by default; octaves now use stick actions. Button shifts -10..10 excluding zero remain supported.
- `InstrumentConfiguration.TemporarySemitoneModifiers`: immutable abstract-button → semitone-shift map.
  Default Left/Right Bumper = -1/+1. Shifts -127..127 excluding zero are supported.
  Buttons must not overlap note or octave assignments.
- `InstrumentConfiguration.LeftStick`: immutable `DiscreteStickConfiguration` with axis sources,
  threshold/dead zone, and independently remappable/disabled Up/Down/Left/Right actions.
- `StickAction`: None, MomentaryOctaveDown, MomentaryOctaveUp, IncreaseOctave, DecreaseOctave,
  SharpenLastNote, FlattenLastNote.
- `DiscreteStickLatch`: small Core-owned hysteresis state; no per-axis-event allocation.
- `MusicalStateSnapshot`: configured settings remain separate from BaseOctave, PersistentOctaveOffset,
  TemporaryOctaveOffset, TemporarySemitoneOffset, and LeftStickLatch. Held records remain authoritative for release.
- `NoteResolver` / `ScaleDefinition`: pure transposition-aware resolution, also used for UI previews.

Settings are validated before application. Threshold must be finite in (0,1]; dead zone finite in
[0, threshold). Axis sources must be normalized stick X/Y controls, action enums must be valid, and
modifier/note assignments must not overlap. Settings are programmatically configurable now;
their full editing UI and JSON profiles belong to Milestones 6–7.

## Build, run, and validation

Use the SDK setup in [README](../README.md), then:

```powershell
dotnet build PadToMIDI.sln -c Release
dotnet test --project tests/PadToMIDI.Core.Tests -c Release --no-build --no-restore
dotnet test --project tests/PadToMIDI.Midi.Windows.Tests -c Release --no-build --no-restore
dotnet run --project tools/PadToMIDI.InputProbe -c Release
dotnet run --project tools/PadToMIDI.MidiProbe -c Release -- --synth-test
dotnet run --project src/PadToMIDI.App -c Release --no-build --no-restore
```

Validated in this workspace:

- **138 Core tests**; the backend retains its 14 MIDI encoding tests. Coverage includes octave/accidental modifiers, correct
  release, hysteresis/diagonals, threshold/source/action configuration, MIDI/playing-octave boundaries,
  state/history updates, shared pitches, failure cleanup, output switching, and Panic.
- A deterministic **10,000-step** gesture/modifier/configuration stress test compares every emitted
  pitch's ownership with held records and verifies complete final cleanup.
- Native SDL virtual-controller tests exercise both held bumper accidentals, momentary stick Left/Up,
  exact modified release after centering/modifier release, and switching/disconnect cleanup.
- Real Windows MIDI Services loopback receives the explicit octave/accidental Note On/Off sequence,
  including combined modifiers and an already-held note whose bumper state changes.
- Avalonia smoke/render checks verify live octave/shift labels and preview notes. The existing
  `artifacts/current-publish` launch folder is refreshed with Milestone 4 for this workspace.

Physical gamepad/DAW compatibility and audible playback on the user's audio setup still need manual
checks. The existing Windows 11 25H2+ preview SDK requirement remains unchanged.
