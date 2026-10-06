using PadToMIDI.Core.Input;
using PadToMIDI.Core.Midi;
using PadToMIDI.Core.Musical;
using Xunit;

namespace PadToMIDI.Core.Tests;

public sealed class MidiInstrumentTests
{
    private static readonly GamepadDeviceId Device = new(7);
    private static void Select(MidiInstrument instrument) => instrument.Process(new(GamepadEventKind.Selected, Device));
    private static void Button(MidiInstrument instrument, PhysicalControl control, bool down) =>
        instrument.Process(new(GamepadEventKind.ControlChanged, Device, control, down ? 1 : 0));

    [Fact]
    public async Task OutputSwitchReleasesAndDrainsOldDestinationBeforeConnectingNewDestination()
    {
        var output = new FakeOutput();
        using var instrument = new MidiInstrument(output);
        await instrument.ConnectOutputAsync("old", TestContext.Current.CancellationToken);
        Select(instrument);
        Button(instrument, PhysicalControl.FaceSouth, true);
        await instrument.ConnectOutputAsync("new", TestContext.Current.CancellationToken);
        Assert.Contains("old:NoteOn:0:55:100", output.Log);
        int off = output.Log.IndexOf("old:NoteOff:0:55:0");
        int drain = output.Log.IndexOf("flush", off);
        int disconnect = output.Log.IndexOf("disconnect:old", drain);
        int connect = output.Log.IndexOf("connect:new", disconnect);
        Assert.True(off >= 0 && off < drain && drain < disconnect && disconnect < connect);
        Assert.Empty(instrument.CaptureSnapshot().HeldNotes);
        Button(instrument, PhysicalControl.FaceSouth, false);
        Assert.DoesNotContain("new:NoteOff:0:55:0", output.Log);
        Button(instrument, PhysicalControl.FaceSouth, true);
        Assert.Contains("new:NoteOn:0:55:100", output.Log);
    }

    [Fact]
    public async Task ChannelChangesRetainOriginalChannelForHeldNoteOff()
    {
        var output = new FakeOutput();
        using var instrument = new MidiInstrument(output);
        await instrument.ConnectOutputAsync("synth", TestContext.Current.CancellationToken);
        Select(instrument);
        Button(instrument, PhysicalControl.FaceSouth, true);
        instrument.SetMidiChannel(15);
        Button(instrument, PhysicalControl.FaceEast, true);
        Button(instrument, PhysicalControl.FaceSouth, false);
        Assert.Contains("synth:NoteOff:0:55:0", output.Log);
        Assert.Contains("synth:NoteOn:15:60:100", output.Log);
        Assert.Throws<ArgumentOutOfRangeException>(() => instrument.SetMidiChannel(16));
    }

    [Fact]
    public async Task SwitchingOutputReleasesModifiedNoteAndClearsMomentaryModifiers()
    {
        var output = new FakeOutput();
        using var instrument = new MidiInstrument(output);
        await instrument.ConnectOutputAsync("old", TestContext.Current.CancellationToken);
        Select(instrument);
        instrument.Process(new(GamepadEventKind.ControlChanged, Device, PhysicalControl.LeftStickY, -1));
        Button(instrument, PhysicalControl.RightBumper, true);
        Button(instrument, PhysicalControl.FaceSouth, true);
        await instrument.ConnectOutputAsync("new", TestContext.Current.CancellationToken);
        Assert.Contains("old:NoteOff:0:68:0", output.Log);
        var state = instrument.CaptureSnapshot();
        Assert.Empty(state.HeldNotes);
        Assert.Equal(3, state.BaseOctave);
        Assert.Equal(0, state.TemporaryOctaveOffset);
        Assert.Equal(0, state.TemporarySemitoneOffset);
        Assert.Null(state.LeftStickLatch);
        Button(instrument, PhysicalControl.FaceSouth, false);
        Button(instrument, PhysicalControl.FaceSouth, true);
        Assert.Contains("new:NoteOn:0:55:100", output.Log);
    }

    [Fact]
    public async Task PanicStopsModifiedPitchAndClearsMomentaryModifiers()
    {
        var output = new FakeOutput();
        using var instrument = new MidiInstrument(output);
        await instrument.ConnectOutputAsync("synth", TestContext.Current.CancellationToken);
        Select(instrument);
        instrument.Process(new(GamepadEventKind.ControlChanged, Device, PhysicalControl.LeftStickY, -1));
        Button(instrument, PhysicalControl.LeftBumper, true);
        Button(instrument, PhysicalControl.FaceSouth, true);
        instrument.Panic();
        Assert.Contains("synth:NoteOff:0:66:0", output.Log);
        Assert.Contains("synth:PitchBend:0:0:8192", output.Log);
        var state = instrument.CaptureSnapshot();
        Assert.Empty(state.HeldNotes);
        Assert.Equal(3, state.BaseOctave);
        Assert.Equal(0, state.TemporaryOctaveOffset);
        Assert.Equal(0, state.TemporarySemitoneOffset);
        Assert.Null(state.LeftStickLatch);
        Assert.Null(state.LastNote);
    }

