using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Input;
using PadToMIDI.Core.Midi;
using PadToMIDI.Core.Musical;
using Xunit;

namespace PadToMIDI.Core.Tests;

public sealed class ExpressionTests
{
    private static readonly GamepadDeviceId Device = new(1);
    private static readonly AnalogResponseConfiguration Linear = new() { DeadZone = 0, UpdateRateHz = 0 };
    private static InstrumentConfiguration TestSettings => new()
    {
        Expression = new()
        {
            DPadAftertouch = new() { MinimumThreshold = 0, Response = Linear },
            FaceAftertouch = new() { Source = PhysicalControl.RightTrigger, MinimumThreshold = 0, Response = Linear },
            PitchBend = new() { Response = Linear },
            Timbre = new() { Response = Linear with { Invert = true } }
        }
    };

    [Theory]
    [InlineData(PhysicalControl.LeftTrigger, PhysicalControl.DPadRight, 53)]
    [InlineData(PhysicalControl.RightTrigger, PhysicalControl.FaceSouth, 55)]
    public void TriggersControlOnlyTheirActiveGroupAndSuppressDuplicatePressure(PhysicalControl trigger, PhysicalControl button, byte pitch)
    {
        var (engine, messages, _) = Create();
        Set(engine, trigger, 1); // No active note: no message.
        Assert.Empty(messages);
        Set(engine, button, 1);
        Assert.Equal(new MidiEvent(MidiEventKind.PolyphonicPressure, 0, pitch, 127), messages[^1]);
        int count = messages.Count;
        Set(engine, trigger, 1);
        Tick(engine);
        Assert.Equal(count, messages.Count);
        Set(engine, trigger, 0);
        Assert.Equal(new MidiEvent(MidiEventKind.PolyphonicPressure, 0, pitch, 0), messages[^1]);
        Set(engine, button, 0);
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOff, 0, pitch, 0), messages[^1]);
    }

    [Fact]
    public void LatestActiveSelectionClearsOldPressureAndFallsBackWhenReleased()
    {
        var (engine, messages, _) = Create();
        Set(engine, PhysicalControl.DPadDown, 1);
        Set(engine, PhysicalControl.LeftTrigger, 1);
        Set(engine, PhysicalControl.DPadRight, 1);
        Assert.Equal(new MidiEvent(MidiEventKind.PolyphonicPressure, 0, 48, 0), messages[^2]);
        Assert.Equal(new MidiEvent(MidiEventKind.PolyphonicPressure, 0, 53, 127), messages[^1]);
        Set(engine, PhysicalControl.DPadRight, 0);
        Assert.Equal(new[] { new MidiEvent(MidiEventKind.PolyphonicPressure, 0, 53, 0),
            new(MidiEventKind.NoteOff, 0, 53, 0), new(MidiEventKind.PolyphonicPressure, 0, 48, 127) }, messages.TakeLast(3));
        Assert.Equal((byte?)48, engine.CaptureSnapshot().LastActiveDPadNote?.MidiNote);
    }

    [Fact]
    public void AftertouchUsesHeldPitchAndChannelAfterConfigurationAndAccidentalChanges()
    {
        var (engine, messages, _) = Create();
        Set(engine, PhysicalControl.RightBumper, 1);
        Set(engine, PhysicalControl.FaceSouth, 1);
        Set(engine, PhysicalControl.RightBumper, 0);
        engine.UpdateConfiguration(TestSettings with { BaseOctave = 6, Root = PitchClass.E, MidiChannel = 7 });
        Set(engine, PhysicalControl.RightTrigger, 1);
        Assert.Equal(new MidiEvent(MidiEventKind.PolyphonicPressure, 0, 56, 127), messages[^1]);
        Set(engine, PhysicalControl.FaceSouth, 0);
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOff, 0, 56, 0), messages[^1]);
    }

    [Fact]
    public void OptionalRetuneClearsOldPressureAndSelectsAdjustedPitch()
    {
        var (engine, messages, _) = Create(TestSettings with
        { LeftStick = new() { Right = StickAction.SharpenLastNote } });
        Set(engine, PhysicalControl.FaceSouth, 1);
        Set(engine, PhysicalControl.RightTrigger, 1);
        Set(engine, PhysicalControl.LeftStickX, 1);
        Assert.Equal(new[] { new MidiEvent(MidiEventKind.PolyphonicPressure, 0, 55, 0), new(MidiEventKind.NoteOff, 0, 55, 0),
            new(MidiEventKind.NoteOn, 0, 56, 100), new(MidiEventKind.PolyphonicPressure, 0, 56, 127) }, messages.TakeLast(4));
        Set(engine, PhysicalControl.FaceSouth, 0);
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOff, 0, 56, 0), messages[^1]);
    }

    [Fact]
    public void SharedPitchUsesMostRecentPhysicalOwnerWithoutConflictingGroupTraffic()
    {
        var (engine, messages, clock) = Create(TestSettings with
        { Mappings = InstrumentConfiguration.DefaultMappings.SetItem(PhysicalControl.FaceSouth, new FixedNoteAction(48)) });
        Set(engine, PhysicalControl.DPadDown, 1);
        Set(engine, PhysicalControl.LeftTrigger, 1);
        Set(engine, PhysicalControl.RightTrigger, 0.5f);
        Set(engine, PhysicalControl.FaceSouth, 1);
        Assert.Equal(new MidiEvent(MidiEventKind.PolyphonicPressure, 0, 48, 64), messages[^1]);
        int count = messages.Count;
        clock.Advance(100); Tick(engine);
        Assert.Equal(count, messages.Count);
        Set(engine, PhysicalControl.FaceSouth, 0);
        Assert.Equal(new MidiEvent(MidiEventKind.PolyphonicPressure, 0, 48, 127), messages[^1]);
        Assert.Single(messages, m => m.Kind == MidiEventKind.NoteOn);
    }

    [Theory]
    [InlineData(-1f, 0)]
    [InlineData(0f, 8192)]
    [InlineData(1f, 16383)]
    public void PitchBendEndpointsAndCenterAreExact(float input, ushort expected)
    {
        var (engine, messages, _) = Create();
        Set(engine, PhysicalControl.RightStickX, input);
        Assert.Equal(new MidiEvent(MidiEventKind.PitchBend, 0, 0, expected), Assert.Single(messages));
        Assert.Equal(expected, engine.CaptureSnapshot().Expression.PitchBend);
    }

    [Fact]
    public void CenterBypassesPitchBendRateLimitAndSmoothingAndSuppressesDuplicates()
    {
        var settings = TestSettings with { Expression = TestSettings.Expression with
        { PitchBend = new() { Response = Linear with { DeadZone = 0.1f, UpdateRateHz = 1, SmoothingMilliseconds = 100 } } } };
        var (engine, messages, clock) = Create(settings);
        Set(engine, PhysicalControl.RightStickX, 1);
        clock.Advance(1000); Tick(engine);
        Assert.True(messages[^1].Data2 > 8192);
        Set(engine, PhysicalControl.RightStickX, 0.05f);
        Assert.Equal(new MidiEvent(MidiEventKind.PitchBend, 0, 0, 8192), messages[^1]);
        int count = messages.Count;
        clock.Advance(2000); Tick(engine);
        Set(engine, PhysicalControl.RightStickX, -0.05f);
        Assert.Equal(count, messages.Count);
    }

    [Fact]
    public void LatestDeferredValueIsSentByHeartbeatEvenWhenAxisStopsMoving()
    {
        var (engine, messages, clock) = Create(TestSettings with { Expression = TestSettings.Expression with
        { PitchBend = new() { Response = Linear with { UpdateRateHz = 10 } } } });
        Set(engine, PhysicalControl.RightStickX, 0.25f);
        clock.Advance(10); Set(engine, PhysicalControl.RightStickX, 0.5f);
        clock.Advance(10); Set(engine, PhysicalControl.RightStickX, 1);
        Assert.Single(messages);
        clock.Advance(80); Tick(engine);
        Assert.Equal(16383, messages[^1].Data2);
        Assert.Equal(2, messages.Count);
    }

    [Theory]
    [InlineData(-1f, 127)]
    [InlineData(0f, 64)]
    [InlineData(1f, 0)]
    public void DefaultTimbreMovesUpToMaximumAndUsesCc74(float value, ushort expected)
    {
        var (engine, messages, _) = Create();
        Set(engine, PhysicalControl.RightStickY, value);
        Assert.Equal(new MidiEvent(MidiEventKind.ControlChange, 0, 74, expected, 64), Assert.Single(messages));
        Assert.Equal(expected, engine.CaptureSnapshot().Expression.Timbre);
    }

    [Theory]
    [InlineData(ResponseCurve.Linear, 12288)]
    [InlineData(ResponseCurve.Quadratic, 10240)]
    [InlineData(ResponseCurve.SquareRoot, 13984)]
    public void PitchResponseCurveIsConfigurable(ResponseCurve curve, ushort expected)
    {
        var (engine, messages, _) = Create(TestSettings with { Expression = TestSettings.Expression with
        { PitchBend = new() { Response = Linear with { Curve = curve } } } });
        Set(engine, PhysicalControl.RightStickX, 0.5f);
        Assert.Equal(expected, Assert.Single(messages).Data2);
    }

    [Fact]
    public void AxisSourceSensitivityInversionAndTimbreCcAreConfigurable()
    {
        var (engine, messages, _) = Create(TestSettings with { Expression = TestSettings.Expression with
        {
            PitchBend = new() { Source = PhysicalControl.LeftStickX, Response = Linear with { Invert = true, Sensitivity = 2 } },
            Timbre = new() { Source = PhysicalControl.LeftStickY, ControllerNumber = 11, Response = Linear }
        } });
        Set(engine, PhysicalControl.LeftStickX, 0.5f);
        Assert.Equal(new MidiEvent(MidiEventKind.PitchBend, 0, 0, 0), messages[^1]);
        Set(engine, PhysicalControl.LeftStickY, 1);
        Assert.Equal(new MidiEvent(MidiEventKind.ControlChange, 0, 11, 127, 64), messages[^1]);
    }

    [Fact]
    public void TriggerDeadZoneThresholdCurveAndSmoothingSettleAndReleaseExactlyToZero()
    {
        var (engine, messages, clock) = Create(TestSettings with { Expression = TestSettings.Expression with
        { DPadAftertouch = new() { MinimumThreshold = 0.25f, Response = Linear with { DeadZone = 0.2f, Curve = ResponseCurve.Quadratic, SmoothingMilliseconds = 100 } } } });
        Set(engine, PhysicalControl.DPadDown, 1);
        Set(engine, PhysicalControl.LeftTrigger, 0.3f);
        clock.Advance(1000); Tick(engine);
        Assert.Single(messages);
        Set(engine, PhysicalControl.LeftTrigger, 0.7f); // After dead zone/threshold: 0.5; quadratic: 0.25.
        clock.Advance(100); Tick(engine);
        Assert.Equal(20, messages[^1].Data2);
        clock.Advance(1000); Tick(engine);
        Assert.Equal(32, messages[^1].Data2);
        Set(engine, PhysicalControl.LeftTrigger, 0);
        Assert.Equal(new MidiEvent(MidiEventKind.PolyphonicPressure, 0, 48, 0), messages[^1]);
    }

    [Fact]
    public void TriggerSourceCanBeSwappedAndDisabled()
    {
        var (engine, messages, _) = Create(TestSettings with { Expression = TestSettings.Expression with
        { DPadAftertouch = new() { Source = PhysicalControl.RightTrigger, MinimumThreshold = 0, Response = Linear }, FaceAftertouch = new() { Enabled = false } } });
        Set(engine, PhysicalControl.DPadDown, 1);
        Set(engine, PhysicalControl.FaceSouth, 1);
        Set(engine, PhysicalControl.RightTrigger, 1);
        Assert.Equal(new MidiEvent(MidiEventKind.PolyphonicPressure, 0, 48, 127), messages[^1]);
        Assert.DoesNotContain(messages, m => m.Kind == MidiEventKind.PolyphonicPressure && m.Data1 == 55);
    }

    [Theory]
    [InlineData(GamepadEventKind.Disconnected)]
    [InlineData(GamepadEventKind.Faulted)]
    [InlineData(GamepadEventKind.Stopped)]
    [InlineData(GamepadEventKind.Selected)]
    public void LifecycleCleanupResetsPressureBendTimbreAndCancelsDeferredValues(GamepadEventKind kind)
    {
        var (engine, messages, clock) = Create();
        Set(engine, PhysicalControl.DPadDown, 1);
        Set(engine, PhysicalControl.LeftTrigger, 1);
        Set(engine, PhysicalControl.RightStickX, 1);
        Set(engine, PhysicalControl.RightStickY, -1);
        int count = messages.Count;
        engine.Process(new(kind, Device));
        var cleanup = messages.Skip(count).ToArray();
        Assert.Contains(new MidiEvent(MidiEventKind.PolyphonicPressure, 0, 48, 0), cleanup);
        Assert.Contains(new MidiEvent(MidiEventKind.PitchBend, 0, 0, 8192), cleanup);
        Assert.Contains(new MidiEvent(MidiEventKind.ControlChange, 0, 74, 64, 64), cleanup);
        Assert.Contains(new MidiEvent(MidiEventKind.NoteOff, 0, 48, 0), cleanup);
        Assert.Equal(new ExpressionStateSnapshot(0, 0, 8192, 64), engine.CaptureSnapshot().Expression);
        count = messages.Count;
        clock.Advance(1000); Tick(engine);
        Assert.Equal(count, messages.Count);
    }

    [Fact]
    public void ChannelAndCcChangesRestoreOldDestinationsAndReapplyOnNewChannel()
    {
        var (engine, messages, _) = Create();
        Set(engine, PhysicalControl.FaceSouth, 1);
        Set(engine, PhysicalControl.RightTrigger, 1);
        Set(engine, PhysicalControl.RightStickX, 1);
        Set(engine, PhysicalControl.RightStickY, -1);
        int before = messages.Count;
        engine.UpdateConfiguration(TestSettings with { MidiChannel = 3, Expression = TestSettings.Expression with
        { Timbre = new() { ControllerNumber = 11, Response = Linear with { Invert = true } } } });
        var changes = messages.Skip(before).ToArray();
        Assert.Contains(new MidiEvent(MidiEventKind.PitchBend, 0, 0, 8192), changes);
        Assert.Contains(new MidiEvent(MidiEventKind.ControlChange, 0, 74, 64, 64), changes);
        Assert.Contains(new MidiEvent(MidiEventKind.PitchBend, 3, 0, 16383), changes);
        Assert.Contains(new MidiEvent(MidiEventKind.ControlChange, 3, 11, 127, 64), changes);
        Assert.Equal(new MidiEvent(MidiEventKind.PolyphonicPressure, 0, 55, 127), changes.First(m => m.Kind == MidiEventKind.PolyphonicPressure && m.Data2 == 127));
        Assert.DoesNotContain(changes, m => m.Kind == MidiEventKind.NoteOff);
    }

    [Fact]
    public void DisablingExpressionRestoresControllersWithoutReleasingNotes()
    {
        var (engine, messages, _) = Create();
        Set(engine, PhysicalControl.FaceSouth, 1);
        Set(engine, PhysicalControl.RightTrigger, 1);
        Set(engine, PhysicalControl.RightStickX, 1);
        engine.UpdateConfiguration(TestSettings with { Expression = TestSettings.Expression with
        { FaceAftertouch = new() { Enabled = false }, PitchBend = new() { Enabled = false } } });
        Assert.Contains(new MidiEvent(MidiEventKind.PitchBend, 0, 0, 8192), messages);
        Assert.Contains(new MidiEvent(MidiEventKind.PolyphonicPressure, 0, 55, 0), messages);
        Assert.Single(engine.CaptureSnapshot().HeldNotes);
        Assert.DoesNotContain(messages, m => m.Kind == MidiEventKind.NoteOff);
    }

    [Theory]
    [InlineData(-0.1f, 1f, 0d)]
    [InlineData(1f, 1f, 0d)]
    [InlineData(float.NaN, 1f, 0d)]
    [InlineData(0.1f, 0f, 0d)]
    [InlineData(0.1f, float.PositiveInfinity, 0d)]
    [InlineData(0.1f, 1f, -1d)]
    [InlineData(0.1f, 1f, double.NaN)]
    public void InvalidAnalogSettingsAreRejected(float deadZone, float sensitivity, double smoothing)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MappingEngine(TestSettings with { Expression = TestSettings.Expression with
        { PitchBend = new() { Response = new() { DeadZone = deadZone, Sensitivity = sensitivity, SmoothingMilliseconds = smoothing } } } }));
    }

    [Fact]
    public void InvalidSourcesCurveThresholdRateAndChannelModeCcAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PitchBendConfiguration { Source = PhysicalControl.LeftTrigger }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new AftertouchConfiguration { Source = PhysicalControl.FaceSouth }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new AftertouchConfiguration { MinimumThreshold = 1 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnalogResponseConfiguration { Curve = (ResponseCurve)99 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnalogResponseConfiguration { UpdateRateHz = -1 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimbreConfiguration { ControllerNumber = 123 }.Validate());
    }

    [Fact]
    public void PressureRateLimitRetainsLatestValueAndZeroAlwaysBypassesIt()
    {
        var (engine, messages, clock) = Create(TestSettings with { Expression = TestSettings.Expression with
        { FaceAftertouch = TestSettings.Expression.FaceAftertouch with { Response = Linear with { UpdateRateHz = 10 } } } });
        Set(engine, PhysicalControl.FaceSouth, 1);
        Set(engine, PhysicalControl.RightTrigger, 0.25f);
        clock.Advance(10); Set(engine, PhysicalControl.RightTrigger, 0.5f);
        clock.Advance(10); Set(engine, PhysicalControl.RightTrigger, 1);
        Assert.Equal(2, messages.Count);
        clock.Advance(80); Tick(engine);
        Assert.Equal(new MidiEvent(MidiEventKind.PolyphonicPressure, 0, 55, 127), messages[^1]);
        Set(engine, PhysicalControl.RightTrigger, 0);
        Assert.Equal(new MidiEvent(MidiEventKind.PolyphonicPressure, 0, 55, 0), messages[^1]);
    }

    [Fact]
    public void ReleasingSelectedNoteCancelsDeferredPressureForItsOldPitch()
    {
        var (engine, messages, clock) = Create(TestSettings with { Expression = TestSettings.Expression with
        { FaceAftertouch = TestSettings.Expression.FaceAftertouch with { Response = Linear with { UpdateRateHz = 1 } } } });
        Set(engine, PhysicalControl.FaceSouth, 1);
        Set(engine, PhysicalControl.RightTrigger, 0.25f);
        Set(engine, PhysicalControl.RightTrigger, 1);
        Set(engine, PhysicalControl.FaceSouth, 0);
        int before = messages.Count;
        clock.Advance(2000); Tick(engine);
        Assert.Equal(before, messages.Count);
        Set(engine, PhysicalControl.FaceEast, 1);
        Assert.Equal(new MidiEvent(MidiEventKind.PolyphonicPressure, 0, 60, 127), messages[^1]);
        Assert.DoesNotContain(messages, m => m.Kind == MidiEventKind.PolyphonicPressure && m.Data1 == 55 && m.Data2 == 127);
    }

    [Fact]
    public void TimbreSmoothingSettlesOnHeartbeatAndCenterResetsWithoutAnExpressionTail()
    {
        var (engine, messages, clock) = Create(TestSettings with { Expression = TestSettings.Expression with
        { Timbre = TestSettings.Expression.Timbre with { Response = Linear with { Invert = true, SmoothingMilliseconds = 100 } } } });
        Set(engine, PhysicalControl.RightStickY, -1);
        Assert.Equal(64, messages[^1].Data2);
        clock.Advance(100); Tick(engine);
        Assert.Equal(104, messages[^1].Data2);
        clock.Advance(1000); Tick(engine);
        Assert.Equal(127, messages[^1].Data2);
        Set(engine, PhysicalControl.RightStickY, 0);
        Assert.Equal(64, messages[^1].Data2);
        int before = messages.Count;
        clock.Advance(1000); Tick(engine);
        Assert.Equal(before, messages.Count);
    }

    [Fact]
    public void OtherControllerCannotChangeExpressionOrFlushPendingValues()
    {
        var (engine, messages, clock) = Create();
        engine.Process(new(GamepadEventKind.ControlChanged, new(2), PhysicalControl.RightStickX, 1));
        clock.Advance(1000);
        engine.Process(new(GamepadEventKind.Tick, new(2)));
        Assert.Empty(messages);
        Assert.Equal(new ExpressionStateSnapshot(0, 0, 8192, 64), engine.CaptureSnapshot().Expression);
    }

    [Theory]
    [InlineData(MidiEventKind.PolyphonicPressure)]
    [InlineData(MidiEventKind.PitchBend)]
    [InlineData(MidiEventKind.ControlChange)]
    public void FailedExpressionCallbackRetainsResetInformationForFatalCleanup(MidiEventKind kind)
    {
        var (engine, messages, _) = Create();
        Set(engine, PhysicalControl.FaceSouth, 1);
        bool failed = false;
        engine.MidiGenerated += message =>
        {
            if (!failed && message.Kind == kind) { failed = true; throw new InvalidOperationException("Send failure"); }
        };
        PhysicalControl source = kind switch
        {
            MidiEventKind.PolyphonicPressure => PhysicalControl.RightTrigger,
            MidiEventKind.PitchBend => PhysicalControl.RightStickX,
            _ => PhysicalControl.RightStickY
        };
        Assert.Throws<InvalidOperationException>(() => Set(engine, source, 1));
        engine.Reset();
        Assert.Contains(new MidiEvent(MidiEventKind.NoteOff, 0, 55, 0), messages);
        Assert.Equal(new ExpressionStateSnapshot(0, 0, 8192, 64), engine.CaptureSnapshot().Expression);
    }

    private static (MappingEngine Engine, List<MidiEvent> Messages, ManualTime Clock) Create(InstrumentConfiguration? configuration = null)
    {
        var clock = new ManualTime();
        var engine = new MappingEngine(configuration ?? TestSettings, clock);
        var messages = new List<MidiEvent>();
        engine.MidiGenerated += messages.Add;
        engine.Process(new(GamepadEventKind.Selected, Device));
        return (engine, messages, clock);
    }
    private static void Set(MappingEngine engine, PhysicalControl control, float value) => engine.Process(new(GamepadEventKind.ControlChanged, Device, control, value));
    private static void Tick(MappingEngine engine) => engine.Process(new(GamepadEventKind.Tick, Device));
    private sealed class ManualTime : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => timestamp;
        public void Advance(int milliseconds) => timestamp += milliseconds;
    }
}
