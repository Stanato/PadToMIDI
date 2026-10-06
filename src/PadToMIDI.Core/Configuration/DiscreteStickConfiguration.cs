using PadToMIDI.Core.Input;

namespace PadToMIDI.Core.Configuration;

public enum StickDirection { Up, Down, Left, Right }
public enum StickAction { None, IncreaseOctave, DecreaseOctave, SharpenLastNote, FlattenLastNote, MomentaryOctaveDown, MomentaryOctaveUp }

/// <summary>One cardinal action per excursion, rearmed only when both axes enter the neutral zone.</summary>
public sealed record DiscreteStickConfiguration
{
    public PhysicalControl XAxis { get; init; } = PhysicalControl.LeftStickX;
    public PhysicalControl YAxis { get; init; } = PhysicalControl.LeftStickY;
    public float Threshold { get; init; } = 0.65f;
    public float DeadZone { get; init; } = 0.25f;
    public StickAction Up { get; init; } = StickAction.MomentaryOctaveUp;
    public StickAction Down { get; init; } = StickAction.MomentaryOctaveDown;
    public StickAction Right { get; init; } = StickAction.IncreaseOctave;
    public StickAction Left { get; init; } = StickAction.DecreaseOctave;

    public StickAction ActionFor(StickDirection direction) => direction switch
    {
        StickDirection.Up => Up,
        StickDirection.Down => Down,
        StickDirection.Right => Right,
        StickDirection.Left => Left,
        _ => throw new ArgumentOutOfRangeException(nameof(direction))
    };

    public void Validate()
    {
        if (XAxis is not (PhysicalControl.LeftStickX or PhysicalControl.RightStickX))
            throw new ArgumentOutOfRangeException(nameof(XAxis));
        if (YAxis is not (PhysicalControl.LeftStickY or PhysicalControl.RightStickY))
            throw new ArgumentOutOfRangeException(nameof(YAxis));
        if (!float.IsFinite(Threshold) || Threshold is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Threshold));
        if (!float.IsFinite(DeadZone) || DeadZone < 0 || DeadZone >= Threshold)
            throw new ArgumentOutOfRangeException(nameof(DeadZone));
        if (!Enum.IsDefined(Up) || !Enum.IsDefined(Down) || !Enum.IsDefined(Left) || !Enum.IsDefined(Right))
            throw new ArgumentException("Unknown discrete stick action.");
    }
}
