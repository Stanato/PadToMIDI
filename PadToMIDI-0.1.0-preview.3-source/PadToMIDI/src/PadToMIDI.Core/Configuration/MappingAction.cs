using System.Text.Json.Serialization;

namespace PadToMIDI.Core.Configuration;

/// <summary>Extensible action family. Later milestones can add CC and expression actions.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ScaleDegreeAction), "scaleDegree")]
[JsonDerivedType(typeof(FixedNoteAction), "fixedNote")]
public abstract record MappingAction;

public sealed record ScaleDegreeAction : MappingAction
{
    public int Degree { get; }

    public ScaleDegreeAction(int degree)
    {
        if (degree < 1)
            throw new ArgumentOutOfRangeException(nameof(degree));
        Degree = degree;
    }
}

public sealed record FixedNoteAction : MappingAction
{
    public byte MidiNote { get; }

    public FixedNoteAction(byte midiNote)
    {
        if (midiNote > 127)
            throw new ArgumentOutOfRangeException(nameof(midiNote));
        MidiNote = midiNote;
    }
}
