using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Input;
using PadToMIDI.Core.Midi;
using PadToMIDI.Core.Musical;
using PadToMIDI.Core.Profiles;
using Xunit;

namespace PadToMIDI.Core.Tests;

public sealed class ProfileJsonTests
{
    public static TheoryData<ScaleDefinition> Presets => new(ScalePresets.All);

    [Theory]
    [MemberData(nameof(Presets))]
    public void EveryPresetRoundTripsWithoutRuntimeState(ScaleDefinition scale)
    {
        var profile = InstrumentProfile.Default with { Configuration = new() { Scale = scale, Root = PitchClass.FSharp } };
        string json = ProfileJson.Serialize(profile);
        var restored = ProfileJson.Deserialize(json);
        Assert.Equal(json, ProfileJson.Serialize(restored));
        Assert.Equal(scale.Intervals, restored.Configuration.Scale.Intervals);
        Assert.Same(scale, restored.Configuration.Scale);
        Assert.Contains("\"FaceSouth\"", json);
        Assert.Contains("\"type\": \"scaleDegree\"", json);
        Assert.DoesNotContain("heldNotes", json);
    }

    [Fact]
    public void CustomScaleMappingsResponsesAndPreferencesRoundTrip()
    {
        var profile = InstrumentProfile.Default with
        {
            Id = Guid.NewGuid(), Name = "Custom expressive", ControllerName = "Generic Pad", MidiEndpointId = "native-id|group-2", MidiEndpointName = "Loopback",
            Configuration = new()
            {
                Root = PitchClass.B, Scale = new("Custom", [0, 1, 7]), BaseOctave = 6, FixedVelocity = 77, MidiChannel = 15,
                Mappings = InstrumentConfiguration.DefaultMappings.SetItem(PhysicalControl.FaceSouth, new FixedNoteAction(64)),
                TemporaryOctaveModifiers = ImmutableDictionary<PhysicalControl, int>.Empty.Add(PhysicalControl.LeftStickButton, -2),
                LeftStick = new() { Up = StickAction.IncreaseOctave, Down = StickAction.DecreaseOctave, Left = StickAction.FlattenLastNote, Right = StickAction.SharpenLastNote, Threshold = 0.8f, DeadZone = 0.3f },
                Expression = new()
                {
                    DPadAftertouch = new() { Source = PhysicalControl.RightTrigger, MinimumThreshold = 0.2f, Response = new() { Curve = ResponseCurve.SquareRoot, Sensitivity = 2, DeadZone = 0.1f, SmoothingMilliseconds = 15, UpdateRateHz = 99 } },
                    FaceAftertouch = new() { Enabled = false },
                    PitchBend = new() { Source = PhysicalControl.LeftStickX, Response = new() { Invert = true, Curve = ResponseCurve.Quadratic, UpdateRateHz = 0 } },
                    Timbre = new() { ControllerNumber = 11, Enabled = false }
                }
            }
        };
        string json = ProfileJson.Serialize(profile);
        Assert.Equal(json, ProfileJson.Serialize(ProfileJson.Deserialize(json)));
        Assert.Equal(64, Assert.IsType<FixedNoteAction>(ProfileJson.Deserialize(json).Configuration.Mappings[PhysicalControl.FaceSouth]).MidiNote);
    }

    [Theory]
    [InlineData("version", "2")]
    [InlineData("id", "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("name", "null")]
    [InlineData("name", "\"  invalid  \"")]
    [InlineData("configuration", "null")]
    public void InvalidDocumentIsRejected(string property, string value)
    {
        var node = JsonNode.Parse(ProfileJson.Serialize(InstrumentProfile.Default))!;
        node[property] = JsonNode.Parse(value);
        Assert.Throws<JsonException>(() => ProfileJson.Deserialize(node.ToJsonString()));
    }

    [Theory]
    [InlineData("root", "\"Unknown\"")]
    [InlineData("root", "0")]
    [InlineData("midiChannel", "16")]
    [InlineData("fixedVelocity", "0")]
    [InlineData("baseOctave", "10")]
    [InlineData("scale", "{\"name\":\"Bad\",\"intervals\":[0,2,2]}")]
    [InlineData("mappings", "{\"FaceSouth\":{\"type\":\"other\"}}")]
    [InlineData("mappings", "{\"FaceSouth\":{\"type\":\"scaleDegree\",\"degree\":0}}")]
    [InlineData("expression", "null")]
    public void InvalidConfigurationIsRejectedBeforeApplication(string property, string value)
    {
        var node = JsonNode.Parse(ProfileJson.Serialize(InstrumentProfile.Default))!;
        node["configuration"]![property] = JsonNode.Parse(value);
        Assert.Throws<JsonException>(() => ProfileJson.Deserialize(node.ToJsonString()));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"name\":\"one\",\"name\":\"two\"}")]
    [InlineData("{\"futureProperty\":true}")]
    [InlineData("{")]
    public void MissingUnknownDuplicateOrMalformedJsonIsRejected(string json) =>
        Assert.ThrowsAny<JsonException>(() => ProfileJson.Deserialize(json));

    [Fact]
    public void OversizedJsonIsRejected() => Assert.Throws<JsonException>(() => ProfileJson.Deserialize(new string(' ', ProfileJson.MaximumBytes + 1)));

    [Fact]
    public void LoadingSameConfiguredOctaveResetsPersistentShiftWithoutLosingHeldPitch()
    {
        var engine = new MappingEngine();
        var messages = new List<MidiEvent>(); engine.MidiGenerated += messages.Add;
        var device = new GamepadDeviceId(1);
        void Set(PhysicalControl control, float value) => engine.Process(new(GamepadEventKind.ControlChanged, device, control, value));
        engine.Process(new(GamepadEventKind.Selected, device));
        Set(PhysicalControl.LeftStickX, 1); Set(PhysicalControl.LeftStickX, 0);
        Set(PhysicalControl.FaceSouth, 1);
        var restored = ProfileJson.Deserialize(ProfileJson.Serialize(InstrumentProfile.Default));
        engine.UpdateConfiguration(restored.Configuration, resetPersistentOctave: true);
        Assert.Equal(3, engine.CaptureSnapshot().BaseOctave);
        Set(PhysicalControl.FaceSouth, 0); Set(PhysicalControl.FaceSouth, 1);
        Assert.Equal(new[] { new MidiEvent(MidiEventKind.NoteOn, 0, 67, 100), new(MidiEventKind.NoteOff, 0, 67, 0), new(MidiEventKind.NoteOn, 0, 55, 100) }, messages);
    }

    [Fact]
    public void JsonAllowsCommentsAndTrailingCommas()
    {
        string json = ProfileJson.Serialize(InstrumentProfile.Default).TrimEnd();
        json = "// Human-editable profile\n" + json[..^1] + ",\n}";
        Assert.Equal("Default C Major", ProfileJson.Deserialize(json).Name);
    }

    [Fact]
    public async Task ShippedDefaultJsonMatchesCurrentCoreDefaults()
    {
        string json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "default-c-major.json"), TestContext.Current.CancellationToken);
        Assert.Equal(ProfileJson.Serialize(InstrumentProfile.Default), ProfileJson.Serialize(ProfileJson.Deserialize(json)));
    }
}
