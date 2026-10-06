using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Input;
using PadToMIDI.Core.Midi;

namespace PadToMIDI.Core.Musical;

public readonly record struct ExpressionStateSnapshot(int DPadPressure, int FacePressure, int PitchBend = 8192, int Timbre = 64);

/// <summary>Fixed-size expression ledgers, sampled by the input owner; no UI or native dependencies.</summary>
internal sealed class ExpressionEngine(Action<MidiEvent> emit)
{
    private AnalogSmoother dpad, face, bend, timbre;
    private readonly MidiValueGate[] pressureValues = new MidiValueGate[16 * 128];
    private readonly MidiValueGate[] bendValues = new MidiValueGate[16];
    private readonly MidiValueGate[] ccValues = new MidiValueGate[16 * 128];
    private NoteRecord? dpadTarget, faceTarget;
    private bool bendActive, timbreActive;

    public void Update(MusicalState state, double now, PhysicalControl? changed = null)
    {
        var settings = state.Configuration.Expression;
        double dpadLevel = dpad.Sample(AnalogResponse.Trigger(state.Controls.GetValue(settings.DPadAftertouch.Source), settings.DPadAftertouch), now, settings.DPadAftertouch.Response.SmoothingMilliseconds);
        double faceLevel = face.Sample(AnalogResponse.Trigger(state.Controls.GetValue(settings.FaceAftertouch.Source), settings.FaceAftertouch), now, settings.FaceAftertouch.Response.SmoothingMilliseconds);
        double bendTarget = settings.PitchBend.Enabled ? AnalogResponse.Signed(state.Controls.GetValue(settings.PitchBend.Source), settings.PitchBend.Response) : 0;
        double timbreTarget = settings.Timbre.Enabled ? AnalogResponse.Signed(state.Controls.GetValue(settings.Timbre.Source), settings.Timbre.Response) : 0;
        double bendLevel = bend.Sample(bendTarget, now, settings.PitchBend.Response.SmoothingMilliseconds);
        double timbreLevel = timbre.Sample(timbreTarget, now, settings.Timbre.Response.SmoothingMilliseconds);
        int dpadPressure = (int)Math.Round(dpadLevel * 127), facePressure = (int)Math.Round(faceLevel * 127);
        int pitch = bendLevel < 0 ? (int)Math.Round(8192 + bendLevel * 8192) : (int)Math.Round(8192 + bendLevel * 8191);
        int cc = (int)Math.Round((timbreLevel + 1) * 63.5);
        state.Expression = new(dpadPressure, facePressure, pitch, cc);

        var nextDPad = settings.DPadAftertouch.Enabled ? state.LatestActive(NoteGroup.DPad) : null;
        var nextFace = settings.FaceAftertouch.Enabled ? state.LatestActive(NoteGroup.FaceButtons) : null;
        ClearPrevious(dpadTarget, nextDPad, nextFace, now);
        ClearPrevious(faceTarget, nextFace, nextDPad, now);
        // Shared MIDI 1 pitches cannot have independent pressure; the latest physical owner wins.
        bool selected = dpadTarget != nextDPad || faceTarget != nextFace;
        SendGroup(nextDPad, nextFace, dpadPressure, now, settings.DPadAftertouch.Response.UpdateRateHz, selected);
        SendGroup(nextFace, nextDPad, facePressure, now, settings.FaceAftertouch.Response.UpdateRateHz, selected);
        dpadTarget = nextDPad; faceTarget = nextFace;

        byte channel = state.Configuration.MidiChannel;
        bendActive |= changed == settings.PitchBend.Source || bendTarget != 0;
        timbreActive |= changed == settings.Timbre.Source || timbreTarget != 0;
        if (settings.PitchBend.Enabled && bendActive && bendValues[channel].Accept(pitch, now, settings.PitchBend.Response.UpdateRateHz, bendTarget == 0))
            emit(new(MidiEventKind.PitchBend, channel, 0, (ushort)pitch));
        if (settings.Timbre.Enabled && timbreActive && ccValues[channel * 128 + settings.Timbre.ControllerNumber].Accept(cc, now, settings.Timbre.Response.UpdateRateHz, timbreTarget == 0))
            emit(new(MidiEventKind.ControlChange, channel, settings.Timbre.ControllerNumber, (ushort)cc, 64));
    }

    private static bool Contains(NoteRecord? chord, NoteRecord note) => chord?.Contains(note.Channel, note.MidiNote) == true;

    private void SendGroup(NoteRecord? target, NoteRecord? other, int pressure, double now, double rate, bool selected)
    {
        if (target is not { } chord) return;
        for (int voice = 0; voice < chord.VoiceCount; voice++)
        {
            var note = chord.VoiceAt(voice);
            if (!Contains(other, note) || chord.Sequence > other!.Value.Sequence)
                SendPressure(note, pressure, now, rate, selected);
        }
    }

    private void ClearPrevious(NoteRecord? previous, NoteRecord? current, NoteRecord? other, double now)
    {
        if (previous is not { } chord) return;
        for (int voice = 0; voice < chord.VoiceCount; voice++)
        {
            var note = chord.VoiceAt(voice);
            if (!Contains(current, note) && !Contains(other, note)) ClearPressure(note, now);
        }
    }

    private void SendPressure(NoteRecord? target, int pressure, double now, double rate, bool selected)
    {
        if (target is not { } note) return;
        ref var gate = ref pressureValues[note.Channel * 128 + note.MidiNote];
        if (!gate.HasValue && pressure == 0) return;
        if (gate.Accept(pressure, now, rate, selected || pressure == 0))
            emit(new(MidiEventKind.PolyphonicPressure, note.Channel, note.MidiNote, (ushort)pressure));
    }

    public void ClearPressure(NoteRecord note, double now) => SendPressure(note, 0, now, 0, true);

    public void Reset(MusicalState state, double now)
    {
        for (int index = 0; index < pressureValues.Length; index++)
        {
            if (pressureValues[index] is { HasValue: true, Value: > 0 })
                emit(new(MidiEventKind.PolyphonicPressure, (byte)(index / 128), (byte)(index % 128), 0));
            if (ccValues[index] is { HasValue: true, Value: not 64 })
                emit(new(MidiEventKind.ControlChange, (byte)(index / 128), (byte)(index % 128), 64, 64));
        }
        for (byte channel = 0; channel < bendValues.Length; channel++)
            if (bendValues[channel] is { HasValue: true, Value: not 8192 }) emit(new(MidiEventKind.PitchBend, channel, 0, 8192));
        Array.Clear(pressureValues); Array.Clear(bendValues); Array.Clear(ccValues);
        dpad = face = bend = timbre = default;
        dpadTarget = faceTarget = null;
        bendActive = timbreActive = false;
        state.Expression = new(0, 0, 8192, 64);
    }
}
