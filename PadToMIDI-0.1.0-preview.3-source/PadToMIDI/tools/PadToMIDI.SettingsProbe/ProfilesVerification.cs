using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PadToMIDI.App.Services;
using PadToMIDI.App.ViewModels;
using PadToMIDI.App.Views;
using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Input;
using PadToMIDI.Core.Midi;
using PadToMIDI.Core.Musical;
using PadToMIDI.Core.Profiles;
using DesktopApp = PadToMIDI.App.App;

namespace PadToMIDI.SettingsProbe;

internal static class ProfilesVerification
{
    public static int Run()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "profile-probe-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var store = new FileProfileStore(Path.Combine(root, "library"));
        var seed = InstrumentProfile.Default with
        {
            Id = Guid.NewGuid(), Name = "Startup restored", Configuration = new() { BaseOctave = 5, FixedVelocity = 88 },
            ControllerName = "Profile test pad", MidiEndpointId = "profile-probe", MidiEndpointName = "Profile test endpoint"
        };
        store.SaveAsync(seed).GetAwaiter().GetResult(); store.WriteActiveIdAsync(seed.Id).GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine("artifacts", "profile-probe-path.txt"), Path.Combine(root, "library"));
        var input = new ProbeInput(); var output = new ProbeOutput();
        using var session = new InstrumentSession(input, output);
        input.Run(default); session.ConnectOutputAsync("profile-probe").AsTask().GetAwaiter().GetResult();
        var dialogs = new ProbeDialogs(Path.Combine(root, "export.json"));
        return AppBuilder.Configure(() => new DesktopApp(input, session, output, store, profileDialogs: dialogs)).UsePlatformDetect()
            .AfterSetup(_ => Dispatcher.UIThread.Post(async () => await Verify(input, session, output, dialogs, store, root)))
            .StartWithClassicDesktopLifetime([]);
    }

    private static async Task Verify(ProbeInput input, InstrumentSession session, ProbeOutput output, ProbeDialogs dialogs, FileProfileStore store, string root)
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        try
        {
            var main = (MainWindow)desktop.MainWindow!;
            var model = (MainWindowViewModel)main.DataContext!;
            var profiles = model.Profiles!;
            await Until(() => profiles.IsIdle && profiles.ActiveName == "Startup restored");
            Assert(session.CaptureSnapshot().State.BaseOctave == 5, "Saved profile restored by actual application startup");
            main.FindControl<Button>("OpenProfiles")!.Command!.Execute(null);
            await Until(() => desktop.Windows.OfType<ProfilesWindow>().Any());
            var window = desktop.Windows.OfType<ProfilesWindow>().Single();
            await Click(window, "ResetProfile", profiles);
            Assert(session.CaptureSnapshot().State.BaseOctave == 3, "Default reset");
            window.FindControl<TextBox>("ProfileName")!.Text = "Stage C Major";
            await Click(window, "CreateProfile", profiles);
            Guid id = profiles.Selected!.Id;
            Assert(profiles.Selected.Name == "Stage C Major", "Create uses actual name binding");
            input.Set(PhysicalControl.FaceSouth, 1);
            input.Set(PhysicalControl.RightTrigger, 1);
            input.Set(PhysicalControl.RightStickY, -1);
            var editor = new PadToMIDI.App.ViewModels.Settings.ConfigurationEditorViewModel(session.CaptureSnapshot().State.Configuration, session.UpdateConfiguration);
            editor.Root = editor.Roots.Single(root => root.Value == PitchClass.FSharp); editor.Scale = ScalePresets.Dorian;
            editor.BaseOctave = 4; editor.Velocity = 77; editor.Channel = 3; editor.Timbre.ControllerNumber = 11;
            var face = editor.Buttons.Single(button => button.Control == PhysicalControl.FaceSouth);
            face.Kind = PadToMIDI.App.ViewModels.Settings.ButtonAssignmentKind.FixedNote; face.Value = 64;
            editor.Apply();
            input.Set(PhysicalControl.LeftStickX, 1); input.Set(PhysicalControl.LeftStickX, 0);
            Assert(session.CaptureSnapshot().State.BaseOctave == 5, "Persistent octave moved before Save");
            await Click(window, "SaveProfile", profiles);
            var saved = (await store.ListAsync()).Profiles.Single(profile => profile.Id == id);
            Assert(saved.Configuration.BaseOctave == 5 && saved.Configuration.MidiChannel == 2 && saved.Configuration.Expression.Timbre.ControllerNumber == 11, "Save captures the persistent octave and applied settings");
            Assert(saved.ControllerName == "Profile test pad" && saved.MidiEndpointId == "profile-probe", "Device preferences saved");
            await Click(window, "ResetProfile", profiles);
            Assert(AssertSingleHeld(session).MidiNote == 55, "Profile switching retains old held note");
            input.Set(PhysicalControl.FaceSouth, 0);
            Assert(output.Messages.Contains(new(MidiEventKind.NoteOff, 0, 55, 0)), "Exact old release after profile reset");
            window.FindControl<ListBox>("ProfileList")!.SelectedItem = profiles.Profiles.Single(profile => profile.Id == id);
            Assert(session.CaptureSnapshot().State.Configuration.Root == PitchClass.C, "Selection alone does not load");
            await Click(window, "LoadProfile", profiles);
            input.Set(PhysicalControl.FaceSouth, 1);
            Assert(AssertSingleHeld(session).MidiNote == 64 && AssertSingleHeld(session).Channel == 2, "Loaded custom mapping plays on saved channel");
            await Click(window, "ExportProfile", profiles);
            Assert(File.Exists(dialogs.Path), "Export button routes through file dialog boundary");
            await Click(window, "ImportProfile", profiles);
            Assert(profiles.Selected!.Id != id && profiles.ActiveName == "Stage C Major", "Import copy does not overwrite or load");
            await Click(window, "DuplicateProfile", profiles);
            window.FindControl<TextBox>("ProfileName")!.Text = "Renamed copy";
            await Click(window, "RenameProfile", profiles);
            Assert(profiles.Selected!.Name == "Renamed copy", "Rename binding");
            await Click(window, "DeleteProfile", profiles);
            Assert(profiles.DeletePending, "Delete requires explicit confirmation");
            await Render(window, "profiles-delete");
            await Click(window, "ConfirmDeleteProfile", profiles);
            Assert(profiles.Profiles.All(profile => profile.Name != "Renamed copy"), "Delete confirmed stored copy");
            string invalid = Path.Combine(root, "invalid.json"); await File.WriteAllTextAsync(invalid, "{");
            dialogs.Path = invalid;
            await Click(window, "ImportProfile", profiles, expectError: true);
            Assert(profiles.HasError && AssertSingleHeld(session).MidiNote == 64, "Bad import leaves playing state intact");
            await Render(window, "profiles-validation");
            await Click(window, "ProfilePanic", profiles, expectError: true);
            Assert(session.CaptureSnapshot().State.HeldNotes.IsEmpty, "Dialog Panic after storage error");
            var missing = InstrumentProfile.Default with { Id = Guid.NewGuid(), Name = "Missing devices", ControllerName = "Absent pad", MidiEndpointId = "absent-endpoint" };
            await store.SaveAsync(missing); await profiles.RefreshAsync(missing.Id);
            await Click(window, "LoadProfile", profiles);
            Assert(!output.IsConnected && model.Midi!.SelectedEndpoint is null && profiles.Status.Contains("unavailable"), "Unavailable routes disconnect safely and require manual selection");
            window.FindControl<ListBox>("ProfileList")!.SelectedItem = profiles.Profiles.Single(profile => profile.Id == id);
            await Click(window, "LoadProfile", profiles);
            Assert(!output.IsConnected && profiles.Status.Contains("choose Connect"), "Profile load selects a MIDI route without auto-connecting");
            await Render(window, "profiles-library");
            Assert(await store.ReadActiveIdAsync() == id, "Loaded profile remembered for next launch");
            window.Close(); await Task.Delay(150);
            await session.ConnectOutputAsync("profile-probe");
            input.Set(PhysicalControl.FaceSouth, 0);
            input.Set(PhysicalControl.FaceSouth, 1);
            Assert(AssertSingleHeld(session).MidiNote == 64, "Main Panic starts with an active note");
            main.FindControl<Button>("GlobalPanic")!.Command!.Execute(null);
            Assert(session.CaptureSnapshot().State.HeldNotes.IsEmpty, "Main window Panic");
            main.FindControl<Button>("OpenHelp")!.Command!.Execute(null);
            await Until(() => desktop.Windows.OfType<HelpWindow>().Any());
            var help = desktop.Windows.OfType<HelpWindow>().Single();
            input.Set(PhysicalControl.FaceSouth, 0); input.Set(PhysicalControl.FaceSouth, 1);
            Assert(AssertSingleHeld(session).MidiNote == 64, "Help Panic starts with an active note");
            help.FindControl<Button>("HelpPanic")!.Command!.Execute(null);
            Assert(session.CaptureSnapshot().State.HeldNotes.IsEmpty, "Help window Panic");
            await Render(help, "milestone-8-help"); help.Close();
            await Render(main, "milestone-8-main");
            main.Width = 920; main.Height = 700;
            await Render(main, "milestone-8-compact");
            // Save a mode set by physical Start, not only one applied through Settings.
            var chordConfiguration = session.CaptureSnapshot().State.Configuration with
                { Chords = new() { Shape = ChordShape.Minor } };
            session.UpdateConfiguration(chordConfiguration);
            input.Set(PhysicalControl.Start,0); input.Set(PhysicalControl.Start,1);
            Assert(session.CaptureSnapshot().State.Configuration.Chords.Enabled, "Physical mode set before profile Save");
            profiles.SaveCommand.Execute(null); await Until(() => profiles.IsIdle);
            Assert(!profiles.HasError, profiles.Status);
            var chordSaved = (await store.ListAsync()).Profiles.Single(profile => profile.Id == id);
            Assert(chordSaved.Configuration.Chords.Enabled && chordSaved.Configuration.Chords.Shape == ChordShape.Minor, "Profile saves live mode and chord shape");
            profiles.ResetCommand.Execute(null); await Until(() => profiles.IsIdle);
            Assert(!session.CaptureSnapshot().State.Configuration.Chords.Enabled, "Default profile resets to note mode");
            profiles.Selected = profiles.Profiles.Single(profile => profile.Id == id);
            profiles.LoadCommand.Execute(null); await Until(() => profiles.IsIdle);
            Assert(session.CaptureSnapshot().State.Configuration.Chords.Enabled, "Profile restores chord mode");
            input.Set(PhysicalControl.FaceSouth,0); input.Set(PhysicalControl.FaceSouth,1);
            Assert(session.CaptureSnapshot().State.HeldNotes.Length == 3, "Restored explicit shape plays fixed-note chord");
            await Render(main,"chord-mode-profiles-compact"); session.Panic();
            Console.WriteLine("Profiles/release UI: startup restore, real bindings, profile management, damaged JSON, exact releases, preferences, main/Help Panic and normal/compact layouts PASS.");
            desktop.Shutdown(0);
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); session.Panic(); desktop.Shutdown(1); }
    }

    private static NoteRecord AssertSingleHeld(InstrumentSession session) => session.CaptureSnapshot().State.HeldNotes.Single();
    private static async Task Click(Window window, string name, ProfilesViewModel model, bool expectError = false)
    {
        window.FindControl<Button>(name)!.Command!.Execute(null);
        await Until(() => model.IsIdle);
        if (!expectError) Assert(!model.HasError, model.Status);
    }
    private static async Task Until(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!predicate()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Profile UI operation timed out."); await Task.Delay(20); }
        await Task.Delay(50);
    }
    private static async Task Render(Window window, string name)
    {
        await Task.Delay(100);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(Path.Combine("artifacts", name + ".png"), PngBitmapEncoderOptions.Default);
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class ProbeDialogs(string path) : IProfileFileDialogs
    {
        public string Path { get; set; } = path;
        public Task<string?> ImportAsync() => Task.FromResult<string?>(Path);
        public Task<string?> ExportAsync(string suggestedName) => Task.FromResult<string?>(Path);
    }
    private sealed class ProbeInput : IGamepadInput
    {
        private readonly GamepadStateBuffer buffer = new();
        public event Action<GamepadInputEvent>? InputReceived;
        public void Run(CancellationToken token)
        {
            buffer.SetDevices([new(new(1), "Profile test pad")]);
            buffer.Apply(new(GamepadEventKind.Selected, new(1)));
            InputReceived?.Invoke(new(GamepadEventKind.Selected, new(1)));
        }
        public void Set(PhysicalControl control, float value)
        {
            var input = new GamepadInputEvent(GamepadEventKind.ControlChanged, new(1), control, value);
            buffer.Apply(input); InputReceived?.Invoke(input);
        }
        public void SelectDevice(GamepadDeviceId? id)
        {
            var old = buffer.CaptureSnapshot().SelectedDeviceId;
            if (old == id) return;
            if (old is { } previous) Publish(new(GamepadEventKind.Disconnected, previous));
            if (id is { } current) Publish(new(GamepadEventKind.Selected, current));
        }
        private void Publish(GamepadInputEvent input) { buffer.Apply(input); InputReceived?.Invoke(input); }
        public GamepadInputSnapshot CaptureSnapshot() => buffer.CaptureSnapshot();
    }
    private sealed class ProbeOutput : IMidiOutput
    {
        public ConcurrentQueue<MidiEvent> Messages { get; } = new();
        public bool IsConnected { get; private set; }
        public event Action? Faulted { add { } remove { } }
        public bool TrySend(in MidiEvent message) { Messages.Enqueue(message); return true; }
        public MidiOutputSnapshot CaptureSnapshot() => new(IsConnected ? MidiOutputState.Connected : MidiOutputState.Ready, "Profile test output", IsConnected ? "profile-probe" : null);
        public ValueTask<IReadOnlyList<MidiEndpoint>> GetEndpointsAsync(CancellationToken token = default) => ValueTask.FromResult<IReadOnlyList<MidiEndpoint>>([new("profile-probe", "Profile test endpoint")]);
        public ValueTask ConnectAsync(string id, CancellationToken token = default) { IsConnected = true; return ValueTask.CompletedTask; }
        public ValueTask DisconnectAsync(CancellationToken token = default) { IsConnected = false; return ValueTask.CompletedTask; }
        public ValueTask FlushAsync(CancellationToken token = default) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
