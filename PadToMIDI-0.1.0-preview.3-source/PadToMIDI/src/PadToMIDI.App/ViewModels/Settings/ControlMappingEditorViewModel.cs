using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Input;

namespace PadToMIDI.App.ViewModels.Settings;

public enum ButtonAssignmentKind { None, ScaleDegree, FixedNote, OctaveModifier, SemitoneModifier }

/// <summary>Edits one configuration assignment; never handles live input or generates MIDI.</summary>
public sealed class ControlMappingEditorViewModel : ObservableObject
{
    private ButtonAssignmentKind kind;
    private decimal? value;
    public PhysicalControl Control { get; }
    public string Name => Control.ToString();
    public static IReadOnlyList<ButtonAssignmentKind> Kinds { get; } = Enum.GetValues<ButtonAssignmentKind>();
    public ButtonAssignmentKind Kind
    {
        get => kind;
        set
        {
            if (!SetProperty(ref kind, value)) return;
            Notify(nameof(ValueEnabled)); Notify(nameof(Minimum)); Notify(nameof(Maximum)); Notify(nameof(ValueHint));
            Value = value switch { ButtonAssignmentKind.FixedNote => 60, ButtonAssignmentKind.None => 0, _ => 1 };
        }
    }
    public decimal? Value { get => value; set { if (SetProperty(ref this.value, value)) Notify(nameof(ValueHint)); } }
    public bool ValueEnabled => Kind != ButtonAssignmentKind.None;
    public decimal Minimum => Kind switch { ButtonAssignmentKind.ScaleDegree => 1, ButtonAssignmentKind.OctaveModifier => -10, ButtonAssignmentKind.SemitoneModifier => -127, _ => 0 };
    public decimal Maximum => Kind switch { ButtonAssignmentKind.ScaleDegree => int.MaxValue, ButtonAssignmentKind.OctaveModifier => 10, _ => 127 };
    public string ValueHint => Kind switch
    {
        ButtonAssignmentKind.ScaleDegree => "Scale degree (1-based)",
        ButtonAssignmentKind.FixedNote => Value is >= 0 and <= 127 ? $"MIDI note · {NoteDisplay.Name((byte)Value.Value)}" : "MIDI note number",
        ButtonAssignmentKind.OctaveModifier => "Octaves while held; nonzero",
        ButtonAssignmentKind.SemitoneModifier => "Semitones while held; nonzero",
        _ => "Unassigned"
    };

    public ControlMappingEditorViewModel(PhysicalControl control, InstrumentConfiguration configuration)
    {
        Control = control;
        if (configuration.TemporaryOctaveModifiers.TryGetValue(control, out int octave)) { kind = ButtonAssignmentKind.OctaveModifier; value = octave; }
        else if (configuration.TemporarySemitoneModifiers.TryGetValue(control, out int semitones)) { kind = ButtonAssignmentKind.SemitoneModifier; value = semitones; }
        else if (configuration.Mappings.GetValueOrDefault(control) is ScaleDegreeAction degree) { kind = ButtonAssignmentKind.ScaleDegree; value = degree.Degree; }
        else if (configuration.Mappings.GetValueOrDefault(control) is FixedNoteAction note) { kind = ButtonAssignmentKind.FixedNote; value = note.MidiNote; }
        else { kind = ButtonAssignmentKind.None; value = 0; }
    }

    public int ReadValue()
    {
        int result = EditorNumber.Integer(Value, (int)Minimum, (int)Maximum, $"{Name} value");
        if (Kind is ButtonAssignmentKind.OctaveModifier or ButtonAssignmentKind.SemitoneModifier && result == 0)
            throw new InvalidOperationException($"{Name}: use a nonzero modifier shift, or choose None.");
        return result;
    }
}
