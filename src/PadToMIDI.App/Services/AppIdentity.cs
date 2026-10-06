using System.Reflection;

namespace PadToMIDI.App.Services;

public static class AppIdentity
{
    public static string Version => typeof(AppIdentity).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "development";
    public static string DisplayName => $"PadToMIDI {Version}";
}
