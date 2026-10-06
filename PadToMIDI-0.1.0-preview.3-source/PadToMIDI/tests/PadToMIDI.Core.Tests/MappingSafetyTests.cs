using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Input;
using PadToMIDI.Core.Midi;
using PadToMIDI.Core.Musical;
using Xunit;

namespace PadToMIDI.Core.Tests;

public sealed class MappingSafetyTests
{
    [Fact]
    public void MixedOctaveAccidentalAndConfigurationGesturesAlwaysMatchSoundingPitchOwnership()
    {
        var engine = new MappingEngine();
        var active = new HashSet<(byte Channel, byte Note)>();
        var device = new GamepadDeviceId(1);
        engine.MidiGenerated += message =>
        {
            var identity = (message.Channel, message.Data1);
            if (message.Kind == MidiEventKind.NoteOn) Assert.True(active.Add(identity), "Duplicate sounding pitch");
            else if (message.Kind == MidiEventKind.NoteOff) Assert.True(active.Remove(identity), "Release without sounding owner");
            else if (message.Kind == MidiEventKind.PolyphonicPressure) Assert.Contains(identity, active);
        };
        var random = new Random(202604);
        var buttons = InstrumentConfiguration.DefaultMappings.Keys.Concat(InstrumentConfiguration.DefaultSemitoneModifiers.Keys).ToArray();
        engine.Process(new(GamepadEventKind.Selected, device));
        for (int step = 0; step < 10_000; step++)
        {
            switch (random.Next(10))
            {
                case 0:
                case 1:
                    engine.Process(new(GamepadEventKind.ControlChanged, device, buttons[random.Next(buttons.Length)], random.Next(2)));
                    break;
                case 2:
                case 3:
                    engine.Process(new(GamepadEventKind.ControlChanged, device,
                        random.Next(2) == 0 ? PhysicalControl.LeftStickX : PhysicalControl.LeftStickY, random.Next(-1, 2)));
                    break;
                case 4:
                    engine.UpdateConfiguration(new()
                    {
                        Root = (PitchClass)random.Next(12), BaseOctave = random.Next(-1, 10),
                        MidiChannel = (byte)random.Next(16), FixedVelocity = (byte)random.Next(1, 128),
                        Mappings = InstrumentConfiguration.DefaultMappings.SetItem(buttons[0], new FixedNoteAction((byte)random.Next(128)))
                    });
                    break;
                case 5: engine.Process(new(GamepadEventKind.Selected, device)); break;
                case 6:
                    engine.Process(new(GamepadEventKind.ControlChanged, device, PhysicalControl.LeftStickX, 0));
                    engine.Process(new(GamepadEventKind.ControlChanged, device, PhysicalControl.LeftStickY, 0));
                    break;
                case 7:
                case 8:
                    engine.Process(new(GamepadEventKind.ControlChanged, device,
                        random.Next(2) == 0 ? PhysicalControl.LeftTrigger : PhysicalControl.RightTrigger, random.Next(101) / 100f));
                    break;
                case 9:
                    engine.Process(new(GamepadEventKind.ControlChanged, device,
                        random.Next(2) == 0 ? PhysicalControl.RightStickX : PhysicalControl.RightStickY, random.Next(-100, 101) / 100f));
                    engine.Process(new(GamepadEventKind.Tick, device));
                    break;
            }
            var state = engine.CaptureSnapshot();
            Assert.InRange(state.BaseOctave, -1, 9);
            Assert.InRange(state.Expression.DPadPressure, 0, 127);
            Assert.InRange(state.Expression.FacePressure, 0, 127);
            Assert.InRange(state.Expression.PitchBend, 0, 16383);
            Assert.InRange(state.Expression.Timbre, 0, 127);
            Assert.True(active.SetEquals(state.HeldNotes.Select(note => (note.Channel, note.MidiNote))), $"Ownership mismatch at {step}");
        }
        engine.Reset();
        Assert.Empty(active);
    }

