using System.Collections.Immutable;

namespace PadToMIDI.Core.Musical;

public static class ScalePresets
{
    public static ScaleDefinition Major { get; } = new("Major", [0, 2, 4, 5, 7, 9, 11]);
    public static ScaleDefinition NaturalMinor { get; } = new("Natural Minor", [0, 2, 3, 5, 7, 8, 10]);
    public static ScaleDefinition HarmonicMinor { get; } = new("Harmonic Minor", [0, 2, 3, 5, 7, 8, 11]);
    /// <summary>Ascending melodic minor; the same intervals are used in either playing direction.</summary>
    public static ScaleDefinition MelodicMinor { get; } = new("Melodic Minor", [0, 2, 3, 5, 7, 9, 11]);
    public static ScaleDefinition Dorian { get; } = new("Dorian", [0, 2, 3, 5, 7, 9, 10]);
    public static ScaleDefinition Phrygian { get; } = new("Phrygian", [0, 1, 3, 5, 7, 8, 10]);
    public static ScaleDefinition Lydian { get; } = new("Lydian", [0, 2, 4, 6, 7, 9, 11]);
    public static ScaleDefinition Mixolydian { get; } = new("Mixolydian", [0, 2, 4, 5, 7, 9, 10]);
    public static ScaleDefinition Locrian { get; } = new("Locrian", [0, 1, 3, 5, 6, 8, 10]);
    public static ScaleDefinition MajorPentatonic { get; } = new("Major Pentatonic", [0, 2, 4, 7, 9]);
    public static ScaleDefinition MinorPentatonic { get; } = new("Minor Pentatonic", [0, 3, 5, 7, 10]);
    public static ScaleDefinition Blues { get; } = new("Blues", [0, 3, 5, 6, 7, 10]);
    public static ScaleDefinition Chromatic { get; } = new("Chromatic", [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]);

    public static ImmutableArray<ScaleDefinition> All { get; } =
    [Major, NaturalMinor, HarmonicMinor, MelodicMinor, Dorian, Phrygian, Lydian, Mixolydian,
        Locrian, MajorPentatonic, MinorPentatonic, Blues, Chromatic];
}
