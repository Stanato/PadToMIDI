using System.Collections.Immutable;
using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Input;
using PadToMIDI.Core.Midi;
using PadToMIDI.Core.Musical;
using Xunit;

namespace PadToMIDI.Core.Tests;

public sealed class MomentaryPitchTests
{
    private static readonly GamepadDeviceId Device = new(1);

    [Theory]
    [InlineData(PhysicalControl.LeftBumper, 54)]
    [InlineData(PhysicalControl.RightBumper, 56)]
    public void BumperAccidentalAppliesOnlyWhileHeldAndReleaseRemembersPitch(PhysicalControl modifier, byte pitch)
    {
        var (engine, messages) = Create();
        Set(engine, PhysicalControl.FaceSouth, 1);
        Set(engine, PhysicalControl.FaceSouth, 0);
        Set(engine, modifier, 1);
        Set(engine, PhysicalControl.FaceSouth, 1);
        Set(engine, modifier, 0);
        Set(engine, PhysicalControl.FaceSouth, 0);
        Set(engine, PhysicalControl.FaceSouth, 1);
        Set(engine, PhysicalControl.FaceSouth, 0);
        Assert.Equal(new[]
        {
            new MidiEvent(MidiEventKind.NoteOn, 0, 55, 100), new(MidiEventKind.NoteOff, 0, 55, 0),
            new(MidiEventKind.NoteOn, 0, pitch, 100), new(MidiEventKind.NoteOff, 0, pitch, 0),
            new(MidiEventKind.NoteOn, 0, 55, 100), new(MidiEventKind.NoteOff, 0, 55, 0)
        }, messages);
        Assert.Equal(3, engine.CaptureSnapshot().BaseOctave);
        Assert.Equal(0, engine.CaptureSnapshot().TemporarySemitoneOffset);
    }

    [Theory]
    [InlineData(PhysicalControl.LeftStickY, -1f, 67, 1)]
    [InlineData(PhysicalControl.LeftStickY, 1f, 43, -1)]
    public void StickDirectionsAreMomentaryOctavesWithoutChangingBase(PhysicalControl axis, float value, byte pitch, int shift)
    {
        var (engine, messages) = Create();
        Set(engine, axis, value);
        Set(engine, axis, value * 0.9f);
        Assert.Equal(shift, engine.CaptureSnapshot().TemporaryOctaveOffset);
        Assert.Equal(3, engine.CaptureSnapshot().BaseOctave);
        Set(engine, PhysicalControl.FaceSouth, 1);
        Set(engine, axis, 0);
        Assert.Equal(0, engine.CaptureSnapshot().TemporaryOctaveOffset);
        Set(engine, PhysicalControl.FaceSouth, 0);
        Set(engine, PhysicalControl.FaceSouth, 1);
        Assert.Equal(new[] { new MidiEvent(MidiEventKind.NoteOn, 0, pitch, 100),
            new(MidiEventKind.NoteOff, 0, pitch, 0), new(MidiEventKind.NoteOn, 0, 55, 100) }, messages);
    }

    [Theory]
    [InlineData(-1f, 2, 43)]
    [InlineData(1f, 4, 67)]
    public void HorizontalOctavePersistsAfterCenterAndChangesOncePerExcursion(float direction, int octave, byte pitch)
    {
        var (engine, messages) = Create();
        Set(engine, PhysicalControl.LeftStickX, direction);
        Set(engine, PhysicalControl.LeftStickX, direction * 0.9f);
        Assert.Equal(octave, engine.CaptureSnapshot().BaseOctave);
        Assert.Equal(0, engine.CaptureSnapshot().TemporaryOctaveOffset);
        Set(engine, PhysicalControl.LeftStickX, 0);
        Assert.Equal(octave, engine.CaptureSnapshot().BaseOctave);
        Set(engine, PhysicalControl.FaceSouth, 1);
        Set(engine, PhysicalControl.LeftStickX, -direction);
        Set(engine, PhysicalControl.LeftStickX, 0);
        Assert.Equal(3, engine.CaptureSnapshot().BaseOctave);
        Assert.Equal(pitch, Assert.Single(engine.CaptureSnapshot().HeldNotes).MidiNote);
        Set(engine, PhysicalControl.FaceSouth, 0);
        Set(engine, PhysicalControl.FaceSouth, 1);
        Assert.Equal(new[] { new MidiEvent(MidiEventKind.NoteOn, 0, pitch, 100),
            new(MidiEventKind.NoteOff, 0, pitch, 0), new(MidiEventKind.NoteOn, 0, 55, 100) }, messages);
    }

