using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Input;

namespace PadToMIDI.App.ViewModels.Settings;

public sealed class StickEditorViewModel : ObservableObject
{
    private PhysicalControl xAxis, yAxis;
    private decimal? threshold, deadZone;
    private StickAction up, down, left, right;
    public static IReadOnlyList<PhysicalControl> XAxes { get; } = [PhysicalControl.LeftStickX, PhysicalControl.RightStickX];
    public static IReadOnlyList<PhysicalControl> YAxes { get; } = [PhysicalControl.LeftStickY, PhysicalControl.RightStickY];
    public static IReadOnlyList<StickAction> Actions { get; } = Enum.GetValues<StickAction>();
    public PhysicalControl XAxis { get => xAxis; set => SetProperty(ref xAxis, value); }
    public PhysicalControl YAxis { get => yAxis; set => SetProperty(ref yAxis, value); }
    public decimal? Threshold { get => threshold; set => SetProperty(ref threshold, value); }
    public decimal? DeadZone { get => deadZone; set => SetProperty(ref deadZone, value); }
    public StickAction Up { get => up; set => SetProperty(ref up, value); }
    public StickAction Down { get => down; set => SetProperty(ref down, value); }
    public StickAction Left { get => left; set => SetProperty(ref left, value); }
    public StickAction Right { get => right; set => SetProperty(ref right, value); }

    public StickEditorViewModel(DiscreteStickConfiguration configuration)
    {
        xAxis = configuration.XAxis; yAxis = configuration.YAxis; threshold = (decimal)configuration.Threshold;
        deadZone = (decimal)configuration.DeadZone; up = configuration.Up; down = configuration.Down;
        left = configuration.Left; right = configuration.Right;
    }
    public DiscreteStickConfiguration Build()
    {
        var result = new DiscreteStickConfiguration
        {
            XAxis = XAxis, YAxis = YAxis, Threshold = (float)EditorNumber.Required(Threshold, "stick threshold"),
            DeadZone = (float)EditorNumber.Required(DeadZone, "stick dead zone"), Up = Up, Down = Down, Left = Left, Right = Right
        };
        if (result.DeadZone >= result.Threshold) throw new InvalidOperationException("Stick dead zone must be smaller than its action threshold.");
        result.Validate(); return result;
    }
}
