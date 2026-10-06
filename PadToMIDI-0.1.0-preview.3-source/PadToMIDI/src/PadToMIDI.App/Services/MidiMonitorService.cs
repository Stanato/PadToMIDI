using Avalonia.Threading;
using PadToMIDI.App.ViewModels;
using PadToMIDI.Core.Midi;

namespace PadToMIDI.App.Services;

/// <summary>Observes native topology revisions; endpoint enumeration never runs in the input or UI thread.</summary>
public sealed class MidiMonitorService : IDisposable
{
    private readonly IMidiOutput output;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private long revision = -1;
    private bool refreshing;
    private bool disposed;
    public MidiViewModel Model { get; }

    public MidiMonitorService(IMidiOutput output, InstrumentSession instrument)
    {
        this.output = output;
        Model = new(RefreshAsync, id => instrument.ConnectOutputAsync(id).AsTask(),
            () => instrument.DisconnectOutputAsync().AsTask(), instrument.SetMidiChannel, instrument.Panic);
        timer.Tick += OnTick;
    }

    public void Start() { timer.Start(); Model.RefreshCommand.Execute(null); }
    private void OnTick(object? sender, EventArgs args)
    {
        var snapshot = output.CaptureSnapshot();
        Model.Update(snapshot);
        if (!refreshing && revision != snapshot.EndpointRevision) Model.RefreshCommand.Execute(null);
    }
    private async Task RefreshAsync()
    {
        if (refreshing || disposed) return;
        refreshing = true;
        revision = output.CaptureSnapshot().EndpointRevision;
        try
        {
            var endpoints = await output.GetEndpointsAsync();
            if (!disposed) Model.UpdateEndpoints(endpoints, output.CaptureSnapshot().EndpointId);
        }
        finally { refreshing = false; }
    }
    public void Dispose() { disposed = true; timer.Stop(); timer.Tick -= OnTick; }
}