    [Theory]
    [InlineData(PhysicalControl.LeftStickX, -1f, PhysicalControl.LeftBumper, 42)]
    [InlineData(PhysicalControl.LeftStickX, -1f, PhysicalControl.RightBumper, 44)]
    [InlineData(PhysicalControl.LeftStickY, -1f, PhysicalControl.LeftBumper, 66)]
    [InlineData(PhysicalControl.LeftStickY, -1f, PhysicalControl.RightBumper, 68)]
    public void OctaveAndAccidentalCombineBeforeResolvingAndRememberOriginalChannelVelocity(
        PhysicalControl axis, float value, PhysicalControl bumper, byte pitch)
    {
        var (engine, messages) = Create(new() { MidiChannel = 4, FixedVelocity = 77 });
        Set(engine, axis, value);
        Set(engine, bumper, 1);
        Set(engine, PhysicalControl.FaceSouth, 1);
        engine.UpdateConfiguration(new() { Root = PitchClass.D, BaseOctave = 6, MidiChannel = 7, FixedVelocity = 20 });
        Set(engine, axis, 0);
        Set(engine, bumper, 0);
        Set(engine, PhysicalControl.FaceSouth, 0);
        Assert.Equal(new[] { new MidiEvent(MidiEventKind.NoteOn, 4, pitch, 77), new(MidiEventKind.NoteOff, 4, pitch, 0) }, messages);
    }

    [Fact]
    public void ModifierChangesDoNotRetuneHeldNotesAndBothBumpersCancel()
    {
        var (engine, messages) = Create();
        Set(engine, PhysicalControl.FaceSouth, 1);
        Set(engine, PhysicalControl.RightBumper, 1);
        Assert.Single(messages);
        Assert.Equal(55, Assert.Single(engine.CaptureSnapshot().HeldNotes).MidiNote);
        Set(engine, PhysicalControl.LeftBumper, 1);
        Set(engine, PhysicalControl.FaceEast, 1);
        Assert.Equal(60, messages[^1].Data1);
        Assert.Equal(0, engine.CaptureSnapshot().TemporarySemitoneOffset);
        engine.Reset();
        Assert.Contains(new MidiEvent(MidiEventKind.NoteOff, 0, 55, 0), messages);
        Assert.Contains(new MidiEvent(MidiEventKind.NoteOff, 0, 60, 0), messages);
    }

    [Fact]
    public void ThresholdDeadZoneAndUnassignedDirectionsDoNotChangePlayingOctave()
    {
        var (engine, _) = Create(new()
        {
            LeftStick = new() { Left = StickAction.MomentaryOctaveDown, Down = StickAction.None, Right = StickAction.None }
        });
        Set(engine, PhysicalControl.LeftStickX, -0.64f);
        Assert.Equal(0, engine.CaptureSnapshot().TemporaryOctaveOffset);
        Set(engine, PhysicalControl.LeftStickX, -0.65f);
        Assert.Equal(-1, engine.CaptureSnapshot().TemporaryOctaveOffset);
        Set(engine, PhysicalControl.LeftStickX, -0.3f);
        Assert.Equal(-1, engine.CaptureSnapshot().TemporaryOctaveOffset);
        Set(engine, PhysicalControl.LeftStickX, -0.25f);
        Assert.Equal(0, engine.CaptureSnapshot().TemporaryOctaveOffset);
        Set(engine, PhysicalControl.LeftStickX, 1);
        Assert.Equal(0, engine.CaptureSnapshot().TemporaryOctaveOffset);
        Set(engine, PhysicalControl.LeftStickX, 0);
        Set(engine, PhysicalControl.LeftStickY, 1);
        Assert.Equal(0, engine.CaptureSnapshot().TemporaryOctaveOffset);
        Assert.Equal(3, engine.CaptureSnapshot().BaseOctave);
    }

