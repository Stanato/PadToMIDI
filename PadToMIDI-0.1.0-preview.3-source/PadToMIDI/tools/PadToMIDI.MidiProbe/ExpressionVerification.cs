using System.Collections.Concurrent;
using PadToMIDI.Core.Input;
using PadToMIDI.Core.Midi;
using PadToMIDI.Core.Musical;
using PadToMIDI.Midi.Windows;

namespace PadToMIDI.MidiProbe;

internal static class ExpressionVerification
{
    public static void Run(MidiInstrument instrument, WindowsMidiOutput output, ConcurrentQueue<uint> received, GamepadDeviceId device)
    {
        int before = received.Count;
        void Set(PhysicalControl control, float value) => instrument.Process(new(GamepadEventKind.ControlChanged, device, control, value));
        Set(PhysicalControl.DPadRight, 1);
        Set(PhysicalControl.FaceSouth, 1);
        Set(PhysicalControl.LeftTrigger, 1);
        Set(PhysicalControl.RightTrigger, 1);
        Set(PhysicalControl.RightStickX, -1);
        // The second extreme is deferred; no new physical event is needed to send it.
        Set(PhysicalControl.RightStickX, 1);
        Thread.Sleep(20);
        instrument.Process(new(GamepadEventKind.Tick, device));
        Set(PhysicalControl.RightStickX, 0);
        Set(PhysicalControl.RightStickY, -1);
        Set(PhysicalControl.DPadUp, 1);
        Set(PhysicalControl.DPadUp, 0); // Pressure falls back to the older held F3.
        instrument.Panic();
        output.FlushAsync().AsTask().GetAwaiter().GetResult();
        MidiEvent[] expected =
        [
            new(MidiEventKind.NoteOn, 0, 53, 100), new(MidiEventKind.NoteOn, 0, 55, 100),
            new(MidiEventKind.PolyphonicPressure, 0, 53, 127), new(MidiEventKind.PolyphonicPressure, 0, 55, 127),
            new(MidiEventKind.PitchBend, 0, 0, 0), new(MidiEventKind.PitchBend, 0, 0, 16383), new(MidiEventKind.PitchBend, 0, 0, 8192),
            new(MidiEventKind.ControlChange, 0, 74, 127),
            new(MidiEventKind.NoteOn, 0, 50, 100), new(MidiEventKind.PolyphonicPressure, 0, 53, 0), new(MidiEventKind.PolyphonicPressure, 0, 50, 127),
            new(MidiEventKind.PolyphonicPressure, 0, 50, 0), new(MidiEventKind.NoteOff, 0, 50, 0), new(MidiEventKind.PolyphonicPressure, 0, 53, 127),
            new(MidiEventKind.PolyphonicPressure, 0, 53, 0), new(MidiEventKind.PolyphonicPressure, 0, 55, 0),
            new(MidiEventKind.ControlChange, 0, 74, 64),
            new(MidiEventKind.NoteOff, 0, 55, 0), new(MidiEventKind.NoteOff, 0, 53, 0),
            new(MidiEventKind.ControlChange, 0, 123, 0), new(MidiEventKind.ChannelPressure, 0, 0, 0), new(MidiEventKind.PitchBend, 0, 0, 8192)
        ];
        if (!SpinWait.SpinUntil(() => received.Count >= before + expected.Length, TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException("Expression packets were not received.");
        uint[] actual = received.Skip(before).ToArray();
        if (!actual.SequenceEqual(expected.Select(message => Midi1UmpEncoder.Encode(message, 0))))
            throw new InvalidOperationException($"Expression UMP sequence mismatch: {string.Join(',', actual.Select(word => word.ToString("X8")))}");
        var state = instrument.CaptureSnapshot();
        if (!state.HeldNotes.IsEmpty || state.Expression != new ExpressionStateSnapshot(0, 0, 8192, 64))
            throw new InvalidOperationException("Expression cleanup retained state.");
        Console.WriteLine("PASS: group polyphonic pressure/selection/fallback, full pitch bend/center, CC74, deferred updates, and Panic cleanup crossed Windows MIDI Services.");
    }
}
