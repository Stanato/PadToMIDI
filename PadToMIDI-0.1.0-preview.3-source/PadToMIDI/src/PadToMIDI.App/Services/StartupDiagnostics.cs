using System.Runtime.InteropServices;

namespace PadToMIDI.App.Services;

internal static class StartupDiagnostics
{
    public static void Show(string message, bool unattended)
    {
        Console.Error.WriteLine(message);
        if (!unattended && OperatingSystem.IsWindows()) MessageBox(0, message, "PadToMIDI", 0x10);
    }

    public static void Report(Exception error, bool unattended)
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PadToMIDI", "Logs");
        try
        {
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "startup.log"), $"{DateTimeOffset.UtcNow:O} {error}{Environment.NewLine}");
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { }
        Show($"PadToMIDI could not start: {error.Message}\nDetails: {directory}", unattended);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(nint owner, string text, string caption, uint type);
}
