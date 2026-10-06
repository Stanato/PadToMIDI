using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using PadToMIDI.App.Services;
using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Input;
using PadToMIDI.Core.Musical;

namespace PadToMIDI.App.ViewModels;

public sealed class MainWindowViewModel(Action<GamepadDeviceId?> selectDevice) : ObservableObject
{
    public MidiViewModel? Midi { get; init; }
    public AsyncCommand? SettingsCommand { get; init; }
    public AsyncCommand? ProfilesCommand { get; init; }
    public ProfilesViewModel? Profiles { get; init; }
    public AsyncCommand? HelpCommand { get; init; }
    public AsyncCommand? PanicCommand { get; init; }
    public string ApplicationName => AppIdentity.DisplayName;
    private ImmutableArray<GamepadDevice> lastDevices;
    private GamepadDevice? selectedDevice;
    private bool applyingSnapshot;
    private GamepadValues values;
    private InputStatus status;
    private string message = "Starting SDL3…";
    private long eventCount;
    private long midiEventCount = -1;
    private InstrumentConfiguration? configuration;
    private int baseOctave = int.MinValue;
    private int temporaryOctaveOffset = int.MinValue;
    private int temporarySemitoneOffset = int.MinValue;
    private string scaleLabel = "";
    private string settingsLabel = "";
    private string heldNotesLabel = "Held notes: none";
    private string lastNoteLabel = "Last note: none";
    private string dpadPressureLabel = "0 · no active D-pad note";
    private string facePressureLabel = "0 · no active face note";
    private string pitchBendLabel = "8192 · center";
    private string timbreLabel = "CC74 · 64";
    private string playingModeLabel = "Note mode · Start toggles chords";
    private string playingMessage = "";
    public string PlayingModeLabel => playingModeLabel;
    public string PlayingMessage => playingMessage;

    public ObservableCollection<GamepadDevice> Devices { get; } = [];
    public ObservableCollection<MappingPreviewViewModel> NoteMappings { get; } = [];
    public ObservableCollection<MidiActivityViewModel> MidiActivity { get; } = [];
    public string ScaleLabel => scaleLabel;
    public string SettingsLabel => settingsLabel;
    public string HeldNotesLabel => heldNotesLabel;
    public string LastNoteLabel => lastNoteLabel;
    public string DPadPressureLabel => dpadPressureLabel;
    public string FacePressureLabel => facePressureLabel;
    public string PitchBendLabel => pitchBendLabel;
    public string TimbreLabel => timbreLabel;
    public string MidiEventCountLabel => $"{Math.Max(0, midiEventCount):N0} generated MIDI events";
    public bool HasMidiActivity => midiEventCount > 0;
    public IReadOnlyList<ButtonViewModel> Buttons { get; } = Enum.GetValues<PhysicalControl>()
        .Where(control => control < PhysicalControl.LeftTrigger)
        .Select(control => new ButtonViewModel(control)).ToArray();

    public GamepadDevice? SelectedDevice
    {
        get => selectedDevice;
        set
        {
            if (SetProperty(ref selectedDevice, value) && !applyingSnapshot)
                selectDevice(value?.Id);
        }
    }

    public GamepadValues Values => values;
    public string ConnectionLabel => status switch
    {
        InputStatus.Connected => "CONNECTED",
        InputStatus.Faulted => "INPUT ERROR",
        InputStatus.Starting => "STARTING",
        InputStatus.Stopped => "STOPPED",
        _ => "WAITING FOR CONTROLLER"
    };
    public string Message => message;
    public string DeviceCountLabel => $"{Devices.Count} detected";
    public string EventCountLabel => $"{eventCount:N0} input changes";
    public string LeftTriggerLabel => values.LeftTrigger.ToString("P0", CultureInfo.InvariantCulture);
    public string RightTriggerLabel => values.RightTrigger.ToString("P0", CultureInfo.InvariantCulture);
    public string LeftStickLabel => $"X {values.LeftStickX:+0.000;-0.000;0.000}   Y {values.LeftStickY:+0.000;-0.000;0.000}";
    public string RightStickLabel => $"X {values.RightStickX:+0.000;-0.000;0.000}   Y {values.RightStickY:+0.000;-0.000;0.000}";

