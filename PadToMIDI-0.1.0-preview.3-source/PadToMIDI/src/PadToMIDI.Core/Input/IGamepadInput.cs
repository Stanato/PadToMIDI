namespace PadToMIDI.Core.Input;

public interface IGamepadInput
{
    /// <summary>
    /// Runs synchronously on the input owner thread. Subscribers must be fast,
    /// nonblocking, and must not touch UI controls. A mapping engine subscribes here.
    /// </summary>
    event Action<GamepadInputEvent>? InputReceived;

    /// <summary>Runs until cancellation; a platform backend may require the process main thread.</summary>
    void Run(CancellationToken cancellationToken);

    /// <summary>Queues selection for the input owner thread. Null deselects all devices.</summary>
    void SelectDevice(GamepadDeviceId? deviceId);

    /// <summary>Thread-safe observation, intentionally separate from input event delivery.</summary>
    GamepadInputSnapshot CaptureSnapshot();
}
