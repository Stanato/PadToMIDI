using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PadToMIDI.App.Services;
using PadToMIDI.App.ViewModels;
using PadToMIDI.App.ViewModels.Settings;
using PadToMIDI.App.Views;
using PadToMIDI.Core.Input;
using PadToMIDI.Core.Midi;
using PadToMIDI.Core.Profiles;

namespace PadToMIDI.App;

public partial class App : Application
{
    private readonly IGamepadInput? input;
    private readonly InstrumentSession? instrument;
    private readonly IMidiOutput? output;
    private readonly IProfileStore? profileStore;
    private readonly IProfileFileDialogs? profileDialogs;
    private readonly bool enableProfiles = true;

    // Avalonia's XAML loader/previewer requires a parameterless constructor.
    public App() { }

    public App(IGamepadInput input, InstrumentSession instrument, IMidiOutput? output = null, IProfileStore? profileStore = null, bool enableProfiles = true, IProfileFileDialogs? profileDialogs = null)
    {
        this.input = input;
        this.instrument = instrument;
        this.output = output;
        this.profileStore = profileStore;
        this.profileDialogs = profileDialogs;
        this.enableProfiles = enableProfiles;
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var controllerInput = input ?? throw new InvalidOperationException("The desktop host must supply IGamepadInput.");
            var session = instrument ?? throw new InvalidOperationException("The desktop host must supply InstrumentSession.");
            var midiMonitor = output is null ? null : new MidiMonitorService(output, session);
            MainWindow? window = null;
            ProfilesWindow? profilesWindow = null;
            MainWindowViewModel? model = null;
            var dialogs = profileDialogs ?? new ProfileFileDialogs(() => profilesWindow ?? (Avalonia.Controls.TopLevel)window!);
            ProfilesViewModel? profiles = !enableProfiles ? null : new(
                profileStore ?? FileProfileStore.CreateDefault(),
                () =>
                {
                    var snapshot = session.CaptureSnapshot().State;
                    var inputSnapshot = controllerInput.CaptureSnapshot();
                    return new InstrumentProfile
                    {
                        Version = InstrumentProfile.CurrentVersion, Id = Guid.NewGuid(), Name = "Current instrument",
                        Configuration = snapshot.Configuration with { BaseOctave = snapshot.BaseOctave },
                        ControllerName = inputSnapshot.Devices.FirstOrDefault(device => device.Id == inputSnapshot.SelectedDeviceId)?.Name,
                        MidiEndpointId = model?.Midi?.SelectedEndpoint?.Id,
                        MidiEndpointName = model?.Midi?.SelectedEndpoint?.Name
                    };
                }, session.ApplyProfile,
                async profile =>
                {
                    var messages = new List<string>();
                    if (profile.ControllerName is { } controllerName)
                    {
                        var inputSnapshot = controllerInput.CaptureSnapshot();
                        var matches = inputSnapshot.Devices.Where(device => device.Name == controllerName).ToArray();
                        if (matches.Length == 1)
                        {
                            if (inputSnapshot.SelectedDeviceId != matches[0].Id) controllerInput.SelectDevice(matches[0].Id);
                        }
                        else
                        {
                            controllerInput.SelectDevice(null);
                            messages.Add($"Controller '{controllerName}' is missing or ambiguous; select manually.");
                        }
                    }
                    if (profile.MidiEndpointId is { } endpointId && output is not null && model?.Midi is { } midi)
                    {
                        if (output.IsConnected && output.CaptureSnapshot().EndpointId != endpointId) await session.DisconnectOutputAsync();
                        midi.UpdateEndpoints(await output.GetEndpointsAsync());
                        midi.SelectedEndpoint = midi.Endpoints.FirstOrDefault(endpoint => endpoint.Id == endpointId);
                        messages.Add(midi.SelectedEndpoint is null ? "Saved MIDI endpoint unavailable; select another output." :
                            output.IsConnected ? "MIDI route retained." : "MIDI endpoint selected; choose Connect.");
                    }
                    return string.Join(" ", messages);
                }, session.Panic, dialogs.ImportAsync, dialogs.ExportAsync);
            model = new MainWindowViewModel(controllerInput.SelectDevice)
            {
                Midi = midiMonitor?.Model,
                Profiles = profiles,
                PanicCommand = new(() => { session.Panic(); return Task.CompletedTask; }, error => midiMonitor?.Model.ShowError(error)),
                HelpCommand = new(async () => await new HelpWindow { DataContext = model }.ShowDialog(window!), error => midiMonitor?.Model.ShowError(error)),
                ProfilesCommand = profiles is null ? null : new(async () =>
                {
                    profilesWindow = new ProfilesWindow { DataContext = profiles };
                    try { await profilesWindow.ShowDialog(window!); }
                    finally { profilesWindow = null; }
                }, error => midiMonitor?.Model.ShowError(error)),
                SettingsCommand = new(async () =>
                {
                    var editor = new ConfigurationEditorViewModel(session.CaptureSnapshot().State.Configuration, session.UpdateConfiguration, session.Panic);
                    var dialog = new SettingsWindow { DataContext = editor };
                    editor.CloseRequested += () => dialog.Close();
                    await dialog.ShowDialog(window!);
                }, error => midiMonitor?.Model.ShowError(error))
            };
            var monitor = new ControllerMonitorService(controllerInput, session, model);
            window = new MainWindow { DataContext = model };
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => { midiMonitor?.Dispose(); monitor.Dispose(); };
            monitor.Start();
            midiMonitor?.Start();
            profiles?.InitializeCommand.Execute(null);

            if (desktop.Args?.Contains("--smoke-test") == true)
            {
                // Developer startup check also produces a visual layout artifact.
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    try
                    {
                        Directory.CreateDirectory("artifacts");
                        using var bitmap = new RenderTargetBitmap(
                            new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
                        bitmap.Render(window);
                        bitmap.Save("artifacts/monitor-preview.png", PngBitmapEncoderOptions.Default);
                        var snapshot = controllerInput.CaptureSnapshot();
                        Console.WriteLine($"UI initialized. Input: {snapshot.Status}; controllers: {snapshot.Devices.Length}; " +
                            $"generated MIDI events: {session.CaptureSnapshot().EventCount}; MIDI: {output?.CaptureSnapshot().State}; {snapshot.Message}");
                        desktop.Shutdown(snapshot.Status == InputStatus.Faulted ? 1 : 0);
                    }
                    catch (Exception error)
                    {
                        Console.Error.WriteLine(error);
                        desktop.Shutdown(1);
                    }
                };
                timer.Start();
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
