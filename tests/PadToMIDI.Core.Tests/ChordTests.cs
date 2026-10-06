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

public sealed class ChordTests
{
    private static readonly GamepadDeviceId Device = new(1);
    private static readonly PhysicalControl[] Buttons = [PhysicalControl.DPadDown, PhysicalControl.DPadUp,
        PhysicalControl.DPadLeft, PhysicalControl.DPadRight, PhysicalControl.FaceSouth, PhysicalControl.FaceNorth,
        PhysicalControl.FaceWest, PhysicalControl.FaceEast];
    public static TheoryData<ScaleDefinition, int[][]> Triads => new()
    {
        { ScalePresets.Major, [[0,4,7],[2,5,9],[4,7,11],[5,9,12],[7,11,14],[9,12,16],[11,14,17],[12,16,19]] },
        { ScalePresets.NaturalMinor, [[0,3,7],[2,5,8],[3,7,10],[5,8,12],[7,10,14],[8,12,15],[10,14,17],[12,15,19]] },
        { ScalePresets.HarmonicMinor, [[0,3,7],[2,5,8],[3,7,11],[5,8,12],[7,11,14],[8,12,15],[11,14,17],[12,15,19]] },
        { ScalePresets.MelodicMinor, [[0,3,7],[2,5,9],[3,7,11],[5,9,12],[7,11,14],[9,12,15],[11,14,17],[12,15,19]] },
        { ScalePresets.Dorian, [[0,3,7],[2,5,9],[3,7,10],[5,9,12],[7,10,14],[9,12,15],[10,14,17],[12,15,19]] },
        { ScalePresets.Phrygian, [[0,3,7],[1,5,8],[3,7,10],[5,8,12],[7,10,13],[8,12,15],[10,13,17],[12,15,19]] },
        { ScalePresets.Lydian, [[0,4,7],[2,6,9],[4,7,11],[6,9,12],[7,11,14],[9,12,16],[11,14,18],[12,16,19]] },
        { ScalePresets.Mixolydian, [[0,4,7],[2,5,9],[4,7,10],[5,9,12],[7,10,14],[9,12,16],[10,14,17],[12,16,19]] },
        { ScalePresets.Locrian, [[0,3,6],[1,5,8],[3,6,10],[5,8,12],[6,10,13],[8,12,15],[10,13,17],[12,15,18]] }
    };

    [Theory]
    [MemberData(nameof(Triads))]
    public void EveryDegreeAndRootMatchesIndependentTheoryVoicings(ScaleDefinition scale, int[][] expected)
    {
        foreach (var root in Enum.GetValues<PitchClass>())
        {
            var (engine, messages) = Create(new() { Scale = scale, Root = root, Chords = new() { Enabled = true } });
            for (int degree = 0; degree < 8; degree++)
            {
                messages.Clear(); Set(engine, Buttons[degree], 1);
                var pitches = expected[degree].Select(offset => (byte)(48 + (int)root + offset)).ToArray();
                Assert.Equal(pitches, messages.Where(message => message.Kind == MidiEventKind.NoteOn).Select(message => message.Data1));
                Assert.Equal(pitches, engine.CaptureSnapshot().HeldNotes.Select(note => note.MidiNote));
                Set(engine, Buttons[degree], 0);
                Assert.Equal(pitches, messages.Where(message => message.Kind == MidiEventKind.NoteOff).Select(message => message.Data1));
                Assert.Empty(engine.CaptureSnapshot().HeldNotes);
            }
        }
    }