    public void Update(GamepadInputSnapshot snapshot)
    {
        applyingSnapshot = true;
        try
        {
            if (lastDevices != snapshot.Devices)
            {
                lastDevices = snapshot.Devices;
                Devices.Clear();
                foreach (var device in snapshot.Devices)
                    Devices.Add(device);
                Notify(nameof(DeviceCountLabel));
            }
            SelectedDevice = Devices.FirstOrDefault(device => device.Id == snapshot.SelectedDeviceId);
        }
        finally { applyingSnapshot = false; }

        if (SetProperty(ref status, snapshot.Status))
            Notify(nameof(ConnectionLabel));
        SetProperty(ref message, snapshot.Message, nameof(Message));
        if (SetProperty(ref eventCount, snapshot.EventCount))
            Notify(nameof(EventCountLabel));
        if (SetProperty(ref values, snapshot.Values, nameof(Values)))
        {
            Notify(nameof(LeftTriggerLabel));
            Notify(nameof(RightTriggerLabel));
            Notify(nameof(LeftStickLabel));
            Notify(nameof(RightStickLabel));
            foreach (var button in Buttons)
                button.IsPressed = values.IsPressed(button.Control);
        }
    }

    public void UpdateInstrument(InstrumentSnapshot snapshot)
    {
        var musical = snapshot.State;
        SetProperty(ref playingModeLabel, (musical.Configuration.Chords.Enabled ? "Chord mode" : "Note mode") +
            (musical.Configuration.Chords.ToggleButton is { } toggle ? $" · {toggle} toggles" : ""), nameof(PlayingModeLabel));
        SetProperty(ref playingMessage, musical.PlayingMessage, nameof(PlayingMessage));
        Midi?.ObserveChannel(musical.Configuration.MidiChannel);
        SetProperty(ref dpadPressureLabel, $"{musical.Expression.DPadPressure} · " +
            (musical.LastActiveDPadNote is { } dpad ? NoteDisplay.Gesture(dpad) : "no active D-pad note"), nameof(DPadPressureLabel));
        SetProperty(ref facePressureLabel, $"{musical.Expression.FacePressure} · " +
            (musical.LastActiveFaceNote is { } face ? NoteDisplay.Gesture(face) : "no active face note"), nameof(FacePressureLabel));
        SetProperty(ref pitchBendLabel, $"{musical.Expression.PitchBend} · " +
            (musical.Expression.PitchBend == 8192 ? "center" : "bend"), nameof(PitchBendLabel));
        SetProperty(ref timbreLabel, $"CC{musical.Configuration.Expression.Timbre.ControllerNumber} · {musical.Expression.Timbre}", nameof(TimbreLabel));
        if (configuration != musical.Configuration || baseOctave != musical.BaseOctave ||
            temporaryOctaveOffset != musical.TemporaryOctaveOffset || temporarySemitoneOffset != musical.TemporarySemitoneOffset)
        {
            configuration = musical.Configuration;
            baseOctave = musical.BaseOctave;
            temporaryOctaveOffset = musical.TemporaryOctaveOffset;
            temporarySemitoneOffset = musical.TemporarySemitoneOffset;
            SetProperty(ref scaleLabel, $"{NoteDisplay.RootName(configuration.Root, configuration.Scale)} {configuration.Scale.Name}", nameof(ScaleLabel));
            SetProperty(ref settingsLabel, $"Octave {baseOctave} · Shift {temporaryOctaveOffset:+0;-0;0} · Semitones {temporarySemitoneOffset:+0;-0;0} · Velocity {configuration.FixedVelocity} · Channel {configuration.MidiChannel + 1}", nameof(SettingsLabel));
            NoteMappings.Clear();
            foreach (var mapping in configuration.Mappings.OrderBy(pair => pair.Value is ScaleDegreeAction degree ? degree.Degree : int.MaxValue))
            {
                string action = mapping.Value is ScaleDegreeAction degree ? $"Degree {degree.Degree}" : "Fixed";
                string note = ChordResolver.TryResolve(configuration, mapping.Key, baseOctave, temporaryOctaveOffset, temporarySemitoneOffset, out var gesture, out var reason) ? NoteDisplay.Gesture(gesture) : reason;
                NoteMappings.Add(new(mapping.Key, action, note));
            }
        }

        string held = musical.HeldNotes.IsEmpty ? "Held notes: none" :
            $"Held notes: {string.Join(", ", musical.HeldNotes.Select(NoteDisplay.Name))}";
        SetProperty(ref heldNotesLabel, held, nameof(HeldNotesLabel));
        SetProperty(ref lastNoteLabel, musical.LastNote is { } last ? $"Last played: {NoteDisplay.Gesture(last)}" : "Last note: none", nameof(LastNoteLabel));
        if (midiEventCount != snapshot.EventCount)
        {
            midiEventCount = snapshot.EventCount;
            Notify(nameof(MidiEventCountLabel));
            Notify(nameof(HasMidiActivity));
            MidiActivity.Clear();
            foreach (var entry in snapshot.Activity) MidiActivity.Add(new(entry));
        }
    }
}
