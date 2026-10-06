using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Input;

namespace PadToMIDI.Core.Musical;

public static class ChordResolver
{
    public static bool CanEnable(InstrumentConfiguration configuration) => configuration.Chords.Shape != ChordShape.ScaleTriad ||
        (configuration.Chords.HarmonyScale ?? configuration.Scale).Intervals.Length == 7;

    public static bool TryResolve(InstrumentConfiguration configuration, PhysicalControl control,
        int baseOctave, int octaveShift, int semitoneShift, out NoteRecord record, out string reason)
    {
        record = default; reason = "Out of MIDI range";
        if (!configuration.Mappings.TryGetValue(control, out var action)) return false;
        long raw;
        int letter = -1;
        if (action is ScaleDegreeAction degree)
        {
            long index = (long)degree.Degree - 1;
            raw = (baseOctave + 1L) * 12 + (int)configuration.Root + index / configuration.Scale.Intervals.Length * 12 +
                configuration.Scale.Intervals[(int)(index % configuration.Scale.Intervals.Length)];
            letter = NoteSpelling.DegreeLetter(configuration.Root, configuration.Scale, degree.Degree);
        }
        else if (action is FixedNoteAction fixedNote) raw = fixedNote.MidiNote;
        else return false;
        long root = raw + (long)octaveShift * 12 + semitoneShift;
        long third = root, fifth = root;
        if (configuration.Chords.Enabled)
        {
            if (configuration.Chords.Shape == ChordShape.ScaleTriad)
            {
                if (action is FixedNoteAction) { reason = "Fixed notes need an explicit chord shape"; return false; }
                var harmony = configuration.Chords.HarmonyScale ?? configuration.Scale;
                if (harmony.Intervals.Length != 7) { reason = "Choose a seven-note harmony scale or explicit chord shape"; return false; }
                int offset = (int)((raw - (int)configuration.Root) % 12 + 12) % 12;
                int index = harmony.Intervals.IndexOf(offset);
                if (index < 0) { reason = "Note is outside the harmony scale; choose an explicit chord shape"; return false; }
                third += harmony.Intervals[(index + 2) % 7] - harmony.Intervals[index] + (index + 2) / 7 * 12;
                fifth += harmony.Intervals[(index + 4) % 7] - harmony.Intervals[index] + (index + 4) / 7 * 12;
                letter = (NoteSpelling.TonicLetter(configuration.Root, harmony) + index) % 7;
            }
            else
            {
                third += configuration.Chords.Shape is ChordShape.Minor or ChordShape.Diminished ? 3 : 4;
                fifth += configuration.Chords.Shape switch { ChordShape.Diminished => 6, ChordShape.Augmented => 8, _ => 7 };
                if (letter < 0 && root is >= 0 and <= 127)
                    letter = NoteSpelling.TonicLetter((PitchClass)(root % 12), configuration.Chords.Shape is ChordShape.Minor or ChordShape.Diminished ? ScalePresets.NaturalMinor : ScalePresets.Major);
            }
        }
        if (root is < 0 or > 127 || third is < 0 or > 127 || fifth is < 0 or > 127) return false;
        record = new(control, (byte)root, configuration.MidiChannel, configuration.FixedVelocity, NoteGroup.Other, 0,
            configuration.Chords.Enabled ? (byte)third : null, configuration.Chords.Enabled ? (byte)fifth : null, letter);
        reason = ""; return true;
    }
}
