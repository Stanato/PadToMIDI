using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Input;
using PadToMIDI.Core.Midi;
using PadToMIDI.Core.Musical;
using Xunit;

namespace PadToMIDI.Core.Tests;

public sealed class ScalePresetTests
{
    public static TheoryData<ScaleDefinition, byte[]> ExpectedDegrees => new()
    {
        { ScalePresets.Major, [48, 50, 52, 53, 55, 57, 59, 60] },
        { ScalePresets.NaturalMinor, [48, 50, 51, 53, 55, 56, 58, 60] },
        { ScalePresets.HarmonicMinor, [48, 50, 51, 53, 55, 56, 59, 60] },
        { ScalePresets.MelodicMinor, [48, 50, 51, 53, 55, 57, 59, 60] },
        { ScalePresets.Dorian, [48, 50, 51, 53, 55, 57, 58, 60] },
        { ScalePresets.Phrygian, [48, 49, 51, 53, 55, 56, 58, 60] },
        { ScalePresets.Lydian, [48, 50, 52, 54, 55, 57, 59, 60] },
        { ScalePresets.Mixolydian, [48, 50, 52, 53, 55, 57, 58, 60] },
        { ScalePresets.Locrian, [48, 49, 51, 53, 54, 56, 58, 60] },
        { ScalePresets.MajorPentatonic, [48, 50, 52, 55, 57, 60, 62, 64] },
        { ScalePresets.MinorPentatonic, [48, 51, 53, 55, 58, 60, 63, 65] },
        { ScalePresets.Blues, [48, 51, 53, 54, 55, 58, 60, 63] },
        { ScalePresets.Chromatic, [48, 49, 50, 51, 52, 53, 54, 55] }
    };

    [Theory]
    [MemberData(nameof(ExpectedDegrees))]
    public void EightButtonsResolveEveryPresetUsingItsDegreeCount(ScaleDefinition scale, byte[] pitches)
    {
        var engine = new MappingEngine(new() { Scale = scale });
        var messages = new List<MidiEvent>();
        engine.MidiGenerated += messages.Add;
        var device = new GamepadDeviceId(1);
        engine.Process(new(GamepadEventKind.Selected, device));
        var buttons = InstrumentConfiguration.DefaultMappings.OrderBy(pair => ((ScaleDegreeAction)pair.Value).Degree).Select(pair => pair.Key).ToArray();
        for (int index = 0; index < buttons.Length; index++)
        {
            engine.Process(new(GamepadEventKind.ControlChanged, device, buttons[index], 1));
            Assert.Equal(new MidiEvent(MidiEventKind.NoteOn, 0, pitches[index], 100), messages[^1]);
            engine.Process(new(GamepadEventKind.ControlChanged, device, buttons[index], 0));
            Assert.Equal(new MidiEvent(MidiEventKind.NoteOff, 0, pitches[index], 0), messages[^1]);
        }
    }

    [Theory]
    [MemberData(nameof(ExpectedDegrees))]
    public void ApplyingEachPresetAndRootRetainsHeldReleaseIdentity(ScaleDefinition scale, byte[] pitches)
    {
        var engine = new MappingEngine();
        var messages = new List<MidiEvent>();
        engine.MidiGenerated += messages.Add;
        var device = new GamepadDeviceId(1);
        engine.Process(new(GamepadEventKind.Selected, device));
        engine.Process(new(GamepadEventKind.ControlChanged, device, PhysicalControl.FaceSouth, 1));
        engine.UpdateConfiguration(new() { Scale = scale, Root = PitchClass.FSharp, BaseOctave = 4, FixedVelocity = 77 });
        engine.Process(new(GamepadEventKind.ControlChanged, device, PhysicalControl.FaceSouth, 0));
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOff, 0, 55, 0), messages[^1]);
        engine.Process(new(GamepadEventKind.ControlChanged, device, PhysicalControl.FaceSouth, 1));
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOn, 0, (byte)(pitches[4] + 18), 77), messages[^1]);
    }

    [Fact]
    public void CatalogIsUniqueAndPresetsWrapForEveryRoot()
    {
        Assert.Equal(13, ScalePresets.All.Length);
        Assert.Equal(13, ScalePresets.All.Select(scale => scale.Name).Distinct().Count());
        foreach (var scale in ScalePresets.All)
        foreach (var root in Enum.GetValues<PitchClass>())
        {
            Assert.True(scale.TryGetMidiNote(root, 3, scale.Intervals.Length + 1, out byte next));
            Assert.Equal(60 + (int)root, next);
        }
    }
}
