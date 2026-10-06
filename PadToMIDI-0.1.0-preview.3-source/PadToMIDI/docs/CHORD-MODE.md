# Chord mode

Available in **0.1.0-preview.2**. Press the normalized **Start** button (Menu/Options position)
to toggle single notes and chords. One press toggles once; holding it does not repeat.
The mode appears beneath the scale on the main window. Toggle changes affect new presses.
Held gestures release the exact notes and channels captured when they began.

Default chord mode plays root-position scale triads at the existing degree assignments:

| Button | C Major chord | Pitches at octave 3 |
| --- | --- | --- |
| D-pad Down | C | C3 E3 G3 |
| D-pad Up | Dm | D3 F3 A3 |
| D-pad Left | Em | E3 G3 B3 |
| D-pad Right | F | F3 A3 C4 |
| Face South | G | G3 B3 D4 |
| Face North | Am | A3 C4 E4 |
| Face West | Bdim | B3 D4 F4 |
| Face East | C | C4 E4 G4 |

ScaleTriad uses positions d, d+2, d+4 of a seven-note harmony scale, wrapping upwards.
Major/minor/diminished/augmented qualities come from those actual pitches. The same resolver
handles all roots, major/minor forms, modes and custom seven-note scales. Unusual custom
voicings are labeled “scale triad” instead of claiming a conventional triad quality.
Natural Minor includes a minor dominant; Harmonic Minor includes a major dominant and
augmented third-degree triad. Melodic Minor uses the existing ascending form in both directions.
This harmonizes the chosen collection; it does not infer tonal function, progression, or voice leading.
See [triad construction](https://www.musictheory.net/lessons/43) and
[minor-key harmony](https://musictheory.pugetsound.edu/mt21c/DiatonicChordsInMinor.html).

## Settings and profiles

Settings → Chords controls current mode, toggle button, chord shape and harmony scale.
Apply settings, then Create/Save in Profiles. The current mode and harmony choices are saved.
Existing profiles without chord settings load in Note mode with Start reserved as the toggle.
Legacy profiles that already assign Start to a note or modifier retain that assignment and disable
the new toggle; choose another toggle button in Settings to enable it.
Assign None to the toggle button in Buttons; conflicting assignments fail validation.
Choose Disabled as the toggle to use that physical button for another mapping.

“Use selected scale” is the default harmony choice. Pentatonic, minor blues and chromatic
collections need a separate seven-note harmony scale or an explicit shape. An unsuccessful
Start toggle leaves Note mode active and shows an explanation. If Chord mode is applied
with unsupported harmony, the preview explains the problem and note presses are rejected.

A separate harmony scale shares the melody scale's tonic. Chords are built on the *actual
melody note*, not its button position. Thus C major pentatonic position 4 is G and produces
G–B–D when harmonized by C Major. Roots outside the harmony collection are rejected.
Explicit Major/Minor/Diminished/Augmented shapes work on every melody or fixed-note root;
they can contain notes outside the selected scale. Fixed-note mappings require an explicit shape.

## Expression and safety

Momentary/persistent octaves shift every tone by 12 semitones. Bumpers shift every tone by
one semitone, preserving quality. Optional sharp/flat-last-note stick actions retune the whole
selected chord, preserving velocity and updating releases. A chord outside MIDI 0–127 is
rejected as a whole; its pitches are never clamped or silently omitted.

Each trigger controls every tone of the latest active chord in its group. Previous selection
pressure clears and falls back to the older active chord on release. Redundant messages and
rate limiting retain the existing behavior. MIDI 1 cannot independently express coincident
channel/pitch voices: shared pitches remain sounding until all owners release, and the most
recent active group selection controls their pressure. Bend/timbre remain channel-wide.
Panic, device/output transitions and faults clear every tracked voice.

Pitch spelling follows scale letters, including flats, double accidentals, E# and B# where
appropriate. Enharmonic tonic spellings are selected to reduce accidentals; octave labels
follow the written letter (B#3 and C4 have the same MIDI pitch). Fixed/chromatic notes use
generic pitch names when no diatonic spelling is defined. MIDI activity logs remain raw protocol
note names because MIDI messages carry no key/spelling metadata.

## Verification

Verified preview 2: **327 xUnit tests passed** (277 Core, 32 App, 18 MIDI), native SDL virtual
controller integration, real Windows MIDI Services loopback, real Avalonia settings/profile/Help
checks, and installer/portable integrity, startup, upgrade, running-instance guards, graceful
shutdown, full uninstall and user-data preservation. Installer verification uses isolated TESTMODE
fixtures; normal Start Menu/Installed Apps integration retains the manual check described in
[Milestone 8](MILESTONE-8.md).

Core tests exercise independent triad expectations for every default seven-note preset in
all 12 roots, exact releases, modifiers, toggle edges, shared voices/pressure, invalid choices,
range boundaries, JSON compatibility, spelling, and 3,000 random state transitions.
The settings probe checks the real checkbox/shape binding and live mode/chord display.
The SDL virtual-controller probe checks native Start and held chord releases; the MIDI loopback
probe checks actual chord notes, expression and Panic crossing Windows MIDI Services.

```powershell
dotnet test --project tests/PadToMIDI.Core.Tests -c Release
dotnet test --project tests/PadToMIDI.App.Tests -c Release
dotnet run --project tools/PadToMIDI.SettingsProbe -c Release
dotnet run --project tools/PadToMIDI.InputProbe -c Release
dotnet run --project tools/PadToMIDI.MidiProbe -c Release -- --loopback-test
```

Build updated installer/ZIP with `tools/Build-Release.ps1` and verify using `tools/Test-Release.ps1`.
Outputs are under `artifacts/releases/0.1.0-preview.3`. These remain unsigned preview builds.