    [Theory]
    [InlineData(GamepadEventKind.Disconnected)]
    [InlineData(GamepadEventKind.Stopped)]
    [InlineData(GamepadEventKind.Faulted)]
    public async Task InputLossStopsAllNotesAndResetsControllers(GamepadEventKind kind)
    {
        var output = new FakeOutput();
        using var instrument = new MidiInstrument(output);
        await instrument.ConnectOutputAsync("synth", TestContext.Current.CancellationToken);
        Select(instrument);
        Button(instrument, PhysicalControl.DPadDown, true);
        Button(instrument, PhysicalControl.FaceSouth, true);
        instrument.Process(new(kind, Device));
        Assert.Contains("synth:NoteOff:0:48:0", output.Log);
        Assert.Contains("synth:NoteOff:0:55:0", output.Log);
        Assert.Contains("synth:ControlChange:0:123:0", output.Log);
        Assert.Contains("synth:ChannelPressure:0:0:0", output.Log);
        Assert.Contains("synth:PitchBend:0:0:8192", output.Log);
        Assert.Empty(instrument.CaptureSnapshot().HeldNotes);
    }

    [Fact]
    public async Task OtherControllerDisconnectDoesNotStopSelectedNotes()
    {
        var output = new FakeOutput();
        using var instrument = new MidiInstrument(output);
        await instrument.ConnectOutputAsync("synth", TestContext.Current.CancellationToken);
        Select(instrument);
        Button(instrument, PhysicalControl.DPadDown, true);
        int count = output.Log.Count;
        instrument.Process(new(GamepadEventKind.Disconnected, new(8)));
        Assert.Equal(count, output.Log.Count);
        Assert.Single(instrument.CaptureSnapshot().HeldNotes);
    }

    [Fact]
    public async Task RejectedSubmissionClearsStateAndRequiresReconnect()
    {
        var output = new FakeOutput();
        using var instrument = new MidiInstrument(output);
        await instrument.ConnectOutputAsync("synth", TestContext.Current.CancellationToken);
        Select(instrument);
        output.Reject = true;
        Button(instrument, PhysicalControl.FaceSouth, true);
        Assert.Empty(instrument.CaptureSnapshot().HeldNotes);
        output.Reject = false;
        int count = output.Log.Count;
        Button(instrument, PhysicalControl.FaceEast, true);
        Assert.Equal(count, output.Log.Count);
        await instrument.ConnectOutputAsync("synth", TestContext.Current.CancellationToken);
        Button(instrument, PhysicalControl.FaceEast, false);
        Button(instrument, PhysicalControl.FaceEast, true);
        Assert.Contains("synth:NoteOn:0:60:100", output.Log);
    }

    [Fact]
    public async Task BackendFaultCanBeConsumedWithoutAnotherInputEvent()
    {
        var output = new FakeOutput();
        using var instrument = new MidiInstrument(output);
        await instrument.ConnectOutputAsync("synth", TestContext.Current.CancellationToken);
        Select(instrument);
        Button(instrument, PhysicalControl.FaceSouth, true);
        output.Fail();
        Assert.Empty(instrument.CaptureSnapshot().HeldNotes);
    }

    [Fact]
    public async Task PanicAndDisposeAreIdempotentAndAllowFreshPresses()
    {
        var output = new FakeOutput();
        var instrument = new MidiInstrument(output);
        await instrument.ConnectOutputAsync("synth", TestContext.Current.CancellationToken);
        Select(instrument);
        Button(instrument, PhysicalControl.FaceSouth, true);
        instrument.Panic();
        instrument.Panic();
        Assert.Single(output.Log, entry => entry == "synth:NoteOff:0:55:0");
        Button(instrument, PhysicalControl.FaceSouth, false);
        Button(instrument, PhysicalControl.FaceSouth, true);
        instrument.Dispose();
        instrument.Dispose();
        Assert.Equal(2, output.Log.Count(entry => entry == "synth:NoteOff:0:55:0"));
    }

    [Fact]
    public async Task ReconnectConsumesOldFaultEvenIfNoObserverTickOccurred()
    {
        var output = new FakeOutput();
        using var instrument = new MidiInstrument(output);
        await instrument.ConnectOutputAsync("synth", TestContext.Current.CancellationToken);
        Select(instrument);
        Button(instrument, PhysicalControl.FaceSouth, true);
        output.Fail();
        await instrument.ConnectOutputAsync("synth", TestContext.Current.CancellationToken);
        instrument.CaptureSnapshot();
        Button(instrument, PhysicalControl.FaceSouth, false);
        Button(instrument, PhysicalControl.FaceSouth, true);
        Assert.Equal(2, output.Log.Count(entry => entry == "synth:NoteOn:0:55:100"));
    }

