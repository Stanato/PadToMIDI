using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Input;
using PadToMIDI.Core.Midi;
using PadToMIDI.Core.Musical;
using Xunit;

namespace PadToMIDI.Core.Tests;

public sealed class MappingEngineTests
{
    private static readonly GamepadDeviceId Device = new(1);
    private static readonly GamepadDeviceId OtherDevice = new(2);

    [Theory]
    [InlineData(PhysicalControl.DPadDown, 48)]
    [InlineData(PhysicalControl.DPadUp, 50)]
    [InlineData(PhysicalControl.DPadLeft, 52)]
    [InlineData(PhysicalControl.DPadRight, 53)]
    [InlineData(PhysicalControl.FaceSouth, 55)]
    [InlineData(PhysicalControl.FaceNorth, 57)]
    [InlineData(PhysicalControl.FaceWest, 59)]
    [InlineData(PhysicalControl.FaceEast, 60)]
    public void DefaultCmajorButtonsProduceMatchingOnAndOff(PhysicalControl control, byte expected)
    {
        var (engine, messages) = Create();
        Press(engine, control);
        var held = Assert.Single(engine.CaptureSnapshot().HeldNotes);
        Assert.Equal(control, held.Control);
        Assert.Equal(expected, held.MidiNote);
        Assert.Equal(100, held.Velocity);
        Release(engine, control);
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOn, 0, expected, 100), messages[0]);
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOff, 0, expected, 0), messages[1]);
        Assert.Empty(engine.CaptureSnapshot().HeldNotes);
    }

    [Fact]
    public void DuplicatePressAndReleaseEventsAreIgnored()
    {
        var (engine, messages) = Create();
        Release(engine, PhysicalControl.FaceSouth);
        Press(engine, PhysicalControl.FaceSouth);
        Press(engine, PhysicalControl.FaceSouth);
        Release(engine, PhysicalControl.FaceSouth);
        Release(engine, PhysicalControl.FaceSouth);
        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public void UnmappedButtonsAndAnalogControlsDoNotGenerateNotes()
    {
        var (engine, messages) = Create();
        Press(engine, PhysicalControl.LeftBumper);
        engine.Process(new(GamepadEventKind.ControlChanged, Device, PhysicalControl.LeftTrigger, 1));
        engine.Process(new(GamepadEventKind.ControlChanged, Device, PhysicalControl.LeftStickY, -1));
        Assert.Empty(messages);
        Assert.Empty(engine.CaptureSnapshot().HeldNotes);
    }

    [Fact]
    public void AdjacentDpadAndFaceNotesCanBeHeldAndReleasedIndependently()
    {
        var (engine, messages) = Create();
        Press(engine, PhysicalControl.DPadUp);
        Press(engine, PhysicalControl.DPadLeft);
        Press(engine, PhysicalControl.FaceSouth);
        Assert.Equal(3, engine.CaptureSnapshot().HeldNotes.Length);
        Release(engine, PhysicalControl.DPadUp);
        Assert.Equal(2, engine.CaptureSnapshot().HeldNotes.Length);
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOff, 0, 50, 0), messages[^1]);
        Release(engine, PhysicalControl.FaceSouth);
        Release(engine, PhysicalControl.DPadLeft);
        Assert.Empty(engine.CaptureSnapshot().HeldNotes);
    }

    [Fact]
    public void LastTriggeredAndLastActiveGroupsAreSeparateWithFallback()
    {
        var (engine, _) = Create();
        Press(engine, PhysicalControl.DPadDown);
        Press(engine, PhysicalControl.FaceSouth);
        Press(engine, PhysicalControl.DPadRight);
        var state = engine.CaptureSnapshot();
        Assert.Equal(PhysicalControl.DPadRight, state.LastNote?.Control);
        Assert.Equal(PhysicalControl.DPadRight, state.LastDPadNote?.Control);
        Assert.Equal(PhysicalControl.FaceSouth, state.LastFaceNote?.Control);
        Release(engine, PhysicalControl.DPadRight);
        state = engine.CaptureSnapshot();
        Assert.Equal(PhysicalControl.DPadRight, state.LastDPadNote?.Control);
        Assert.Equal(PhysicalControl.DPadDown, state.LastActiveDPadNote?.Control);
        Release(engine, PhysicalControl.DPadDown);
        Release(engine, PhysicalControl.FaceSouth);
        state = engine.CaptureSnapshot();
        Assert.Null(state.LastActiveDPadNote);
        Assert.Null(state.LastActiveFaceNote);
        Assert.NotNull(state.LastNote);
    }

    [Fact]
    public void ConfigurationChangesOnlyAffectFuturePresses()
    {
        var (engine, messages) = Create();
        Press(engine, PhysicalControl.FaceSouth); // G3, channel 1, velocity 100.
        var minor = new ScaleDefinition("Custom minor", [0, 2, 3, 5, 7, 8, 10]);
        engine.UpdateConfiguration(new()
        {
            Root = PitchClass.D, Scale = minor, BaseOctave = 4, MidiChannel = 5, FixedVelocity = 77,
            Mappings = InstrumentConfiguration.DefaultMappings.SetItem(PhysicalControl.FaceSouth, new ScaleDegreeAction(3))
        });
        Press(engine, PhysicalControl.FaceSouth); // Duplicate down is still ignored.
        Release(engine, PhysicalControl.FaceSouth);
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOff, 0, 55, 0), messages[^1]);
        Press(engine, PhysicalControl.FaceSouth);
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOn, 5, 65, 77), messages[^1]);
        Release(engine, PhysicalControl.FaceSouth);
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOff, 5, 65, 0), messages[^1]);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("scale")]
    [InlineData("octave")]
    [InlineData("mapping")]
    public void EachConfigurationChangePreservesOriginalRelease(string setting)
    {
        var (engine, messages) = Create();
        Press(engine, PhysicalControl.FaceWest); // B3.
        var configuration = new InstrumentConfiguration();
        configuration = setting switch
        {
            "root" => configuration with { Root = PitchClass.FSharp },
            "scale" => configuration with { Scale = new("Custom minor", [0, 2, 3, 5, 7, 8, 10]) },
            "octave" => configuration with { BaseOctave = 5 },
            _ => configuration with { Mappings = configuration.Mappings.Remove(PhysicalControl.FaceWest) }
        };
        engine.UpdateConfiguration(configuration);
        Release(engine, PhysicalControl.FaceWest);
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOff, 0, 59, 0), messages[^1]);
    }

    [Fact]
    public void FixedNoteMappingIsIndependentOfScaleRootAndOctave()
    {
        var configuration = new InstrumentConfiguration
        {
            Root = PitchClass.B, BaseOctave = 9,
            Mappings = InstrumentConfiguration.DefaultMappings.SetItem(PhysicalControl.FaceSouth, new FixedNoteAction(36))
        };
        var (engine, messages) = Create(configuration);
        Press(engine, PhysicalControl.FaceSouth);
        Release(engine, PhysicalControl.FaceSouth);
        Assert.Equal(36, messages[0].Data1);
        Assert.Equal(36, messages[1].Data1);
    }

    [Fact]
    public void UnplayableDegreesDoNotClampToAnUnintendedNote()
    {
        var (engine, messages) = Create(new() { BaseOctave = 9 });
        Press(engine, PhysicalControl.FaceEast); // C10 is out of MIDI range.
        Assert.Empty(messages);
        Assert.Empty(engine.CaptureSnapshot().HeldNotes);
        engine.UpdateConfiguration(new() { BaseOctave = 3 });
        Press(engine, PhysicalControl.FaceEast); // Still the same held edge.
        Assert.Empty(messages);
        Release(engine, PhysicalControl.FaceEast);
        Press(engine, PhysicalControl.FaceEast);
        Assert.Equal(60, Assert.Single(messages).Data1);
    }

    [Theory]
    [InlineData(GamepadEventKind.Disconnected)]
    [InlineData(GamepadEventKind.Stopped)]
    [InlineData(GamepadEventKind.Faulted)]
    public void LifecycleCleanupStopsEveryHeldNoteAndClearsMusicalState(GamepadEventKind kind)
    {
        var (engine, messages) = Create();
        foreach (var control in InstrumentConfiguration.DefaultMappings.Keys) Press(engine, control);
        engine.Process(new(kind, Device));
        Assert.Equal(8, messages.Count(message => message.Kind == MidiEventKind.NoteOff));
        Assert.Equal(messages.Where(message => message.Kind == MidiEventKind.NoteOn).Select(message => message.Data1).Order(),
            messages.Where(message => message.Kind == MidiEventKind.NoteOff).Select(message => message.Data1).Order());
        var state = engine.CaptureSnapshot();
        Assert.Empty(state.HeldNotes);
        Assert.Equal(default, state.Controls);
        Assert.Null(state.Device);
        Assert.Null(state.LastNote);
        Assert.Null(state.LastDPadNote);
        Assert.Null(state.LastFaceNote);
    }

    [Fact]
    public void SwitchingControllersReleasesOldNotesBeforeNewInput()
    {
        var (engine, messages) = Create();
        Press(engine, PhysicalControl.FaceSouth);
        engine.Process(new(GamepadEventKind.Selected, OtherDevice));
        engine.Process(new(GamepadEventKind.ControlChanged, Device, PhysicalControl.FaceSouth, 1));
        engine.Process(new(GamepadEventKind.Disconnected, Device));
        engine.Process(new(GamepadEventKind.ControlChanged, OtherDevice, PhysicalControl.FaceSouth, 1));
        Assert.Equal(new[] { MidiEventKind.NoteOn, MidiEventKind.NoteOff, MidiEventKind.NoteOn }, messages.Select(message => message.Kind));
        Assert.Equal(OtherDevice, engine.CaptureSnapshot().Device);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedPitchRemainsActiveUntilLastOwnerReleases(bool reverseReleaseOrder)
    {
        var configuration = new InstrumentConfiguration
        {
            Mappings = InstrumentConfiguration.DefaultMappings
                .SetItem(PhysicalControl.FaceSouth, new FixedNoteAction(60))
                .SetItem(PhysicalControl.FaceEast, new FixedNoteAction(60))
        };
        var (engine, messages) = Create(configuration);
        Press(engine, PhysicalControl.FaceSouth);
        Press(engine, PhysicalControl.FaceEast);
        Assert.Single(messages);
        Assert.Equal(2, engine.CaptureSnapshot().HeldNotes.Length);
        Release(engine, reverseReleaseOrder ? PhysicalControl.FaceEast : PhysicalControl.FaceSouth);
        Assert.Single(messages);
        Release(engine, reverseReleaseOrder ? PhysicalControl.FaceSouth : PhysicalControl.FaceEast);
        Assert.Equal(2, messages.Count);
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOff, 0, 60, 0), messages[^1]);
    }

    [Fact]
    public void SamePitchOnDifferentChannelsHasIndependentOwnership()
    {
        var (engine, messages) = Create();
        Press(engine, PhysicalControl.DPadDown);
        engine.UpdateConfiguration(new()
        {
            MidiChannel = 1,
            Mappings = InstrumentConfiguration.DefaultMappings.SetItem(PhysicalControl.FaceSouth, new FixedNoteAction(48))
        });
        Press(engine, PhysicalControl.FaceSouth);
        engine.Reset();
        Assert.Equal(2, messages.Count(message => message.Kind == MidiEventKind.NoteOn));
        Assert.Equal(2, messages.Count(message => message.Kind == MidiEventKind.NoteOff));
        Assert.Contains(new MidiEvent(MidiEventKind.NoteOff, 0, 48, 0), messages);
        Assert.Contains(new MidiEvent(MidiEventKind.NoteOff, 1, 48, 0), messages);
    }

    [Fact]
    public void ResetIsIdempotentAndEngineCanPlayAfterReconnect()
    {
        var (engine, messages) = Create();
        Press(engine, PhysicalControl.FaceSouth);
        engine.Reset();
        engine.Reset();
        Press(engine, PhysicalControl.FaceSouth);
        Assert.Equal(2, messages.Count);
        engine.Process(new(GamepadEventKind.Selected, Device));
        Press(engine, PhysicalControl.FaceSouth);
        Assert.Equal(3, messages.Count);
    }

    [Fact]
    public void CapturedMusicalStateIsImmutable()
    {
        var (engine, _) = Create();
        Press(engine, PhysicalControl.FaceSouth);
        var snapshot = engine.CaptureSnapshot();
        engine.UpdateConfiguration(new() { Root = PitchClass.D });
        Release(engine, PhysicalControl.FaceSouth);
        Assert.Single(snapshot.HeldNotes);
        Assert.Equal(PitchClass.C, snapshot.Configuration.Root);
        Assert.True(snapshot.Controls.IsPressed(PhysicalControl.FaceSouth));
    }

    [Fact]
    public void InvalidConfigurationIsRejectedWithoutMutatingHeldNotes()
    {
        var (engine, messages) = Create();
        Press(engine, PhysicalControl.FaceSouth);
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.UpdateConfiguration(new() { FixedVelocity = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.UpdateConfiguration(new() { MidiChannel = 16 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.UpdateConfiguration(new() { BaseOctave = 10 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.UpdateConfiguration(new() { Root = (PitchClass)12 }));
        Assert.Throws<ArgumentException>(() => engine.UpdateConfiguration(new()
        {
            Mappings = InstrumentConfiguration.DefaultMappings.SetItem(PhysicalControl.LeftTrigger, new ScaleDegreeAction(1))
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedNoteAction(128));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScaleDegreeAction(0));
        Release(engine, PhysicalControl.FaceSouth);
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOff, 0, 55, 0), messages[^1]);
    }

    private static (MappingEngine Engine, List<MidiEvent> Messages) Create(InstrumentConfiguration? configuration = null)
    {
        var engine = new MappingEngine(configuration);
        var messages = new List<MidiEvent>();
        engine.MidiGenerated += messages.Add;
        engine.Process(new(GamepadEventKind.Selected, Device));
        return (engine, messages);
    }

    private static void Press(MappingEngine engine, PhysicalControl control) =>
        engine.Process(new(GamepadEventKind.ControlChanged, Device, control, 1));
    private static void Release(MappingEngine engine, PhysicalControl control) =>
        engine.Process(new(GamepadEventKind.ControlChanged, Device, control, 0));
}
