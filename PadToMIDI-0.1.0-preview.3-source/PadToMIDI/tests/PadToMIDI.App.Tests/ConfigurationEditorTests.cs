using PadToMIDI.App.Services;
using PadToMIDI.App.ViewModels;
using PadToMIDI.App.ViewModels.Settings;
using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Input;
using PadToMIDI.Core.Musical;
using Xunit;

namespace PadToMIDI.App.Tests;

public sealed class ConfigurationEditorTests
{
    [Fact]
    public void DraftEditsDoNotReachTheEngineUntilAppliedAndApplyRetainsHeldPitch()
    {
        var input = new TestInput();
        using var session = new InstrumentSession(input);
        input.Set(PhysicalControl.FaceSouth, 1);
        var editor = new ConfigurationEditorViewModel(session.CaptureSnapshot().State.Configuration, session.UpdateConfiguration);
        editor.Root = editor.Roots.Single(root => root.Value == PitchClass.FSharp);
        editor.Scale = ScalePresets.Dorian; editor.BaseOctave = 4; editor.Velocity = 77;
        var face = editor.Buttons.Single(button => button.Control == PhysicalControl.FaceSouth);
        face.Kind = ButtonAssignmentKind.FixedNote; face.Value = 64;
        Assert.Equal(PitchClass.C, session.CaptureSnapshot().State.Configuration.Root);
        editor.Apply();
        Assert.Equal(PitchClass.FSharp, session.CaptureSnapshot().State.Configuration.Root);
        Assert.Equal(55, Assert.Single(session.CaptureSnapshot().State.HeldNotes).MidiNote);
        input.Set(PhysicalControl.FaceSouth, 0);
        Assert.Equal(55, session.CaptureSnapshot().Activity[0].Message.Data1);
        input.Set(PhysicalControl.FaceSouth, 1);
        var held = Assert.Single(session.CaptureSnapshot().State.HeldNotes);
        Assert.Equal(64, held.MidiNote); Assert.Equal(77, held.Velocity);
    }

    [Theory]
    [InlineData(ButtonAssignmentKind.None, 0)]
    [InlineData(ButtonAssignmentKind.ScaleDegree, 9)]
    [InlineData(ButtonAssignmentKind.FixedNote, 64)]
    [InlineData(ButtonAssignmentKind.OctaveModifier, -2)]
    [InlineData(ButtonAssignmentKind.SemitoneModifier, -1)]
    public void ButtonAssignmentChangesRemoveThePreviousActionFamily(ButtonAssignmentKind kind, int value)
    {
        var editor = new ConfigurationEditorViewModel(new(), _ => { });
        var bumper = editor.Buttons.Single(button => button.Control == PhysicalControl.RightBumper);
        bumper.Kind = kind; bumper.Value = value;
        var candidate = editor.BuildConfiguration();
        Assert.Equal(kind == ButtonAssignmentKind.SemitoneModifier, candidate.TemporarySemitoneModifiers.ContainsKey(bumper.Control));
        Assert.Equal(kind == ButtonAssignmentKind.OctaveModifier, candidate.TemporaryOctaveModifiers.ContainsKey(bumper.Control));
        Assert.Equal(kind is ButtonAssignmentKind.FixedNote or ButtonAssignmentKind.ScaleDegree, candidate.Mappings.ContainsKey(bumper.Control));
        if (kind == ButtonAssignmentKind.FixedNote) Assert.Equal(new FixedNoteAction(64), candidate.Mappings[bumper.Control]);
    }

    [Theory]
    [InlineData("velocity")]
    [InlineData("stick")]
    [InlineData("modifier")]
    [InlineData("cc")]
    [InlineData("channel")]
    public void InvalidDraftIsRejectedWithoutApplyingAnyOfItsOtherChanges(string invalid)
    {
        var input = new TestInput();
        using var session = new InstrumentSession(input);
        input.Set(PhysicalControl.FaceSouth, 1);
        var original = session.CaptureSnapshot();
        var editor = new ConfigurationEditorViewModel(original.State.Configuration, session.UpdateConfiguration) { BaseOctave = 5 };
        switch (invalid)
        {
            case "velocity": editor.Velocity = null; break;
            case "stick": editor.Stick.DeadZone = 0.8m; editor.Stick.Threshold = 0.7m; break;
            case "modifier": editor.Buttons.Single(button => button.Control == PhysicalControl.LeftBumper).Value = 0; break;
            case "cc": editor.Timbre.ControllerNumber = 123; break;
            case "channel": editor.Channel = 1.5m; break;
        }
        editor.ApplyCommand.Execute(null);
        Assert.StartsWith("Settings were not applied:", editor.Status);
        var after = session.CaptureSnapshot();
        Assert.Equal(original.State.Configuration, after.State.Configuration);
        Assert.Equal(original.EventCount, after.EventCount);
        Assert.Equal(55, Assert.Single(after.State.HeldNotes).MidiNote);
    }

