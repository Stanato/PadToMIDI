using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Input;
using PadToMIDI.Core.Musical;

namespace PadToMIDI.App.ViewModels.Settings;

public sealed record HarmonyChoice(string Name, ScaleDefinition? Scale);
public sealed record ToggleChoice(string Name, PhysicalControl? Control);

public sealed class ChordEditorViewModel : ObservableObject
{
    private bool enabled;
    private ChordShape shape;
    private HarmonyChoice? harmony;
    private ToggleChoice? toggle;
    public bool Enabled { get => enabled; set => SetProperty(ref enabled, value); }
    public ChordShape Shape { get => shape; set => SetProperty(ref shape, value); }
    public HarmonyChoice? Harmony { get => harmony; set => SetProperty(ref harmony, value); }
    public ToggleChoice? Toggle { get => toggle; set => SetProperty(ref toggle, value); }
    public IReadOnlyList<ChordShape> Shapes { get; } = Enum.GetValues<ChordShape>();
    public IReadOnlyList<HarmonyChoice> Harmonies { get; }
    public IReadOnlyList<ToggleChoice> Toggles { get; } = new[] { new ToggleChoice("Disabled", null) }.Concat(
        Enum.GetValues<PhysicalControl>().Where(control => control < PhysicalControl.LeftTrigger).Select(control => new ToggleChoice(control.ToString(), control))).ToArray();

    public ChordEditorViewModel(ChordConfiguration configuration)
    {
        var scales = ScalePresets.All.Where(scale => scale.Intervals.Length == 7).ToList();
        if (configuration.HarmonyScale is { } custom && !scales.Contains(custom)) scales.Add(custom);
        Harmonies = new[] { new HarmonyChoice("Use selected scale", null) }.Concat(scales.Select(scale => new HarmonyChoice(scale.Name, scale))).ToArray();
        Enabled = configuration.Enabled; Shape = configuration.Shape;
        Harmony = Harmonies.Single(choice => choice.Scale == configuration.HarmonyScale);
        Toggle = Toggles.Single(choice => choice.Control == configuration.ToggleButton);
    }

    public ChordConfiguration Build() => new()
    {
        Enabled = Enabled, Shape = Shape,
        HarmonyScale = (Harmony ?? throw new InvalidOperationException("Choose a harmony scale.")).Scale,
        ToggleButton = (Toggle ?? throw new InvalidOperationException("Choose a chord toggle button.")).Control
    };
}
