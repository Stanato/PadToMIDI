using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using PadToMIDI.Core.Input;
using PadToMIDI.Core.Midi;
using PadToMIDI.Core.Musical;
using PadToMIDI.Input.Sdl;
using SDL;
using static SDL.SDL3;

internal static unsafe class Program
{
    private static int Main()
    {
        SDL_SetMainReady();
        if (!SDL_Init(SDL_InitFlags.SDL_INIT_GAMEPAD)) throw new Exception(SDL_GetError());
        var first = Attach("Virtual controller 1");
        var second = Attach("Virtual controller 2");
        var firstHandle = SDL_OpenJoystick(first);
        var secondHandle = SDL_OpenJoystick(second);
        SDL_JoystickID hotplugged = default;
        SDL_SetJoystickVirtualAxis(firstHandle, 4, short.MinValue);
        SDL_SetJoystickVirtualAxis(firstHandle, 5, short.MinValue);
        SDL_SetJoystickVirtualAxis(secondHandle, 4, short.MinValue);
        SDL_SetJoystickVirtualAxis(secondHandle, 5, short.MinValue);
        var input = new SdlGamepadInput();
        var engine = new MappingEngine();
        var engineGate = new object();
        MusicalStateSnapshot MusicalSnapshot() { lock (engineGate) return engine.CaptureSnapshot(); }
        var midi = new ConcurrentQueue<MidiEvent>();
        engine.MidiGenerated += midi.Enqueue;
        input.InputReceived += e => { lock (engineGate) engine.Process(e); };
        var received = new ConcurrentQueue<GamepadInputEvent>();
        input.InputReceived += received.Enqueue;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        input.SelectDevice(new((uint)first));
        var driver = Task.Run(() =>
        {
            try
            {
                Await(() => input.CaptureSnapshot().SelectedDeviceId?.Value == (uint)first, "First controller discovery/selection");
                Assert(input.CaptureSnapshot().Devices.Length >= 2, "Multiple controller discovery");
                SDL_SetJoystickVirtualButton(firstHandle, 0, true);
                SDL_SetJoystickVirtualAxis(firstHandle, 0, short.MinValue / 2); // Below the musical action threshold.
                SDL_SetJoystickVirtualAxis(firstHandle, 4, short.MaxValue);
                Await(() => input.CaptureSnapshot().Values is { LeftStickX: -0.5f, LeftTrigger: 1 } values && values.IsPressed(PhysicalControl.FaceSouth), "Normalized button/stick/trigger");
                Await(() => midi.Any(message => message == new MidiEvent(MidiEventKind.NoteOn, 0, 55, 100)), "Core emits G3 Note On from native input");
                SDL_SetJoystickVirtualButton(firstHandle, 0, false);
                Await(() => !input.CaptureSnapshot().Values.IsPressed(PhysicalControl.FaceSouth), "Button release");
                Await(() => midi.Any(message => message == new MidiEvent(MidiEventKind.NoteOff, 0, 55, 0)), "Core emits matching G3 Note Off");
                SDL_SetJoystickVirtualAxis(firstHandle, 4, short.MinValue);
                Await(() => MusicalSnapshot().Expression.DPadPressure == 0, "Trigger neutral before default note checks");

                var noteButtons = new (int NativeButton, byte Pitch)[]
                {
                    (12, 48), (11, 50), (13, 52), (14, 53), (0, 55), (3, 57), (2, 59), (1, 60)
                };
                foreach (var (button, pitch) in noteButtons)
                {
                    int countBefore = midi.Count;
                    SDL_SetJoystickVirtualButton(firstHandle, button, true);
                    Await(() => midi.Count > countBefore, $"Native note button {button} press");
                    Assert(midi.Last() == new MidiEvent(MidiEventKind.NoteOn, 0, pitch, 100), "Default C Major native mapping");
                    SDL_SetJoystickVirtualButton(firstHandle, button, false);
                    Await(() => midi.Count > countBefore + 1, $"Native note button {button} release");
                    Assert(midi.Last() == new MidiEvent(MidiEventKind.NoteOff, 0, pitch, 0), "Default native note release");
                }

                SDL_SetJoystickVirtualButton(firstHandle, 14, true);
                SDL_SetJoystickVirtualButton(firstHandle, 0, true);
                Await(() => MusicalSnapshot().HeldNotes.Length == 2, "Hold separate expression groups");
                SDL_SetJoystickVirtualAxis(firstHandle, 4, short.MaxValue);
                SDL_SetJoystickVirtualAxis(firstHandle, 5, short.MaxValue);
                Await(() => midi.Contains(new(MidiEventKind.PolyphonicPressure, 0, 53, 127)) &&
                    midi.Contains(new(MidiEventKind.PolyphonicPressure, 0, 55, 127)), "Native L2/R2 group pressure");
                SDL_SetJoystickVirtualAxis(firstHandle, 2, short.MinValue);
                Await(() => midi.Contains(new(MidiEventKind.PitchBend, 0, 0, 0)), "Native minimum pitch bend");
                SDL_SetJoystickVirtualAxis(firstHandle, 2, short.MaxValue);
                Await(() => midi.Contains(new(MidiEventKind.PitchBend, 0, 0, 16383)), "Native maximum pitch bend and heartbeat flush");
                SDL_SetJoystickVirtualAxis(firstHandle, 2, 0);
                Await(() => midi.Last(m => m.Kind == MidiEventKind.PitchBend).Data2 == 8192, "Native exact bend center");
                SDL_SetJoystickVirtualAxis(firstHandle, 3, short.MinValue);
                Await(() => midi.Contains(new(MidiEventKind.ControlChange, 0, 74, 127, 64)), "Native Up gives maximum CC74");
                SDL_SetJoystickVirtualAxis(firstHandle, 3, 0);
                Await(() => midi.Last(m => m.Kind == MidiEventKind.ControlChange).Data2 == 64, "Native timbre neutral");
                SDL_SetJoystickVirtualAxis(firstHandle, 4, short.MinValue);
                SDL_SetJoystickVirtualAxis(firstHandle, 5, short.MinValue);
                Await(() => MusicalSnapshot().Expression.DPadPressure == 0 && MusicalSnapshot().Expression.FacePressure == 0, "Pressure release resets");
                SDL_SetJoystickVirtualButton(firstHandle, 14, false);
                SDL_SetJoystickVirtualButton(firstHandle, 0, false);
                Await(() => MusicalSnapshot().HeldNotes.IsEmpty, "Release expression test notes");

                SDL_SetJoystickVirtualAxis(firstHandle, 0, 0);
                Await(() => input.CaptureSnapshot().Values.LeftStickX == 0, "Stick neutral before musical gestures");
                foreach (var (modifier, pitch) in new (int Button, byte Pitch)[] { (9, 54), (10, 56) })
                {
                    var control = modifier == 9 ? PhysicalControl.LeftBumper : PhysicalControl.RightBumper;
                    SDL_SetJoystickVirtualButton(firstHandle, modifier, true);
                    Await(() => input.CaptureSnapshot().Values.IsPressed(control), "Momentary modifier down");
                    int count = midi.Count;
                    SDL_SetJoystickVirtualButton(firstHandle, 0, true);
                    Await(() => midi.Count > count && midi.Last() == new MidiEvent(MidiEventKind.NoteOn, 0, pitch, 100), "Shifted native Note On");
                    SDL_SetJoystickVirtualButton(firstHandle, modifier, false);
                    Await(() => !input.CaptureSnapshot().Values.IsPressed(control), "Modifier released before note");
                    SDL_SetJoystickVirtualButton(firstHandle, 0, false);
                    Await(() => midi.Count > count + 1 && midi.Last() == new MidiEvent(MidiEventKind.NoteOff, 0, pitch, 0), "Exact shifted native Note Off");
                }
                foreach (var (value, shift, pitch) in new (short Value, int Shift, byte Pitch)[] { (short.MaxValue, -1, 43), (short.MinValue, 1, 67) })
                {
                    const int axis = 1;
                    SDL_SetJoystickVirtualAxis(firstHandle, axis, value);
                    Await(() => MusicalSnapshot().TemporaryOctaveOffset == shift, "Native momentary stick octave");
                    Assert(MusicalSnapshot().BaseOctave == 3, "Stick leaves persistent base unchanged");
                    SDL_SetJoystickVirtualAxis(firstHandle, axis, (short)(Math.Sign(value) * 30000));
                    int count = midi.Count;
                    SDL_SetJoystickVirtualButton(firstHandle, 0, true);
                    Await(() => midi.Count > count && midi.Last() == new MidiEvent(MidiEventKind.NoteOn, 0, pitch, 100), "Native stick-shifted Note On");
                    SDL_SetJoystickVirtualAxis(firstHandle, axis, 0);
                    Await(() => MusicalSnapshot().TemporaryOctaveOffset == 0, "Center restores octave before release");
                    SDL_SetJoystickVirtualButton(firstHandle, 0, false);
                    Await(() => midi.Count > count + 1 && midi.Last() == new MidiEvent(MidiEventKind.NoteOff, 0, pitch, 0), "Exact octave Note Off after centering");
                }

                SDL_SetJoystickVirtualButton(firstHandle, 6, true); // SDL standardized Start/Menu position.
                Await(() => MusicalSnapshot().Configuration.Chords.Enabled, "Native Start toggles chord mode");
                SDL_SetJoystickVirtualButton(firstHandle, 0, true);
                Await(() => MusicalSnapshot().HeldNotes.Length == 3, "Native face press plays a triad");
                Assert(MusicalSnapshot().HeldNotes.Select(note => note.MidiNote).SequenceEqual(new byte[] {55,59,62}), "Native G major chord");
                SDL_SetJoystickVirtualButton(firstHandle, 6, false);
                Await(() => !input.CaptureSnapshot().Values.IsPressed(PhysicalControl.Start), "Native Start release rearms toggle");
                SDL_SetJoystickVirtualButton(firstHandle, 6, true);
                Await(() => !MusicalSnapshot().Configuration.Chords.Enabled, "Native second Start returns to note mode");
                Assert(MusicalSnapshot().HeldNotes.Length == 3, "Mode change retains held native triad");
                SDL_SetJoystickVirtualButton(firstHandle, 6, false);
                SDL_SetJoystickVirtualButton(firstHandle, 0, false);
                Await(() => MusicalSnapshot().HeldNotes.IsEmpty, "Native chord releases every voice");

                int beforeSwitch = midi.Count;
                SDL_SetJoystickVirtualButton(firstHandle, 9, true);
                Await(() => MusicalSnapshot().TemporarySemitoneOffset == -1, "Native flat modifier held");
                SDL_SetJoystickVirtualButton(firstHandle, 0, true);
                Await(() => midi.Count > beforeSwitch && midi.Last() == new MidiEvent(MidiEventKind.NoteOn, 0, 54, 100), "Hold a flat note before switching");
                input.SelectDevice(new((uint)second));
                Await(() => input.CaptureSnapshot().SelectedDeviceId?.Value == (uint)second, "Controller switching");
                Await(() => midi.Count > beforeSwitch + 1, "Core releases held note on switching");
                Assert(midi.Last() == new MidiEvent(MidiEventKind.NoteOff, 0, 54, 0), "Switch stops adjusted prior note");
                Assert(input.CaptureSnapshot().Values.LeftStickX == 0, "Switch clears old state");
                SDL_SetJoystickVirtualButton(secondHandle, 1, true);
                Await(() => input.CaptureSnapshot().Values.IsPressed(PhysicalControl.FaceEast), "Second controller input");
                Await(() => midi.Last() == new MidiEvent(MidiEventKind.NoteOn, 0, 60, 100), "Second controller plays C4");
                SDL_DetachVirtualJoystick(second);
                Await(() => input.CaptureSnapshot().SelectedDeviceId is null, "Hot-unplug selection cleanup");
                Assert(input.CaptureSnapshot().Values == default, "Disconnect clears input");
                Assert(received.Any(e => e.Kind == GamepadEventKind.Disconnected && e.DeviceId.Value == (uint)second), "Disconnect lifecycle notification");
                Await(() => midi.Last() == new MidiEvent(MidiEventKind.NoteOff, 0, 60, 0), "Disconnect sends matching C4 Note Off");
                hotplugged = Attach("Hot-plugged virtual controller");
                Await(() => input.CaptureSnapshot().Devices.Any(device => device.Id.Value == (uint)hotplugged), "Hot-plug addition discovery");
                input.SelectDevice(new((uint)hotplugged));
                Await(() => input.CaptureSnapshot().SelectedDeviceId?.Value == (uint)hotplugged, "Hot-plugged controller selection");
                Console.WriteLine("Virtual SDL integration: notes, octave/accidental modifiers, L2/R2 poly pressure, pitch bend/center, CC74, heartbeat flushing, switching/disconnect cleanup, hot-plug PASS.");
            }
            finally { stop.Cancel(); }
        });
        int result = 0;
        try
        {
            input.Run(stop.Token);
            driver.GetAwaiter().GetResult();
            Assert(received.Any(e => e.Kind == GamepadEventKind.Stopped), "Shutdown lifecycle notification");
            Assert(engine.CaptureSnapshot().HeldNotes.IsEmpty, "Musical state is empty after shutdown");
        }
        catch (Exception error) { Console.Error.WriteLine(error); result = 1; }
        finally
        {
            SDL_CloseJoystick(firstHandle);
            SDL_CloseJoystick(secondHandle);
            SDL_DetachVirtualJoystick(first);
            if ((uint)hotplugged != 0)
                SDL_DetachVirtualJoystick(hotplugged);
            SDL_Quit();
        }
        return result;
    }

    private static SDL_JoystickID Attach(string name)
    {
        nint bytes = Marshal.StringToCoTaskMemUTF8(name);
        try
        {
            var desc = new SDL_VirtualJoystickDesc
            {
                version = (uint)sizeof(SDL_VirtualJoystickDesc),
                type = (ushort)SDL_JoystickType.SDL_JOYSTICK_TYPE_GAMEPAD,
                naxes = 6,
                nbuttons = 21,
                button_mask = (1u << 21) - 1,
                axis_mask = 63,
                name = (byte*)bytes
            };
            var id = SDL_AttachVirtualJoystick(&desc);
            if ((uint)id == 0) throw new Exception(SDL_GetError());
            return id;
        }
        finally { Marshal.FreeCoTaskMem(bytes); }
    }

    private static void Await(Func<bool> condition, string label)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(3)) throw new Exception($"Timed out: {label}");
            Thread.Sleep(5);
        }
    }
    private static void Assert(bool condition, string label)
    {
        if (!condition) throw new Exception($"Failed: {label}");
    }
}