    [Fact]
    public void ToggleIsEdgeTriggeredAndHeldGesturesKeepTheirOriginalVoiceCount()
    {
        var (engine, messages) = Create();
        Set(engine, PhysicalControl.FaceSouth, 1); // Single G.
        Set(engine, PhysicalControl.Start, 1); Set(engine, PhysicalControl.Start, 1);
        Assert.True(engine.CaptureSnapshot().Configuration.Chords.Enabled);
        Set(engine, PhysicalControl.FaceSouth, 0);
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOff, 0, 55, 0), messages[^1]);
        Set(engine, PhysicalControl.FaceSouth, 1);
        Assert.Equal(new byte[] {55,59,62}, engine.CaptureSnapshot().HeldNotes.Select(note => note.MidiNote));
        Set(engine, PhysicalControl.Start, 0); Set(engine, PhysicalControl.Start, 1);
        Assert.False(engine.CaptureSnapshot().Configuration.Chords.Enabled);
        Set(engine, PhysicalControl.FaceSouth, 0);
        Assert.Equal(new byte[] {55,59,62}, messages.TakeLast(3).Select(message => message.Data1));
        Assert.All(messages.TakeLast(3), message => Assert.Equal(MidiEventKind.NoteOff, message.Kind));
    }

    [Fact]
    public void ModifiersTransposeWholeChordAndConfigurationChangesDoNotChangeReleases()
    {
        var (engine, messages) = Create(new() { Chords = new() { Enabled = true } });
        Set(engine, PhysicalControl.LeftStickY, -1); Set(engine, PhysicalControl.RightBumper, 1);
        Set(engine, PhysicalControl.FaceSouth, 1);
        Assert.Equal(new byte[] {68,72,75}, engine.CaptureSnapshot().HeldNotes.Select(note => note.MidiNote));
        Set(engine, PhysicalControl.LeftStickY, 0); Set(engine, PhysicalControl.RightBumper, 0);
        engine.UpdateConfiguration(new() { Root = PitchClass.E, Scale = ScalePresets.Phrygian, BaseOctave = 1, MidiChannel = 5 });
        Set(engine, PhysicalControl.FaceSouth, 0);
        Assert.Equal(new byte[] {68,72,75}, messages.TakeLast(3).Select(message => message.Data1));
        Assert.All(messages.TakeLast(3), message => { Assert.Equal(MidiEventKind.NoteOff, message.Kind); Assert.Equal(0, message.Channel); });
    }

    [Fact]
    public void SharedPitchesRemainSoundingUntilTheirLastOwnerReleases()
    {
        var (engine, messages) = Create(new() { Chords = new() { Enabled = true } });
        Set(engine, PhysicalControl.DPadDown, 1); Set(engine, PhysicalControl.FaceSouth, 1);
        Assert.Equal(5, messages.Count(message => message.Kind == MidiEventKind.NoteOn));
        Set(engine, PhysicalControl.DPadDown, 0);
        Assert.DoesNotContain(messages, message => message.Kind == MidiEventKind.NoteOff && message.Data1 == 55);
        Set(engine, PhysicalControl.FaceSouth, 0);
        Assert.Equal(5, messages.Count(message => message.Kind == MidiEventKind.NoteOff));
        Assert.Empty(engine.CaptureSnapshot().HeldNotes);
    }

    [Fact]
    public void PressureCoversAllTonesAndNewestGroupWinsOnlySharedPitches()
    {
        var (engine, messages) = Create(new() { Chords = new() { Enabled = true }, Expression = new()
        {
            DPadAftertouch = new() { Response = new() { UpdateRateHz = 0 } },
            FaceAftertouch = new() { Source = PhysicalControl.RightTrigger, Response = new() { UpdateRateHz = 0 } }
        } });
        Set(engine, PhysicalControl.DPadDown, 1); Set(engine, PhysicalControl.LeftTrigger, 1);
        Assert.Equal(new byte[] {48,52,55}, messages.Where(message => message.Kind == MidiEventKind.PolyphonicPressure && message.Data2 == 127).Select(message => message.Data1));
        Set(engine, PhysicalControl.FaceSouth, 1); // Newer face owns shared G and sets its pressure to zero.
        Assert.Contains(new MidiEvent(MidiEventKind.PolyphonicPressure, 0, 55, 0), messages);
        messages.Clear(); Set(engine, PhysicalControl.RightTrigger, 1);
        Assert.Equal(new byte[] {55,59,62}, messages.Where(message => message.Kind == MidiEventKind.PolyphonicPressure && message.Data2 == 127).Select(message => message.Data1));
        int count = messages.Count; Set(engine, PhysicalControl.RightTrigger, 1);
        Assert.Equal(count, messages.Count);
        Set(engine, PhysicalControl.RightTrigger, 0); messages.Clear();
        Set(engine, PhysicalControl.FaceSouth, 0);
        Assert.Contains(new MidiEvent(MidiEventKind.PolyphonicPressure, 0, 55, 127), messages);
        Assert.DoesNotContain(messages, message => message.Kind == MidiEventKind.NoteOff && message.Data1 == 55);
        engine.Reset(); Assert.Empty(engine.CaptureSnapshot().HeldNotes);
    }

    [Fact]
    public void UnsupportedScaleRequiresExplicitHarmonyAndHarmonyUsesMelodyRootNotButtonIndex()
    {
        var (engine, _) = Create(new() { Scale = ScalePresets.MajorPentatonic });
        Set(engine, PhysicalControl.Start, 1);
        Assert.False(engine.CaptureSnapshot().Configuration.Chords.Enabled);
        Assert.Contains("harmony", engine.CaptureSnapshot().PlayingMessage);
        engine.UpdateConfiguration(new() { Scale = ScalePresets.MajorPentatonic,
            Chords = new() { Enabled = true, HarmonyScale = ScalePresets.Major } });
        Set(engine, PhysicalControl.DPadRight, 1); // Pentatonic position 4 is G, not F.
        Assert.Equal(new byte[] {55,59,62}, engine.CaptureSnapshot().HeldNotes.Select(note => note.MidiNote));
    }

    [Fact]
    public void ChromaticOutsideHarmonyAndFixedNotesDoNotGuessChordQuality()
    {
        var (engine, messages) = Create(new() { Scale = ScalePresets.Chromatic,
            Chords = new() { Enabled = true, HarmonyScale = ScalePresets.Major } });
        Set(engine, PhysicalControl.DPadUp, 1); Assert.Empty(messages); // C# outside C major.
        engine.UpdateConfiguration(new() { Chords = new() { Enabled = true },
            Mappings = ImmutableDictionary<PhysicalControl, MappingAction>.Empty.Add(PhysicalControl.FaceSouth, new FixedNoteAction(60)) });
        Set(engine, PhysicalControl.FaceSouth, 1); Assert.Empty(engine.CaptureSnapshot().HeldNotes);
        Set(engine, PhysicalControl.FaceSouth, 0);
        engine.UpdateConfiguration(engine.CaptureSnapshot().Configuration with { Chords = new() { Enabled = true, Shape = ChordShape.Minor } });
        Set(engine, PhysicalControl.FaceSouth, 1);
        Assert.Equal(new byte[] {60,63,67}, engine.CaptureSnapshot().HeldNotes.Select(note => note.MidiNote));
    }

    [Fact]
    public void OutOfRangeChordIsRejectedAtomically()
    {
        var (engine, messages) = Create(new() { BaseOctave = 9, Chords = new() { Enabled = true } });
        Set(engine, PhysicalControl.FaceSouth, 1); Assert.Empty(messages); Assert.Empty(engine.CaptureSnapshot().HeldNotes);
        Set(engine, PhysicalControl.DPadDown, 1);
        Assert.Equal(new byte[] {120,124,127}, engine.CaptureSnapshot().HeldNotes.Select(note => note.MidiNote));
    }

    [Theory]
    [InlineData(ChordShape.Major,64,67)]
    [InlineData(ChordShape.Minor,63,67)]
    [InlineData(ChordShape.Diminished,63,66)]
    [InlineData(ChordShape.Augmented,64,68)]
    public void ExplicitShapesHarmonizeFixedRootsByTheirActualIntervals(ChordShape shape, byte third, byte fifth)
    {
        var (engine, messages) = Create(new() { Chords = new() { Enabled = true, Shape = shape },
            Mappings = ImmutableDictionary<PhysicalControl, MappingAction>.Empty.Add(PhysicalControl.FaceSouth,new FixedNoteAction(60)) });
        Set(engine, PhysicalControl.FaceSouth,1); Set(engine, PhysicalControl.FaceSouth,0);
        Assert.Equal(new byte[] {60,third,fifth}, messages.Where(message => message.Kind == MidiEventKind.NoteOn).Select(message => message.Data1));
        Assert.Equal(new byte[] {60,third,fifth}, messages.Where(message => message.Kind == MidiEventKind.NoteOff).Select(message => message.Data1));
    }

    [Fact]
    public void RetuningActiveChordUpdatesAllReleasePitchesAndPreservesVelocity()
    {
        var (engine, messages) = Create(new() { FixedVelocity = 77, Chords = new() { Enabled = true }, LeftStick = new() { Right = StickAction.SharpenLastNote } });
        Set(engine, PhysicalControl.FaceSouth, 1); messages.Clear(); Set(engine, PhysicalControl.LeftStickX, 1);
        Assert.Equal(new byte[] {55,59,62}, messages.Where(message => message.Kind == MidiEventKind.NoteOff).Select(message => message.Data1));
        Assert.Equal(new byte[] {56,60,63}, messages.Where(message => message.Kind == MidiEventKind.NoteOn).Select(message => message.Data1));
        Assert.All(messages.Where(message => message.Kind == MidiEventKind.NoteOn), message => Assert.Equal(77, message.Data2));
        Set(engine, PhysicalControl.FaceSouth, 0);
        Assert.Equal(new byte[] {56,60,63}, messages.TakeLast(3).Select(message => message.Data1));
    }

    [Fact]
    public void RandomChordsAndConfigurationTransitionsMaintainWireOwnershipThroughDisconnect()
    {
        var (engine, _) = Create(); var active = new HashSet<(byte,byte)>();
        engine.MidiGenerated += message =>
        {
            if (message.Kind == MidiEventKind.NoteOn) Assert.True(active.Add((message.Channel,message.Data1)));
            if (message.Kind == MidiEventKind.NoteOff) Assert.True(active.Remove((message.Channel,message.Data1)));
        };
        var random = new Random(591);
        for (int i = 0; i < 3000; i++)
        {
            if (i % 17 == 0) engine.UpdateConfiguration(new() { Scale = ScalePresets.All[random.Next(9)], Root = (PitchClass)random.Next(12),
                BaseOctave = random.Next(1,6), MidiChannel = (byte)random.Next(3), Chords = new() { Enabled = random.Next(2) == 1 } });
            else if (i % 31 == 0) { engine.Reset(); engine.Process(new(GamepadEventKind.Selected, Device)); }
            else Set(engine, random.Next(10) == 0 ? PhysicalControl.Start : Buttons[random.Next(8)], random.Next(2));
            Assert.True(active.SetEquals(engine.CaptureSnapshot().HeldNotes.Select(note => (note.Channel,note.MidiNote))));
        }
        engine.Process(new(GamepadEventKind.Disconnected, Device)); Assert.Empty(active); Assert.Empty(engine.CaptureSnapshot().HeldNotes);
    }

    [Fact]
    public void ProfilesRoundTripChordSettingsAndOldProfilesDefaultToNoteMode()
    {
        var profile = InstrumentProfile.Default with { Configuration = new() { Chords = new() { Enabled = true, HarmonyScale = ScalePresets.HarmonicMinor, ToggleButton = PhysicalControl.Back } } };
        var restored = ProfileJson.Deserialize(ProfileJson.Serialize(profile));
        Assert.Equal(profile.Configuration.Chords, restored.Configuration.Chords);
        var old = JsonNode.Parse(ProfileJson.Serialize(InstrumentProfile.Default))!;
        old["configuration"]!.AsObject().Remove("chords");
        Assert.Equal(new ChordConfiguration(), ProfileJson.Deserialize(old.ToJsonString()).Configuration.Chords);
        old["configuration"]!["chords"] = JsonNode.Parse("{\"toggleButton\":\"FaceSouth\"}");
        Assert.Throws<JsonException>(() => ProfileJson.Deserialize(old.ToJsonString()));
    }

    [Theory]
    [InlineData(PitchClass.C, 3, "Eb3")]
    [InlineData(PitchClass.C, 6, "Ab3")]
    [InlineData(PitchClass.C, 7, "Bb3")]
    [InlineData(PitchClass.FSharp, 7, "E4")]
    public void ScaleSpellingFollowsLetters(PitchClass root, int degree, string expected)
    {
        Assert.True(ScalePresets.NaturalMinor.TryGetMidiNote(root,3,degree,out byte pitch));
        Assert.Equal(expected, NoteSpelling.Name(pitch, NoteSpelling.DegreeLetter(root,ScalePresets.NaturalMinor,degree)));
    }

    [Fact]
    public void LegacyProfilesWithAssignedStartKeepTheirMappingAndDisableTheNewToggle()
    {
        var old = JsonNode.Parse(ProfileJson.Serialize(InstrumentProfile.Default))!;
        old["configuration"]!.AsObject().Remove("chords");
        old["configuration"]!["mappings"]!["Start"] = JsonNode.Parse("{\"type\":\"scaleDegree\",\"degree\":1}");
        var restored = ProfileJson.Deserialize(old.ToJsonString());
        Assert.Null(restored.Configuration.Chords.ToggleButton);
        var (engine, messages) = Create(restored.Configuration);
        Set(engine,PhysicalControl.Start,1);
        Assert.Equal(new MidiEvent(MidiEventKind.NoteOn,0,48,100),messages.Single());
    }

    [Fact]
    public void EnharmonicOctavesAndRaisedChordThirdAreSpelledCorrectly()
    {
        Assert.Equal("B#3", NoteSpelling.Name(60,6)); Assert.Equal("Cb4", NoteSpelling.Name(59,0));
        Assert.Equal("B#-2", NoteSpelling.Name(0,6));
        Assert.True(ChordResolver.TryResolve(new() { Chords = new() { Enabled = true } }, PhysicalControl.FaceSouth,3,0,1,out var chord,out _));
        Assert.Equal("G#", NoteSpelling.ChordName(chord));
        Assert.Equal("B#3", NoteSpelling.Name(chord.VoiceAt(1).MidiNote,chord.VoiceAt(1).RootLetter));
    }

    private static (MappingEngine,List<MidiEvent>) Create(InstrumentConfiguration? configuration = null)
    {
        var engine = new MappingEngine(configuration); var messages = new List<MidiEvent>();
        engine.MidiGenerated += messages.Add; engine.Process(new(GamepadEventKind.Selected,Device)); return (engine,messages);
    }
    private static void Set(MappingEngine engine, PhysicalControl control, float value) => engine.Process(new(GamepadEventKind.ControlChanged,Device,control,value));
}
