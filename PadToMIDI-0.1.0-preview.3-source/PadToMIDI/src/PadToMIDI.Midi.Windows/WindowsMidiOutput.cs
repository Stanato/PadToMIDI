using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using PadToMIDI.Core.Midi;

namespace PadToMIDI.Midi.Windows;

/// <summary>A bounded nonblocking input submission queue and a dedicated native MIDI owner thread.</summary>
public sealed class WindowsMidiOutput : IMidiOutput
{
    private readonly BlockingCollection<WorkItem> queue = new(4096);
    private readonly Thread worker;
    private readonly SemaphoreSlim requests = new(1, 1);
    private Status status = new(MidiOutputState.Idle, "Select a Windows MIDI Services output.", null);
    private int accepting;
    private int fatalPending;
    private int disposed;
    private long sent;
    private long rejected;
    private long revision;
    public event Action? Faulted;

    public WindowsMidiOutput()
    {
        worker = new Thread(Run) { Name = "Windows MIDI Services", IsBackground = true };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
    }

    public bool IsConnected => Volatile.Read(ref accepting) == 1;
    public MidiOutputSnapshot CaptureSnapshot()
    {
        var current = Volatile.Read(ref status);
        return new(current.State, current.Message, current.EndpointId, Interlocked.Read(ref sent),
            Interlocked.Read(ref rejected), Interlocked.Read(ref revision));
    }

    public bool TrySend(in MidiEvent message)
    {
        if (!IsConnected || Volatile.Read(ref disposed) != 0) return false;
        if (TryEnqueue(new(Command.Send, message))) return true;
        Interlocked.Increment(ref rejected);
        SignalFault();
        return false;
    }

    public async ValueTask<IReadOnlyList<MidiEndpoint>> GetEndpointsAsync(CancellationToken cancellationToken = default) =>
        (IReadOnlyList<MidiEndpoint>)(await RequestAsync(Command.List, null, cancellationToken).ConfigureAwait(false))!;
    public async ValueTask ConnectAsync(string endpointId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        await RequestAsync(Command.Connect, endpointId, cancellationToken).ConfigureAwait(false);
    }
    public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await RequestAsync(Command.Disconnect, null, cancellationToken).ConfigureAwait(false);
    }
    public async ValueTask FlushAsync(CancellationToken cancellationToken = default) =>
        await RequestAsync(Command.Flush, null, cancellationToken).ConfigureAwait(false);

    private async Task<object?> RequestAsync(Command command, string? id, CancellationToken cancellationToken)
    {
        await requests.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (command == Command.Disconnect) Interlocked.Exchange(ref accepting, 0);
            var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Only control operations can wait for queue space. Live TrySend never waits.
            await Task.Run(() => queue.Add(new(command, default, id, completion))).ConfigureAwait(false);
            // Once accepted, finish the transition even if cancellation arrives; native state is deterministic.
            return await completion.Task.ConfigureAwait(false);
        }
        finally { requests.Release(); }
    }

    private void SignalFault()
    {
        if (Volatile.Read(ref disposed) != 0) return;
        Interlocked.Exchange(ref accepting, 0);
        Interlocked.Exchange(ref fatalPending, 1);
        TryEnqueue(new(Command.Wake));
        Faulted?.Invoke();
    }

    private bool TryEnqueue(WorkItem item)
    {
        try { return queue.TryAdd(item); }
        catch (InvalidOperationException) { return false; }
    }

    private void Run()
    {
        int apartment = RoInitialize(1); // RO_INIT_MULTITHREADED
        var client = new WindowsMidiClient(() => Interlocked.Increment(ref revision), SignalFault);
        try
        {
            Marshal.ThrowExceptionForHR(apartment);
            foreach (var item in queue.GetConsumingEnumerable())
            {
                if (Interlocked.Exchange(ref fatalPending, 0) != 0)
                    Fail(client, new InvalidOperationException("MIDI endpoint disconnected or the output queue filled. Reconnect to resume."));
                try
                {
                    switch (item.Command)
                    {
                        case Command.Send:
                            if (client.IsOpen) { client.Send(item.Message); Interlocked.Increment(ref sent); }
                            else Interlocked.Increment(ref rejected);
                            break;
                        case Command.List:
                            var endpoints = client.GetEndpoints();
                            if (!IsConnected) SetStatus(MidiOutputState.Ready, endpoints.Count == 0 ?
                                "No output groups found. Connect a MIDI device or configure a loopback/synth endpoint." : "Select an output and connect.");
                            item.Completion!.SetResult(endpoints);
                            continue;
                        case Command.Connect:
                            SetStatus(MidiOutputState.Connecting, "Opening MIDI output…");
                            client.Connect(item.EndpointId!);
                            Interlocked.Exchange(ref accepting, 1);
                            SetStatus(MidiOutputState.Connected, "Windows MIDI Services preview API", item.EndpointId);
                            break;
                        case Command.Disconnect:
                            client.Close();
                            SetStatus(MidiOutputState.Ready, "MIDI output disconnected.");
                            break;
                        case Command.Stop:
                            try { client.Dispose(); } catch (Exception) { /* Shutdown must still terminate the owner thread. */ }
                            SetStatus(MidiOutputState.Disposed, "MIDI output stopped.");
                            item.Completion!.SetResult(null);
                            return;
                    }
                    item.Completion?.SetResult(null);
                }
                catch (Exception error)
                {
                    if (item.Command == Command.Send) Interlocked.Increment(ref rejected);
                    Fail(client, error, item.Command == Command.List ? MidiOutputState.Unavailable : MidiOutputState.Faulted);
                    item.Completion?.SetException(error);
                }
            }
        }
        catch (Exception error)
        {
            SetStatus(MidiOutputState.Unavailable, error.Message);
            Interlocked.Exchange(ref accepting, 0);
            queue.CompleteAdding();
            while (queue.TryTake(out var item)) item.Completion?.TrySetException(error);
            Faulted?.Invoke();
        }
        finally
        {
            try { client.Dispose(); } catch (Exception) { }
            if (apartment >= 0) RoUninitialize();
        }
    }

    private void Fail(WindowsMidiClient client, Exception error, MidiOutputState state = MidiOutputState.Faulted)
    {
        Interlocked.Exchange(ref accepting, 0);
        try { client.Close(); } catch (Exception) { }
        SetStatus(state, $"{error.Message} Check Windows MIDI Services availability and the selected endpoint.");
        Faulted?.Invoke();
    }

    private void SetStatus(MidiOutputState state, string message, string? id = null) =>
        Volatile.Write(ref status, new(state, message, id));

    public async ValueTask DisposeAsync()
    {
        await requests.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            Interlocked.Exchange(ref accepting, 0);
            if (worker.IsAlive && !queue.IsAddingCompleted)
            {
                var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                await Task.Run(() => queue.Add(new(Command.Stop, default, null, completion))).ConfigureAwait(false);
                await completion.Task.ConfigureAwait(false);
            }
            queue.CompleteAdding();
            await Task.Run(worker.Join).ConfigureAwait(false);
        }
        finally { requests.Release(); }
    }

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoInitialize(uint type);
    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern void RoUninitialize();
    private sealed record Status(MidiOutputState State, string Message, string? EndpointId);
    private enum Command { Send, List, Connect, Disconnect, Flush, Wake, Stop }
    private readonly record struct WorkItem(Command Command, MidiEvent Message = default, string? EndpointId = null,
        TaskCompletionSource<object?>? Completion = null);
}
