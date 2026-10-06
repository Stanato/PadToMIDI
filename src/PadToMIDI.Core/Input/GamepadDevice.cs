namespace PadToMIDI.Core.Input;

/// <summary>A backend-local connection ID, not a durable profile identifier.</summary>
public readonly record struct GamepadDeviceId(uint Value);

public sealed record GamepadDevice(GamepadDeviceId Id, string Name)
{
    public string DisplayName => $"{Name} · #{Id.Value}";
}
