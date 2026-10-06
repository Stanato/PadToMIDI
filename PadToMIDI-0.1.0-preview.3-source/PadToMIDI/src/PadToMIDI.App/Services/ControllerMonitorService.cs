using Avalonia.Threading;
using PadToMIDI.App.ViewModels;
using PadToMIDI.Core.Input;

namespace PadToMIDI.App.Services;

/// <summary>Independent 30 Hz observer. No live input events are delivered through this service.</summary>
public sealed class ControllerMonitorService : IDisposable
{
    private readonly IGamepadInput input;
    private readonly MainWindowViewModel model;
    private readonly InstrumentSession instrument;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(1000d / 30) };

    public ControllerMonitorService(IGamepadInput input, InstrumentSession instrument, MainWindowViewModel model)
    {
        this.input = input;
        this.model = model;
        this.instrument = instrument;
        timer.Tick += OnTick;
    }

    public void Start()
    {
        model.Update(input.CaptureSnapshot());
        model.UpdateInstrument(instrument.CaptureSnapshot());
        timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        model.Update(input.CaptureSnapshot());
        model.UpdateInstrument(instrument.CaptureSnapshot());
    }

    public void Dispose()
    {
        timer.Stop();
        timer.Tick -= OnTick;
    }
}
