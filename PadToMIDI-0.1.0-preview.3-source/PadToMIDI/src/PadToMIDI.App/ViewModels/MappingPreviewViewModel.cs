using PadToMIDI.Core.Input;

namespace PadToMIDI.App.ViewModels;

public sealed record MappingPreviewViewModel(PhysicalControl Control, string ActionLabel, string NoteLabel)
{
    public string ControlLabel => Control.ToString();
}