    [Theory]
    [InlineData(0, PhysicalControl.LeftBumper)]
    [InlineData(127, PhysicalControl.RightBumper)]
    public void OutOfRangeAccidentalDoesNotCreateANoteOrAnUnmatchedRelease(byte pitch, PhysicalControl bumper)
    {
        var (engine, messages) = Create(new()
        {
            Mappings = InstrumentConfiguration.DefaultMappings.SetItem(PhysicalControl.FaceSouth, new FixedNoteAction(pitch))
        });
        Set(engine, bumper, 1);
        Set(engine, PhysicalControl.FaceSouth, 1);
        Set(engine, bumper, 0);
        Set(engine, PhysicalControl.FaceSouth, 0);
        Assert.Empty(messages);
        Assert.Empty(engine.CaptureSnapshot().HeldNotes);
    }

    [Fact]
    public void DisconnectClearsOctaveAndAccidentalAndStopsTheCombinedPitch()
    {
        var (engine, messages) = Create();
        Set(engine, PhysicalControl.LeftStickY, -1);
        Set(engine, PhysicalControl.RightBumper, 1);
        Set(engine, PhysicalControl.FaceSouth, 1);
        engine.Process(new(GamepadEventKind.Disconnected, Device));
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOff, 0, 68, 0), messages[^1]);
        var state = engine.CaptureSnapshot();
        Assert.Empty(state.HeldNotes);
        Assert.Equal(0, state.TemporaryOctaveOffset);
        Assert.Equal(0, state.TemporarySemitoneOffset);
        Assert.Null(state.LeftStickLatch);
    }

    [Fact]
    public void AccidentalSourcesAreConfigurableAndConflictingAssignmentsAreRejected()
    {
        var (engine, messages) = Create(new()
        {
            TemporarySemitoneModifiers = ImmutableDictionary<PhysicalControl, int>.Empty.Add(PhysicalControl.LeftStickButton, 2)
        });
        Set(engine, PhysicalControl.LeftStickButton, 1);
        Set(engine, PhysicalControl.FaceSouth, 1);
        Assert.Equal(57, Assert.Single(messages).Data1);
        Assert.Throws<ArgumentException>(() => new MappingEngine(new()
        { TemporarySemitoneModifiers = InstrumentConfiguration.DefaultSemitoneModifiers.Add(PhysicalControl.FaceSouth, 1) }));
        Assert.Throws<ArgumentException>(() => new MappingEngine(new()
        { TemporaryOctaveModifiers = ImmutableDictionary<PhysicalControl, int>.Empty.Add(PhysicalControl.RightBumper, 1) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MappingEngine(new()
        { TemporarySemitoneModifiers = InstrumentConfiguration.DefaultSemitoneModifiers.SetItem(PhysicalControl.RightBumper, 0) }));
    }

    private static (MappingEngine Engine, List<MidiEvent> Messages) Create(InstrumentConfiguration? configuration = null)
    {
        var engine = new MappingEngine(configuration);
        var messages = new List<MidiEvent>();
        engine.MidiGenerated += messages.Add;
        engine.Process(new(GamepadEventKind.Selected, Device));
        return (engine, messages);
    }
    private static void Set(MappingEngine engine, PhysicalControl control, float value) =>
        engine.Process(new(GamepadEventKind.ControlChanged, Device, control, value));
}
