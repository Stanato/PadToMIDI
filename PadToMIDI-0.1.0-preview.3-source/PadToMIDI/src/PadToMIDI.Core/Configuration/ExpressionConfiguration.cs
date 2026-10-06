using PadToMIDI.Core.Input;

namespace PadToMIDI.Core.Configuration;

public enum ResponseCurve { Linear, Quadratic, SquareRoot }
public enum TriggerExpressionMode { PolyphonicPressure }

public sealed record AnalogResponseConfiguration
{
    public float DeadZone { get; init; } = 0.05f;
    public ResponseCurve Curve { get; init; }
    public float Sensitivity { get; init; } = 1;
    public bool Invert { get; init; }
    public double SmoothingMilliseconds { get; init; }
    /// <summary>Zero disables rate limiting. Neutral resets always bypass the limit.</summary>
    public double UpdateRateHz { get; init; } = 60;

    public void Validate()
    {
        if (!float.IsFinite(DeadZone) || DeadZone is < 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(DeadZone));
        if (!Enum.IsDefined(Curve)) throw new ArgumentOutOfRangeException(nameof(Curve));
        if (!float.IsFinite(Sensitivity) || Sensitivity is <= 0 or > 10) throw new ArgumentOutOfRangeException(nameof(Sensitivity));
        if (!double.IsFinite(SmoothingMilliseconds) || SmoothingMilliseconds is < 0 or > 5000)
            throw new ArgumentOutOfRangeException(nameof(SmoothingMilliseconds));
        if (!double.IsFinite(UpdateRateHz) || UpdateRateHz is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(UpdateRateHz));
    }
}

public sealed record AftertouchConfiguration
{
    public bool Enabled { get; init; } = true;
    public PhysicalControl Source { get; init; } = PhysicalControl.LeftTrigger;
    public TriggerExpressionMode Mode { get; init; }
    /// <summary>Minimum activation level after dead-zone removal, before applying the response curve.</summary>
    public float MinimumThreshold { get; init; } = 0.02f;
    public AnalogResponseConfiguration Response { get; init; } = new();

    public void Validate()
    {
        if (Source is not (PhysicalControl.LeftTrigger or PhysicalControl.RightTrigger)) throw new ArgumentOutOfRangeException(nameof(Source));
        if (!Enum.IsDefined(Mode)) throw new ArgumentOutOfRangeException(nameof(Mode));
        if (!float.IsFinite(MinimumThreshold) || MinimumThreshold is < 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(MinimumThreshold));
        ArgumentNullException.ThrowIfNull(Response);
        Response.Validate();
        if (Response.Invert) throw new ArgumentException("Inversion is only supported for stick expression.", nameof(Response));
    }
}

public sealed record PitchBendConfiguration
{
    public bool Enabled { get; init; } = true;
    public PhysicalControl Source { get; init; } = PhysicalControl.RightStickX;
    public AnalogResponseConfiguration Response { get; init; } = new() { DeadZone = 0.08f };

    public void Validate()
    {
        ValidateAxis(Source);
        ArgumentNullException.ThrowIfNull(Response);
        Response.Validate();
    }

    internal static void ValidateAxis(PhysicalControl source)
    {
        if (source is not (PhysicalControl.LeftStickX or PhysicalControl.LeftStickY or PhysicalControl.RightStickX or PhysicalControl.RightStickY))
            throw new ArgumentOutOfRangeException(nameof(source));
    }
}

public sealed record TimbreConfiguration
{
    public bool Enabled { get; init; } = true;
    public PhysicalControl Source { get; init; } = PhysicalControl.RightStickY;
    public byte ControllerNumber { get; init; } = 74;
    public AnalogResponseConfiguration Response { get; init; } = new() { DeadZone = 0.08f, Invert = true };

    public void Validate()
    {
        PitchBendConfiguration.ValidateAxis(Source);
        // 120..127 are channel-mode commands, not continuous expression controllers.
        if (ControllerNumber > 119) throw new ArgumentOutOfRangeException(nameof(ControllerNumber));
        ArgumentNullException.ThrowIfNull(Response);
        Response.Validate();
    }
}

public sealed record ExpressionConfiguration
{
    public AftertouchConfiguration DPadAftertouch { get; init; } = new();
    public AftertouchConfiguration FaceAftertouch { get; init; } = new() { Source = PhysicalControl.RightTrigger };
    public PitchBendConfiguration PitchBend { get; init; } = new();
    public TimbreConfiguration Timbre { get; init; } = new();

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(DPadAftertouch);
        ArgumentNullException.ThrowIfNull(FaceAftertouch);
        ArgumentNullException.ThrowIfNull(PitchBend);
        ArgumentNullException.ThrowIfNull(Timbre);
        DPadAftertouch.Validate(); FaceAftertouch.Validate(); PitchBend.Validate(); Timbre.Validate();
    }
}