    [Fact]
    public void ClosingDiscardsUnappliedEditsAndResetUsesCurrentCoreDefaults()
    {
        int applies = 0; bool closed = false;
        var editor = new ConfigurationEditorViewModel(new() { BaseOctave = 6 }, _ => applies++);
        editor.CloseRequested += () => closed = true;
        editor.BaseOctave = 7;
        editor.CloseCommand.Execute(null);
        Assert.True(closed); Assert.Equal(0, applies);
        editor.ResetCommand.Execute(null);
        var candidate = editor.BuildConfiguration();
        Assert.Equal(3, candidate.BaseOctave);
        Assert.Equal(StickAction.DecreaseOctave, candidate.LeftStick.Left);
        Assert.Equal(StickAction.MomentaryOctaveUp, candidate.LeftStick.Up);
        Assert.Equal(StickAction.MomentaryOctaveDown, candidate.LeftStick.Down);
        Assert.Equal(StickAction.IncreaseOctave, candidate.LeftStick.Right);
        Assert.Equal(-1, candidate.TemporarySemitoneModifiers[PhysicalControl.LeftBumper]);
        Assert.Equal(1, candidate.TemporarySemitoneModifiers[PhysicalControl.RightBumper]);
        Assert.Equal(0, applies);
    }

    [Fact]
    public void CustomScaleAndResponseSettingsSurviveOpeningAndApplyingEditor()
    {
        var custom = new ScaleDefinition("Custom", [0, 1, 7]);
        var initial = new InstrumentConfiguration
        {
            Scale = custom, Root = PitchClass.B, BaseOctave = 2, MidiChannel = 15,
            Expression = new() { PitchBend = new() { Response = new() { Curve = ResponseCurve.Quadratic, DeadZone = 0.12f, Sensitivity = 2, Invert = true, SmoothingMilliseconds = 23, UpdateRateHz = 120 } } }
        };
        var editor = new ConfigurationEditorViewModel(initial, _ => { });
        Assert.Contains(custom, editor.Scales);
        var result = editor.BuildConfiguration();
        Assert.Same(custom, result.Scale);
        Assert.Equal(initial.Expression, result.Expression);
        Assert.Equal(initial.LeftStick, result.LeftStick);
        Assert.Equal(16, editor.Channel);
    }

    [Fact]
    public void EditsReachEveryExpressionSettingAndStickDirection()
    {
        var editor = new ConfigurationEditorViewModel(new(), _ => { });
        editor.Stick.XAxis = PhysicalControl.RightStickX; editor.Stick.YAxis = PhysicalControl.RightStickY;
        editor.Stick.Up = StickAction.IncreaseOctave; editor.Stick.Down = StickAction.DecreaseOctave;
        editor.Stick.Right = StickAction.SharpenLastNote; editor.Stick.Left = StickAction.FlattenLastNote;
        editor.Stick.Threshold = 0.8m; editor.Stick.DeadZone = 0.1m;
        editor.DPadAftertouch.Source = PhysicalControl.RightTrigger; editor.DPadAftertouch.MinimumThreshold = 0.3m;
        editor.DPadAftertouch.Response.DeadZone = 0.2m; editor.DPadAftertouch.Response.Curve = ResponseCurve.SquareRoot;
        editor.FaceAftertouch.Enabled = false;
        editor.PitchBend.Source = PhysicalControl.LeftStickX; editor.PitchBend.Response.Invert = true;
        editor.PitchBend.Response.Sensitivity = 2; editor.PitchBend.Response.SmoothingMilliseconds = 20;
        editor.PitchBend.Response.UpdateRateHz = 100;
        editor.Timbre.ControllerNumber = 11; editor.Timbre.Response.Invert = false;
        var result = editor.BuildConfiguration();
        Assert.Equal(StickAction.IncreaseOctave, result.LeftStick.Up);
        Assert.Equal(0.8f, result.LeftStick.Threshold);
        Assert.Equal(PhysicalControl.RightTrigger, result.Expression.DPadAftertouch.Source);
        Assert.Equal(0.3f, result.Expression.DPadAftertouch.MinimumThreshold);
        Assert.Equal(ResponseCurve.SquareRoot, result.Expression.DPadAftertouch.Response.Curve);
        Assert.False(result.Expression.FaceAftertouch.Enabled);
        Assert.True(result.Expression.PitchBend.Response.Invert);
        Assert.Equal(20, result.Expression.PitchBend.Response.SmoothingMilliseconds);
        Assert.Equal(100, result.Expression.PitchBend.Response.UpdateRateHz);
        Assert.Equal(11, result.Expression.Timbre.ControllerNumber);
        Assert.False(result.Expression.Timbre.Response.Invert);
    }

