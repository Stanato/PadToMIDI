using System.Collections.Immutable;
using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Input;
using PadToMIDI.Core.Midi;
using PadToMIDI.Core.Musical;
using Xunit;

namespace PadToMIDI.Core.Tests;

public sealed class OctaveAndAccidentalTests
{
    private static readonly GamepadDeviceId Device = new(1);
    // Optional persistent/retrigger actions remain supported with an explicit configuration.
    private static readonly DiscreteStickConfiguration TestStick = new()
    {
        Up = StickAction.IncreaseOctave, Down = StickAction.DecreaseOctave,
        Right = StickAction.SharpenLastNote, Left = StickAction.FlattenLastNote
    };
    private static readonly InstrumentConfiguration TestProfile = new()
    {
        TemporarySemitoneModifiers = ImmutableDictionary<PhysicalControl, int>.Empty,
        TemporaryOctaveModifiers = ImmutableDictionary<PhysicalControl, int>.Empty
            .Add(PhysicalControl.LeftBumper, -1).Add(PhysicalControl.RightBumper, 1),
        LeftStick = TestStick
    };

    [Theory]
    [InlineData(PhysicalControl.LeftBumper, 43)]
    [InlineData(PhysicalControl.RightBumper, 67)]
    public void ModifierReleaseBeforeNoteReleaseRetainsExactShiftedPitch(PhysicalControl modifier, byte pitch)
    {
        var (engine, messages) = Create();
        Set(engine, modifier, 1);
        Set(engine, PhysicalControl.FaceSouth, 1);
        Set(engine, modifier, 0);
        Set(engine, PhysicalControl.FaceSouth, 0);
        Assert.Equal(new[] { new MidiEvent(MidiEventKind.NoteOn, 0, pitch, 100), new(MidiEventKind.NoteOff, 0, pitch, 0) }, messages);
        Assert.Empty(engine.CaptureSnapshot().HeldNotes);
        Set(engine, PhysicalControl.FaceSouth, 1);
        Assert.Equal(55, messages[^1].Data1);
    }

