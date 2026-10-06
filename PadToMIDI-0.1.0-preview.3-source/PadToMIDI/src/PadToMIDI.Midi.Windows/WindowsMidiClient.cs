using PadToMIDI.Core.Midi;
using Windows.Devices.Midi2;
using Windows.Devices.Midi2.Enumeration;

namespace PadToMIDI.Midi.Windows;

/// <summary>All methods run on the MIDI MTA thread; native callbacks only raise lightweight signals.</summary>
internal sealed class WindowsMidiClient(Action topologyChanged, Action disconnected) : IDisposable
{
    private MidiSession? session;
    private MidiEndpointDeviceWatcher? watcher;
    private MidiEndpointConnection? connection;
    private string? activeDeviceId;
    private readonly Dictionary<string, Destination> destinations = new(StringComparer.Ordinal);
    private readonly bool[] held = new bool[16 * 128];
    private readonly ushort?[] controllerResets = new ushort?[16 * 128];
    private readonly bool[] pressured = new bool[16 * 128];
    private ushort usedChannels;
    private byte group;

    public bool IsOpen => connection is not null;

    public void Initialize()
    {
        if (session is not null) return;
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26200))
            throw new InvalidOperationException("The preview MIDI API requires Windows 11 25H2 or newer.");
        if (!MidiApi.EnsureServiceAvailable())
            throw new InvalidOperationException("Windows MIDI Services is unavailable or Legacy API mode is selected. Check Windows MIDI Services Settings.");
        session = MidiSession.Create("PadToMIDI") ?? throw new InvalidOperationException("Unable to create the MIDI session.");
        watcher = MidiEndpointDeviceWatcher.Create(MidiEndpointDeviceInformationFilters.AllStandardEndpoints);
        watcher.Added += OnAdded;
        watcher.Removed += OnRemoved;
        watcher.Updated += OnUpdated;
        watcher.EnumerationCompleted += OnEnumerated;
        watcher.Start();
    }

    public IReadOnlyList<MidiEndpoint> GetEndpoints()
    {
        Initialize();
        destinations.Clear();
        var endpoints = new List<MidiEndpoint>();
        foreach (var device in MidiEndpointDeviceInformation.FindAll(MidiEndpointDeviceInformationSortOrder.Name,
            MidiEndpointDeviceInformationFilters.AllStandardEndpoints))
        {
            var blocks = device.GetDeclaredFunctionBlocks();
            ushort groups = 0;
            if (blocks.Count > 0)
            {
                foreach (var block in blocks)
                    if (block.IsActive && block.Direction is MidiFunctionBlockDirection.BlockInput or MidiFunctionBlockDirection.Bidirectional)
                        groups |= GroupMask(block.FirstGroup.Index, block.GroupCount);
            }
            else
            {
                foreach (var block in device.GetGroupTerminalBlocks())
                    if (block.Direction is MidiGroupTerminalBlockDirection.BlockInput or MidiGroupTerminalBlockDirection.Bidirectional)
                        groups |= GroupMask(block.FirstGroup.Index, block.GroupCount);
            }
            // Do not guess group 1 for unknown metadata: an input-only endpoint must not be offered as output.
            for (byte index = 0; index < 16; index++)
            {
                if ((groups & (1 << index)) == 0) continue;
                string id = $"{device.EndpointDeviceId}|group={index}";
                destinations[id] = new(device.EndpointDeviceId, index);
                endpoints.Add(new(id, $"{device.Name} · Group {index + 1}", index));
            }
        }
        if (connection is not null && !destinations.ContainsKey($"{Volatile.Read(ref activeDeviceId)}|group={group}"))
            disconnected();
        return endpoints.AsReadOnly();
    }

    private static ushort GroupMask(byte first, byte count)
    {
        ushort mask = 0;
        for (int index = first; index < Math.Min(16, first + count); index++) mask |= (ushort)(1 << index);
        return mask;
    }

    public void Connect(string id)
    {
        Close();
        GetEndpoints();
        if (!destinations.TryGetValue(id, out var destination))
            throw new InvalidOperationException("The selected output is no longer available. Refresh the endpoint list.");
        var settings = new MidiEndpointConnectionSettings(false, false);
        var candidate = session!.CreateEndpointConnection(destination.DeviceId, settings)
            ?? throw new InvalidOperationException("Unable to create an endpoint connection.");
        connection = candidate;
        Volatile.Write(ref activeDeviceId, destination.DeviceId);
        group = destination.Group;
        candidate.EndpointDeviceDisconnected += OnDisconnected;
        if (!candidate.Open())
        {
            Close();
            throw new InvalidOperationException("Unable to open the selected endpoint.");
        }
    }

    public void Send(in MidiEvent message)
    {
        if (connection is null) return;
        var result = connection.SendSingleMessageWords(0, Midi1UmpEncoder.Encode(message, group));
        if ((result & MidiSendMessageResults.Succeeded) == 0)
            throw new InvalidOperationException($"MIDI send failed: {result}.");
        usedChannels |= (ushort)(1 << message.Channel);
        if (message.Kind is MidiEventKind.NoteOn or MidiEventKind.NoteOff)
            held[message.Channel * 128 + message.Data1] = message.Kind == MidiEventKind.NoteOn && message.Data2 > 0;
        if (message.Kind == MidiEventKind.ControlChange && message.Data1 is 120 or 123)
            Array.Clear(held, message.Channel * 128, 128);
        if (message.Kind == MidiEventKind.ControlChange && message.ResetValue is { } reset)
            controllerResets[message.Channel * 128 + message.Data1] = message.Data2 == reset ? null : reset;
        if (message.Kind == MidiEventKind.PolyphonicPressure)
            pressured[message.Channel * 128 + message.Data1] = message.Data2 > 0;
    }

    public void Close()
    {
        if (connection is not { } active) return;
        Volatile.Write(ref activeDeviceId, null);
        // The backend ledger covers messages actually sent, including input-side queue failures.
        for (int index = 0; index < controllerResets.Length; index++)
        {
            if (pressured[index]) BestEffort(new(MidiEventKind.PolyphonicPressure, (byte)(index / 128), (byte)(index % 128), 0));
            if (controllerResets[index] is { } reset) BestEffort(new(MidiEventKind.ControlChange, (byte)(index / 128), (byte)(index % 128), reset));
        }
        for (int index = 0; index < held.Length; index++)
            if (held[index]) BestEffort(new(MidiEventKind.NoteOff, (byte)(index / 128), (byte)(index % 128), 0));
        for (byte channel = 0; channel < 16; channel++)
        {
            if ((usedChannels & (1 << channel)) == 0) continue;
            BestEffort(new(MidiEventKind.ControlChange, channel, 123, 0));
            BestEffort(new(MidiEventKind.ChannelPressure, channel, 0, 0));
            BestEffort(new(MidiEventKind.PitchBend, channel, 0, 8192));
        }
        Array.Clear(held);
        Array.Clear(pressured); Array.Clear(controllerResets);
        usedChannels = 0;
        connection = null;
        try { active.EndpointDeviceDisconnected -= OnDisconnected; }
        finally { session?.DisconnectEndpointConnection(active.ConnectionId); }
    }

    private void BestEffort(MidiEvent message)
    {
        try { connection?.SendSingleMessageWords(0, Midi1UmpEncoder.Encode(message, group)); }
        catch (Exception) { /* Removed hardware cannot receive cleanup; continue releasing local ownership. */ }
    }

    private void OnAdded(MidiEndpointDeviceWatcher sender, MidiEndpointDeviceInformationAddedEventArgs args) => topologyChanged();
    private void OnRemoved(MidiEndpointDeviceWatcher sender, MidiEndpointDeviceInformationRemovedEventArgs args)
    {
        topologyChanged();
        // Preview connection callbacks can be absent when AutoReconnect is false.
        if (string.Equals(args.RemovedDevice.EndpointDeviceId, Volatile.Read(ref activeDeviceId), StringComparison.OrdinalIgnoreCase))
            disconnected();
    }
    private void OnUpdated(MidiEndpointDeviceWatcher sender, MidiEndpointDeviceInformationUpdatedEventArgs args) => topologyChanged();
    private void OnEnumerated(MidiEndpointDeviceWatcher sender, object args) => topologyChanged();
    private void OnDisconnected(IMidiEndpointConnectionSource sender, object args)
    {
        if (Volatile.Read(ref activeDeviceId) is not null) disconnected();
    }

    public void Dispose()
    {
        try { Close(); }
        finally
        {
            try
            {
                if (watcher is { } active)
                {
                    watcher = null;
                    try { active.Stop(); }
                    finally
                    {
                        active.Added -= OnAdded;
                        active.Removed -= OnRemoved;
                        active.Updated -= OnUpdated;
                        active.EnumerationCompleted -= OnEnumerated;
                    }
                }
            }
            finally
            {
                session?.Dispose();
                session = null;
            }
        }
    }

    private readonly record struct Destination(string DeviceId, byte Group);
}
