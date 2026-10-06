using PadToMIDI.Core.Musical;

namespace PadToMIDI.App.ViewModels;

/// <summary>Presentation formatting only. Pitch resolution remains in Core.</summary>
internal static class NoteDisplay
{
    private static readonly string[] PitchNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    public static string Name(byte midiNote) => $"{PitchNames[midiNote % 12]}{midiNote / 12 - 1}";
    public static string RootName(PitchClass root) => PitchNames[(int)root];
    public static string RootName(PitchClass root, ScaleDefinition scale) => NoteSpelling.PitchName((byte)root, NoteSpelling.TonicLetter(root, scale));
    public static string Name(NoteRecord note) => NoteSpelling.Name(note.MidiNote, note.RootLetter);
    public static string Gesture(NoteRecord note) => note.VoiceCount == 1 ? Name(note) :
        $"{NoteSpelling.ChordName(note)} ({string.Join(" ", Enumerable.Range(0, note.VoiceCount).Select(voice => Name(note.VoiceAt(voice))))})";
}
