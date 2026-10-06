using PadToMIDI.Core.Midi;
using Xunit;

namespace PadToMIDI.Midi.Windows.Tests;

public sealed class Midi1UmpEncoderTests
{
    [Fact]
    public void ControllerResetMetadataDoesNotChangeTheWireMessage() =>
        Assert.Equal(0x20B04A7Fu, Midi1UmpEncoder.Encode(new(MidiEventKind.ControlChange, 0, 74, 127, 64), 0));

    [Theory]
    [InlineData(MidiEventKind.NoteOn, 60, 64)]
    [InlineData(MidiEventKind.ControlChange, 123, 64)]
    [InlineData(MidiEventKind.ControlChange, 74, 128)]
    public void InvalidCleanupMetadataIsRejected(MidiEventKind kind, byte controller, ushort reset) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Midi1UmpEncoder.Encode(new(kind, 0, controller, 100, reset), 0));

    [Theory]
    [InlineData(MidiEventKind.NoteOn, 0, 60, 100, 0, 0x20903C64u)]
    [InlineData(MidiEventKind.NoteOff, 15, 127, 0, 15, 0x2F8F7F00u)]
    [InlineData(MidiEventKind.PolyphonicPressure, 2, 55, 99, 3, 0x23A23763u)]
    [InlineData(MidiEventKind.ChannelPressure, 2, 0, 99, 3, 0x23D26300u)]
    [InlineData(MidiEventKind.ControlChange, 4, 74, 127, 5, 0x25B44A7Fu)]
    [InlineData(MidiEventKind.PitchBend, 0, 0, 0, 0, 0x20E00000u)]
    [InlineData(MidiEventKind.PitchBend, 0, 0, 8192, 0, 0x20E00040u)]
    [InlineData(MidiEventKind.PitchBend, 0, 0, 16383, 0, 0x20E07F7Fu)]
    public void EncodesMidi1ChannelVoiceWithCorrectGroupAndByteOrder(MidiEventKind kind, byte channel,
        byte first, ushort value, byte group, uint expected) =>
        Assert.Equal(expected, Midi1UmpEncoder.Encode(new(kind, channel, first, value), group));

    [Theory]
    [InlineData(MidiEventKind.NoteOn, 16, 60, 100, 0)]
    [InlineData(MidiEventKind.NoteOn, 0, 128, 100, 0)]
    [InlineData(MidiEventKind.NoteOn, 0, 60, 128, 0)]
    [InlineData(MidiEventKind.NoteOn, 0, 60, 100, 16)]
    [InlineData(MidiEventKind.PitchBend, 0, 0, 16384, 0)]
    [InlineData((MidiEventKind)99, 0, 0, 0, 0)]
    public void RejectsInvalidData(MidiEventKind kind, byte channel, byte first, ushort value, byte group) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Midi1UmpEncoder.Encode(new(kind, channel, first, value), group));
}
