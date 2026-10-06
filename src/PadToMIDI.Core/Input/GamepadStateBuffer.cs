using System.Collections.Immutable;

namespace PadToMIDI.Core.Input;

/// <summary>
/// Small, locked observation buffer. UI sampling never consumes or queues input events.
/// This is controller state only; musical held-note state belongs to the mapping engine.
/// </summary>
public sealed class GamepadStateBuffer
{
    private readonly object gate = new();
    private ImmutableArray<GamepadDevice> devices = [];
    private GamepadDeviceId? selected;
    private GamepadValues values;
    private InputStatus status = InputStatus.Starting;
    private string message = "Starting SDL3…";
    private long eventCount;

    public void SetDevices(ImmutableArray<GamepadDevice> detected)
    {
        lock (gate)
            devices = detected;
    }

    public void SetStatus(InputStatus newStatus, string detail)
    {
        lock (gate)
        {
            status = newStatus;
            message = detail;
        }
    }

    public void Apply(GamepadInputEvent input)
    {
        lock (gate)
        {
            switch (input.Kind)
            {
                case GamepadEventKind.Selected:
                    selected = input.DeviceId;
                    values = default;
                    status = InputStatus.Connected;
                    message = "Controller connected. Input monitoring is active.";
                    break;
                case GamepadEventKind.ControlChanged when selected == input.DeviceId:
                    values = values.WithValue(input.Control, input.Value);
                    eventCount++;
                    break;
                case GamepadEventKind.Disconnected when selected == input.DeviceId:
                    selected = null;
                    values = default;
                    status = InputStatus.Ready;
                    message = "Controller disconnected or deselected. Select a controller to continue.";
                    break;
                case GamepadEventKind.Faulted:
                case GamepadEventKind.Stopped:
                    selected = null;
                    values = default;
                    status = input.Kind == GamepadEventKind.Faulted ? InputStatus.Faulted : InputStatus.Stopped;
                    break;
            }
        }
    }

    public GamepadInputSnapshot CaptureSnapshot()
    {
        lock (gate)
            return new(devices, selected, values, status, message, eventCount);
    }
}
