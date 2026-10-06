using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace PadToMIDI.App.Services;

public interface IProfileFileDialogs
{
    Task<string?> ImportAsync();
    Task<string?> ExportAsync(string suggestedName);
}

public sealed class ProfileFileDialogs(Func<TopLevel> owner) : IProfileFileDialogs
{
    private static readonly FilePickerFileType JsonType = new("PadToMIDI profile") { Patterns = ["*.json"] };

    public async Task<string?> ImportAsync()
    {
        var files = await owner().StorageProvider.OpenFilePickerAsync(new()
        { Title = "Import PadToMIDI profile", AllowMultiple = false, FileTypeFilter = [JsonType] });
        if (files.Count == 0) return null;
        return files[0].TryGetLocalPath() ?? throw new IOException("Choose a local JSON file.");
    }

    public async Task<string?> ExportAsync(string suggestedName)
    {
        var file = await owner().StorageProvider.SaveFilePickerAsync(new()
        { Title = "Export stored profile", SuggestedFileName = suggestedName, DefaultExtension = "json", FileTypeChoices = [JsonType], ShowOverwritePrompt = true });
        return file is null ? null : file.TryGetLocalPath() ?? throw new IOException("Choose a local destination.");
    }
}
