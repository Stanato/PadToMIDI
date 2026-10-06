namespace PadToMIDI.Core.Midi;

public enum MidiEventKind { NoteOn, NoteOff, PolyphonicPressure, ChannelPressure, ControlChange, PitchBend }

/// <summary>
/// Platform-neutral message contract. Channel is 0..15; Data1 is note/CC;
/// Data2 is 0..127, or 0..16383 for pitch bend.
/// ResetValue optionally tells the backend how to restore a continuous CC after a fatal queue failure.
/// </summary>
public readonly record struct MidiEvent(MidiEventKind Kind, byte Channel, byte Data1, ushort Data2,
    ushort? ResetValue = null);
