using PadToMIDI.Core.Configuration;

namespace PadToMIDI.Core.Musical;

/// <summary>Hysteresis prevents repeats and diagonal cascades from separately reported axis events.</summary>
internal struct DiscreteStickLatch
{
    public StickDirection? Direction { get; private set; }

    public StickDirection? Update(float x, float y, DiscreteStickConfiguration configuration)
    {
        float absoluteX = Math.Abs(x), absoluteY = Math.Abs(y);
        if (absoluteX <= configuration.DeadZone && absoluteY <= configuration.DeadZone)
        {
            Direction = null;
            return null;
        }
        if (Direction is not null || Math.Max(absoluteX, absoluteY) < configuration.Threshold) return null;
        Direction = absoluteY >= absoluteX
            ? (y < 0 ? StickDirection.Up : StickDirection.Down)
            : (x < 0 ? StickDirection.Left : StickDirection.Right);
        return Direction;
    }
}