    [Fact]
    public void BothBumpersCancelAndChangingModifiersDoesNotRetuneHeldNotes()
    {
        var (engine, messages) = Create();
        Set(engine, PhysicalControl.LeftBumper, 1);
        Set(engine, PhysicalControl.RightBumper, 1);
        Assert.Equal(0, engine.CaptureSnapshot().TemporaryOctaveOffset);
        Set(engine, PhysicalControl.FaceSouth, 1);
        Set(engine, PhysicalControl.LeftBumper, 0);
        Set(engine, PhysicalControl.RightBumper, 0);
        Assert.Single(messages);
        Assert.Equal(55, Assert.Single(engine.CaptureSnapshot().HeldNotes).MidiNote);
        Set(engine, PhysicalControl.FaceSouth, 0);
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOff, 0, 55, 0), messages[^1]);
    }

    [Fact]
    public void ModifiersCanUseOtherAbstractButtonsAndMultipleOctaveShifts()
    {
        var (engine, messages) = Create(TestProfile with
        {
            TemporaryOctaveModifiers = ImmutableDictionary<PhysicalControl, int>.Empty.Add(PhysicalControl.LeftStickButton, 2)
        });
        Set(engine, PhysicalControl.LeftBumper, 1);
        Set(engine, PhysicalControl.LeftStickButton, 1);
        Set(engine, PhysicalControl.FaceSouth, 1);
        Assert.Equal(79, Assert.Single(messages).Data1);
        Assert.Equal(2, engine.CaptureSnapshot().TemporaryOctaveOffset);
    }

    [Theory]
    [InlineData(9, PhysicalControl.FaceEast, PhysicalControl.LeftBumper, 120)]
    [InlineData(-1, PhysicalControl.FaceEast, PhysicalControl.LeftBumper, 0)]
    public void ModifierIsAppliedBeforeMidiRangeChecking(int octave, PhysicalControl note, PhysicalControl modifier, byte pitch)
    {
        var (engine, messages) = Create(TestProfile with { BaseOctave = octave });
        Set(engine, modifier, 1);
        Set(engine, note, 1);
        Set(engine, modifier, 0);
        Set(engine, note, 0);
        Assert.Equal(pitch, messages[0].Data1);
        Assert.Equal(pitch, messages[1].Data1);
    }

    [Fact]
    public void OutOfRangeShiftedNotesDoNotClampOrCreateReleaseEvents()
    {
        var (engine, messages) = Create(TestProfile with { BaseOctave = -1 });
        Set(engine, PhysicalControl.LeftBumper, 1);
        Set(engine, PhysicalControl.DPadDown, 1);
        Set(engine, PhysicalControl.LeftBumper, 0);
        Set(engine, PhysicalControl.DPadDown, 0);
        Assert.Empty(messages);
        Assert.Empty(engine.CaptureSnapshot().HeldNotes);
    }

    [Fact]
    public void FixedNotesIgnorePersistentOctaveButRespectMomentaryModifiers()
    {
        var (engine, messages) = Create(TestProfile with
        {
            Mappings = InstrumentConfiguration.DefaultMappings.SetItem(PhysicalControl.FaceSouth, new FixedNoteAction(36))
        });
        Set(engine, PhysicalControl.LeftStickY, -1);
        Set(engine, PhysicalControl.RightBumper, 1);
        Set(engine, PhysicalControl.FaceSouth, 1);
        Assert.Equal(48, Assert.Single(messages).Data1);
    }

    [Fact]
    public void UpIsLatchedUntilFullStickNeutralAndHeldNotesKeepOriginalPitch()
    {
        var (engine, messages) = Create();
        Set(engine, PhysicalControl.FaceSouth, 1);
        Set(engine, PhysicalControl.LeftStickY, -0.7f);
        Set(engine, PhysicalControl.LeftStickY, -1);
        Set(engine, PhysicalControl.LeftStickY, -0.5f);
        Set(engine, PhysicalControl.LeftStickY, 1); // Opposite direction without neutral is still the same excursion.
        Assert.Equal(4, engine.CaptureSnapshot().BaseOctave);
        Assert.Equal(StickDirection.Up, engine.CaptureSnapshot().LeftStickLatch);
        Set(engine, PhysicalControl.FaceSouth, 0);
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOff, 0, 55, 0), messages[^1]);
        Set(engine, PhysicalControl.LeftStickY, 0.25f);
        Assert.Null(engine.CaptureSnapshot().LeftStickLatch);
        Set(engine, PhysicalControl.LeftStickY, -1);
        Assert.Equal(5, engine.CaptureSnapshot().BaseOctave);
        Set(engine, PhysicalControl.FaceSouth, 1);
        Assert.Equal(79, messages[^1].Data1);
    }

    [Fact]
    public void BothAxesMustEnterNeutralAndADiagonalDoesNotProduceTwoActions()
    {
        var (engine, _) = Create();
        Set(engine, PhysicalControl.LeftStickY, -1);
        Set(engine, PhysicalControl.LeftStickX, 1);
        Set(engine, PhysicalControl.LeftStickY, 0);
        Set(engine, PhysicalControl.LeftStickY, -1);
        Assert.Equal(4, engine.CaptureSnapshot().BaseOctave);
        Set(engine, PhysicalControl.LeftStickY, 0);
        Set(engine, PhysicalControl.LeftStickX, 0);
        Set(engine, PhysicalControl.LeftStickY, 1);
        Assert.Equal(3, engine.CaptureSnapshot().BaseOctave);
    }

    [Fact]
    public void ThresholdAndDeadZoneAreConfigurableWithHysteresis()
    {
        var (engine, _) = Create(TestProfile with { LeftStick = TestStick with { Threshold = 0.8f, DeadZone = 0.1f } });
        Set(engine, PhysicalControl.LeftStickY, -0.799f);
        Assert.Equal(3, engine.CaptureSnapshot().BaseOctave);
        Set(engine, PhysicalControl.LeftStickY, -0.8f);
        Assert.Equal(4, engine.CaptureSnapshot().BaseOctave);
        Set(engine, PhysicalControl.LeftStickY, -0.11f);
        Set(engine, PhysicalControl.LeftStickY, -0.9f);
        Assert.Equal(4, engine.CaptureSnapshot().BaseOctave);
        Set(engine, PhysicalControl.LeftStickY, -0.1f);
        Set(engine, PhysicalControl.LeftStickY, -0.8f);
        Assert.Equal(5, engine.CaptureSnapshot().BaseOctave);
    }

    [Theory]
    [InlineData(-1, 1f)]
    [InlineData(9, -1f)]
    public void PersistentOctaveStopsAtConfiguredDomainBounds(int octave, float direction)
    {
        var (engine, _) = Create(TestProfile with { BaseOctave = octave });
        for (int index = 0; index < 12; index++)
        {
            Set(engine, PhysicalControl.LeftStickY, direction);
            Set(engine, PhysicalControl.LeftStickY, 0);
        }
        Assert.Equal(octave, engine.CaptureSnapshot().BaseOctave);
    }

    [Fact]
    public void ConfigurationChangesPreservePersistentOffsetUnlessBaseOctaveIsExplicitlyChanged()
    {
        var (engine, _) = Create();
        Set(engine, PhysicalControl.LeftStickY, -1);
        engine.UpdateConfiguration(TestProfile with { MidiChannel = 5 });
        Assert.Equal(4, engine.CaptureSnapshot().BaseOctave);
        Assert.Equal(1, engine.CaptureSnapshot().PersistentOctaveOffset);
        engine.UpdateConfiguration(TestProfile with { BaseOctave = 2 });
        Assert.Equal(2, engine.CaptureSnapshot().BaseOctave);
        Assert.Equal(0, engine.CaptureSnapshot().PersistentOctaveOffset);
    }

    [Fact]
    public void ResetPreservesPersistentOctaveButClearsModifiersAndLatch()
    {
        var (engine, messages) = Create();
        Set(engine, PhysicalControl.LeftStickY, -1);
        Set(engine, PhysicalControl.RightBumper, 1);
        Set(engine, PhysicalControl.FaceSouth, 1); // G5 = 79.
        engine.Reset();
        var state = engine.CaptureSnapshot();
        Assert.Equal(4, state.BaseOctave);
        Assert.Equal(0, state.TemporaryOctaveOffset);
        Assert.Null(state.LeftStickLatch);
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOff, 0, 79, 0), messages[^1]);
        engine.Process(new(GamepadEventKind.Selected, Device));
        Set(engine, PhysicalControl.LeftStickY, 1);
        Assert.Equal(3, engine.CaptureSnapshot().BaseOctave);
    }

    [Fact]
    public void AxisSourcesAndDirectionalActionsCanBeRemappedOrDisabled()
    {
        var (engine, _) = Create(TestProfile with
        {
            LeftStick = TestStick with { XAxis = PhysicalControl.RightStickX, YAxis = PhysicalControl.RightStickY,
                Up = StickAction.None, Right = StickAction.IncreaseOctave, Down = StickAction.DecreaseOctave }
        });
        Set(engine, PhysicalControl.LeftStickY, -1);
        Set(engine, PhysicalControl.RightStickY, -1);
        Assert.Equal(3, engine.CaptureSnapshot().BaseOctave);
        Set(engine, PhysicalControl.RightStickY, 0);
        Set(engine, PhysicalControl.RightStickX, 1);
        Assert.Equal(4, engine.CaptureSnapshot().BaseOctave);
    }

    [Theory]
    [InlineData(1f, 56)]
    [InlineData(-1f, 54)]
    public void ActiveAccidentalStopsOldPitchStartsAdjustedPitchAndReleasesAdjustedPitch(float direction, byte adjusted)
    {
        var (engine, messages) = Create(TestProfile with { FixedVelocity = 77, MidiChannel = 2 });
        Set(engine, PhysicalControl.FaceSouth, 1);
        Set(engine, PhysicalControl.LeftStickX, direction);
        Set(engine, PhysicalControl.LeftStickX, direction); // Held gesture does not repeat.
        var note = Assert.Single(engine.CaptureSnapshot().HeldNotes);
        Assert.Equal(adjusted, note.MidiNote);
        Assert.Equal(77, note.Velocity);
        Assert.Equal(note, engine.CaptureSnapshot().LastNote);
        Assert.Equal(note, engine.CaptureSnapshot().LastFaceNote);
        Assert.Equal(note, engine.CaptureSnapshot().LastActiveFaceNote);
        Set(engine, PhysicalControl.FaceSouth, 0);
        Assert.Equal(new[] { new MidiEvent(MidiEventKind.NoteOn, 2, 55, 77), new(MidiEventKind.NoteOff, 2, 55, 0),
            new(MidiEventKind.NoteOn, 2, adjusted, 77), new(MidiEventKind.NoteOff, 2, adjusted, 0) }, messages);
    }

    [Fact]
    public void RetuningUsesOriginalRecordDespiteChangedRootOctaveChannelVelocityAndModifier()
    {
        var (engine, messages) = Create();
        Set(engine, PhysicalControl.RightBumper, 1);
        Set(engine, PhysicalControl.FaceSouth, 1);
        Set(engine, PhysicalControl.RightBumper, 0);
        engine.UpdateConfiguration(TestProfile with { Root = PitchClass.D, BaseOctave = 5, MidiChannel = 7, FixedVelocity = 42 });
        Set(engine, PhysicalControl.LeftStickX, 1);
        Set(engine, PhysicalControl.FaceSouth, 0);
        Assert.Equal(new[] { new MidiEvent(MidiEventKind.NoteOn, 0, 67, 100), new(MidiEventKind.NoteOff, 0, 67, 0),
            new(MidiEventKind.NoteOn, 0, 68, 100), new(MidiEventKind.NoteOff, 0, 68, 0) }, messages);
    }

    [Fact]
    public void RepeatedAccidentalsRequireNeutralAndUpdateDpadHistory()
    {
        var (engine, messages) = Create();
        Set(engine, PhysicalControl.DPadRight, 1);
        Set(engine, PhysicalControl.LeftStickX, 1);
        Set(engine, PhysicalControl.LeftStickX, 0);
        Set(engine, PhysicalControl.LeftStickX, 1);
        Assert.Equal((byte?)55, engine.CaptureSnapshot().LastDPadNote?.MidiNote);
        Set(engine, PhysicalControl.DPadRight, 0);
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOff, 0, 55, 0), messages[^1]);
    }

    [Theory]
    [InlineData(0, -1f)]
    [InlineData(127, 1f)]
    public void AccidentalAtMidiBoundaryLeavesOriginalNoteAndLedgerUntouched(byte pitch, float direction)
    {
        var (engine, messages) = Create(TestProfile with
        {
            Mappings = InstrumentConfiguration.DefaultMappings.SetItem(PhysicalControl.FaceSouth, new FixedNoteAction(pitch))
        });
        Set(engine, PhysicalControl.FaceSouth, 1);
        Set(engine, PhysicalControl.LeftStickX, direction);
        Assert.Single(messages);
        Assert.Equal(pitch, Assert.Single(engine.CaptureSnapshot().HeldNotes).MidiNote);
        Set(engine, PhysicalControl.FaceSouth, 0);
        Assert.Equal(pitch, messages[^1].Data1);
    }

    [Fact]
    public void EditingReleasedSelectionDoesNotSoundItOrRetuneAnOlderHeldNote()
    {
        var (engine, messages) = Create();
        Set(engine, PhysicalControl.FaceSouth, 1);
        Set(engine, PhysicalControl.FaceEast, 1);
        Set(engine, PhysicalControl.FaceEast, 0);
        int count = messages.Count;
        Set(engine, PhysicalControl.LeftStickX, 1);
        Assert.Equal(count, messages.Count);
        Assert.Equal((byte?)61, engine.CaptureSnapshot().LastNote?.MidiNote);
        Assert.Equal((byte?)55, engine.CaptureSnapshot().LastActiveFaceNote?.MidiNote);
        Set(engine, PhysicalControl.FaceEast, 1); // A fresh press resolves the configured scale degree again.
        Assert.Equal(60, messages[^1].Data1);
    }

    [Fact]
    public void AccidentalWithoutSelectedNoteIsSilentButStillLatches()
    {
        var (engine, messages) = Create();
        Set(engine, PhysicalControl.LeftStickX, 1);
        Set(engine, PhysicalControl.FaceSouth, 1);
        Set(engine, PhysicalControl.LeftStickX, 0.9f);
        Assert.Single(messages);
        Set(engine, PhysicalControl.LeftStickX, 0);
        Set(engine, PhysicalControl.LeftStickX, 1);
        Assert.Equal(56, messages[^1].Data1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetuningIntoOwnedPitchCoalescesUntilBothOwnersRelease(bool reverseRelease)
    {
        var (engine, messages) = Create(TestProfile with
        {
            Mappings = InstrumentConfiguration.DefaultMappings
                .SetItem(PhysicalControl.FaceSouth, new FixedNoteAction(55))
                .SetItem(PhysicalControl.FaceEast, new FixedNoteAction(56))
        });
        Set(engine, PhysicalControl.FaceSouth, 1);
        Set(engine, PhysicalControl.FaceEast, 1);
        Set(engine, PhysicalControl.LeftStickX, -1);
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOff, 0, 56, 0), messages[^1]);
        Assert.Equal(3, messages.Count);
        Assert.All(engine.CaptureSnapshot().HeldNotes, note => Assert.Equal(55, note.MidiNote));
        Set(engine, reverseRelease ? PhysicalControl.FaceEast : PhysicalControl.FaceSouth, 0);
        Assert.Equal(3, messages.Count);
        Set(engine, reverseRelease ? PhysicalControl.FaceSouth : PhysicalControl.FaceEast, 0);
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOff, 0, 55, 0), messages[^1]);
    }

    [Fact]
    public void RetuningSharedPitchDoesNotStopAnotherPhysicalOwner()
    {
        var (engine, messages) = Create(TestProfile with
        {
            Mappings = InstrumentConfiguration.DefaultMappings
                .SetItem(PhysicalControl.FaceSouth, new FixedNoteAction(55))
                .SetItem(PhysicalControl.FaceEast, new FixedNoteAction(55))
        });
        Set(engine, PhysicalControl.FaceSouth, 1);
        Set(engine, PhysicalControl.FaceEast, 1);
        Set(engine, PhysicalControl.LeftStickX, 1);
        Assert.Equal(new[] { new MidiEvent(MidiEventKind.NoteOn, 0, 55, 100), new(MidiEventKind.NoteOn, 0, 56, 100) }, messages);
        engine.Reset();
        Assert.Contains(new MidiEvent(MidiEventKind.NoteOff, 0, 55, 0), messages);
        Assert.Contains(new MidiEvent(MidiEventKind.NoteOff, 0, 56, 0), messages);
    }

    [Theory]
    [InlineData(GamepadEventKind.Disconnected)]
    [InlineData(GamepadEventKind.Faulted)]
    [InlineData(GamepadEventKind.Stopped)]
    public void CleanupStopsAdjustedPitchAndClearsGestureState(GamepadEventKind kind)
    {
        var (engine, messages) = Create();
        Set(engine, PhysicalControl.RightBumper, 1);
        Set(engine, PhysicalControl.FaceSouth, 1);
        Set(engine, PhysicalControl.LeftStickX, -1);
        engine.Process(new(kind, Device));
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOff, 0, 66, 0), messages[^1]);
        var state = engine.CaptureSnapshot();
        Assert.Empty(state.HeldNotes);
        Assert.Null(state.LeftStickLatch);
        Assert.Null(state.LastNote);
        Assert.Equal(0, state.TemporaryOctaveOffset);
    }

    [Theory]
    [InlineData(MidiEventKind.NoteOff, 55)]
    [InlineData(MidiEventKind.NoteOn, 56)]
    public void FailedRetuneDeliveryRetainsExactRecordForCleanup(MidiEventKind failure, byte retainedPitch)
    {
        var (engine, messages) = Create();
        Set(engine, PhysicalControl.FaceSouth, 1);
        bool failed = false;
        engine.MidiGenerated += message =>
        {
            if (!failed && message.Kind == failure) { failed = true; throw new InvalidOperationException("Output failure"); }
        };
        Assert.Throws<InvalidOperationException>(() => Set(engine, PhysicalControl.LeftStickX, 1));
        Assert.Equal(retainedPitch, Assert.Single(engine.CaptureSnapshot().HeldNotes).MidiNote);
        engine.Reset();
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOff, 0, retainedPitch, 0), messages[^1]);
        Assert.Empty(engine.CaptureSnapshot().HeldNotes);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1.1f, 0.25f)]
    [InlineData(0.65f, -0.1f)]
    [InlineData(0.65f, 0.65f)]
    [InlineData(float.NaN, 0.25f)]
    [InlineData(0.65f, float.PositiveInfinity)]
    public void InvalidThresholdsAreRejected(float threshold, float deadZone)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MappingEngine(new()
        { LeftStick = new() { Threshold = threshold, DeadZone = deadZone } }));
    }

    [Fact]
    public void InvalidSourcesActionsAndConflictingModifiersAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MappingEngine(new() { LeftStick = new() { XAxis = PhysicalControl.LeftTrigger } }));
        Assert.Throws<ArgumentException>(() => new MappingEngine(new() { LeftStick = new() { Up = (StickAction)99 } }));
        Assert.Throws<ArgumentException>(() => new MappingEngine(new()
        { TemporaryOctaveModifiers = InstrumentConfiguration.DefaultOctaveModifiers.Add(PhysicalControl.FaceSouth, 1) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MappingEngine(new()
        { TemporaryOctaveModifiers = InstrumentConfiguration.DefaultOctaveModifiers.SetItem(PhysicalControl.LeftBumper, 0) }));
    }

    private static (MappingEngine Engine, List<MidiEvent> Messages) Create(InstrumentConfiguration? configuration = null)
    {
        var engine = new MappingEngine(configuration ?? TestProfile);
        var messages = new List<MidiEvent>();
        engine.MidiGenerated += messages.Add;
        engine.Process(new(GamepadEventKind.Selected, Device));
        return (engine, messages);
    }

    private static void Set(MappingEngine engine, PhysicalControl control, float value) =>
        engine.Process(new(GamepadEventKind.ControlChanged, Device, control, value));
}
