using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Input;

namespace PadToMIDI.App.ViewModels.Settings;

public sealed class AftertouchEditorViewModel : ObservableObject
{
    private bool enabled;
    private PhysicalControl source;
    private decimal? threshold;
    private readonly TriggerExpressionMode mode;
    public string Title { get; }
    public static IReadOnlyList<PhysicalControl> Sources { get; } = [PhysicalControl.LeftTrigger, PhysicalControl.RightTrigger];
    public bool Enabled { get => enabled; set => SetProperty(ref enabled, value); }
    public PhysicalControl Source { get => source; set => SetProperty(ref source, value); }
    public decimal? MinimumThreshold { get => threshold; set => SetProperty(ref threshold, value); }
    public ResponseEditorViewModel Response { get; }

    public AftertouchEditorViewModel(string title, AftertouchConfiguration configuration)
    {
        Title = title; enabled = configuration.Enabled; source = configuration.Source;
        threshold = (decimal)configuration.MinimumThreshold; mode = configuration.Mode;
        Response = new(configuration.Response, false);
    }
    public AftertouchConfiguration Build()
    {
        var result = new AftertouchConfiguration
        {
            Enabled = Enabled, Source = Source, Mode = mode,
            MinimumThreshold = (float)EditorNumber.Required(MinimumThreshold, $"{Title} threshold"), Response = Response.Build()
        };
        result.Validate(); return result;
    }
}

public sealed class AxisExpressionEditorViewModel : ObservableObject
{
    private bool enabled;
    private PhysicalControl source;
    private decimal? controllerNumber;
    public string Title { get; }
    public bool IsTimbre { get; }
    public static IReadOnlyList<PhysicalControl> Sources { get; } =
        [PhysicalControl.LeftStickX, PhysicalControl.LeftStickY, PhysicalControl.RightStickX, PhysicalControl.RightStickY];
    public bool Enabled { get => enabled; set => SetProperty(ref enabled, value); }
    public PhysicalControl Source { get => source; set => SetProperty(ref source, value); }
    public decimal? ControllerNumber { get => controllerNumber; set => SetProperty(ref controllerNumber, value); }
    public ResponseEditorViewModel Response { get; }

    public AxisExpressionEditorViewModel(PitchBendConfiguration configuration)
    {
        Title = "Pitch bend"; enabled = configuration.Enabled; source = configuration.Source;
        Response = new(configuration.Response, true);
    }
    public AxisExpressionEditorViewModel(TimbreConfiguration configuration)
    {
        Title = "Timbre"; IsTimbre = true; enabled = configuration.Enabled; source = configuration.Source;
        controllerNumber = configuration.ControllerNumber; Response = new(configuration.Response, true);
    }
    public PitchBendConfiguration BuildPitchBend()
    {
        var result = new PitchBendConfiguration { Enabled = Enabled, Source = Source, Response = Response.Build() };
        result.Validate(); return result;
    }
    public TimbreConfiguration BuildTimbre()
    {
        var result = new TimbreConfiguration
        {
            Enabled = Enabled, Source = Source, Response = Response.Build(),
            ControllerNumber = (byte)EditorNumber.Integer(ControllerNumber, 0, 119, "timbre CC")
        };
        result.Validate(); return result;
    }
}
