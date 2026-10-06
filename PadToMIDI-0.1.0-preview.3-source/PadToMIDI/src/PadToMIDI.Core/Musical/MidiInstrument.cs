using PadToMIDI.Core.Input;
using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Midi;

namespace PadToMIDI.Core.Musical;

/// <summary>Serializes musical state and coordinates safe output transitions independently of any UI.</summary>
public sealed class MidiInstrument : IDisposable
{
    private readonly object gate = new();
    private readonly SemaphoreSlim transitions = new(1, 1);
    private readonly MappingEngine engine = new();
    private readonly IMidiOutput? output;
    private GamepadDeviceId? selectedDevice;
    private bool suspended;
    private bool forwarding = true;
    private bool disposed;
    private int faultPending;
    private ushort usedChannels;

    public event Action<MidiEvent>? MidiGenerated;

    public MidiInstrument(IMidiOutput? output = null)
    {
        this.output = output;
        engine.MidiGenerated += OnMidi;
        if (output is not null) output.Faulted += OnFault;
    }

    public void Process(GamepadInputEvent input)
    {
        lock (gate)
        {
            if (disposed) return;
            ConsumeFault();
            bool cleanup = input.Kind is GamepadEventKind.Selected or GamepadEventKind.Stopped or GamepadEventKind.Faulted ||
                (input.Kind == GamepadEventKind.Disconnected && selectedDevice == input.DeviceId);
            if (input.Kind == GamepadEventKind.Selected) selectedDevice = input.DeviceId;
            if ((input.Kind == GamepadEventKind.Disconnected && selectedDevice == input.DeviceId) ||
                input.Kind is GamepadEventKind.Stopped or GamepadEventKind.Faulted) selectedDevice = null;
            if (suspended) return;
            engine.Process(input);
            if (cleanup) ResetControllers();
            ConsumeFault();
        }
    }

    public MusicalStateSnapshot CaptureSnapshot()
    {
        lock (gate) { ConsumeFault(); return engine.CaptureSnapshot(); }
    }

    public void SetMidiChannel(byte channel)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            engine.UpdateConfiguration(engine.CaptureSnapshot().Configuration with { MidiChannel = channel });
        }
    }

    public void UpdateConfiguration(InstrumentConfiguration configuration, bool resetPersistentOctave = false)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ConsumeFault();
            engine.UpdateConfiguration(configuration, resetPersistentOctave);
            ConsumeFault();
        }
    }

    public ValueTask ConnectOutputAsync(string endpointId, CancellationToken cancellationToken = default) =>
        ChangeOutputAsync(endpointId, cancellationToken);
    public ValueTask DisconnectOutputAsync(CancellationToken cancellationToken = default) =>
        ChangeOutputAsync(null, cancellationToken);

    private async ValueTask ChangeOutputAsync(string? endpointId, CancellationToken cancellationToken)
    {
        if (output is null) throw new InvalidOperationException("No MIDI backend was supplied.");
        await transitions.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                suspended = true;
                engine.Reset();
                ResetControllers();
                forwarding = false;
            }
            // Cleanup is not cancelled midway: an old output must drain before it is closed.
            await output.FlushAsync().ConfigureAwait(false);
            await output.DisconnectAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (endpointId is not null) await output.ConnectAsync(endpointId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (gate)
            {
                if (output.IsConnected) Interlocked.Exchange(ref faultPending, 0);
                forwarding = output.IsConnected;
                suspended = false;
                if (!disposed && selectedDevice is { } device) engine.Process(new(GamepadEventKind.Selected, device));
            }
            transitions.Release();
        }
    }

    public void Panic()
    {
        lock (gate)
        {
            if (disposed) return;
            engine.Reset();
            ResetControllers();
            if (selectedDevice is { } device) engine.Process(new(GamepadEventKind.Selected, device));
        }
    }

    private void OnMidi(MidiEvent message)
    {
        MidiGenerated?.Invoke(message);
        if (!forwarding || output?.IsConnected != true) return;
        usedChannels |= (ushort)(1 << message.Channel);
        if (!output.TrySend(message)) OnFault();
    }

    private void ResetControllers()
    {
        ushort channels = usedChannels;
        usedChannels = 0;
        for (byte channel = 0; channel < 16; channel++)
        {
            if ((channels & (1 << channel)) == 0) continue;
            OnMidi(new(MidiEventKind.ControlChange, channel, 123, 0));
            OnMidi(new(MidiEventKind.ChannelPressure, channel, 0, 0));
            OnMidi(new(MidiEventKind.PitchBend, channel, 0, 8192));
        }
        usedChannels = 0;
    }

    private void OnFault() => Interlocked.Exchange(ref faultPending, 1);
    private void ConsumeFault()
    {
        if (Interlocked.Exchange(ref faultPending, 0) == 0) return;
        // Backend owns wire cleanup after rejection/loss. Never replay stale notes after reconnect.
        forwarding = false;
        engine.Reset();
        usedChannels = 0;
        if (selectedDevice is { } device) engine.Process(new(GamepadEventKind.Selected, device));
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            engine.Reset();
            ResetControllers();
            disposed = true;
            engine.MidiGenerated -= OnMidi;
            if (output is not null) output.Faulted -= OnFault;
        }
    }
}
