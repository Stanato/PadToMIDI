using System.Collections.Concurrent;
using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Input;
using PadToMIDI.Core.Midi;
using PadToMIDI.Core.Musical;
using PadToMIDI.Midi.Windows;

namespace PadToMIDI.MidiProbe;

internal static class ChordVerification
{
    public static void Run(MidiInstrument instrument, WindowsMidiOutput output, ConcurrentQueue<uint> received, GamepadDeviceId device)
    {
        instrument.Panic();
        instrument.UpdateConfiguration(new() { Expression = new()
        {
            DPadAftertouch = new() { Response = new() { UpdateRateHz = 0 } },
            FaceAftertouch = new() { Source = PhysicalControl.RightTrigger, Response = new() { UpdateRateHz = 0 } }
        } }, resetPersistentOctave: true);
        output.FlushAsync().AsTask().GetAwaiter().GetResult();
        Thread.Sleep(100); int before = received.Count;
        void Set(PhysicalControl control, float value) => instrument.Process(new(GamepadEventKind.ControlChanged,device,control,value));
        Set(PhysicalControl.Start,1); Set(PhysicalControl.Start,1); Set(PhysicalControl.Start,0);
        Set(PhysicalControl.DPadDown,1); Set(PhysicalControl.FaceSouth,1);
        Set(PhysicalControl.LeftTrigger,1); Set(PhysicalControl.RightTrigger,1);
        Set(PhysicalControl.Start,1); Set(PhysicalControl.Start,0); // Keep sounding chords intact in Note mode.
        Set(PhysicalControl.DPadDown,0); Set(PhysicalControl.FaceSouth,0);
        Set(PhysicalControl.RightBumper,1); Set(PhysicalControl.Start,1); Set(PhysicalControl.Start,0);
        Set(PhysicalControl.FaceSouth,1); Set(PhysicalControl.RightBumper,0);
        instrument.Panic(); // Shifted chord releases and all expression cleanup.
        output.FlushAsync().AsTask().GetAwaiter().GetResult();
        MidiEvent[] expectedNotes =
        [
            new(MidiEventKind.NoteOn,0,48,100),new(MidiEventKind.NoteOn,0,52,100),new(MidiEventKind.NoteOn,0,55,100),
            new(MidiEventKind.NoteOn,0,59,100),new(MidiEventKind.NoteOn,0,62,100),
            new(MidiEventKind.NoteOff,0,48,0),new(MidiEventKind.NoteOff,0,52,0),
            new(MidiEventKind.NoteOff,0,55,0),new(MidiEventKind.NoteOff,0,59,0),new(MidiEventKind.NoteOff,0,62,0),
            new(MidiEventKind.NoteOn,0,56,100),new(MidiEventKind.NoteOn,0,60,100),new(MidiEventKind.NoteOn,0,63,100),
            new(MidiEventKind.NoteOff,0,56,0),new(MidiEventKind.NoteOff,0,60,0),new(MidiEventKind.NoteOff,0,63,0)
        ];
        bool IsNote(uint word) => (word >> 28) == 2 && ((word >> 20) & 15) is 8 or 9;
        if (!SpinWait.SpinUntil(() => received.Skip(before).Count(IsNote) >= expectedNotes.Length, TimeSpan.FromSeconds(5)) ||
            !received.Skip(before).Where(IsNote).SequenceEqual(expectedNotes.Select(message => Midi1UmpEncoder.Encode(message,0))))
            throw new InvalidOperationException("Chord wire notes differ: toggle, overlap, transposition or Panic.");
        foreach (byte pitch in new byte[] {48,52,55,59,62,56,60,63})
            if (!received.Skip(before).Contains(Midi1UmpEncoder.Encode(new(MidiEventKind.PolyphonicPressure,0,pitch,127),0)))
                throw new InvalidOperationException($"Chord aftertouch missing for {pitch}.");
        if (!instrument.CaptureSnapshot().HeldNotes.IsEmpty) throw new InvalidOperationException("Chord Panic retained voices.");
        instrument.UpdateConfiguration(new(), resetPersistentOctave:true);
        Console.WriteLine("PASS: Start toggle, full triads, shared-note ownership, chord pressure, chromatic transposition and Panic crossed Windows MIDI Services.");
    }
}
