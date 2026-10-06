using PadToMIDI.Core.Musical;
using Xunit;

namespace PadToMIDI.Core.Tests;

public sealed class ScaleDefinitionTests
{
    [Theory]
    [InlineData(1, 48)]
    [InlineData(2, 50)]
    [InlineData(3, 52)]
    [InlineData(4, 53)]
    [InlineData(5, 55)]
    [InlineData(6, 57)]
    [InlineData(7, 59)]
    [InlineData(8, 60)]
    [InlineData(15, 72)]
    public void MajorDegreesWrapIntoFollowingOctaves(int degree, byte expected)
    {
        Assert.True(ScalePresets.Major.TryGetMidiNote(PitchClass.C, 3, degree, out byte actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RootTranspositionPreservesScaleIntervals()
    {
        Assert.True(ScalePresets.Major.TryGetMidiNote(PitchClass.B, 3, 2, out byte actual));
        Assert.Equal(61, actual); // C#4, not an offset wrapped back into octave 3.
    }

    [Fact]
    public void CustomScaleWithDifferentLengthWrapsByItsOwnDegreeCount()
    {
        var pentatonic = new ScaleDefinition("Custom pentatonic", [0, 2, 4, 7, 9]);
        Assert.True(pentatonic.TryGetMidiNote(PitchClass.C, 3, 6, out byte actual));
        Assert.Equal(60, actual);
        Assert.True(pentatonic.TryGetMidiNote(PitchClass.C, 3, 8, out actual));
        Assert.Equal(64, actual);
    }

    [Theory]
    [InlineData(-1, 1, true, 0)]
    [InlineData(9, 5, true, 127)]
    [InlineData(9, 6, false, 0)]
    [InlineData(3, int.MaxValue, false, 0)]
    public void MidiBoundsAreExplicitWithoutIntegerOverflow(int octave, int degree, bool playable, byte expected)
    {
        Assert.Equal(playable, ScalePresets.Major.TryGetMidiNote(PitchClass.C, octave, degree, out byte actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RejectsEmptyUnrootedDuplicateUnsortedAndOutOfOctaveScales()
    {
        Assert.Throws<ArgumentException>(() => new ScaleDefinition("Empty", []));
        Assert.Throws<ArgumentException>(() => new ScaleDefinition("Unrooted", [2, 4]));
        Assert.Throws<ArgumentException>(() => new ScaleDefinition("Duplicate", [0, 2, 2]));
        Assert.Throws<ArgumentException>(() => new ScaleDefinition("Unsorted", [0, 4, 2]));
        Assert.Throws<ArgumentException>(() => new ScaleDefinition("Outside", [0, 12]));
        Assert.Throws<ArgumentException>(() => new ScaleDefinition(" ", [0]));
    }

    [Fact]
    public void RejectsInvalidRootOctaveAndDegree()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScalePresets.Major.TryGetMidiNote((PitchClass)12, 3, 1, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScalePresets.Major.TryGetMidiNote(PitchClass.C, 10, 1, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScalePresets.Major.TryGetMidiNote(PitchClass.C, 3, 0, out _));
    }
}
