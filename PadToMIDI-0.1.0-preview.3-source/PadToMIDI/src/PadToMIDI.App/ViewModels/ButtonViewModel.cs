using PadToMIDI.Core.Input;

namespace PadToMIDI.App.ViewModels;

public sealed class ButtonViewModel(PhysicalControl control) : ObservableObject
{
    private bool isPressed;
    public PhysicalControl Control { get; } = control;
    public string Name => Control.ToString();
    public bool IsPressed
    {
        get => isPressed;
        set
        {
            if (SetProperty(ref isPressed, value))
                Notify(nameof(StateLabel));
        }
    }
    public string StateLabel => IsPressed ? "HELD" : "—";
}
