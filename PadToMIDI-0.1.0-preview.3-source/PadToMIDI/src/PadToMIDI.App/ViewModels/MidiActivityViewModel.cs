using PadToMIDI.App.Services;
using PadToMIDI.Core.Midi;

namespace PadToMIDI.App.ViewModels;

public sealed record MidiActivityViewModel(MidiActivityEntry Entry)
{
    public string Description => $"#{Entry.Sequence,-4} Ch {Entry.Message.Channel + 1,2}  " + (Entry.Message.Kind switch
    {
        MidiEventKind.NoteOn => $"Note On   {NoteDisplay.Name(Entry.Message.Data1),-4} ({Entry.Message.Data1,3})  Vel {Entry.Message.Data2,3}",
        MidiEventKind.NoteOff => $"Note Off  {NoteDisplay.Name(Entry.Message.Data1),-4} ({Entry.Message.Data1,3})  Vel {Entry.Message.Data2,3}",
        MidiEventKind.ControlChange => $"CC {Entry.Message.Data1,3}  Value {Entry.Message.Data2,3}",
        MidiEventKind.PolyphonicPressure => $"Poly pressure {NoteDisplay.Name(Entry.Message.Data1)}  {Entry.Message.Data2}",
        MidiEventKind.ChannelPressure => $"Channel pressure {Entry.Message.Data2}",
        MidiEventKind.PitchBend => $"Pitch bend {Entry.Message.Data2}",
        _ => Entry.Message.Kind.ToString()
    });
}