    [Fact]
    public async Task InputsDuringTransitionAreNotReplayedToNewOutput()
    {
        var output = new FakeOutput();
        using var instrument = new MidiInstrument(output);
        await instrument.ConnectOutputAsync("old", TestContext.Current.CancellationToken);
        Select(instrument);
        output.FlushBarrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var switching = instrument.ConnectOutputAsync("new", TestContext.Current.CancellationToken).AsTask();
        Button(instrument, PhysicalControl.FaceSouth, true);
        output.FlushBarrier.SetResult();
        await switching;
        Assert.DoesNotContain(output.Log, entry => entry.Contains(":NoteOn:"));
        Assert.Empty(instrument.CaptureSnapshot().HeldNotes);
    }

    [Fact]
    public async Task FailedConnectLeavesStateClearAndInputPreviewUsable()
    {
        var output = new FakeOutput { ThrowConnect = true };
        using var instrument = new MidiInstrument(output);
        Select(instrument);
        await Assert.ThrowsAsync<InvalidOperationException>(() => instrument.ConnectOutputAsync("missing", TestContext.Current.CancellationToken).AsTask());
        Assert.Empty(instrument.CaptureSnapshot().HeldNotes);
        Button(instrument, PhysicalControl.FaceSouth, true);
        Assert.Single(instrument.CaptureSnapshot().HeldNotes);
        Assert.DoesNotContain(output.Log, entry => entry.Contains(":NoteOn:"));
    }

    [Fact]
    public async Task SwitchingOutputResetsExpressionOnOldOutputAndDoesNotReplayDeflectedControls()
    {
        var output = new FakeOutput();
        using var instrument = new MidiInstrument(output);
        await instrument.ConnectOutputAsync("old", TestContext.Current.CancellationToken);
        Select(instrument);
        Button(instrument, PhysicalControl.FaceSouth, true);
        instrument.Process(new(GamepadEventKind.ControlChanged, Device, PhysicalControl.RightTrigger, 1));
        instrument.Process(new(GamepadEventKind.ControlChanged, Device, PhysicalControl.RightStickX, 1));
        instrument.Process(new(GamepadEventKind.ControlChanged, Device, PhysicalControl.RightStickY, -1));
        await instrument.ConnectOutputAsync("new", TestContext.Current.CancellationToken);
        Assert.Contains("old:PolyphonicPressure:0:55:0", output.Log);
        Assert.Contains("old:ControlChange:0:74:64", output.Log);
        Assert.Contains("old:PitchBend:0:0:8192", output.Log);
        int before = output.Log.Count;
        instrument.Process(new(GamepadEventKind.Tick, Device));
        Assert.Equal(before, output.Log.Count);
        Assert.Equal(new ExpressionStateSnapshot(0, 0, 8192, 64), instrument.CaptureSnapshot().Expression);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PanicOrDisposeResetsTriggerAndStickExpression(bool dispose)
    {
        var output = new FakeOutput();
        using var instrument = new MidiInstrument(output);
        await instrument.ConnectOutputAsync("synth", TestContext.Current.CancellationToken);
        Select(instrument);
        Button(instrument, PhysicalControl.DPadDown, true);
        instrument.Process(new(GamepadEventKind.ControlChanged, Device, PhysicalControl.LeftTrigger, 1));
        instrument.Process(new(GamepadEventKind.ControlChanged, Device, PhysicalControl.RightStickX, -1));
        instrument.Process(new(GamepadEventKind.ControlChanged, Device, PhysicalControl.RightStickY, -1));
        if (dispose) instrument.Dispose(); else instrument.Panic();
        Assert.Contains("synth:PolyphonicPressure:0:48:0", output.Log);
        Assert.Contains("synth:ControlChange:0:74:64", output.Log);
        Assert.Contains("synth:PitchBend:0:0:8192", output.Log);
        Assert.Contains("synth:NoteOff:0:48:0", output.Log);
    }

    private sealed class FakeOutput : IMidiOutput
    {
        private string? id;
        public List<string> Log { get; } = [];
        public bool Reject { get; set; }
        public bool ThrowConnect { get; init; }
        public TaskCompletionSource? FlushBarrier { get; set; }
        public bool IsConnected => id is not null;
        public event Action? Faulted;
        public MidiOutputSnapshot CaptureSnapshot() => new(IsConnected ? MidiOutputState.Connected : MidiOutputState.Ready, "Fake", id);
        public ValueTask<IReadOnlyList<MidiEndpoint>> GetEndpointsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<MidiEndpoint>>([new("synth", "Synth")]);
        public ValueTask ConnectAsync(string endpointId, CancellationToken cancellationToken = default)
        {
            if (ThrowConnect) throw new InvalidOperationException("Unavailable");
            id = endpointId; Log.Add($"connect:{id}"); return ValueTask.CompletedTask;
        }
        public ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
        { Log.Add($"disconnect:{id}"); id = null; return ValueTask.CompletedTask; }
        public ValueTask FlushAsync(CancellationToken cancellationToken = default)
        { Log.Add("flush"); return FlushBarrier is { } barrier ? new(barrier.Task) : ValueTask.CompletedTask; }
        public bool TrySend(in MidiEvent message)
        {
            if (Reject) return false;
            Log.Add($"{id}:{message.Kind}:{message.Channel}:{message.Data1}:{message.Data2}");
            return true;
        }
        public void Fail() { id = null; Faulted?.Invoke(); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
