using System.Text;
using System.Text.Json;
using PadToMIDI.Core.Profiles;

namespace PadToMIDI.App.Services;

/// <summary>GUID file names and same-directory replacement keep names portable and writes recoverable.</summary>
public sealed class FileProfileStore(string directory, string? migrationSource = null) : IProfileStore
{
    private readonly string directory = Path.GetFullPath(directory);
    private readonly SemaphoreSlim writes = new(1);
    private readonly string? migrationSource = migrationSource is null ? null : Path.GetFullPath(migrationSource);
    private bool imported;
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PadToMIDI", "Profiles");
    public static FileProfileStore CreateDefault() => new(DefaultDirectory,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GamepadMidi", "Profiles"));

    private Task ImportLegacyAsync() => migrationSource is null || imported ? Task.CompletedTask : Task.Run(ImportLegacyCoreAsync);

    private async Task ImportLegacyCoreAsync()
    {
        await writes.WaitAsync();
        try
        {
            string marker = Path.Combine(directory, ".legacy-imported");
            if (imported || File.Exists(marker) || !Directory.Exists(migrationSource)) { imported = true; return; }
            Directory.CreateDirectory(directory);
            // Copy, never move or overwrite: preserve originals and prefer any existing renamed profile.
            foreach (string source in Directory.EnumerateFiles(migrationSource!, "*.json"))
            {
                string destination = Path.Combine(directory, Path.GetFileName(source));
                if (!File.Exists(destination)) File.Copy(source, destination, overwrite: false);
            }
            string oldActive = Path.Combine(migrationSource!, "last-profile.txt");
            string newActive = Path.Combine(directory, "last-profile.txt");
            if (File.Exists(oldActive) && !File.Exists(newActive)) File.Copy(oldActive, newActive, overwrite: false);
            await File.WriteAllTextAsync(marker, "Previous profile library copied without replacing existing files.");
            imported = true;
        }
        finally { writes.Release(); }
    }
    public bool OwnsPath(string path)
    {
        string relative = Path.GetRelativePath(directory, Path.GetFullPath(path));
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    public Task<ProfileCatalog> ListAsync() => Task.Run(ListCoreAsync);

    private async Task<ProfileCatalog> ListCoreAsync()
    {
        await ImportLegacyAsync();
        var profiles = new List<InstrumentProfile> { InstrumentProfile.Default };
        var errors = new List<string>();
        if (!Directory.Exists(directory)) return new(profiles, errors);
        foreach (string path in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            try
            {
                var profile = ProfileJson.Deserialize(await ReadJsonAsync(path));
                if (Path.GetFileNameWithoutExtension(path) != profile.Id.ToString("D") || profile.Id == InstrumentProfile.DefaultId)
                    throw new JsonException("Profile ID does not match its file name or conflicts with the built-in default.");
                profiles.Add(profile);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or DecoderFallbackException)
            { errors.Add($"{Path.GetFileName(path)}: {error.Message}"); }
        }
        return new(profiles.OrderBy(profile => profile.Id != InstrumentProfile.DefaultId).ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase).ToArray(), errors);
    }

    public Task SaveAsync(InstrumentProfile profile)
    {
        if (profile.Id == InstrumentProfile.DefaultId) throw new InvalidOperationException("The built-in default is read-only. Create or duplicate a profile first.");
        return WriteAtomicAsync(ProfilePath(profile.Id), ProfileJson.Serialize(profile));
    }

    public async Task DeleteAsync(Guid id)
    {
        if (id == InstrumentProfile.DefaultId) throw new InvalidOperationException("The built-in default cannot be deleted.");
        await ImportLegacyAsync();
        await writes.WaitAsync();
        try { File.Delete(ProfilePath(id)); }
        finally { writes.Release(); }
    }

    public async Task<Guid?> ReadActiveIdAsync()
    {
        await ImportLegacyAsync();
        string path = Path.Combine(directory, "last-profile.txt");
        if (!File.Exists(path)) return null;
        string text = await ReadJsonAsync(path);
        return Guid.TryParse(text.Trim(), out var id) && id != Guid.Empty ? id : throw new IOException("The last-profile marker is invalid. Choose a profile and Load to repair it.");
    }

    public Task WriteActiveIdAsync(Guid id) => id == Guid.Empty
        ? throw new ArgumentException("A profile needs a nonempty ID.")
        : WriteAtomicAsync(Path.Combine(directory, "last-profile.txt"), id.ToString("D"));

    private string ProfilePath(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("A profile needs a nonempty ID.");
        return Path.Combine(directory, id.ToString("D") + ".json");
    }

    public static async Task<string> ReadJsonAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        if (stream.Length > ProfileJson.MaximumBytes) throw new IOException("Profile exceeds the 1 MiB limit.");
        byte[] bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes);
        return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
    }

    public Task WriteAtomicAsync(string path, string content) => Task.Run(() => WriteAtomicCoreAsync(path, content));

    private async Task WriteAtomicCoreAsync(string path, string content)
    {
        await ImportLegacyAsync();
        await writes.WaitAsync();
        string? temporary = null;
        try
        {
            string absolute = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            temporary = absolute + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, absolute, overwrite: true);
            temporary = null;
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            writes.Release();
        }
    }
}
