using System.Collections.Immutable;
using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Input;
using PadToMIDI.Core.Midi;
using PadToMIDI.Core.Musical;

namespace PadToMIDI.App.Services;

/// <summary>Composes the input-to-engine path and a bounded, independently sampled activity preview.</summary>
public sealed class InstrumentSession : IDisposable
{
    private const int ActivityCapacity = 8;
    private readonly object gate = new();
    private readonly IGamepadInput input;
    private readonly MidiInstrument engine;
    private readonly MidiActivityEntry[] activity = new MidiActivityEntry[ActivityCapacity];
    private long eventCount;
    private bool disposed;

    public InstrumentSession(IGamepadInput input, IMidiOutput? output = null)
    {
        this.input = input;
        engine = new(output);
        engine.MidiGenerated += RecordMidi;
        input.InputReceived += OnInput;
    }

    private void OnInput(GamepadInputEvent inputEvent)
    {
        engine.Process(inputEvent);
    }

    // No strings or UI work are created per note event. CaptureSnapshot copies the ring on UI ticks.
    private void RecordMidi(MidiEvent message)
    {
        lock (gate)
        {
            long sequence = ++eventCount;
            activity[(int)((sequence - 1) % ActivityCapacity)] = new(sequence, message);
        }
    }

    public InstrumentSnapshot CaptureSnapshot()
    {
        var state = engine.CaptureSnapshot();
        lock (gate)
        {
            int count = (int)Math.Min(eventCount, ActivityCapacity);
            var entries = ImmutableArray.CreateBuilder<MidiActivityEntry>(count);
            for (int i = 0; i < count; i++)
                entries.Add(activity[(int)((eventCount - 1 - i) % ActivityCapacity)]);
            return new(state, entries.MoveToImmutable(), eventCount);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        engine.Dispose();
        disposed = true;
        input.InputReceived -= OnInput;
        engine.MidiGenerated -= RecordMidi;
    }

    public ValueTask ConnectOutputAsync(string endpointId, CancellationToken cancellationToken = default) => engine.ConnectOutputAsync(endpointId, cancellationToken);
    public ValueTask DisconnectOutputAsync(CancellationToken cancellationToken = default) => engine.DisconnectOutputAsync(cancellationToken);
    public void SetMidiChannel(byte channel) => engine.SetMidiChannel(channel);
    public void UpdateConfiguration(InstrumentConfiguration configuration) => engine.UpdateConfiguration(configuration);
    public void ApplyProfile(InstrumentConfiguration configuration) => engine.UpdateConfiguration(configuration, resetPersistentOctave: true);
    public void Panic() => engine.Panic();
}

public readonly record struct MidiActivityEntry(long Sequence, MidiEvent Message);
public sealed record InstrumentSnapshot(MusicalStateSnapshot State, ImmutableArray<MidiActivityEntry> Activity, long EventCount);
