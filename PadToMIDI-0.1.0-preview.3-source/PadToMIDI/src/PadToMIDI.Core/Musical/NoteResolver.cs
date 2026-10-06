using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Input;

namespace PadToMIDI.Core.Musical;

public static class NoteResolver
{
    public static bool TryResolve(InstrumentConfiguration configuration, PhysicalControl control, out byte midiNote)
        => TryResolve(configuration, control, configuration.BaseOctave, 0, out midiNote);

    public static bool TryResolve(InstrumentConfiguration configuration, PhysicalControl control,
        int baseOctave, int temporaryOctaveOffset, out byte midiNote)
        => TryResolve(configuration, control, baseOctave, temporaryOctaveOffset, 0, out midiNote);

    public static bool TryResolve(InstrumentConfiguration configuration, PhysicalControl control,
        int baseOctave, int temporaryOctaveOffset, int temporarySemitoneOffset, out byte midiNote)
    {
        midiNote = 0;
        if (!configuration.Mappings.TryGetValue(control, out var action)) return false;
        switch (action)
        {
            case ScaleDegreeAction degree:
                long semitones = (long)temporaryOctaveOffset * 12 + temporarySemitoneOffset;
                if (semitones is < int.MinValue or > int.MaxValue) return false;
                return configuration.Scale.TryGetMidiNote(configuration.Root, baseOctave, degree.Degree, (int)semitones, out midiNote);
            case FixedNoteAction fixedNote:
                long pitch = fixedNote.MidiNote + (long)temporaryOctaveOffset * 12 + temporarySemitoneOffset;
                if (pitch is < 0 or > 127) return false;
                midiNote = (byte)pitch;
                return true;
            default:
                return false;
        }
    }
}
