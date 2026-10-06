using System.Collections.ObjectModel;
using PadToMIDI.Core.Midi;

namespace PadToMIDI.App.ViewModels;

/// <summary>Presentation and user intents only; musical cleanup and delivery live outside this model.</summary>
public sealed class MidiViewModel : ObservableObject
{
    private MidiEndpoint? selectedEndpoint;
    private string status = "Discovering Windows MIDI Services outputs…";
    private string counts = "0 sent · 0 rejected";
    private decimal? channel = 1;
    private readonly Action<byte> setChannel;
    public ObservableCollection<MidiEndpoint> Endpoints { get; } = [];
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand ConnectCommand { get; }
    public AsyncCommand DisconnectCommand { get; }
    public AsyncCommand PanicCommand { get; }
    public MidiEndpoint? SelectedEndpoint { get => selectedEndpoint; set => SetProperty(ref selectedEndpoint, value); }
    public string Status => status;
    public string Counts => counts;
    public decimal? Channel
    {
        get => channel;
        set { if (value is >= 1 and <= 16 && value == decimal.Truncate(value.Value) && SetProperty(ref channel, value)) setChannel((byte)(value.Value - 1)); }
    }

    public MidiViewModel(Func<Task> refresh, Func<string, Task> connect, Func<Task> disconnect,
        Action<byte> setChannel, Action panic)
    {
        this.setChannel = setChannel;
        RefreshCommand = new(refresh, ShowError);
        ConnectCommand = new(() => SelectedEndpoint is { } endpoint ? connect(endpoint.Id) :
            Task.FromException(new InvalidOperationException("Select a MIDI output first.")), ShowError);
        DisconnectCommand = new(disconnect, ShowError);
        PanicCommand = new(() => { panic(); return Task.CompletedTask; }, ShowError);
    }

    public void UpdateEndpoints(IReadOnlyList<MidiEndpoint> endpoints, string? connectedId = null)
    {
        string? selected = SelectedEndpoint?.Id ?? connectedId;
        if (Endpoints.SequenceEqual(endpoints)) return;
        Endpoints.Clear();
        foreach (var endpoint in endpoints) Endpoints.Add(endpoint);
        SelectedEndpoint = Endpoints.FirstOrDefault(endpoint => endpoint.Id == selected) ?? Endpoints.FirstOrDefault();
    }

    public void Update(MidiOutputSnapshot snapshot)
    {
        string? endpoint = Endpoints.FirstOrDefault(candidate => candidate.Id == snapshot.EndpointId)?.Name;
        string label = snapshot.State == MidiOutputState.Connected && endpoint is not null ? $"Connected to {endpoint}. {snapshot.Message}" : $"{snapshot.State}: {snapshot.Message}";
        SetProperty(ref status, label, nameof(Status));
        SetProperty(ref counts, $"{snapshot.SentCount:N0} sent · {snapshot.RejectedCount:N0} rejected", nameof(Counts));
    }
    public void ShowError(Exception error) => SetProperty(ref status, error.Message, nameof(Status));
    public void ObserveChannel(byte value) => SetProperty(ref channel, (decimal?)(value + 1), nameof(Channel));
}
