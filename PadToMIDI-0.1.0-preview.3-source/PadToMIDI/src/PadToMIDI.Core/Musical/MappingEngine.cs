using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Input;
using PadToMIDI.Core.Midi;

namespace PadToMIDI.Core.Musical;

/// <summary>Single-owner musical engine. No UI, native input types, or MIDI backend dependencies.</summary>
public sealed class MappingEngine
{
    private readonly MusicalState state;
    private readonly ExpressionEngine expression;
    private readonly TimeProvider time;
    private readonly long origin;
    private double Now => time.GetElapsedTime(origin).TotalMilliseconds;
    private readonly int[] pitchOwners = new int[16 * 128];

    /// <summary>Synchronous, nonblocking delivery on the input owner thread.</summary>
    public event Action<MidiEvent>? MidiGenerated;

    public MappingEngine(InstrumentConfiguration? configuration = null, TimeProvider? timeProvider = null)
    {
        configuration ??= new();
        configuration.Validate();
        state = new(configuration);
        time = timeProvider ?? TimeProvider.System;
        origin = time.GetTimestamp();
        expression = new(message => MidiGenerated?.Invoke(message));
    }

    public MusicalStateSnapshot CaptureSnapshot() => state.CaptureSnapshot();

    /// <summary>Changes future presses only. Held records and physical edge state are retained.</summary>
    public void UpdateConfiguration(InstrumentConfiguration configuration, bool resetPersistentOctave = false)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        bool resetExpression = state.Configuration.Expression != configuration.Expression || state.Configuration.MidiChannel != configuration.MidiChannel;
        if (resetExpression) expression.Reset(state, Now);
        if (resetPersistentOctave || state.Configuration.BaseOctave != configuration.BaseOctave) state.PersistentOctaveOffset = 0;
        state.Configuration = configuration;
        state.PlayingMessage = "";
        if (resetExpression) expression.Update(state, Now);
    }

    public void Process(GamepadInputEvent input)
    {
        switch (input.Kind)
        {
            case GamepadEventKind.Selected:
                Reset();
                state.Device = input.DeviceId;
                break;
            case GamepadEventKind.Disconnected when state.Device == input.DeviceId:
            case GamepadEventKind.Faulted:
            case GamepadEventKind.Stopped:
                Reset();
                break;
            case GamepadEventKind.ControlChanged when state.Device == input.DeviceId:
                ChangeControl(input.Control, input.Value);
                expression.Update(state, Now, input.Control);
                break;
            case GamepadEventKind.Tick when state.Device == input.DeviceId:
                expression.Update(state, Now);
                break;
        }
    }

    /// <summary>Stops owned pitches and clears held controls, selections, and latches; retains the playing octave.</summary>
    public void Reset()
    {
        expression.Reset(state, Now);
        for (int i = 0; i < state.Held.Length; i++)
            ReleaseNote((PhysicalControl)i);
        state.Device = null;
        state.Controls = default;
        state.LeftStickLatch = default;
        state.LastNote = null;
        state.LastDPadNote = null;
        state.LastFaceNote = null;
        state.Sequence = 0;
        state.PlayingMessage = "";
    }

    private void ChangeControl(PhysicalControl control, float value)
    {
        bool previouslyPressed = state.Controls.IsPressed(control);
        state.Controls = state.Controls.WithValue(control, value);
        var stick = state.Configuration.LeftStick;
        if (control == stick.XAxis || control == stick.YAxis)
        {
            if (state.LeftStickLatch.Update(state.Controls.GetValue(stick.XAxis), state.Controls.GetValue(stick.YAxis), stick) is { } direction)
                PerformStickAction(stick.ActionFor(direction));
            return;
        }
        if (control >= PhysicalControl.LeftTrigger) return;
        bool pressed = state.Controls.IsPressed(control);
        if (pressed == previouslyPressed) return;
        if (control == state.Configuration.Chords.ToggleButton)
        {
            if (pressed)
            {
                if (!state.Configuration.Chords.Enabled && !ChordResolver.CanEnable(state.Configuration))
                    state.PlayingMessage = "Chord mode needs a seven-note harmony scale or explicit chord shape in Settings.";
                else
                {
                    state.Configuration = state.Configuration with { Chords = state.Configuration.Chords with { Enabled = !state.Configuration.Chords.Enabled } };
                    state.PlayingMessage = "";
                }
            }
            if (!pressed) ReleaseNote(control); // A remapped toggle can still own an older held note.
            return;
        }
        if (pressed)
        {
            if (!state.Configuration.TemporaryOctaveModifiers.ContainsKey(control) &&
                !state.Configuration.TemporarySemitoneModifiers.ContainsKey(control)) PressNote(control);
        }
        else ReleaseNote(control);
    }

    private void PressNote(PhysicalControl control)
    {
        var configuration = state.Configuration;
        if (!configuration.Mappings.ContainsKey(control)) return;
        if (!ChordResolver.TryResolve(configuration, control, state.BaseOctave, state.TemporaryOctaveOffset, state.TemporarySemitoneOffset, out var record, out var reason))
        { state.PlayingMessage = reason; return; }
        state.PlayingMessage = "";
        record = record with { Group = GroupOf(control), Sequence = ++state.Sequence };
        HoldNote(record);
    }

    private void HoldNote(NoteRecord record)
    {
        state.Held[(int)record.Control] = record;
        RememberNote(record);
        // MIDI 1.0 cannot distinguish two simultaneous voices on the same channel/pitch.
        // Coalesce shared pitches until the last owning physical control releases.
        for (int voice = 0; voice < record.VoiceCount; voice++)
        {
            byte pitch = record.PitchAt(voice);
            if (pitchOwners[record.Channel * 128 + pitch]++ == 0)
                MidiGenerated?.Invoke(new(MidiEventKind.NoteOn, record.Channel, pitch, record.Velocity));
        }
    }

    private void RememberNote(NoteRecord record)
    {
        state.LastNote = record;
        if (record.Group == NoteGroup.DPad) state.LastDPadNote = record;
        if (record.Group == NoteGroup.FaceButtons) state.LastFaceNote = record;
    }

    private void PerformStickAction(StickAction action)
    {
        switch (action)
        {
            case StickAction.IncreaseOctave:
                state.PersistentOctaveOffset = Math.Min(9, state.BaseOctave + 1) - state.Configuration.BaseOctave;
                break;
            case StickAction.DecreaseOctave:
                state.PersistentOctaveOffset = Math.Max(-1, state.BaseOctave - 1) - state.Configuration.BaseOctave;
                break;
            case StickAction.SharpenLastNote: TransposeLastNote(1); break;
            case StickAction.FlattenLastNote: TransposeLastNote(-1); break;
        }
    }

    private void TransposeLastNote(int semitones)
    {
        if (state.LastNote is not { } selected) return;
        int pitch = selected.MidiNote + semitones;
        if (pitch is < 0 or > 127) return;
        if (selected.Third + semitones is < 0 or > 127 || selected.Fifth + semitones is < 0 or > 127) return;
        var adjusted = selected with { MidiNote = (byte)pitch,
            Third = selected.Third is { } third ? (byte)(third + semitones) : null,
            Fifth = selected.Fifth is { } fifth ? (byte)(fifth + semitones) : null };
        if (state.Held[(int)selected.Control] is { } held && held.Sequence == selected.Sequence)
        {
            ReleaseNote(selected.Control);
            HoldNote(adjusted);
        }
        else RememberNote(adjusted); // Editing a released selection must not accidentally create a sounding note.
    }

    private void ReleaseNote(PhysicalControl control)
    {
        if (state.Held[(int)control] is not { } record) return;
        for (int voice = 0; voice < record.VoiceCount; voice++)
        {
            byte pitch = record.PitchAt(voice);
            int index = record.Channel * 128 + pitch;
            if (pitchOwners[index] == 1)
            {
                expression.ClearPressure(record.VoiceAt(voice), Now);
                MidiGenerated?.Invoke(new(MidiEventKind.NoteOff, record.Channel, pitch, 0));
            }
            pitchOwners[index]--;
        }
        state.Held[(int)control] = null;
    }

    private static NoteGroup GroupOf(PhysicalControl control) => control switch
    {
        PhysicalControl.DPadDown or PhysicalControl.DPadUp or PhysicalControl.DPadLeft or PhysicalControl.DPadRight => NoteGroup.DPad,
        PhysicalControl.FaceSouth or PhysicalControl.FaceNorth or PhysicalControl.FaceWest or PhysicalControl.FaceEast => NoteGroup.FaceButtons,
        _ => NoteGroup.Other
    };
}
