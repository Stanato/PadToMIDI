using System.Collections.Concurrent;
using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Input;
using PadToMIDI.Core.Midi;
using PadToMIDI.Core.Musical;
using PadToMIDI.Midi.Windows;

namespace PadToMIDI.MidiProbe;

internal static class ConfigurationVerification
{
    public static void Run(MidiInstrument instrument, WindowsMidiOutput output, ConcurrentQueue<uint> received, GamepadDeviceId device)
    {
        int before = received.Count;
        void Set(PhysicalControl control, float value) => instrument.Process(new(GamepadEventKind.ControlChanged, device, control, value));
        Set(PhysicalControl.FaceSouth, 1); // G3 must retain its old channel/pitch through Apply.
        Set(PhysicalControl.RightStickY, -1);
        instrument.UpdateConfiguration(new()
        {
            Root = PitchClass.FSharp, Scale = ScalePresets.Dorian, BaseOctave = 4, FixedVelocity = 77, MidiChannel = 2,
            Mappings = InstrumentConfiguration.DefaultMappings.SetItem(PhysicalControl.FaceSouth, new FixedNoteAction(64)),
            Expression = new() { Timbre = new() { ControllerNumber = 11 } }
        });
        Set(PhysicalControl.FaceSouth, 0);
        Set(PhysicalControl.FaceSouth, 1);
        Set(PhysicalControl.FaceSouth, 0);
        Set(PhysicalControl.DPadDown, 1);
        Set(PhysicalControl.DPadDown, 0);
        Set(PhysicalControl.RightStickY, 0);
        output.FlushAsync().AsTask().GetAwaiter().GetResult();
        uint last = Midi1UmpEncoder.Encode(new(MidiEventKind.ControlChange, 2, 11, 64), 0);
        if (!SpinWait.SpinUntil(() => received.Skip(before).Contains(last), TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException("Configuration sequence did not drain to the receiver.");
        uint[] actual = received.Skip(before).ToArray();
        MidiEvent[] notes =
        [
            new(MidiEventKind.NoteOn, 0, 55, 100), new(MidiEventKind.NoteOff, 0, 55, 0),
            new(MidiEventKind.NoteOn, 2, 64, 77), new(MidiEventKind.NoteOff, 2, 64, 0),
            new(MidiEventKind.NoteOn, 2, 66, 77), new(MidiEventKind.NoteOff, 2, 66, 0)
        ];
        if (!actual.Where(word => ((word >> 20) & 15) is 8 or 9).SequenceEqual(notes.Select(message => Midi1UmpEncoder.Encode(message, 0))) ||
            !actual.Contains(Midi1UmpEncoder.Encode(new(MidiEventKind.ControlChange, 0, 74, 64), 0)) ||
            !actual.Contains(Midi1UmpEncoder.Encode(new(MidiEventKind.ControlChange, 2, 11, 127), 0)))
            throw new InvalidOperationException("Edited scale/fixed mapping/channel/velocity/CC or held release did not match wire output.");
        instrument.UpdateConfiguration(new());
        Console.WriteLine("PASS: edited F# Dorian, fixed mapping, velocity/channel/custom CC, and old held-note release crossed Windows MIDI Services.");
    }
}
