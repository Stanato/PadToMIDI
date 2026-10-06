namespace PadToMIDI.Core.Input;

/// <summary>A value snapshot: no mutable arrays and no allocation per analog event.</summary>
public readonly record struct GamepadValues(
    ulong Buttons = 0,
    float LeftTrigger = 0,
    float RightTrigger = 0,
    float LeftStickX = 0,
    float LeftStickY = 0,
    float RightStickX = 0,
    float RightStickY = 0)
{
    public bool IsPressed(PhysicalControl control) =>
        control < PhysicalControl.LeftTrigger && (Buttons & (1UL << (int)control)) != 0;

    public float GetValue(PhysicalControl control) => control switch
    {
        PhysicalControl.LeftTrigger => LeftTrigger,
        PhysicalControl.RightTrigger => RightTrigger,
        PhysicalControl.LeftStickX => LeftStickX,
        PhysicalControl.LeftStickY => LeftStickY,
        PhysicalControl.RightStickX => RightStickX,
        PhysicalControl.RightStickY => RightStickY,
        _ => IsPressed(control) ? 1 : 0
    };

    public GamepadValues WithValue(PhysicalControl control, float value)
    {
        if (!Enum.IsDefined(control))
            throw new ArgumentOutOfRangeException(nameof(control));
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value));

        return control switch
        {
            PhysicalControl.LeftTrigger => this with { LeftTrigger = Math.Clamp(value, 0, 1) },
            PhysicalControl.RightTrigger => this with { RightTrigger = Math.Clamp(value, 0, 1) },
            PhysicalControl.LeftStickX => this with { LeftStickX = Math.Clamp(value, -1, 1) },
            PhysicalControl.LeftStickY => this with { LeftStickY = Math.Clamp(value, -1, 1) },
            PhysicalControl.RightStickX => this with { RightStickX = Math.Clamp(value, -1, 1) },
            PhysicalControl.RightStickY => this with { RightStickY = Math.Clamp(value, -1, 1) },
            _ => this with
            {
                Buttons = value > 0.5f
                    ? Buttons | (1UL << (int)control)
                    : Buttons & ~(1UL << (int)control)
            }
        };
    }
}
