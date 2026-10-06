using PadToMIDI.Core.Input;
using PadToMIDI.Core.Musical;

namespace PadToMIDI.Core.Configuration;

public enum ChordShape { ScaleTriad, Major, Minor, Diminished, Augmented }

public sealed record ChordConfiguration
{
    public bool Enabled { get; init; }
    public PhysicalControl? ToggleButton { get; init; } = PhysicalControl.Start;
    /// <summary>Null uses the selected melody scale. An explicit harmony scale shares its tonic.</summary>
    public ScaleDefinition? HarmonyScale { get; init; }
    public ChordShape Shape { get; init; }

    public void Validate()
    {
        if (!Enum.IsDefined(Shape)) throw new ArgumentOutOfRangeException(nameof(Shape));
        if (ToggleButton is { } button && (!Enum.IsDefined(button) || button >= PhysicalControl.LeftTrigger))
            throw new ArgumentException("Chord toggle must be a physical button.");
        if (HarmonyScale is { Intervals.Length: not 7 })
            throw new ArgumentException("Choose a seven-note harmony scale, or an explicit chord shape.");
    }
}
