using System.Windows.Input;

namespace PadToMIDI.App.ViewModels;

public sealed class AsyncCommand(Func<Task> execute, Action<Exception> onError) : ICommand
{
    private bool running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !running;
    public async void Execute(object? parameter)
    {
        if (running) return;
        running = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await execute(); }
        catch (Exception error) { onError(error); }
        finally { running = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}
