namespace PadToMIDI.Core.Input;

public enum GamepadEventKind
{
    Selected,
    ControlChanged,
    Disconnected,
    Stopped,
    Faulted,
    /// <summary>Input-owner heartbeat for settling smoothing and deferred continuous values.</summary>
    Tick
}

/// <summary>
/// Timestamp is Stopwatch ticks. Buttons/triggers are 0..1; sticks are -1..1,
/// with negative X left and negative Y up. Only selected-device input is emitted.
/// </summary>
public readonly record struct GamepadInputEvent(
    GamepadEventKind Kind,
    GamepadDeviceId DeviceId,
    PhysicalControl Control = default,
    float Value = 0,
    long Timestamp = 0);
