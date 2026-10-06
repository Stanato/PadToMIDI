InstrumentConfiguration, note mapping actions, octave modifiers, and discrete stick settings are immutable.
ExpressionConfiguration contains independent trigger, pitch-bend, and timbre response settings.
The Avalonia Settings editor builds validated immutable configurations without generating MIDI.
Core Profiles serializes validated configuration using System.Text.Json. The App profile store handles
filesystem persistence; Save captures applied settings and the persistent octave without runtime state.
Configuration remains separate from runtime controller and musical state.
