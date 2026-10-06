using System.Collections.Concurrent;
using PadToMIDI.Core.Input;
using PadToMIDI.Core.Midi;
using PadToMIDI.Core.Musical;
using PadToMIDI.Midi.Windows;

namespace PadToMIDI.MidiProbe;

internal static class GestureVerification
{
    public static void Run(MidiInstrument instrument, WindowsMidiOutput output, ConcurrentQueue<uint> received, GamepadDeviceId device)
    {
        int before = received.Count(IsNote);
        void Set(PhysicalControl control, float value) => instrument.Process(new(GamepadEventKind.ControlChanged, device, control, value));
        Set(PhysicalControl.LeftBumper, 1);
        Set(PhysicalControl.FaceSouth, 1);
        Set(PhysicalControl.LeftBumper, 0);
        Set(PhysicalControl.FaceSouth, 0);
        Set(PhysicalControl.RightBumper, 1);
        Set(PhysicalControl.FaceSouth, 1);
        Set(PhysicalControl.RightBumper, 0);
        Set(PhysicalControl.FaceSouth, 0);
        Set(PhysicalControl.LeftStickY, 1);
        Set(PhysicalControl.FaceSouth, 1);
        Set(PhysicalControl.LeftStickY, 0);
        Set(PhysicalControl.FaceSouth, 0);
        Set(PhysicalControl.LeftStickY, -1);
        Set(PhysicalControl.LeftStickY, -0.9f);
        Set(PhysicalControl.FaceSouth, 1);
        Set(PhysicalControl.LeftStickY, 0);
        Set(PhysicalControl.FaceSouth, 0);
        Set(PhysicalControl.LeftStickY, -1);
        Set(PhysicalControl.RightBumper, 1);
        Set(PhysicalControl.FaceSouth, 1);
        Set(PhysicalControl.RightBumper, 0);
        Set(PhysicalControl.LeftStickY, 0);
        Set(PhysicalControl.FaceSouth, 0);
        Set(PhysicalControl.FaceSouth, 1);
        Set(PhysicalControl.LeftBumper, 1); // Changing a modifier must not retune the already-held G3.
        Set(PhysicalControl.FaceSouth, 0);
        Set(PhysicalControl.LeftBumper, 0);
        Set(PhysicalControl.LeftStickY, 0);
        output.FlushAsync().AsTask().GetAwaiter().GetResult();
        MidiEvent[] expected =
        [
            new(MidiEventKind.NoteOn, 0, 54, 100), new(MidiEventKind.NoteOff, 0, 54, 0),
            new(MidiEventKind.NoteOn, 0, 56, 100), new(MidiEventKind.NoteOff, 0, 56, 0),
            new(MidiEventKind.NoteOn, 0, 43, 100), new(MidiEventKind.NoteOff, 0, 43, 0),
            new(MidiEventKind.NoteOn, 0, 67, 100), new(MidiEventKind.NoteOff, 0, 67, 0),
            new(MidiEventKind.NoteOn, 0, 68, 100), new(MidiEventKind.NoteOff, 0, 68, 0),
            new(MidiEventKind.NoteOn, 0, 55, 100), new(MidiEventKind.NoteOff, 0, 55, 0)
        ];
        if (!SpinWait.SpinUntil(() => received.Count(IsNote) >= before + expected.Length, TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException("Timed out receiving octave/accidental notes.");
        uint[] actual = received.Where(IsNote).Skip(before).ToArray();
        if (!actual.SequenceEqual(expected.Select(message => Midi1UmpEncoder.Encode(message, 0))))
            throw new InvalidOperationException("Octave/accidental wire sequence did not match expected Note On/Off identities.");
        var state = instrument.CaptureSnapshot();
        if (state.BaseOctave != 3 || !state.HeldNotes.IsEmpty || state.LeftStickLatch is not null ||
            state.TemporaryOctaveOffset != 0 || state.TemporarySemitoneOffset != 0)
            throw new InvalidOperationException("Gesture sequence retained unexpected runtime state.");
        Console.WriteLine("PASS: momentary stick octaves, held bumper accidentals, combined modifiers, and exact releases crossed Windows MIDI Services.");
    }
    private static bool IsNote(uint word) => (word >> 28) == 2 && ((word >> 20) & 15) is 8 or 9;
}