    [Fact]
    public void MixedConfigurationAndInputSequencesAlwaysMatchTheEmittedPitchLedger()
    {
        var engine = new MappingEngine();
        var active = new HashSet<(byte Channel, byte Note)>();
        var device = new GamepadDeviceId(1);
        engine.MidiGenerated += message =>
        {
            var key = (message.Channel, message.Data1);
            if (message.Kind == MidiEventKind.NoteOn) Assert.True(active.Add(key), "Unexpected duplicate Note On");
            else Assert.True(active.Remove(key), "Note Off without a matching Note On");
        };
        var random = new Random(2026);
        var controls = InstrumentConfiguration.DefaultMappings.Keys.ToArray();
        engine.Process(new(GamepadEventKind.Selected, device));
        for (int i = 0; i < 2_000; i++)
        {
            var control = controls[random.Next(controls.Length)];
            switch (random.Next(6))
            {
                case 0:
                case 1:
                    engine.Process(new(GamepadEventKind.ControlChanged, device, control, 1));
                    break;
                case 2:
                case 3:
                    engine.Process(new(GamepadEventKind.ControlChanged, device, control, 0));
                    break;
                case 4:
                    engine.UpdateConfiguration(new()
                    {
                        Root = (PitchClass)random.Next(12), BaseOctave = random.Next(-1, 10),
                        MidiChannel = (byte)random.Next(16), FixedVelocity = (byte)random.Next(1, 128),
                        Mappings = InstrumentConfiguration.DefaultMappings.SetItem(control, new FixedNoteAction((byte)random.Next(128)))
                    });
                    break;
                case 5:
                    engine.Process(new(GamepadEventKind.Selected, device));
                    break;
            }
            var owned = engine.CaptureSnapshot().HeldNotes.Select(note => (note.Channel, note.MidiNote)).ToHashSet();
            Assert.True(active.SetEquals(owned), $"Emitted/held state diverged at step {i}");
        }
        engine.Reset();
        Assert.Empty(active);
    }

    [Fact]
    public void AFailedNoteOnSubscriberLeavesRecordAvailableForFatalCleanup()
    {
        var engine = new MappingEngine();
        var messages = new List<MidiEvent>();
        var device = new GamepadDeviceId(1);
        engine.MidiGenerated += message =>
        {
            messages.Add(message);
            if (message.Kind == MidiEventKind.NoteOn) throw new InvalidOperationException("Output failure");
        };
        engine.Process(new(GamepadEventKind.Selected, device));
        Assert.Throws<InvalidOperationException>(() =>
            engine.Process(new(GamepadEventKind.ControlChanged, device, PhysicalControl.FaceSouth, 1)));
        Assert.Single(engine.CaptureSnapshot().HeldNotes);
        engine.Process(new(GamepadEventKind.Faulted, device));
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOff, 0, 55, 0), messages[^1]);
        Assert.Empty(engine.CaptureSnapshot().HeldNotes);
    }

    [Fact]
    public void AFailedNoteOffSubscriberCanRetryCleanupWithOriginalIdentity()
    {
        var engine = new MappingEngine();
        var messages = new List<MidiEvent>();
        bool failNextOff = true;
        var device = new GamepadDeviceId(1);
        engine.MidiGenerated += message =>
        {
            messages.Add(message);
            if (message.Kind == MidiEventKind.NoteOff && failNextOff)
            {
                failNextOff = false;
                throw new InvalidOperationException("Output failure");
            }
        };
        engine.Process(new(GamepadEventKind.Selected, device));
        engine.Process(new(GamepadEventKind.ControlChanged, device, PhysicalControl.FaceSouth, 1));
        Assert.Throws<InvalidOperationException>(() =>
            engine.Process(new(GamepadEventKind.ControlChanged, device, PhysicalControl.FaceSouth, 0)));
        Assert.Single(engine.CaptureSnapshot().HeldNotes);
        engine.UpdateConfiguration(new() { BaseOctave = 5, MidiChannel = 8 });
        engine.Process(new(GamepadEventKind.Faulted, device));
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOff, 0, 55, 0), messages[^1]);
        Assert.Empty(engine.CaptureSnapshot().HeldNotes);
    }
}
