using System.Text.Json;
using PadToMIDI.App.Services;
using PadToMIDI.App.ViewModels;
using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Profiles;
using Xunit;

namespace PadToMIDI.App.Tests;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "PadToMIDI.Tests." + Guid.NewGuid().ToString("N"));
    private string Library => Path.Combine(root, "library");

    [Fact]
    public async Task RenamedLibraryCopiesProfilesAndActiveSelectionWithoutChangingOriginals()
    {
        string previous = Path.Combine(root, "previous");
        var original = new FileProfileStore(previous);
        var profile = InstrumentProfile.Default with { Id = Guid.NewGuid(), Name = "Previous setup",
            Configuration = InstrumentProfile.Default.Configuration with { Chords = new() { Enabled = true } } };
        await original.SaveAsync(profile);
        await original.WriteActiveIdAsync(profile.Id);
        string originalJson = await File.ReadAllTextAsync(Path.Combine(previous, profile.Id + ".json"), TestContext.Current.CancellationToken);

        var renamed = new FileProfileStore(Library, previous);
        Assert.Equal(profile.Id, await renamed.ReadActiveIdAsync());
        var catalog = await renamed.ListAsync();
        Assert.Empty(catalog.Errors);
        Assert.Equal(originalJson, ProfileJson.Serialize(catalog.Profiles.Single(item => item.Id == profile.Id)));
        Assert.Equal(originalJson, await File.ReadAllTextAsync(Path.Combine(previous, profile.Id + ".json"), TestContext.Current.CancellationToken));
        Assert.Equal(profile.Id, await original.ReadActiveIdAsync());
    }

    [Fact]
    public async Task MigrationPrefersExistingProfilesAndDoesNotRestoreDeletedImportsOnRestart()
    {
        string previous = Path.Combine(root, "previous");
        var original = new FileProfileStore(previous);
        var shared = InstrumentProfile.Default with { Id = Guid.NewGuid(), Name = "Old version" };
        var imported = shared with { Id = Guid.NewGuid(), Name = "Imported" };
        await original.SaveAsync(shared);
        await original.SaveAsync(imported);
        await original.WriteActiveIdAsync(imported.Id);
        var existing = new FileProfileStore(Library);
        await existing.SaveAsync(shared with { Name = "Edited in new version" });
        await existing.WriteActiveIdAsync(shared.Id);

        var renamed = new FileProfileStore(Library, previous);
        var catalog = await renamed.ListAsync();
        Assert.Empty(catalog.Errors);
        Assert.Equal("Edited in new version", catalog.Profiles.Single(item => item.Id == shared.Id).Name);
        Assert.Equal(shared.Id, await renamed.ReadActiveIdAsync());
        await renamed.DeleteAsync(imported.Id);
        catalog = await new FileProfileStore(Library, previous).ListAsync();
        Assert.DoesNotContain(catalog.Profiles, item => item.Id == imported.Id);
        Assert.Contains((await original.ListAsync()).Profiles, item => item.Id == imported.Id);
    }

    [Fact]
    public async Task MissingPreviousLibraryLeavesDefaultAvailableWithoutCreatingDirectories()
    {
        string previous = Path.Combine(root, "missing");
        var renamed = new FileProfileStore(Library, previous);
        Assert.Null(await renamed.ReadActiveIdAsync());
        Assert.Equal(InstrumentProfile.DefaultId, Assert.Single((await renamed.ListAsync()).Profiles).Id);
        Assert.False(Directory.Exists(previous));
        Assert.False(Directory.Exists(Library));
    }

    [Fact]
    public async Task SaveReloadRenameReplaceAndDeletePreserveFileIdentity()
    {
        var store = new FileProfileStore(Library);
        var profile = InstrumentProfile.Default with { Id = Guid.NewGuid(), Name = "My instrument" };
        await store.SaveAsync(profile);
        await store.WriteActiveIdAsync(profile.Id);
        profile = profile with { Name = "Renamed", Configuration = profile.Configuration with { FixedVelocity = 88 } };
        await store.SaveAsync(profile);
        var catalog = await new FileProfileStore(Library).ListAsync();
        Assert.Empty(catalog.Errors); Assert.Equal(2, catalog.Profiles.Count);
        Assert.Equal(ProfileJson.Serialize(profile), ProfileJson.Serialize(catalog.Profiles.Single(item => item.Id == profile.Id)));
        Assert.Equal(profile.Id, await store.ReadActiveIdAsync());
        Assert.Single(Directory.GetFiles(Library, "*.json")); Assert.Empty(Directory.GetFiles(Library, "*.tmp"));
        await store.DeleteAsync(profile.Id);
        Assert.Single((await store.ListAsync()).Profiles);
    }

    [Fact]
    public async Task CorruptMismatchedAndInvalidUtf8FilesAreReportedWithoutLosingValidProfiles()
    {
        Directory.CreateDirectory(Library);
        var store = new FileProfileStore(Library);
        var valid = InstrumentProfile.Default with { Id = Guid.NewGuid(), Name = "Valid" };
        await store.SaveAsync(valid);
        await File.WriteAllTextAsync(Path.Combine(Library, Guid.NewGuid() + ".json"), "{", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Library, Guid.NewGuid() + ".json"), ProfileJson.Serialize(valid), TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(Library, Guid.NewGuid() + ".json"), [0xFF], TestContext.Current.CancellationToken);
        var catalog = await store.ListAsync();
        Assert.Equal(3, catalog.Errors.Count); Assert.Equal(2, catalog.Profiles.Count);
        Assert.Equal("{", await File.ReadAllTextAsync(Directory.GetFiles(Library, "*.json").Single(path => File.ReadAllText(path) == "{"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FailedSaveRetainsPreviousValidFileAndCleansTemporaryFiles()
    {
        var store = new FileProfileStore(Library);
        var profile = InstrumentProfile.Default with { Id = Guid.NewGuid(), Name = "Valid" };
        await store.SaveAsync(profile);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(profile with { Name = "" }));
        Assert.Equal("Valid", (await store.ListAsync()).Profiles.Single(item => item.Id == profile.Id).Name);
        await using (var locked = new FileStream(Path.Combine(Library, profile.Id + ".json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.SaveAsync(profile with { Name = "Blocked replacement" }));
        Assert.Equal("Valid", (await store.ListAsync()).Profiles.Single(item => item.Id == profile.Id).Name);
        Assert.Empty(Directory.GetFiles(Library, "*.tmp"));
        string blocked = Path.Combine(root, "blocked"); Directory.CreateDirectory(blocked);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.WriteAtomicAsync(blocked, "valid content"));
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }

    [Fact]
    public async Task BuiltInDefaultIsAlwaysAvailableAndCannotBeOverwrittenOrDeleted()
    {
        var store = new FileProfileStore(Library);
        Assert.Equal("Default C Major", Assert.Single((await store.ListAsync()).Profiles).Name);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(InstrumentProfile.Default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteAsync(InstrumentProfile.DefaultId));
        Assert.False(Directory.Exists(Library));
    }

    [Fact]
    public async Task LifecycleCreatesFromCurrentSavesReloadsDuplicatesRenamesAndDeletes()
    {
        InstrumentConfiguration current = new() { BaseOctave = 4 };
        var store = new FileProfileStore(Library);
        var model = CreateModel(store, () => current, value => current = value);
        await model.InitializeAsync();
        current = current with { BaseOctave = 4 };
        model.Name = "Stage"; await model.CreateAsync();
        var original = model.Selected!; Assert.Equal("Stage", model.ActiveName);
        current = current with { FixedVelocity = 88 }; await model.SaveAsync();
        current = new(); await model.LoadAsync(model.Selected!);
        Assert.Equal(4, current.BaseOctave); Assert.Equal(88, current.FixedVelocity);
        await model.DuplicateAsync();
        Assert.NotEqual(original.Id, model.Selected!.Id); Assert.Equal("Stage", model.ActiveName);
        model.Name = "Copy renamed"; await model.RenameAsync();
        Assert.Equal("Copy renamed", model.Selected!.Name);
        await model.LoadAsync(model.Selected);
        model.DeleteCommand.Execute(null); Assert.True(model.DeletePending);
        await model.DeleteAsync();
        Assert.Equal("Default C Major", model.ActiveName); Assert.Equal(3, current.BaseOctave);
        Assert.Equal(2, model.Profiles.Count);
        var reopened = CreateModel(new FileProfileStore(Library), () => current, value => current = value);
        await reopened.InitializeAsync(); Assert.Equal("Default C Major", reopened.ActiveName);
    }

    [Fact]
    public async Task SavedProfileRestoresAfterRestart()
    {
        InstrumentConfiguration current = new();
        var store = new FileProfileStore(Library);
        var model = CreateModel(store, () => current, value => current = value);
        await model.InitializeAsync(); current = new() { BaseOctave = 5, MidiChannel = 4 };
        model.Name = "Restored"; await model.CreateAsync();
        current = new();
        var reopened = CreateModel(new FileProfileStore(Library), () => current, value => current = value);
        await reopened.InitializeAsync();
        Assert.Equal("Restored", reopened.ActiveName); Assert.Equal(5, current.BaseOctave); Assert.Equal(4, current.MidiChannel);
    }

    [Fact]
    public async Task ImportExportNeverOverwriteIdsOrApplyUntilLoad()
    {
        InstrumentConfiguration current = new(); int applies = 0;
        var store = new FileProfileStore(Library);
        var model = CreateModel(store, () => current, value => { current = value; applies++; });
        await model.InitializeAsync(); applies = 0;
        string export = Path.Combine(root, "export.json"); await model.ExportAsync(export);
        await model.ImportAsync(export);
        Assert.NotEqual(InstrumentProfile.DefaultId, model.Selected!.Id); Assert.Equal(0, applies);
        var firstId = model.Selected.Id; await model.ImportAsync(export);
        Assert.NotEqual(firstId, model.Selected!.Id); Assert.Equal(3, model.Profiles.Count);
        Assert.NotEqual(model.Profiles[1].Name, model.Profiles[2].Name);
        await Assert.ThrowsAsync<ArgumentException>(() => model.ExportAsync(Path.Combine(Library, "default.json")));
    }

    [Fact]
    public async Task InvalidImportAndUnavailableStorageDoNotApplyConfiguration()
    {
        int applies = 0;
        var store = new FileProfileStore(Library);
        var model = CreateModel(store, () => new(), _ => applies++);
        await model.InitializeAsync(); applies = 0;
        string invalid = Path.Combine(root, "invalid.json"); await File.WriteAllTextAsync(invalid, "{", TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<JsonException>(() => model.ImportAsync(invalid));
        Assert.Equal(0, applies); Assert.Single(model.Profiles);
        string blocked = Path.Combine(root, "blocked"); await File.WriteAllTextAsync(blocked, "file", TestContext.Current.CancellationToken);
        var unavailable = CreateModel(new FileProfileStore(blocked), () => new(), _ => applies++);
        unavailable.Name = "Cannot write";
        await Assert.ThrowsAnyAsync<IOException>(() => unavailable.CreateAsync()); Assert.Equal(0, applies);
    }

    [Theory]
    [InlineData("invalid-marker")]
    [InlineData("missing-profile")]
    public async Task InvalidStartupStateReportsErrorAndKeepsDefaultAvailable(string kind)
    {
        var store = new FileProfileStore(Library); Directory.CreateDirectory(Library);
        await File.WriteAllTextAsync(Path.Combine(Library, "last-profile.txt"), kind == "invalid-marker" ? "invalid" : Guid.NewGuid().ToString(), TestContext.Current.CancellationToken);
        var model = CreateModel(store, () => new(), _ => { });
        await model.InitializeAsync();
        Assert.True(model.HasError); Assert.Single(model.Profiles); Assert.Equal("Default C Major", model.ActiveName);
    }

    [Fact]
    public async Task DeleteRequiresConfirmationAndInvalidRenameKeepsOriginal()
    {
        var store = new FileProfileStore(Library);
        var model = CreateModel(store, () => new(), _ => { }); await model.InitializeAsync();
        model.Name = "Original"; await model.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(model.DeleteAsync);
        model.Name = "Default C Major"; await Assert.ThrowsAsync<ArgumentException>(model.RenameAsync);
        Assert.Equal("Original", model.Selected!.Name);
    }

    [Fact]
    public async Task PanicIsIndependentOfProfileStorageErrors()
    {
        int panics = 0;
        var model = new ProfilesViewModel(new FileProfileStore(Library), () => InstrumentProfile.Default, _ => { }, _ => Task.FromResult(""),
            () => panics++, () => Task.FromResult<string?>(null), _ => Task.FromResult<string?>(null));
        await model.InitializeAsync();
        model.SaveCommand.Execute(null); Assert.True(model.HasError);
        model.PanicCommand.Execute(null); Assert.Equal(1, panics);
    }

    [Fact]
    public async Task OversizedFileIsReportedWithoutBeingReadIntoTheInstrument()
    {
        Directory.CreateDirectory(Library);
        await File.WriteAllBytesAsync(Path.Combine(Library, Guid.NewGuid() + ".json"), new byte[ProfileJson.MaximumBytes + 1], TestContext.Current.CancellationToken);
        var catalog = await new FileProfileStore(Library).ListAsync();
        Assert.Single(catalog.Profiles); Assert.Contains("1 MiB", Assert.Single(catalog.Errors));
    }

    private static ProfilesViewModel CreateModel(IProfileStore store, Func<InstrumentConfiguration> capture, Action<InstrumentConfiguration> apply) => new(
        store, () => InstrumentProfile.Default with { Configuration = capture() }, apply, _ => Task.FromResult(""), () => { },
        () => Task.FromResult<string?>(null), _ => Task.FromResult<string?>(null));

    public void Dispose()
    {
        string absolute = Path.GetFullPath(root);
        string expected = Path.GetFullPath(Path.GetTempPath());
        if (!absolute.StartsWith(expected, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith("PadToMIDI.Tests.", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected test cleanup path.");
        if (Directory.Exists(absolute)) Directory.Delete(absolute, recursive: true);
    }
}
