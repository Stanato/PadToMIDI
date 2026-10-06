Scale definitions, normalized note actions, momentary/persistent octaves, and sharp/flat gestures
live in the mapping engine and musical state. Every Note On creates a held-note record used for
its matching Note Off; retuning updates that record before the original physical control releases.
The engine is single-owner and independent of UI, input, and MIDI platform packages.
ExpressionEngine processes trigger pressure, bend, and timbre with time-based smoothing, deduplication,
rate limiting, and cleanup. Input-owner ticks deliver deferred values without routing through UI.
ScalePresets.All contains Major, Natural/Harmonic/Melodic Minor, Dorian, Phrygian, Lydian,
Mixolydian, Locrian, Major/Minor Pentatonic, Blues, and Chromatic. Melodic Minor uses ascending
intervals in both directions. Arbitrary valid scales use the same resolver and can be added later.
ChordResolver constructs scale triads or explicit major/minor/diminished/augmented shapes.
NoteRecord retains every tone of the physical gesture; snapshots flatten voices and group selection
retains the full chord. Start toggles on a press edge; held records never change because of a toggle.
NoteSpelling provides key-aware letter spellings without allocating in analog expression processing.
