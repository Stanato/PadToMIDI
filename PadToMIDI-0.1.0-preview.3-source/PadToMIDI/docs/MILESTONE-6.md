# Milestone 6 — Configuration editor and scale catalog

The Settings window now edits scale, button assignments, discrete stick actions, and all four
expression lanes. Core contains all 13 requested scale presets. No NuGet or native dependencies
were added; the existing dependency/license inventory is unchanged.

## Using Settings

Open **Settings** on the main window. Controller input and MIDI generation continue while it is open.

| Tab | Editable settings |
| --- | --- |
| Scale | Root, scale preset, base octave, velocity, MIDI channel |
| Buttons | Every normalized button: None, scale degree, fixed note, octave modifier, semitone modifier |
| Stick actions | X/Y sources, threshold, neutral dead zone, four directional actions |
| Expression | D-pad/face pressure, pitch bend, timbre: enable, sources, responses, timbre CC |

**Apply** validates the entire draft before updating the instrument. Missing, fractional, or
out-of-range numeric settings produce an inline error without partially applying changes.
**Reset draft to defaults** loads the current Default C Major configuration into the editor;
choose Apply to use it. **Close** discards unapplied edits. **Panic** works without applying the draft.
Reopening the editor starts from the current applied settings.

The initial Milestone 6 editor was session-only. [Milestone 7](MILESTONE-7.md) now adds JSON profiles,
profile management, and import/export. Choose Save in Profiles to keep applied settings.

## Scales and mappings

| Preset | Semitone intervals from root |
| --- | --- |
| Major | 0, 2, 4, 5, 7, 9, 11 |
| Natural Minor | 0, 2, 3, 5, 7, 8, 10 |
| Harmonic Minor | 0, 2, 3, 5, 7, 8, 11 |
| Melodic Minor | 0, 2, 3, 5, 7, 9, 11 |
| Dorian | 0, 2, 3, 5, 7, 9, 10 |
| Phrygian | 0, 1, 3, 5, 7, 8, 10 |
| Lydian | 0, 2, 4, 6, 7, 9, 11 |
| Mixolydian | 0, 2, 4, 5, 7, 9, 10 |
| Locrian | 0, 1, 3, 5, 6, 8, 10 |
| Major Pentatonic | 0, 2, 4, 7, 9 |
| Minor Pentatonic | 0, 3, 5, 7, 10 |
| Blues | 0, 3, 5, 6, 7, 10 |
| Chromatic | 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 |

Degrees wrap by the chosen scale's length. For example, Major Pentatonic degree 6 is the next
octave's root. Melodic Minor uses ascending intervals in both directions. The preset catalog lives
in Core; custom `ScaleDefinition` values are supported by the engine and preserved when editing an
existing configuration. This UI selects presets rather than defining new interval lists.

Button assignments are exclusive. A scale degree follows root/scale/octave; a fixed note specifies
a MIDI pitch before configured momentary transposition. Modifier values are signed, nonzero offsets.
Button CC actions and per-note/MPE expression remain future extensions.

Defaults preserve your changes: left-stick **Up/Down = momentary octave up/down**, **Left/Right =
persistent octave down/up**; held **Left Bumper = flat**, **Right Bumper = sharp** on newly pressed notes.
Horizontal changes remain after centering; vertical shifts clear on centering and apply relative to
the current persistent octave. Holding a direction does not repeatedly change the octave.
The stick editor also offers persistent octave and optional last-note sharp/flat actions. Its action
threshold must exceed the neutral dead zone. A full return to neutral rearms the next action.

## Expression and safety

Each expression lane has its own dead zone, curve (Linear, Quadratic, Square Root), sensitivity,
smoothing time, and update rate. Trigger lanes also have a minimum threshold; stick lanes allow
inversion. Sources can be reassigned within the appropriate trigger/stick controls. Timbre CC is
0–119; channel-mode commands 120–127 cannot be used for continuous timbre. Rate 0 disables rate
limiting; smoothing 0 disables filtering. See [Milestone 5](MILESTONE-5.md) for response semantics.

Apply changes future note presses. Existing held records retain their exact pitch, channel, and
velocity, so releasing after root/scale/octave/mapping/channel edits stops the original note.
Expression transitions reset old channel/controller destinations before generating values for new
settings. Existing held-note pressure follows the held record's original pitch/channel.

Editor ViewModels build immutable configuration; they contain no note generation or MIDI sends.
`InstrumentSession` delegates Apply to Core's `MidiInstrument`, which coordinates the transition
with the input path. Input is never routed through the editor or Avalonia controls.

## Build, run, and verification

Use the SDK/environment setup in [README](../README.md), then:

```powershell
dotnet restore PadToMIDI.sln --locked-mode
dotnet build PadToMIDI.sln -c Release --no-restore
dotnet test --project tests/PadToMIDI.Core.Tests -c Release --no-build --no-restore
dotnet test --project tests/PadToMIDI.App.Tests -c Release --no-build --no-restore
dotnet test --project tests/PadToMIDI.Midi.Windows.Tests -c Release --no-build --no-restore
dotnet run --project tools/PadToMIDI.SettingsProbe -c Release
dotnet run --project tools/PadToMIDI.MidiProbe -c Release -- --loopback-test
dotnet run --project src/PadToMIDI.App -c Release --no-build --no-restore
```

Validated in this workspace:

- **209 Core + 16 editor + 18 MIDI encoding tests = 243 passing tests.** New cases cover every
  preset, all-root degree wrapping, held-note releases across scale changes, assignment families,
  atomic draft validation, expression settings, reset/close, Panic, and channel observation.
- Actual Avalonia controls/bindings tested with synthetic input and in-memory MIDI. All four tabs
  rendered; invalid Apply leaves MIDI/configuration unchanged. Valid Apply verifies F# Dorian,
  fixed-note mapping, velocity/channel changes, old held releases, custom CC cleanup, reset,
  discard/reopen, and Panic. Screenshots are saved to `artifacts/settings-*.png`.
- Real Windows MIDI Services loopback verifies edited F# Dorian, fixed mapping, velocity, channel,
  CC11, and the previous held note's exact release over received UMP, alongside the existing
  expression and backend cleanup checks. Temporary test endpoints are removed afterward.
- Debug/Release builds and the refreshed published app startup smoke test pass without warnings.

Editor unit/UI tests require Windows but no physical controller or MIDI service. The native loopback
test requires the existing Windows MIDI Services runtime. Physical controller/DAW compatibility and
audible expression remain manual checks.

The existing published directory has been refreshed so the previous quick-launch command still works:

```powershell
.\.tools\dotnet\dotnet.exe artifacts\current-publish\PadToMIDI.dll
```

Windows 11 25H2+ remains required by the pinned preview MIDI SDK. Next: Milestone 7 profiles and JSON
persistence, followed by Milestone 8 packaging and release polish.
