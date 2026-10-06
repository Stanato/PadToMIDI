using PadToMIDI.Core.Midi;

namespace PadToMIDI.Midi.Windows;

/// <summary>MIDI 1 channel voice messages in 32-bit Universal MIDI Packets.</summary>
public static class Midi1UmpEncoder
{
    public static uint Encode(in MidiEvent message, byte group)
    {
        if (group > 15 || message.Channel > 15 || message.Data1 > 127 ||
            message.Data2 > (message.Kind == MidiEventKind.PitchBend ? 16383 : 127))
            throw new ArgumentOutOfRangeException(nameof(message));
        if (message.ResetValue is { } reset && (message.Kind != MidiEventKind.ControlChange || message.Data1 > 119 || reset > 127))
            throw new ArgumentOutOfRangeException(nameof(message));
        uint status = message.Kind switch
        {
            MidiEventKind.NoteOff => 0x8u,
            MidiEventKind.NoteOn => 0x9u,
            MidiEventKind.PolyphonicPressure => 0xAu,
            MidiEventKind.ControlChange => 0xBu,
            MidiEventKind.ChannelPressure => 0xDu,
            MidiEventKind.PitchBend => 0xEu,
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        };
        uint first = message.Data1, second = message.Data2;
        if (message.Kind == MidiEventKind.ChannelPressure) { first = second; second = 0; }
        if (message.Kind == MidiEventKind.PitchBend) { first = second & 127; second >>= 7; }
        return 0x20000000u | ((uint)group << 24) | (status << 20) |
            ((uint)message.Channel << 16) | (first << 8) | second;
    }
}
