using Avalonia;
using PadToMIDI.App.Services;
using PadToMIDI.Input.Sdl;
using PadToMIDI.Midi.Windows;

namespace PadToMIDI.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception error) { StartupDiagnostics.Report(error, args.Contains("--smoke-test")); return 1; }
    }

    private static int Run(string[] args)
    {
        if (args.Contains("--version")) { Console.WriteLine(AppIdentity.DisplayName); return 0; }
        if (args.Contains("--print-default-profile"))
        {
            Console.Write(Core.Profiles.ProfileJson.Serialize(Core.Profiles.InstrumentProfile.Default));
            return 0;
        }
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The application host targets Windows.");
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26200) ||
            Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "InstallationType", null) is not "Client")
        {
            StartupDiagnostics.Show("PadToMIDI requires Windows 11 25H2 or newer (x64).", args.Contains("--smoke-test"));
            return 2;
        }
        // Prevent a renamed build from competing with an older running version for MIDI notes.
        if (Mutex.TryOpenExisting("Local\\GamepadMidi.Desktop", out var previousInstance))
        {
            previousInstance.Dispose();
            StartupDiagnostics.Show("A previous version is running. Close it before starting PadToMIDI.", args.Contains("--smoke-test"));
            return 3;
        }
        using var instance = new Mutex(initiallyOwned: true, "Local\\PadToMIDI.Desktop", out bool firstInstance);
        if (!firstInstance)
        {
            StartupDiagnostics.Show("PadToMIDI is already running. Use the existing window.", args.Contains("--smoke-test"));
            return 3;
        }

        using var shutdown = new CancellationTokenSource();
        var input = new SdlGamepadInput();
        var output = new WindowsMidiOutput();
        using var instrument = new InstrumentSession(input, output);
        int profileDirectoryIndex = Array.IndexOf(args, "--profile-directory");
        Core.Profiles.IProfileStore? profiles = profileDirectoryIndex < 0 ? null :
            profileDirectoryIndex + 1 < args.Length ? new FileProfileStore(args[profileDirectoryIndex + 1]) :
            throw new ArgumentException("--profile-directory requires a path.");
        int exitCode = 0;
        var uiThread = new Thread(() =>
        {
            try
            {
                exitCode = AppBuilder.Configure(() => new App(input, instrument, output, profiles))
                    .UsePlatformDetect()
                    .LogToTrace()
                    .StartWithClassicDesktopLifetime(args);
            }
            catch (Exception error)
            {
                StartupDiagnostics.Report(error, args.Contains("--smoke-test"));
                exitCode = 1;
            }
            finally { shutdown.Cancel(); }
        }) { Name = "Avalonia UI" };
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();

        // SDL_Init and its event pump stay on the process main thread. UI latency
        // cannot hold up the input path; only CaptureSnapshot crosses into the UI.
        try
        {
            input.Run(shutdown.Token);
            uiThread.Join();
        }
        finally
        {
            shutdown.Cancel();
            if (uiThread.IsAlive) uiThread.Join();
            instrument.Dispose();
            output.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        if (input.CaptureSnapshot().Status == Core.Input.InputStatus.Faulted)
            exitCode = 1;
        return exitCode;
    }
}
