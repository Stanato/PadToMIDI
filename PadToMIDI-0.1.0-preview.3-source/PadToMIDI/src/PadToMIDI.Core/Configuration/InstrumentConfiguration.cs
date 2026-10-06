using System.Collections.Immutable;
using PadToMIDI.Core.Input;
using PadToMIDI.Core.Musical;

namespace PadToMIDI.Core.Configuration;

/// <summary>Immutable settings, separate from the runtime held-note ledger.</summary>
public sealed record InstrumentConfiguration
{
    public PitchClass Root { get; init; } = PitchClass.C;
    public ScaleDefinition Scale { get; init; } = ScalePresets.Major;
    public int BaseOctave { get; init; } = 3;
    public byte FixedVelocity { get; init; } = 100;
    /// <summary>Zero-based MIDI channel; zero is channel 1 in the user interface.</summary>
    public byte MidiChannel { get; init; }
    public ImmutableDictionary<PhysicalControl, MappingAction> Mappings { get; init; } = DefaultMappings;
    public ImmutableDictionary<PhysicalControl, int> TemporaryOctaveModifiers { get; init; } = DefaultOctaveModifiers;
    public ImmutableDictionary<PhysicalControl, int> TemporarySemitoneModifiers { get; init; } = DefaultSemitoneModifiers;
    public DiscreteStickConfiguration LeftStick { get; init; } = new();
    public ExpressionConfiguration Expression { get; init; } = new();
    public ChordConfiguration Chords { get; init; } = new();

    public static ImmutableDictionary<PhysicalControl, int> DefaultOctaveModifiers { get; } =
        ImmutableDictionary<PhysicalControl, int>.Empty;

    public static ImmutableDictionary<PhysicalControl, int> DefaultSemitoneModifiers { get; } =
        new Dictionary<PhysicalControl, int>
        {
            [PhysicalControl.LeftBumper] = -1,
            [PhysicalControl.RightBumper] = 1
        }.ToImmutableDictionary();

    public static ImmutableDictionary<PhysicalControl, MappingAction> DefaultMappings { get; } =
        new Dictionary<PhysicalControl, MappingAction>
        {
            [PhysicalControl.DPadDown] = new ScaleDegreeAction(1),
            [PhysicalControl.DPadUp] = new ScaleDegreeAction(2),
            [PhysicalControl.DPadLeft] = new ScaleDegreeAction(3),
            [PhysicalControl.DPadRight] = new ScaleDegreeAction(4),
            [PhysicalControl.FaceSouth] = new ScaleDegreeAction(5),
            [PhysicalControl.FaceNorth] = new ScaleDegreeAction(6),
            [PhysicalControl.FaceWest] = new ScaleDegreeAction(7),
            [PhysicalControl.FaceEast] = new ScaleDegreeAction(8)
        }.ToImmutableDictionary();

    public void Validate()
    {
        if (!Enum.IsDefined(Root)) throw new ArgumentOutOfRangeException(nameof(Root));
        ArgumentNullException.ThrowIfNull(Scale);
        if (BaseOctave is < -1 or > 9) throw new ArgumentOutOfRangeException(nameof(BaseOctave));
        if (FixedVelocity is < 1 or > 127) throw new ArgumentOutOfRangeException(nameof(FixedVelocity));
        if (MidiChannel > 15) throw new ArgumentOutOfRangeException(nameof(MidiChannel));
        ArgumentNullException.ThrowIfNull(Mappings);
        ArgumentNullException.ThrowIfNull(TemporaryOctaveModifiers);
        ArgumentNullException.ThrowIfNull(TemporarySemitoneModifiers);
        ArgumentNullException.ThrowIfNull(LeftStick);
        LeftStick.Validate();
        ArgumentNullException.ThrowIfNull(Expression);
        Expression.Validate();
        ArgumentNullException.ThrowIfNull(Chords);
        Chords.Validate();
        if (Chords.ToggleButton is { } toggle && (Mappings.ContainsKey(toggle) ||
            TemporaryOctaveModifiers.ContainsKey(toggle) || TemporarySemitoneModifiers.ContainsKey(toggle)))
            throw new ArgumentException("The chord toggle cannot also be a note or modifier. Choose another toggle button, or disable it.");
        foreach (var (control, shift) in TemporaryOctaveModifiers)
        {
            if (!Enum.IsDefined(control) || control >= PhysicalControl.LeftTrigger || Mappings.ContainsKey(control))
                throw new ArgumentException("Octave modifiers require buttons without note assignments.", nameof(TemporaryOctaveModifiers));
            if (shift is < -10 or > 10 || shift == 0)
                throw new ArgumentOutOfRangeException(nameof(TemporaryOctaveModifiers));
        }
        foreach (var (control, action) in Mappings)
        {
            if (!Enum.IsDefined(control) || control >= PhysicalControl.LeftTrigger)
                throw new ArgumentException("Note actions require a physical button.", nameof(Mappings));
            if (action is not (ScaleDegreeAction or FixedNoteAction))
                throw new ArgumentException("Unsupported or null mapping action.", nameof(Mappings));
        }
        foreach (var (control, shift) in TemporarySemitoneModifiers)
        {
            if (!Enum.IsDefined(control) || control >= PhysicalControl.LeftTrigger ||
                Mappings.ContainsKey(control) || TemporaryOctaveModifiers.ContainsKey(control))
                throw new ArgumentException("Semitone modifiers require buttons without note or octave assignments.", nameof(TemporarySemitoneModifiers));
            if (shift is < -127 or > 127 || shift == 0)
                throw new ArgumentOutOfRangeException(nameof(TemporarySemitoneModifiers));
        }
    }
}
