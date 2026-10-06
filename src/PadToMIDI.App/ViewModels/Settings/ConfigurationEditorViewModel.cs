using System.Collections.Immutable;
using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Input;
using PadToMIDI.Core.Musical;

namespace PadToMIDI.App.ViewModels.Settings;

public sealed record RootChoice(PitchClass Value, string Name);

/// <summary>A staged configuration editor. Core validates/applies the candidate as one update.</summary>
public sealed class ConfigurationEditorViewModel : ObservableObject
{
    private readonly Action<InstrumentConfiguration> apply;
    private InstrumentConfiguration baseline;
    private RootChoice? root;
    private ScaleDefinition? scale;
    private decimal? octave, velocity, channel;
    private string status = "Edit settings, then choose Apply. Save in Profiles to keep changes. Closing discards unapplied edits.";
    private bool hasError;
    public IReadOnlyList<RootChoice> Roots { get; } = Enum.GetValues<PitchClass>().Select(value => new RootChoice(value, value switch
    {
        PitchClass.CSharp => "C# / Db", PitchClass.DSharp => "D# / Eb", PitchClass.FSharp => "F# / Gb",
        PitchClass.GSharp => "G# / Ab", PitchClass.ASharp => "A# / Bb", _ => NoteDisplay.RootName(value)
    })).ToArray();
    public IReadOnlyList<ScaleDefinition> Scales { get; private set; } = ScalePresets.All;
    public RootChoice? Root { get => root; set => SetProperty(ref root, value); }
    public ScaleDefinition? Scale { get => scale; set => SetProperty(ref scale, value); }
    public decimal? BaseOctave { get => octave; set => SetProperty(ref octave, value); }
    public decimal? Velocity { get => velocity; set => SetProperty(ref velocity, value); }
    public decimal? Channel { get => channel; set => SetProperty(ref channel, value); }
    public IReadOnlyList<ControlMappingEditorViewModel> Buttons { get; private set; } = [];
    public StickEditorViewModel Stick { get; private set; } = null!;
    public ChordEditorViewModel Chords { get; private set; } = null!;
    public AftertouchEditorViewModel DPadAftertouch { get; private set; } = null!;
    public AftertouchEditorViewModel FaceAftertouch { get; private set; } = null!;
    public AxisExpressionEditorViewModel PitchBend { get; private set; } = null!;
    public AxisExpressionEditorViewModel Timbre { get; private set; } = null!;
    public string Status => status;
    public bool HasError => hasError;
    public AsyncCommand ApplyCommand { get; }
    public AsyncCommand ResetCommand { get; }
    public AsyncCommand CloseCommand { get; }
    public AsyncCommand? PanicCommand { get; }
    public event Action? CloseRequested;

    public ConfigurationEditorViewModel(InstrumentConfiguration configuration, Action<InstrumentConfiguration> apply, Action? panic = null)
    {
        this.apply = apply; baseline = configuration;
        Load(configuration);
        ApplyCommand = new(() => { Apply(); return Task.CompletedTask; }, ShowError);
        ResetCommand = new(() => { Load(new()); SetProperty(ref hasError, false, nameof(HasError)); SetProperty(ref status, "Default C Major loaded into the draft. Choose Apply to use it.", nameof(Status)); return Task.CompletedTask; }, ShowError);
        CloseCommand = new(() => { CloseRequested?.Invoke(); return Task.CompletedTask; }, ShowError);
        if (panic is not null) PanicCommand = new(() => { panic(); return Task.CompletedTask; }, ShowError);
    }

    private void Load(InstrumentConfiguration configuration)
    {
        configuration.Validate();
        Root = Roots.Single(item => item.Value == configuration.Root);
        Scales = ScalePresets.All.Contains(configuration.Scale) ? ScalePresets.All : ScalePresets.All.Add(configuration.Scale);
        Notify(nameof(Scales)); Scale = configuration.Scale;
        BaseOctave = configuration.BaseOctave; Velocity = configuration.FixedVelocity; Channel = configuration.MidiChannel + 1;
        Buttons = Enum.GetValues<PhysicalControl>().Where(control => control < PhysicalControl.LeftTrigger)
            .Select(control => new ControlMappingEditorViewModel(control, configuration)).ToArray();
        Stick = new(configuration.LeftStick);
        Chords = new(configuration.Chords);
        Notify(nameof(Chords));
        DPadAftertouch = new("D-pad aftertouch", configuration.Expression.DPadAftertouch);
        FaceAftertouch = new("Face-button aftertouch", configuration.Expression.FaceAftertouch);
        PitchBend = new(configuration.Expression.PitchBend); Timbre = new(configuration.Expression.Timbre);
        Notify(nameof(Buttons)); Notify(nameof(Stick)); Notify(nameof(DPadAftertouch)); Notify(nameof(FaceAftertouch));
        Notify(nameof(PitchBend)); Notify(nameof(Timbre));
    }

    public InstrumentConfiguration BuildConfiguration()
    {
        var mappings = ImmutableDictionary.CreateBuilder<PhysicalControl, MappingAction>();
        var octaves = ImmutableDictionary.CreateBuilder<PhysicalControl, int>();
        var semitones = ImmutableDictionary.CreateBuilder<PhysicalControl, int>();
        foreach (var button in Buttons)
        {
            if (button.Kind == ButtonAssignmentKind.None) continue;
            int value = button.ReadValue();
            switch (button.Kind)
            {
                case ButtonAssignmentKind.ScaleDegree: mappings.Add(button.Control, new ScaleDegreeAction(value)); break;
                case ButtonAssignmentKind.FixedNote: mappings.Add(button.Control, new FixedNoteAction((byte)value)); break;
                case ButtonAssignmentKind.OctaveModifier: octaves.Add(button.Control, value); break;
                case ButtonAssignmentKind.SemitoneModifier: semitones.Add(button.Control, value); break;
                default: throw new InvalidOperationException($"Unknown assignment for {button.Name}.");
            }
        }
        var candidate = baseline with
        {
            Root = Root?.Value ?? throw new InvalidOperationException("Select a root note."),
            Scale = Scale ?? throw new InvalidOperationException("Select a scale."),
            BaseOctave = EditorNumber.Integer(BaseOctave, -1, 9, "base octave"),
            FixedVelocity = (byte)EditorNumber.Integer(Velocity, 1, 127, "velocity"),
            MidiChannel = (byte)(EditorNumber.Integer(Channel, 1, 16, "MIDI channel") - 1),
            Mappings = mappings.ToImmutable(), TemporaryOctaveModifiers = octaves.ToImmutable(), TemporarySemitoneModifiers = semitones.ToImmutable(),
            LeftStick = Stick.Build(), Chords = Chords.Build(), Expression = new()
            {
                DPadAftertouch = DPadAftertouch.Build(), FaceAftertouch = FaceAftertouch.Build(),
                PitchBend = PitchBend.BuildPitchBend(), Timbre = Timbre.BuildTimbre()
            }
        };
        candidate.Validate(); return candidate;
    }

    public void Apply()
    {
        var candidate = BuildConfiguration();
        apply(candidate);
        baseline = candidate;
        SetProperty(ref hasError, false, nameof(HasError));
        SetProperty(ref status, "Settings applied. Held notes retain their original release pitches.", nameof(Status));
    }

    private void ShowError(Exception error)
    {
        SetProperty(ref hasError, true, nameof(HasError));
        SetProperty(ref status, $"Settings were not applied: {error.Message}", nameof(Status));
    }
}
