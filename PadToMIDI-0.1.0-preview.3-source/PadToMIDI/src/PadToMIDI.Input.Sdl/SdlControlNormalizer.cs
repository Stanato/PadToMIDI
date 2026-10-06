using PadToMIDI.Core.Input;
using SDL;

namespace PadToMIDI.Input.Sdl;

internal static class SdlControlNormalizer
{
    public static PhysicalControl? Button(SDL_GamepadButton button) => button switch
    {
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH => PhysicalControl.FaceSouth,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH => PhysicalControl.FaceNorth,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST => PhysicalControl.FaceWest,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST => PhysicalControl.FaceEast,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN => PhysicalControl.DPadDown,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP => PhysicalControl.DPadUp,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT => PhysicalControl.DPadLeft,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT => PhysicalControl.DPadRight,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER => PhysicalControl.LeftBumper,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER => PhysicalControl.RightBumper,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK => PhysicalControl.LeftStickButton,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_STICK => PhysicalControl.RightStickButton,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK => PhysicalControl.Back,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START => PhysicalControl.Start,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_GUIDE => PhysicalControl.Guide,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_MISC1 => PhysicalControl.Misc1,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_PADDLE1 => PhysicalControl.RightPaddle1,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_PADDLE1 => PhysicalControl.LeftPaddle1,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_PADDLE2 => PhysicalControl.RightPaddle2,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_PADDLE2 => PhysicalControl.LeftPaddle2,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_TOUCHPAD => PhysicalControl.Touchpad,
        _ => null
    };

    public static PhysicalControl? Axis(SDL_GamepadAxis axis) => axis switch
    {
        SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX => PhysicalControl.LeftStickX,
        SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY => PhysicalControl.LeftStickY,
        SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX => PhysicalControl.RightStickX,
        SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY => PhysicalControl.RightStickY,
        SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER => PhysicalControl.LeftTrigger,
        SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER => PhysicalControl.RightTrigger,
        _ => null
    };

    // Asymmetric denominators preserve both exact stick endpoints and exact zero.
    public static float AxisValue(SDL_GamepadAxis axis, short value) =>
        axis is SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER or SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER
            ? Math.Clamp(value / 32767f, 0, 1)
            : value < 0 ? value / 32768f : value / 32767f;
}
