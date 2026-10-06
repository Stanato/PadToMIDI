using System.Collections.Immutable;

namespace PadToMIDI.Core.Input;

public enum InputStatus { Starting, Ready, Connected, Faulted, Stopped }

public sealed record GamepadInputSnapshot(
    ImmutableArray<GamepadDevice> Devices,
    GamepadDeviceId? SelectedDeviceId,
    GamepadValues Values,
    InputStatus Status,
    string Message,
    long EventCount);
