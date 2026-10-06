namespace PadToMIDI.Core.Musical;

/// <summary>Letter spelling is independent of MIDI pitch; C-based octaves follow scientific notation.</summary>
public static class NoteSpelling
{
    private static readonly int[] Naturals = [0, 2, 4, 5, 7, 9, 11];
    private static readonly string[] Letters = ["C", "D", "E", "F", "G", "A", "B"];
    private static readonly string[] Chromatic = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"];
    private static readonly int[] MajorPentatonicLetters = [0, 1, 2, 4, 5];
    private static readonly int[] MinorPentatonicLetters = [0, 2, 3, 4, 6];
    private static readonly int[] BluesLetters = [0, 2, 3, 4, 4, 6];
    private static int Accidental(int pitch, int letter) => (pitch - Naturals[letter] + 18) % 12 - 6;

    public static int TonicLetter(PitchClass root, ScaleDefinition scale)
    {
        int best = 0, cost = int.MaxValue;
        for (int letter = 0; letter < 7; letter++)
        {
            int accidental = Accidental((int)root, letter);
            if (Math.Abs(accidental) > 1) continue;
            int score = Math.Abs(accidental);
            if (scale.Intervals.Length == 7)
                for (int degree = 0; degree < 7; degree++)
                    score += Math.Abs(Accidental(((int)root + scale.Intervals[degree]) % 12, (letter + degree) % 7));
            // Prefer the first minimal spelling; F# wins the equal-cost F#/Gb choice.
            if (score < cost) { best = letter; cost = score; }
        }
        return best;
    }

    public static int DegreeLetter(PitchClass root, ScaleDefinition scale, int degree)
    {
        int index = (degree - 1) % scale.Intervals.Length;
        int letterOffset;
        if (scale.Intervals.Length == 7) letterOffset = index;
        else if (scale.Intervals.SequenceEqual(ScalePresets.MajorPentatonic.Intervals)) letterOffset = MajorPentatonicLetters[index];
        else if (scale.Intervals.SequenceEqual(ScalePresets.MinorPentatonic.Intervals)) letterOffset = MinorPentatonicLetters[index];
        else if (scale.Intervals.SequenceEqual(ScalePresets.Blues.Intervals)) letterOffset = BluesLetters[index];
        else return -1;
        return (TonicLetter(root, scale) + letterOffset) % 7;
    }

    public static string PitchName(byte midiNote, int letter = -1)
    {
        if (letter < 0) return Chromatic[midiNote % 12];
        int accidental = Accidental(midiNote % 12, letter);
        if (Math.Abs(accidental) > 2) return Chromatic[midiNote % 12];
        return Letters[letter] + (accidental < 0 ? new string('b', -accidental) : new string('#', accidental));
    }

    public static string Name(byte midiNote, int letter = -1)
    {
        int octave = midiNote / 12 - 1;
        if (letter >= 0)
        {
            int accidental = Accidental(midiNote % 12, letter);
            if (Math.Abs(accidental) <= 2) octave = (int)Math.Floor((midiNote - accidental) / 12d) - 1;
        }
        return PitchName(midiNote, letter) + octave;
    }

    public static string ChordName(NoteRecord chord)
    {
        string quality = (chord.Third - chord.MidiNote, chord.Fifth - chord.MidiNote) switch
        {
            (4, 7) => "", (3, 7) => "m", (3, 6) => "dim", (4, 8) => "aug", _ => " (scale triad)"
        };
        return PitchName(chord.MidiNote, chord.RootLetter) + quality;
    }
}
