using PadToMIDI.Core.Input;

namespace PadToMIDI.Core.Musical;

public enum NoteGroup { DPad, FaceButtons, Other }

/// <summary>The exact identity of a triggered note, retained until its physical control is released.</summary>
public readonly record struct NoteRecord(
    PhysicalControl Control, byte MidiNote, byte Channel, byte Velocity, NoteGroup Group, long Sequence,
    byte? Third = null, byte? Fifth = null, int RootLetter = -1)
{
    public int VoiceCount => Third is null ? 1 : 3;
    public byte PitchAt(int voice) => voice switch
    {
        0 => MidiNote, 1 when Third is { } third => third, 2 when Fifth is { } fifth => fifth,
        _ => throw new ArgumentOutOfRangeException(nameof(voice))
    };
    public NoteRecord VoiceAt(int voice) => this with
    {
        MidiNote = PitchAt(voice), Third = null, Fifth = null,
        RootLetter = RootLetter < 0 ? -1 : (RootLetter + voice * 2) % 7
    };
    public bool Contains(byte channel, byte pitch)
    {
        if (Channel != channel) return false;
        for (int i = 0; i < VoiceCount; i++) if (PitchAt(i) == pitch) return true;
        return false;
    }
}
