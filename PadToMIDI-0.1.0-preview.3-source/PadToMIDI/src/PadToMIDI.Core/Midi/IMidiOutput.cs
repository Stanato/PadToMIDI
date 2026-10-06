namespace PadToMIDI.Core.Midi;

/// <summary>Platform output boundary. Native work belongs to the backend's own thread.</summary>
public interface IMidiOutput : IAsyncDisposable
{
    bool IsConnected { get; }
    MidiOutputSnapshot CaptureSnapshot();
    event Action? Faulted;
    ValueTask<IReadOnlyList<MidiEndpoint>> GetEndpointsAsync(CancellationToken cancellationToken = default);
    ValueTask ConnectAsync(string endpointId, CancellationToken cancellationToken = default);
    ValueTask DisconnectAsync(CancellationToken cancellationToken = default);
    /// <summary>Waits until previously accepted messages have been processed.</summary>
    ValueTask FlushAsync(CancellationToken cancellationToken = default);

    /// <summary>Nonblocking submission from the input owner thread; backend reports failures.</summary>
    bool TrySend(in MidiEvent message);
}

public sealed record MidiEndpoint(string Id, string Name, byte Group = 0);

public enum MidiOutputState { Idle, Ready, Connecting, Connected, Unavailable, Faulted, Disposed }
public readonly record struct MidiOutputSnapshot(MidiOutputState State, string Message,
    string? EndpointId = null, long SentCount = 0, long RejectedCount = 0, long EndpointRevision = 0);
