using System.Collections.Concurrent;
using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Input;
using PadToMIDI.Core.Midi;
using PadToMIDI.Core.Musical;
using PadToMIDI.Core.Profiles;
using PadToMIDI.Midi.Windows;

namespace PadToMIDI.MidiProbe;

internal static class ProfileVerification
{
    public static void Run(MidiInstrument instrument, WindowsMidiOutput output, ConcurrentQueue<uint> received, GamepadDeviceId device)
    {
        int before = received.Count;
        void Set(PhysicalControl control, float value) => instrument.Process(new(GamepadEventKind.ControlChanged, device, control, value));
        Set(PhysicalControl.FaceSouth, 1);
        Set(PhysicalControl.LeftStickX, 1); Set(PhysicalControl.LeftStickX, 0);
        var profile = InstrumentProfile.Default with
        {
            Id = Guid.NewGuid(), Name = "Profile wire check",
            Configuration = new()
            {
                Root = PitchClass.FSharp, Scale = ScalePresets.NaturalMinor, BaseOctave = 5, FixedVelocity = 91, MidiChannel = 4,
                Mappings = InstrumentConfiguration.DefaultMappings.SetItem(PhysicalControl.FaceSouth, new FixedNoteAction(72)),
                Expression = new() { Timbre = new() { ControllerNumber = 11 } }
            }
        };
        var restored = ProfileJson.Deserialize(ProfileJson.Serialize(profile));
        instrument.UpdateConfiguration(restored.Configuration, resetPersistentOctave: true);
        Set(PhysicalControl.FaceSouth, 0); Set(PhysicalControl.FaceSouth, 1);
        Set(PhysicalControl.DPadDown, 1);
        instrument.UpdateConfiguration(ProfileJson.Deserialize(ProfileJson.Serialize(InstrumentProfile.Default)).Configuration, resetPersistentOctave: true);
        Set(PhysicalControl.FaceSouth, 0); Set(PhysicalControl.DPadDown, 0);
        Set(PhysicalControl.FaceSouth, 1); Set(PhysicalControl.FaceSouth, 0);
        output.FlushAsync().AsTask().GetAwaiter().GetResult();
        MidiEvent[] expected =
        [
            new(MidiEventKind.NoteOn, 0, 55, 100), new(MidiEventKind.NoteOff, 0, 55, 0),
            new(MidiEventKind.NoteOn, 4, 72, 91), new(MidiEventKind.NoteOn, 4, 78, 91),
            new(MidiEventKind.NoteOff, 4, 72, 0), new(MidiEventKind.NoteOff, 4, 78, 0),
            new(MidiEventKind.NoteOn, 0, 55, 100), new(MidiEventKind.NoteOff, 0, 55, 0)
        ];
        bool IsNote(uint word) => (word >> 28) == 2 && ((word >> 20) & 15) is 8 or 9;
        if (!SpinWait.SpinUntil(() => received.Skip(before).Count(IsNote) >= expected.Length, TimeSpan.FromSeconds(5)) ||
            !received.Skip(before).Where(IsNote).SequenceEqual(expected.Select(message => Midi1UmpEncoder.Encode(message, 0))))
            throw new InvalidOperationException("JSON profile load/reset did not preserve exact held-note releases on the MIDI wire.");
        var state = instrument.CaptureSnapshot();
        if (!state.HeldNotes.IsEmpty || state.BaseOctave != 3 || state.PersistentOctaveOffset != 0)
            throw new InvalidOperationException("Profile reset retained stale musical state.");
        Console.WriteLine("PASS: JSON profile roundtrip/load/default reset, persistent octave reset, and exact old/new channel releases crossed Windows MIDI Services.");
    }
}