    [Fact]
    public void ObservingChannelDoesNotGenerateAnEditIntent()
    {
        int intents = 0;
        var midi = new MidiViewModel(() => Task.CompletedTask, _ => Task.CompletedTask, () => Task.CompletedTask, _ => intents++, () => { });
        midi.ObserveChannel(4);
        Assert.Equal(5, midi.Channel); Assert.Equal(0, intents);
        midi.Channel = 6;
        Assert.Equal(1, intents);
    }

    [Fact]
    public void ValidationErrorClearsAfterApplyAndDialogPanicDoesNotApplyItsDraft()
    {
        var input = new TestInput();
        using var session = new InstrumentSession(input);
        input.Set(PhysicalControl.FaceSouth, 1);
        var editor = new ConfigurationEditorViewModel(session.CaptureSnapshot().State.Configuration, session.UpdateConfiguration, session.Panic)
        { BaseOctave = 5, Velocity = null };
        editor.ApplyCommand.Execute(null);
        Assert.True(editor.HasError);
        editor.PanicCommand!.Execute(null);
        Assert.Empty(session.CaptureSnapshot().State.HeldNotes);
        Assert.Equal(3, session.CaptureSnapshot().State.Configuration.BaseOctave);
        editor.Velocity = 88;
        editor.ApplyCommand.Execute(null);
        Assert.False(editor.HasError);
        Assert.Equal(5, session.CaptureSnapshot().State.Configuration.BaseOctave);
        Assert.Equal(88, session.CaptureSnapshot().State.Configuration.FixedVelocity);
    }

    [Fact]
    public void ChordDraftAppliesExplicitShapeAndReservesItsToggleWithoutTouchingHeldNotes()
    {
        var input = new TestInput(); using var session = new InstrumentSession(input);
        input.Set(PhysicalControl.FaceSouth, 1);
        var editor = new ConfigurationEditorViewModel(session.CaptureSnapshot().State.Configuration, session.UpdateConfiguration);
        editor.Chords.Enabled = true; editor.Chords.Shape = ChordShape.Minor;
        editor.Chords.Toggle = editor.Chords.Toggles.Single(choice => choice.Control == PhysicalControl.Back);
        editor.Apply();
        Assert.Single(session.CaptureSnapshot().State.HeldNotes);
        input.Set(PhysicalControl.FaceSouth, 0); input.Set(PhysicalControl.FaceSouth, 1);
        Assert.Equal(new byte[] {55,58,62}, session.CaptureSnapshot().State.HeldNotes.Select(note => note.MidiNote));
        input.Set(PhysicalControl.Back, 1);
        Assert.False(session.CaptureSnapshot().State.Configuration.Chords.Enabled);
        Assert.Equal(3, session.CaptureSnapshot().State.HeldNotes.Length);
    }

    [Fact]
    public void ToggleConflictsAreRejectedAtomicallyAndCanBeDisabledForCustomMappings()
    {
        InstrumentConfiguration? applied = null;
        var editor = new ConfigurationEditorViewModel(new(), configuration => applied = configuration);
        editor.Buttons.Single(button => button.Control == PhysicalControl.Start).Kind = ButtonAssignmentKind.ScaleDegree;
        editor.ApplyCommand.Execute(null);
        Assert.True(editor.HasError); Assert.Null(applied);
        editor.Chords.Toggle = editor.Chords.Toggles.Single(choice => choice.Control is null);
        editor.Apply(); Assert.Null(applied!.Chords.ToggleButton);
    }

    [Fact]
    public void ModeObserverAndPreviewFollowControllerToggleAndShowTheWholeChord()
    {
        var input = new TestInput(); using var session = new InstrumentSession(input);
        input.Set(PhysicalControl.Start, 1);
        var model = new MainWindowViewModel(_ => { }); model.UpdateInstrument(session.CaptureSnapshot());
        Assert.StartsWith("Chord mode", model.PlayingModeLabel);
        Assert.Equal("G (G3 B3 D4)", model.NoteMappings.Single(mapping => mapping.Control == PhysicalControl.FaceSouth).NoteLabel);
        input.Set(PhysicalControl.FaceWest, 1); model.UpdateInstrument(session.CaptureSnapshot());
        Assert.Contains("Bdim", model.LastNoteLabel);
    }

    private sealed class TestInput : IGamepadInput
    {
        public event Action<GamepadInputEvent>? InputReceived;
        private bool selected;
        public void Run(CancellationToken cancellationToken) { }
        public void SelectDevice(GamepadDeviceId? id) { }
        public GamepadInputSnapshot CaptureSnapshot() => new GamepadStateBuffer().CaptureSnapshot();
        public void Set(PhysicalControl control, float value)
        {
            if (!selected) { selected = true; InputReceived?.Invoke(new(GamepadEventKind.Selected, new(1))); }
            InputReceived?.Invoke(new(GamepadEventKind.ControlChanged, new(1), control, value));
        }
    }
}
