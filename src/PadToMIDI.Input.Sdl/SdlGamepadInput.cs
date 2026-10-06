using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using PadToMIDI.Core.Input;
using SDL;
using static SDL.SDL3;

namespace PadToMIDI.Input.Sdl;

/// <summary>All native handles and SDL event pumping are owned by Run's main thread.</summary>
public sealed unsafe class SdlGamepadInput : IGamepadInput
{
    private readonly GamepadStateBuffer state = new();
    private readonly ConcurrentQueue<GamepadDeviceId?> selections = new();
    private ImmutableArray<GamepadDevice> devices = [];
    private SDL_Gamepad* gamepad;
    private GamepadDeviceId? selected;
    private GamepadValues values;
    private bool autoSelect = true;
    private int started;

    public event Action<GamepadInputEvent>? InputReceived;

    public void SelectDevice(GamepadDeviceId? deviceId) => selections.Enqueue(deviceId);
    public GamepadInputSnapshot CaptureSnapshot() => state.CaptureSnapshot();

    public void Run(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref started, 1) != 0)
            throw new InvalidOperationException("An SDL input instance can only run once.");

        bool initialized = false;
        bool faulted = false;
        try
        {
            SDL_SetMainReady();
            SDL_SetAppMetadata("PadToMIDI", "0.1.0", "PadToMIDI.App");
            SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
            if (!SDL_Init(SDL_InitFlags.SDL_INIT_GAMEPAD))
                throw new InvalidOperationException($"SDL initialization failed: {SDL_GetError()}");
            initialized = true;
            state.SetStatus(InputStatus.Ready, "Connect an SDL-compatible controller to begin.");
            DiscoverDevices();

            // A bounded native wait avoids busy polling and bounds selection/shutdown latency.
            while (!cancellationToken.IsCancellationRequested)
            {
                while (selections.TryDequeue(out var requested))
                {
                    autoSelect = false;
                    ChangeSelection(requested);
                }

                SDL_Event input;
                if (SDL_WaitEventTimeout(&input, 4))
                    HandleEvent(in input);

                // Limit each batch so continuous input cannot starve cancellation or UI commands.
                for (int i = 0; i < 256 && !cancellationToken.IsCancellationRequested; i++)
                {
                    if (!SDL_PollEvent(&input))
                        break;
                    HandleEvent(in input);
                }
                if (selected is { } device)
                    InputReceived?.Invoke(new(GamepadEventKind.Tick, device, Timestamp: Stopwatch.GetTimestamp()));
            }
        }
        catch (Exception error)
        {
            faulted = true;
            // A failed downstream subscriber is a fatal pipeline error too.
            try { Publish(new(GamepadEventKind.Faulted, selected ?? default, Timestamp: Stopwatch.GetTimestamp())); }
            catch (Exception) { /* Native cleanup below must run even if a subscriber fails again. */ }
            state.SetStatus(InputStatus.Faulted, $"Input stopped: {error.Message}");
        }
        finally
        {
            try
            {
                if (!faulted)
                    Publish(new(GamepadEventKind.Stopped, selected ?? default, Timestamp: Stopwatch.GetTimestamp()));
            }
            catch (Exception error)
            {
                state.Apply(new(GamepadEventKind.Faulted, selected ?? default));
                state.SetStatus(InputStatus.Faulted, $"Shutdown subscriber failed: {error.Message}");
            }
            finally
            {
                if (gamepad != null)
                    SDL_CloseGamepad(gamepad);
                gamepad = null;
                selected = null;
                values = default;
                if (initialized)
                    SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_GAMEPAD);
            }
        }
    }

    private void HandleEvent(in SDL_Event input)
    {
        switch (input.Type)
        {
            case SDL_EventType.SDL_EVENT_GAMEPAD_ADDED:
                DiscoverDevices();
                break;
            case SDL_EventType.SDL_EVENT_GAMEPAD_REMAPPED:
                var remapped = selected;
                if (remapped?.Value == (uint)input.gdevice.which)
                {
                    ChangeSelection(null);
                    DiscoverDevices();
                    ChangeSelection(remapped);
                }
                else
                    DiscoverDevices();
                break;
            case SDL_EventType.SDL_EVENT_GAMEPAD_REMOVED:
                if (selected?.Value == (uint)input.gdevice.which)
                    ChangeSelection(null);
                DiscoverDevices();
                break;
            case SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_DOWN:
            case SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_UP:
                if (selected?.Value == (uint)input.gbutton.which &&
                    SdlControlNormalizer.Button(input.gbutton.Button) is { } button)
                    PublishControl(button, input.gbutton.down ? 1 : 0);
                break;
            case SDL_EventType.SDL_EVENT_GAMEPAD_AXIS_MOTION:
                if (selected?.Value == (uint)input.gaxis.which &&
                    SdlControlNormalizer.Axis(input.gaxis.Axis) is { } axis)
                    PublishControl(axis, SdlControlNormalizer.AxisValue(input.gaxis.Axis, input.gaxis.value));
                break;
        }
    }

    private void DiscoverDevices()
    {
        int count;
        var ids = SDL_GetGamepads(&count);
        if (ids == null)
            throw new InvalidOperationException($"Controller discovery failed: {SDL_GetError()}");
        try
        {
            var builder = ImmutableArray.CreateBuilder<GamepadDevice>(count);
            for (int i = 0; i < count; i++)
                builder.Add(new(new((uint)ids[i]), SDL_GetGamepadNameForID(ids[i]) ?? "Unnamed controller"));
            devices = builder.MoveToImmutable();
            state.SetDevices(devices);
        }
        finally { SDL_free(ids); }

        if (selected is { } id && !devices.Any(device => device.Id == id))
            ChangeSelection(null);
        if (selected is null && autoSelect && !devices.IsEmpty)
            ChangeSelection(devices[0].Id);
    }

    private void ChangeSelection(GamepadDeviceId? requested)
    {
        if (requested == selected)
            return;
        if (selected is { } previous)
        {
            // Also used on switching: the engine cleans up before a new Selected event.
            try { Publish(new(GamepadEventKind.Disconnected, previous, Timestamp: Stopwatch.GetTimestamp())); }
            finally
            {
                SDL_CloseGamepad(gamepad);
                gamepad = null;
                selected = null;
                values = default;
            }
        }
        if (requested is not { } next)
            return;
        if (!devices.Any(device => device.Id == next))
        {
            state.SetStatus(InputStatus.Ready, "That controller is no longer connected.");
            return;
        }

        gamepad = SDL_OpenGamepad((SDL_JoystickID)next.Value);
        if (gamepad == null)
        {
            state.SetStatus(InputStatus.Ready, $"Could not open controller: {SDL_GetError()}");
            return;
        }
        selected = next;
        Publish(new(GamepadEventKind.Selected, next, Timestamp: Stopwatch.GetTimestamp()));
        ReadInitialState();
    }

    private void ReadInitialState()
    {
        for (int i = 0; i < (int)SDL_GamepadButton.SDL_GAMEPAD_BUTTON_COUNT; i++)
            if (SdlControlNormalizer.Button((SDL_GamepadButton)i) is { } button)
                PublishControl(button, SDL_GetGamepadButton(gamepad, (SDL_GamepadButton)i) ? 1 : 0);
        for (int i = 0; i < (int)SDL_GamepadAxis.SDL_GAMEPAD_AXIS_COUNT; i++)
            if (SdlControlNormalizer.Axis((SDL_GamepadAxis)i) is { } axis)
                PublishControl(axis, SdlControlNormalizer.AxisValue((SDL_GamepadAxis)i,
                    SDL_GetGamepadAxis(gamepad, (SDL_GamepadAxis)i)));
    }

    private void PublishControl(PhysicalControl control, float value)
    {
        if (values.GetValue(control) == value || selected is not { } id)
            return;
        values = values.WithValue(control, value);
        Publish(new(GamepadEventKind.ControlChanged, id, control, value, Stopwatch.GetTimestamp()));
    }

    private void Publish(GamepadInputEvent input)
    {
        state.Apply(input);
        InputReceived?.Invoke(input);
    }
}
