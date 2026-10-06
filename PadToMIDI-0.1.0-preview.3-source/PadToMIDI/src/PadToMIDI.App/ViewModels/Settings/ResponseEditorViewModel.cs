using PadToMIDI.Core.Configuration;

namespace PadToMIDI.App.ViewModels.Settings;

public sealed class ResponseEditorViewModel : ObservableObject
{
    private decimal? deadZone, sensitivity, smoothing, rate;
    private ResponseCurve curve;
    private bool invert;
    public static IReadOnlyList<ResponseCurve> Curves { get; } = Enum.GetValues<ResponseCurve>();
    public bool AllowInversion { get; }
    public decimal? DeadZone { get => deadZone; set => SetProperty(ref deadZone, value); }
    public decimal? Sensitivity { get => sensitivity; set => SetProperty(ref sensitivity, value); }
    public decimal? SmoothingMilliseconds { get => smoothing; set => SetProperty(ref smoothing, value); }
    public decimal? UpdateRateHz { get => rate; set => SetProperty(ref rate, value); }
    public ResponseCurve Curve { get => curve; set => SetProperty(ref curve, value); }
    public bool Invert { get => invert; set => SetProperty(ref invert, value); }

    public ResponseEditorViewModel(AnalogResponseConfiguration configuration, bool allowInversion)
    {
        AllowInversion = allowInversion;
        deadZone = (decimal)configuration.DeadZone; sensitivity = (decimal)configuration.Sensitivity;
        smoothing = (decimal)configuration.SmoothingMilliseconds; rate = (decimal)configuration.UpdateRateHz;
        curve = configuration.Curve; invert = configuration.Invert;
    }

    public AnalogResponseConfiguration Build()
    {
        var result = new AnalogResponseConfiguration
        {
            DeadZone = (float)EditorNumber.Required(DeadZone, "dead zone"),
            Sensitivity = (float)EditorNumber.Required(Sensitivity, "sensitivity"),
            SmoothingMilliseconds = (double)EditorNumber.Required(SmoothingMilliseconds, "smoothing time"),
            UpdateRateHz = (double)EditorNumber.Required(UpdateRateHz, "update rate"), Curve = Curve, Invert = Invert
        };
        result.Validate();
        return result;
    }
}
