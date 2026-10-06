using System.Collections.ObjectModel;
using PadToMIDI.App.Services;
using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Profiles;

namespace PadToMIDI.App.ViewModels;

public sealed class ProfilesViewModel : ObservableObject
{
    private readonly IProfileStore store;
    private readonly Func<InstrumentProfile> capture;
    private readonly Action<InstrumentConfiguration> apply;
    private readonly Func<InstrumentProfile, Task<string>> restorePreferences;
    private readonly Func<Task<string?>> importPath;
    private readonly Func<string, Task<string?>> exportPath;
    private readonly FileProfileStore transfer = new(FileProfileStore.DefaultDirectory);
    private InstrumentProfile? selected;
    private Guid activeId = InstrumentProfile.DefaultId;
    private string activeName = "Default C Major";
    private string name = "My C Major";
    private string status = "Loading profiles…";
    private string warnings = "";
    private bool busy, error, deletePending;
    public ObservableCollection<InstrumentProfile> Profiles { get; } = [];
    public InstrumentProfile? Selected
    {
        get => selected;
        set { if (SetProperty(ref selected, value)) { Name = value?.Name ?? ""; DeletePending = false; Notify(nameof(CanModify)); Notify(nameof(SelectedDetails)); } }
    }
    public string Name { get => name; set => SetProperty(ref name, value); }
    public string ActiveName => activeName;
    public string Status => status;
    public string Warnings => warnings;
    public bool HasWarnings => warnings.Length > 0;
    public bool HasError => error;
    public bool IsIdle => !busy;
    public bool CanModify => IsIdle && Selected is { } profile && profile.Id != InstrumentProfile.DefaultId;
    public bool DeletePending { get => deletePending; private set => SetProperty(ref deletePending, value); }
    public string SelectedDetails => Selected is { } profile
        ? $"{NoteDisplay.RootName(profile.Configuration.Root, profile.Configuration.Scale)} {profile.Configuration.Scale.Name} · Octave {profile.Configuration.BaseOctave} · Velocity {profile.Configuration.FixedVelocity} · Channel {profile.Configuration.MidiChannel + 1}\nMode: {(profile.Configuration.Chords.Enabled ? "Chords" : "Notes")} · Shape: {profile.Configuration.Chords.Shape}\nController: {profile.ControllerName ?? "automatic selection"}\nMIDI: {profile.MidiEndpointName ?? profile.MidiEndpointId ?? "select manually"}"
        : "Select a profile.";
    public AsyncCommand InitializeCommand { get; }
    public AsyncCommand CreateCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand LoadCommand { get; }
    public AsyncCommand DuplicateCommand { get; }
    public AsyncCommand RenameCommand { get; }
    public AsyncCommand DeleteCommand { get; }
    public AsyncCommand ConfirmDeleteCommand { get; }
    public AsyncCommand CancelDeleteCommand { get; }
    public AsyncCommand ResetCommand { get; }
    public AsyncCommand ImportCommand { get; }
    public AsyncCommand ExportCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand PanicCommand { get; }

