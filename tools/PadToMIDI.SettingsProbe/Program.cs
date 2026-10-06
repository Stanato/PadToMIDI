using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DesktopApp = PadToMIDI.App.App;
using PadToMIDI.App.Services;
using PadToMIDI.App.ViewModels;
using PadToMIDI.App.ViewModels.Settings;
using PadToMIDI.App.Views;
using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Input;
using PadToMIDI.Core.Midi;
using PadToMIDI.Core.Musical;

namespace PadToMIDI.SettingsProbe;

/// <summary>Real desktop controls and bindings; synthetic input/output keep the check hardware-free.</summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--profiles-test")) return ProfilesVerification.Run();
        var input = new ProbeInput();
        var output = new ProbeOutput();
        using var session = new InstrumentSession(input, output);
        session.ConnectOutputAsync("probe").AsTask().GetAwaiter().GetResult();
        input.Run(default);
        input.Set(PhysicalControl.FaceSouth, 1);
        input.Set(PhysicalControl.RightTrigger, 1);
        input.Set(PhysicalControl.RightStickX, 1);
        input.Set(PhysicalControl.RightStickY, -1);
        return AppBuilder.Configure(() => new DesktopApp(input, session, output, enableProfiles: false)).UsePlatformDetect()
            .AfterSetup(_ => Dispatcher.UIThread.Post(async () => await Verify(input, session, output)))
            .StartWithClassicDesktopLifetime([]);
    }

    private static async Task Verify(ProbeInput input, InstrumentSession session, ProbeOutput output)
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        try
        {
            await Task.Delay(200);
            var main = (MainWindow)desktop.MainWindow!;
            main.FindControl<Button>("OpenSettings")!.Command!.Execute(null);
            await Task.Delay(200);
            var window = desktop.Windows.OfType<SettingsWindow>().Single();
            var editor = (ConfigurationEditorViewModel)window.DataContext!;
            Assert(editor.Scales.Count == 13, "Scale preset catalog");
            window.FindControl<ComboBox>("RootSelector")!.SelectedItem = editor.Roots.Single(root => root.Value == PitchClass.FSharp);
            window.FindControl<ComboBox>("ScaleSelector")!.SelectedItem = ScalePresets.Dorian;
            window.FindControl<NumericUpDown>("OctaveInput")!.Value = 4;
            window.FindControl<NumericUpDown>("VelocityInput")!.Value = 77;
            window.FindControl<NumericUpDown>("ChannelInput")!.Value = 3;
            editor.Stick.DeadZone = 0.8m;
            editor.Stick.Threshold = 0.7m;
            var original = session.CaptureSnapshot();
            window.FindControl<Button>("ApplySettings")!.Command!.Execute(null);
            Assert(editor.Status.StartsWith("Settings were not applied:"), "Inline validation error");
            Assert(session.CaptureSnapshot().State.Configuration == original.State.Configuration, "Invalid draft stayed unapplied");
            Assert(session.CaptureSnapshot().EventCount == original.EventCount, "Validation generated no MIDI");
            await Render(window, "settings-validation");

            editor.Stick.DeadZone = 0.2m;
            var tabs = window.FindControl<TabControl>("SettingsTabs")!;
            tabs.SelectedIndex = 1;
            await Task.Delay(100);
            var face = editor.Buttons.Single(button => button.Control == PhysicalControl.FaceSouth);
            var selector = window.GetVisualDescendants().OfType<ComboBox>().Single(control => ReferenceEquals(control.DataContext, face));
            selector.SelectedItem = ButtonAssignmentKind.FixedNote;
            var value = window.GetVisualDescendants().OfType<NumericUpDown>().Single(control => ReferenceEquals(control.DataContext, face));
            value.Value = 64;
            Assert(face.Kind == ButtonAssignmentKind.FixedNote && face.Value == 64, "Mapping controls changed the draft");
            editor.Timbre.ControllerNumber = 11;
            editor.PitchBend.Response.Invert = true;
            editor.PitchBend.Response.Sensitivity = 0.5m;
            editor.PitchBend.Response.DeadZone = 0.15m;
            window.FindControl<Button>("ApplySettings")!.Command!.Execute(null);
            var applied = session.CaptureSnapshot().State;
            Assert(applied.Configuration.Root == PitchClass.FSharp && applied.Configuration.Scale == ScalePresets.Dorian &&
                applied.Configuration.BaseOctave == 4 && applied.Configuration.FixedVelocity == 77 && applied.Configuration.MidiChannel == 2, "Scale/velocity/channel controls applied");
            Assert(applied.Configuration.Expression.Timbre.ControllerNumber == 11 && applied.Expression.PitchBend == 4096, "Expression edits applied");
            Assert(applied.HeldNotes.Single().MidiNote == 55 && applied.HeldNotes.Single().Channel == 0, "Held note kept original identity");
            input.Set(PhysicalControl.FaceSouth, 0);
            Assert(output.Messages.Contains(new(MidiEventKind.NoteOff, 0, 55, 0)), "Original note released after editing");
            input.Set(PhysicalControl.FaceSouth, 1);
            Assert(output.Messages.Contains(new(MidiEventKind.NoteOn, 2, 64, 77)), "Fixed mapping affected the next press");
            input.Set(PhysicalControl.FaceSouth, 0);
            input.Set(PhysicalControl.DPadDown, 1);
            Assert(output.Messages.Contains(new(MidiEventKind.NoteOn, 2, 66, 77)), "F sharp Dorian scale degree played");
            Assert(output.Messages.Contains(new(MidiEventKind.ControlChange, 0, 74, 64, 64)) &&
                output.Messages.Contains(new(MidiEventKind.ControlChange, 2, 11, 127, 64)), "Old CC restored, new CC applied");
            for (int index = 0; index < 4; index++)
            {
                tabs.SelectedIndex = index;
                await Render(window, new[] { "settings-scale", "settings-buttons", "settings-stick", "settings-expression" }[index]);
            }
            window.FindControl<Button>("SettingsPanic")!.Command!.Execute(null);
            Assert(session.CaptureSnapshot().State.HeldNotes.IsEmpty, "Panic available while settings are open");
            input.Set(PhysicalControl.DPadDown, 0);
            input.Set(PhysicalControl.DPadDown, 1);
            window.FindControl<Button>("ResetSettings")!.Command!.Execute(null);
            Assert(editor.Root!.Value == PitchClass.C && editor.Stick.Left == StickAction.DecreaseOctave &&
                editor.Stick.Right == StickAction.IncreaseOctave && editor.Stick.Up == StickAction.MomentaryOctaveUp &&
                editor.Stick.Down == StickAction.MomentaryOctaveDown, "Reset draft uses current defaults");
            Assert(session.CaptureSnapshot().State.Configuration.Root == PitchClass.FSharp, "Reset draft did not apply");
            editor.CloseCommand.Execute(null);
            await Task.Delay(100);
            Assert(session.CaptureSnapshot().State.Configuration.Root == PitchClass.FSharp, "Close discarded draft reset");
            main.FindControl<Button>("OpenSettings")!.Command!.Execute(null);
            await Task.Delay(100);
            window = desktop.Windows.OfType<SettingsWindow>().Single();
            editor = (ConfigurationEditorViewModel)window.DataContext!;
            Assert(editor.Root!.Value == PitchClass.FSharp && editor.Timbre.ControllerNumber == 11, "Reopening loaded applied settings");
            window.FindControl<Button>("ResetSettings")!.Command!.Execute(null);
            window.FindControl<Button>("ApplySettings")!.Command!.Execute(null);
            input.Set(PhysicalControl.DPadDown, 0);
            Assert(output.Messages.Contains(new(MidiEventKind.NoteOff, 2, 66, 0)), "Reset-to-default retained old release identity");
            editor.CloseCommand.Execute(null);
            await Render(main, "milestone-6-main");
            var mainModel = (MainWindowViewModel)main.DataContext!;
            Assert(mainModel.Midi!.Channel == 1, "Channel observer stayed synchronized");
            main.FindControl<Button>("OpenSettings")!.Command!.Execute(null);
            await Task.Delay(100);
            window = desktop.Windows.OfType<SettingsWindow>().Single();
            editor = (ConfigurationEditorViewModel)window.DataContext!;
            window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 4;
            window.FindControl<CheckBox>("ChordEnabled")!.IsChecked = true;
            window.FindControl<ComboBox>("ChordShape")!.SelectedItem = PadToMIDI.Core.Configuration.ChordShape.ScaleTriad;
            window.FindControl<Button>("ApplySettings")!.Command!.Execute(null);
            Assert(session.CaptureSnapshot().State.Configuration.Chords.Enabled, "Chord checkbox binding applied");
            input.Set(PhysicalControl.FaceSouth, 1);
            Assert(session.CaptureSnapshot().State.HeldNotes.Select(note => note.MidiNote).SequenceEqual(new byte[] {55,59,62}), "Real chord setting starts full G triad");
            await Render(window, "settings-chords");
            input.Set(PhysicalControl.Start, 1);
            Assert(!session.CaptureSnapshot().State.Configuration.Chords.Enabled, "Controller Start toggles mode outside the UI path");
            Assert(session.CaptureSnapshot().State.HeldNotes.Length == 3, "Toggle leaves held chord intact");
            input.Set(PhysicalControl.FaceSouth, 0);
            input.Set(PhysicalControl.Start, 0); input.Set(PhysicalControl.Start, 1);
            input.Set(PhysicalControl.FaceWest, 1);
            editor.CloseCommand.Execute(null);
            await Render(main, "chord-mode-main");
            Assert(mainModel.PlayingModeLabel.StartsWith("Chord mode") && mainModel.LastNoteLabel.Contains("Bdim"), "Live mode and chord names observed");
            session.Panic();
            Console.WriteLine("Settings UI: all tabs rendered; real scale/mapping bindings, validation, Apply, held releases, expression cleanup, reset/discard/reopen, channel sync PASS.");
            desktop.Shutdown(0);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            desktop.Shutdown(1);
        }
    }

    private static async Task Render(Window window, string name)
    {
        await Task.Delay(100);
        Directory.CreateDirectory("artifacts");
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
        bitmap.Render(window);
        bitmap.Save($"artifacts/{name}.png", PngBitmapEncoderOptions.Default);
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ProbeInput : IGamepadInput
    {
        private readonly GamepadStateBuffer state = new();
        public event Action<GamepadInputEvent>? InputReceived;
        public GamepadInputSnapshot CaptureSnapshot() => state.CaptureSnapshot();
        public void SelectDevice(GamepadDeviceId? id) { }
        public void Run(CancellationToken cancellationToken)
        {
            state.SetDevices([new(new(1), "Settings verification controller")]);
            Publish(new(GamepadEventKind.Selected, new(1)));
        }
        public void Set(PhysicalControl control, float value) => Publish(new(GamepadEventKind.ControlChanged, new(1), control, value));
        private void Publish(GamepadInputEvent message) { state.Apply(message); InputReceived?.Invoke(message); }
    }

    private sealed class ProbeOutput : IMidiOutput
    {
        private string? endpoint;
        public ConcurrentQueue<MidiEvent> Messages { get; } = new();
        public bool IsConnected => endpoint is not null;
        public event Action? Faulted { add { } remove { } }
        public MidiOutputSnapshot CaptureSnapshot() => new(IsConnected ? MidiOutputState.Connected : MidiOutputState.Ready, "Verification output", endpoint, Messages.Count);
        public ValueTask<IReadOnlyList<MidiEndpoint>> GetEndpointsAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<MidiEndpoint>>([new("probe", "Settings verification output")]);
        public ValueTask ConnectAsync(string endpointId, CancellationToken cancellationToken = default) { endpoint = endpointId; return ValueTask.CompletedTask; }
        public ValueTask DisconnectAsync(CancellationToken cancellationToken = default) { endpoint = null; return ValueTask.CompletedTask; }
        public ValueTask FlushAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public bool TrySend(in MidiEvent message) { Messages.Enqueue(message); return true; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
