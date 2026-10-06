using PadToMIDI.Core.Input;
using Xunit;

namespace PadToMIDI.Core.Tests;

public sealed class GamepadStateBufferTests
{
    private static readonly GamepadDeviceId First = new(1);
    private static readonly GamepadDeviceId Second = new(2);

    [Fact]
    public void StartsWithEmptyNeutralState()
    {
        var snapshot = new GamepadStateBuffer().CaptureSnapshot();
        Assert.Empty(snapshot.Devices);
        Assert.Null(snapshot.SelectedDeviceId);
        Assert.Equal(default, snapshot.Values);
        Assert.Equal(InputStatus.Starting, snapshot.Status);
    }

    [Fact]
    public void MultipleDevicesAreVisibleWithoutChangingSelection()
    {
        var state = new GamepadStateBuffer();
        state.SetDevices([new(First, "Generic one"), new(Second, "Generic two")]);
        state.Apply(new(GamepadEventKind.Selected, Second));
        var snapshot = state.CaptureSnapshot();
        Assert.Equal(2, snapshot.Devices.Length);
        Assert.Equal(Second, snapshot.SelectedDeviceId);
    }

    [Fact]
    public void InputFromUnselectedDeviceCannotAffectMonitor()
    {
        var state = SelectedState();
        state.Apply(new(GamepadEventKind.ControlChanged, Second, PhysicalControl.FaceSouth, 1));
        Assert.False(state.CaptureSnapshot().Values.IsPressed(PhysicalControl.FaceSouth));
        Assert.Equal(0, state.CaptureSnapshot().EventCount);
    }

    [Fact]
    public void TracksSimultaneousButtonsAndIndependentAxes()
    {
        var state = SelectedState();
        state.Apply(new(GamepadEventKind.ControlChanged, First, PhysicalControl.DPadUp, 1));
        state.Apply(new(GamepadEventKind.ControlChanged, First, PhysicalControl.DPadLeft, 1));
        state.Apply(new(GamepadEventKind.ControlChanged, First, PhysicalControl.RightStickX, -1));
        state.Apply(new(GamepadEventKind.ControlChanged, First, PhysicalControl.LeftTrigger, 0.7f));
        var values = state.CaptureSnapshot().Values;
        Assert.True(values.IsPressed(PhysicalControl.DPadUp));
        Assert.True(values.IsPressed(PhysicalControl.DPadLeft));
        Assert.Equal(-1, values.RightStickX);
        Assert.Equal(0.7f, values.LeftTrigger);
    }

    [Fact]
    public void ReleaseClearsOnlyItsPhysicalControl()
    {
        var state = SelectedState();
        state.Apply(new(GamepadEventKind.ControlChanged, First, PhysicalControl.FaceSouth, 1));
        state.Apply(new(GamepadEventKind.ControlChanged, First, PhysicalControl.LeftBumper, 1));
        state.Apply(new(GamepadEventKind.ControlChanged, First, PhysicalControl.FaceSouth, 0));
        Assert.False(state.CaptureSnapshot().Values.IsPressed(PhysicalControl.FaceSouth));
        Assert.True(state.CaptureSnapshot().Values.IsPressed(PhysicalControl.LeftBumper));
    }

    [Fact]
    public void SnapshotDoesNotChangeWhenLaterEventsArrive()
    {
        var state = SelectedState();
        var before = state.CaptureSnapshot();
        state.Apply(new(GamepadEventKind.ControlChanged, First, PhysicalControl.FaceSouth, 1));
        Assert.False(before.Values.IsPressed(PhysicalControl.FaceSouth));
        Assert.True(state.CaptureSnapshot().Values.IsPressed(PhysicalControl.FaceSouth));
    }

    [Fact]
    public void DisconnectClearsAllInputAndSelection()
    {
        var state = ActiveState();
        state.Apply(new(GamepadEventKind.Disconnected, First));
        var snapshot = state.CaptureSnapshot();
        Assert.Null(snapshot.SelectedDeviceId);
        Assert.Equal(default, snapshot.Values);
        Assert.Equal(InputStatus.Ready, snapshot.Status);
    }

    [Fact]
    public void DisconnectOfOtherDeviceDoesNotClearSelectedController()
    {
        var state = ActiveState();
        state.Apply(new(GamepadEventKind.Disconnected, Second));
        Assert.Equal(First, state.CaptureSnapshot().SelectedDeviceId);
        Assert.True(state.CaptureSnapshot().Values.IsPressed(PhysicalControl.FaceSouth));
    }

    [Fact]
    public void SwitchingDevicesResetsOldInputAndIgnoresLateOldEvents()
    {
        var state = ActiveState();
        state.Apply(new(GamepadEventKind.Selected, Second));
        state.Apply(new(GamepadEventKind.ControlChanged, First, PhysicalControl.FaceSouth, 1));
        Assert.Equal(Second, state.CaptureSnapshot().SelectedDeviceId);
        Assert.Equal(default, state.CaptureSnapshot().Values);
    }

    [Theory]
    [InlineData(GamepadEventKind.Faulted, InputStatus.Faulted)]
    [InlineData(GamepadEventKind.Stopped, InputStatus.Stopped)]
    public void TerminalEventsResetAllInput(GamepadEventKind kind, InputStatus expected)
    {
        var state = ActiveState();
        state.Apply(new(kind, First));
        var snapshot = state.CaptureSnapshot();
        Assert.Equal(default, snapshot.Values);
        Assert.Null(snapshot.SelectedDeviceId);
        Assert.Equal(expected, snapshot.Status);
    }

    [Fact]
    public async Task ConcurrentObservationSeesCompleteValueSnapshots()
    {
        var state = SelectedState();
        var writer = Task.Run(() =>
        {
            for (int i = 0; i < 10_000; i++)
                state.Apply(new(GamepadEventKind.ControlChanged, First, PhysicalControl.RightStickX, i % 2 == 0 ? -1 : 1));
        }, TestContext.Current.CancellationToken);
        for (int i = 0; i < 10_000; i++)
        {
            var snapshot = state.CaptureSnapshot();
            Assert.Equal(First, snapshot.SelectedDeviceId);
            Assert.InRange(snapshot.Values.RightStickX, -1, 1);
        }
        await writer;
        Assert.Equal(10_000, state.CaptureSnapshot().EventCount);
    }

    private static GamepadStateBuffer SelectedState()
    {
        var state = new GamepadStateBuffer();
        state.Apply(new(GamepadEventKind.Selected, First));
        return state;
    }

    private static GamepadStateBuffer ActiveState()
    {
        var state = SelectedState();
        state.Apply(new(GamepadEventKind.ControlChanged, First, PhysicalControl.FaceSouth, 1));
        state.Apply(new(GamepadEventKind.ControlChanged, First, PhysicalControl.LeftTrigger, 0.8f));
        state.Apply(new(GamepadEventKind.ControlChanged, First, PhysicalControl.RightStickX, -0.5f));
        return state;
    }
}