    public ProfilesViewModel(IProfileStore store, Func<InstrumentProfile> capture, Action<InstrumentConfiguration> apply,
        Func<InstrumentProfile, Task<string>> restorePreferences, Action panic,
        Func<Task<string?>> importPath, Func<string, Task<string?>> exportPath)
    {
        this.store = store; this.capture = capture; this.apply = apply; this.restorePreferences = restorePreferences;
        this.importPath = importPath; this.exportPath = exportPath;
        InitializeCommand = Command(InitializeAsync); CreateCommand = Command(CreateAsync); SaveCommand = Command(SaveAsync);
        LoadCommand = Command(() => LoadAsync(RequireSelected())); DuplicateCommand = Command(DuplicateAsync);
        RenameCommand = Command(RenameAsync); RefreshCommand = Command(() => RefreshAsync(Selected?.Id));
        DeleteCommand = Command(() => { RequireMutable(); DeletePending = true; return Task.CompletedTask; });
        ConfirmDeleteCommand = Command(DeleteAsync);
        CancelDeleteCommand = Command(() => { DeletePending = false; return Task.CompletedTask; });
        ResetCommand = Command(() => LoadAsync(InstrumentProfile.Default));
        ImportCommand = Command(async () => { if (await importPath() is { } path) await ImportAsync(path); });
        ExportCommand = Command(async () =>
        {
            var profile = RequireSelected();
            string suggestion = string.Concat(profile.Name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)) + ".json";
            if (await exportPath(suggestion) is { } path) await ExportAsync(path);
        });
        PanicCommand = new(() => { panic(); return Task.CompletedTask; }, ShowError);
    }

    private AsyncCommand Command(Func<Task> operation) => new(() => RunAsync(operation), ShowError);
    private async Task RunAsync(Func<Task> operation)
    {
        if (busy) return;
        SetProperty(ref busy, true, nameof(IsIdle)); Notify(nameof(CanModify));
        SetProperty(ref error, false, nameof(HasError));
        try { await operation(); }
        finally { SetProperty(ref busy, false, nameof(IsIdle)); Notify(nameof(CanModify)); }
    }

    public async Task InitializeAsync()
    {
        await RefreshAsync(null);
        Guid? id;
        try { id = await store.ReadActiveIdAsync(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { ShowError(exception); return; }
        var profile = Profiles.FirstOrDefault(item => item.Id == id);
        if (id is not null && profile is null)
        {
            Selected = Profiles[0];
            ShowError(new IOException("The last profile is missing or invalid. Default C Major is running; choose a profile and Load."));
            return;
        }
        await LoadAsync(profile ?? Profiles[0]);
    }

    public async Task RefreshAsync(Guid? selectId)
    {
        var catalog = await store.ListAsync();
        Profiles.Clear(); foreach (var profile in catalog.Profiles) Profiles.Add(profile);
        Selected = Profiles.FirstOrDefault(profile => profile.Id == selectId) ?? Profiles.FirstOrDefault(profile => profile.Id == activeId) ?? Profiles.FirstOrDefault();
        SetProperty(ref warnings, string.Join(Environment.NewLine, catalog.Errors), nameof(Warnings)); Notify(nameof(HasWarnings));
    }

    public async Task CreateAsync()
    {
        var profile = capture() with { Id = Guid.NewGuid(), Name = UniqueName(Name.Trim()) };
        await store.SaveAsync(profile);
        await RememberAsync(profile);
        await RefreshAsync(profile.Id);
        SetStatus($"Created {profile.Name} from the current instrument. Settings changes need Save to persist.");
    }

    public async Task SaveAsync()
    {
        var selectedProfile = RequireMutable();
        var profile = capture() with { Id = selectedProfile.Id, Name = selectedProfile.Name };
        await store.SaveAsync(profile);
        await RememberAsync(profile);
        await RefreshAsync(profile.Id);
        SetStatus($"Saved the current instrument as {profile.Name}.");
    }

    public async Task LoadAsync(InstrumentProfile profile)
    {
        profile.Validate();
        apply(profile.Configuration);
        // The musical transition is already complete if disk/device preference restoration fails.
        activeId = profile.Id; SetProperty(ref activeName, profile.Name, nameof(ActiveName));
        Selected = Profiles.FirstOrDefault(item => item.Id == profile.Id) ?? profile;
        string routes = await restorePreferences(profile);
        try { await store.WriteActiveIdAsync(profile.Id); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { throw new IOException($"{profile.Name} is loaded, but its startup selection could not be saved: {exception.Message}", exception); }
        SetStatus($"Loaded {profile.Name}. {routes} Settings changes need Save to persist.");
    }

    public async Task DuplicateAsync()
    {
        var original = RequireSelected();
        var duplicate = original with { Id = Guid.NewGuid(), Name = UniqueName(original.Name + " copy") };
        await store.SaveAsync(duplicate); await RefreshAsync(duplicate.Id);
        SetStatus($"Duplicated {original.Name}. Choose Load to use the copy.");
    }

    public async Task RenameAsync()
    {
        var original = RequireMutable();
        string candidate = Name.Trim();
        if (Profiles.Any(profile => profile.Id != original.Id && string.Equals(profile.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Another profile already has that name.");
        var renamed = original with { Name = candidate };
        await store.SaveAsync(renamed);
        if (activeId == renamed.Id) SetProperty(ref activeName, renamed.Name, nameof(ActiveName));
        await RefreshAsync(renamed.Id); SetStatus($"Renamed profile to {renamed.Name}.");
    }

    public async Task DeleteAsync()
    {
        var profile = RequireMutable();
        if (!DeletePending) throw new InvalidOperationException("Choose Delete, then confirm deletion.");
        await store.DeleteAsync(profile.Id); DeletePending = false;
        await RefreshAsync(null);
        if (activeId == profile.Id) await LoadAsync(InstrumentProfile.Default);
        else SetStatus($"Deleted {profile.Name}.");
    }

    public async Task ImportAsync(string path)
    {
        // Import never overwrites an existing ID and never changes the playing instrument.
        var profile = ProfileJson.Deserialize(await FileProfileStore.ReadJsonAsync(path));
        profile = profile with { Id = Guid.NewGuid(), Name = UniqueName(profile.Name) };
        await store.SaveAsync(profile); await RefreshAsync(profile.Id);
        SetStatus($"Imported {profile.Name}. Choose Load to use it.");
    }

    public async Task ExportAsync(string path)
    {
        var profile = RequireSelected();
        string json = ProfileJson.Serialize(profile);
        // A picker may target a library file. Do not bypass profile Save/identity rules through export.
        if (store is FileProfileStore files && files.OwnsPath(path))
            throw new ArgumentException("Export outside the profile library. Use Save for library profiles.");
        await transfer.WriteAtomicAsync(path, json);
        SetStatus($"Exported stored profile {profile.Name}. Use Save first to include current settings edits.");
    }

    private async Task RememberAsync(InstrumentProfile profile)
    {
        activeId = profile.Id; SetProperty(ref activeName, profile.Name, nameof(ActiveName));
        try { await store.WriteActiveIdAsync(profile.Id); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { throw new IOException($"{profile.Name} was saved, but its startup selection could not be saved: {exception.Message}", exception); }
    }
    private InstrumentProfile RequireSelected() => Selected ?? throw new InvalidOperationException("Select a profile first.");
    private InstrumentProfile RequireMutable()
    {
        var profile = RequireSelected();
        return profile.Id != InstrumentProfile.DefaultId ? profile : throw new InvalidOperationException("Default C Major is read-only. Create or Duplicate a profile first.");
    }
    private string UniqueName(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) throw new ArgumentException("Enter a profile name.");
        string basis = candidate.Length > 70 ? candidate[..70] : candidate;
        string result = basis;
        for (int i = 2; Profiles.Any(profile => string.Equals(profile.Name, result, StringComparison.OrdinalIgnoreCase)); i++) result = $"{basis} ({i})";
        return result;
    }
    private void SetStatus(string value) { SetProperty(ref error, false, nameof(HasError)); SetProperty(ref status, value, nameof(Status)); }
    private void ShowError(Exception exception)
    {
        SetProperty(ref error, true, nameof(HasError));
        SetProperty(ref status, $"Profile operation failed: {exception.Message}", nameof(Status));
    }
}
