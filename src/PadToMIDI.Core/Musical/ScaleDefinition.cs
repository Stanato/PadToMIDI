using System.Collections.Immutable;

namespace PadToMIDI.Core.Musical;

/// <summary>Ordered semitone offsets within an octave. Degrees are one-based and wrap by octave.</summary>
public sealed class ScaleDefinition
{
    public string Name { get; }
    public ImmutableArray<int> Intervals { get; }

    public ScaleDefinition(string name, ImmutableArray<int> intervals)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (intervals.IsDefaultOrEmpty || intervals[0] != 0)
            throw new ArgumentException("A scale must begin at its root (offset zero).", nameof(intervals));
        for (int i = 0; i < intervals.Length; i++)
            if (intervals[i] is < 0 or > 11 || (i > 0 && intervals[i] <= intervals[i - 1]))
                throw new ArgumentException("Scale offsets must increase strictly within 0..11.", nameof(intervals));
        Name = name;
        Intervals = intervals;
    }

    /// <summary>C4 is MIDI 60; C-1 is MIDI 0. An unplayable result returns false, never clamps pitch.</summary>
    public bool TryGetMidiNote(PitchClass root, int baseOctave, int degree, out byte midiNote)
        => TryGetMidiNote(root, baseOctave, degree, 0, out midiNote);

    /// <summary>Applies transposition before range checking so octave modifiers can bring a degree into range.</summary>
    public bool TryGetMidiNote(PitchClass root, int baseOctave, int degree, int semitoneOffset, out byte midiNote)
    {
        if (!Enum.IsDefined(root))
            throw new ArgumentOutOfRangeException(nameof(root));
        if (baseOctave is < -1 or > 9)
            throw new ArgumentOutOfRangeException(nameof(baseOctave));
        if (degree < 1)
            throw new ArgumentOutOfRangeException(nameof(degree));

        long index = (long)degree - 1;
        long pitch = (baseOctave + 1) * 12 + (int)root +
            index / Intervals.Length * 12 + Intervals[(int)(index % Intervals.Length)] + semitoneOffset;
        midiNote = pitch is >= 0 and <= 127 ? (byte)pitch : (byte)0;
        return pitch is >= 0 and <= 127;
    }
}
