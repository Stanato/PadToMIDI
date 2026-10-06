using PadToMIDI.Core.Configuration;

namespace PadToMIDI.Core.Musical;

internal static class AnalogResponse
{
    public static double Signed(float input, AnalogResponseConfiguration configuration)
    {
        double magnitude = Math.Abs(input);
        if (magnitude <= configuration.DeadZone) return 0;
        double shaped = Shape((magnitude - configuration.DeadZone) / (1 - configuration.DeadZone), configuration);
        return Math.CopySign(shaped, configuration.Invert ? -input : input);
    }

    public static double Trigger(float input, AftertouchConfiguration configuration)
    {
        if (!configuration.Enabled || input <= configuration.Response.DeadZone) return 0;
        double level = (input - configuration.Response.DeadZone) / (1 - configuration.Response.DeadZone);
        if (level <= configuration.MinimumThreshold) return 0;
        return Shape((level - configuration.MinimumThreshold) / (1 - configuration.MinimumThreshold), configuration.Response);
    }

    private static double Shape(double value, AnalogResponseConfiguration configuration)
    {
        value = configuration.Curve switch
        {
            ResponseCurve.Quadratic => value * value,
            ResponseCurve.SquareRoot => Math.Sqrt(value),
            _ => value
        };
        return Math.Clamp(value * configuration.Sensitivity, 0, 1);
    }
}

internal struct AnalogSmoother
{
    private double value;
    private double previousTarget;
    private double timestamp;
    private bool initialized;

    public double Sample(double target, double now, double milliseconds)
    {
        if (!initialized) { timestamp = now; initialized = true; }
        double elapsed = Math.Max(0, now - timestamp);
        timestamp = now;
        // Neutral bypasses smoothing, preventing a long pitch/pressure tail after physical release.
        if (target == 0 || milliseconds == 0) value = target;
        else value += (previousTarget - value) * (1 - Math.Exp(-elapsed / milliseconds));
        previousTarget = target;
        if (Math.Abs(target - value) < 0.00001) value = target;
        return value;
    }
}

internal struct MidiValueGate
{
    public bool HasValue { get; private set; }
    public int Value { get; private set; }
    private double timestamp;

    public bool Accept(int value, double now, double rateHz, bool immediate = false)
    {
        if (HasValue && Value == value) return false;
        if (!immediate && HasValue && rateHz > 0 && now - timestamp < 1000 / rateHz) return false;
        HasValue = true;
        Value = value;
        timestamp = now;
        return true;
    }
}
