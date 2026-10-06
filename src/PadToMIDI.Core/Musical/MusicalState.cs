using System.Collections.Immutable;
using PadToMIDI.Core.Configuration;
using PadToMIDI.Core.Input;

namespace PadToMIDI.Core.Musical;

/// <summary>Mutable engine-owned state; observers receive immutable snapshots instead.</summary>
internal sealed class MusicalState(InstrumentConfiguration configuration)
{
    public InstrumentConfiguration Configuration = configuration;
    public GamepadDeviceId? Device;
    public GamepadValues Controls;
    public readonly NoteRecord?[] Held = new NoteRecord?[Enum.GetValues<PhysicalControl>().Length];
    public NoteRecord? LastNote;
    public NoteRecord? LastDPadNote;
    public NoteRecord? LastFaceNote;
    public long Sequence;
    public string PlayingMessage = "";
    public int PersistentOctaveOffset;
    public DiscreteStickLatch LeftStickLatch;
    public ExpressionStateSnapshot Expression = new(0, 0, 8192, 64);

    public NoteRecord? LatestActive(NoteGroup group)
    {
        NoteRecord? latest = null;
        foreach (var record in Held)
            if (record is { } note && note.Group == group && (latest is null || note.Sequence > latest.Value.Sequence)) latest = note;
        return latest;
    }
    public int BaseOctave => Configuration.BaseOctave + PersistentOctaveOffset;
    public int TemporaryOctaveOffset
    {
        get
        {
            int shift = 0;
            foreach (var (control, offset) in Configuration.TemporaryOctaveModifiers)
                if (Controls.IsPressed(control)) shift += offset;
            if (LeftStickLatch.Direction is { } direction)
                shift += Configuration.LeftStick.ActionFor(direction) switch
                {
                    StickAction.MomentaryOctaveDown => -1,
                    StickAction.MomentaryOctaveUp => 1,
                    _ => 0
                };
            return shift;
        }
    }

    public int TemporarySemitoneOffset
    {
        get
        {
            int shift = 0;
            foreach (var (control, offset) in Configuration.TemporarySemitoneModifiers)
                if (Controls.IsPressed(control)) shift += offset;
            return shift;
        }
    }

    public MusicalStateSnapshot CaptureSnapshot()
    {
        var held = ImmutableArray.CreateBuilder<NoteRecord>();
        NoteRecord? activeDPad = null;
        NoteRecord? activeFace = null;
        foreach (var record in Held)
        {
            if (record is not { } note) continue;
            for (int voice = 0; voice < note.VoiceCount; voice++) held.Add(note.VoiceAt(voice));
            if (note.Group == NoteGroup.DPad && (activeDPad is null || note.Sequence > activeDPad.Value.Sequence))
                activeDPad = note;
            if (note.Group == NoteGroup.FaceButtons && (activeFace is null || note.Sequence > activeFace.Value.Sequence))
                activeFace = note;
        }
        return new(Configuration, Device, Controls, held.ToImmutable(), LastNote,
            LastDPadNote, LastFaceNote, activeDPad, activeFace,
            BaseOctave, PersistentOctaveOffset, TemporaryOctaveOffset, LeftStickLatch.Direction, TemporarySemitoneOffset, Expression, PlayingMessage);
    }
}

public sealed record MusicalStateSnapshot(
    InstrumentConfiguration Configuration,
    GamepadDeviceId? Device,
    GamepadValues Controls,
    ImmutableArray<NoteRecord> HeldNotes,
    NoteRecord? LastNote,
    NoteRecord? LastDPadNote,
    NoteRecord? LastFaceNote,
    NoteRecord? LastActiveDPadNote,
    NoteRecord? LastActiveFaceNote,
    int BaseOctave,
    int PersistentOctaveOffset,
    int TemporaryOctaveOffset,
    StickDirection? LeftStickLatch,
    int TemporarySemitoneOffset,
    ExpressionStateSnapshot Expression,
    string PlayingMessage = "");
