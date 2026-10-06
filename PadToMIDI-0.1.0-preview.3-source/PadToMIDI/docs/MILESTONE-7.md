# Milestone 7 — Profiles, JSON persistence, and recovery

Profiles now support create, save, load, duplicate, rename, delete, import/export, default reset,
and startup restoration. No packages/native dependencies were added. Musical processing remains
independent of Avalonia, SDL3, and Windows MIDI Services.

## Workflow

1. Configure the instrument in Settings and choose Apply.
2. Open Profiles, enter a name, and choose Create from current.
3. After subsequent Settings edits, select the desired editable profile and choose Save current to selected.
4. Select another profile and choose Load selected to use it. Selecting alone is a preview.
5. Restart: the last loaded/saved profile restores. Choose Connect to enable its selected MIDI endpoint.

| Action | Behavior |
| --- | --- |
| Create from current | Saves current configuration/persistent octave/device preferences under a new ID |
| Save current to selected | Replaces the selected editable profile with the current instrument |
| Load selected | Applies saved configuration and restores available device selections |
| Duplicate stored | Copies saved values under a new ID/name, without loading |
| Rename | Updates the stored name while retaining the ID |
| Delete | Requires confirmation; deleting the active profile loads Default C Major |
| Import JSON | Validates first, creates a new ID/unique name, does not overwrite or load |
| Export stored JSON | Exports the saved selection; Save first to include current edits |
| Load Default C Major | Restores the read-only built-in default configuration |
| Panic | Releases notes/resets expression independently of file operations or errors |

The built-in default matches your latest controls: Up/Down momentary octave up/down, Left/Right
persistent octave down/up, held Left/Right Bumper flat/sharp, group trigger pressure, and right-stick
bend/CC74. Each stick excursion acts once until both axes return to neutral. The shipped full JSON
example is [default-c-major.json](../src/PadToMIDI.App/Profiles/default-c-major.json).

## Storage and format

Default library: `%LOCALAPPDATA%\PadToMIDI\Profiles`.

- Editable profiles: `<guid>.json`; display names never become library paths.
- Startup selection: `last-profile.txt`, containing the loaded/saved GUID.
- The default is built into Core and cannot be overwritten/deleted through the library.
- Optional `--profile-directory <path>` selects a separate library for testing or a portable setup.

Schema version 1 includes `version`, `id`, `name`, `configuration`, and optional `controllerName`,
`midiEndpointId`, `midiEndpointName`. Configuration contains the complete scale intervals, root,
base octave, velocity, zero-based MIDI channel, button mappings/modifiers, discrete stick settings,
and four independent expression configurations. Mappings use explicit action discriminators:

```json
"FaceSouth": { "type": "scaleDegree", "degree": 5 },
"FaceEast": { "type": "fixedNote", "midiNote": 60 }
```

Enums use normalized control/action names. Comments/trailing commas are accepted. Unsupported
versions/actions, numeric/unknown enum values, duplicate/unknown fields, invalid ranges/nulls,
malformed scales, and files over 1 MiB are rejected. Custom scales roundtrip; known presets reuse
the Core catalog rather than appearing twice in the editor. Held notes, temporary modifiers,
controller connection IDs, pressure, filters, and directional latches are never saved.

Library enumeration and writes execute off the UI/input path. Writes serialize through a gate,
flush a unique temporary file, then replace the destination in the same directory. Failed replacement
preserves the prior valid file and removes the temporary file when possible. Invalid library files
are reported individually and remain untouched. A corrupt/missing startup selection leaves the
default available and reports a recovery message; Load repairs the marker. Interrupted temporary
files are ignored. Concurrent app instances use the same files; the last successful save wins.

Settings Apply changes the current session. Persistence requires an explicit profile Save/Create;
unsaved edits are discarded on restart. Saving uses the persistent playing octave, excluding any
momentary shift. Duplicate/export use stored values rather than silently capturing unsaved edits.

## Devices and note safety

Controller preferences match one uniquely named currently detected gamepad. Missing or ambiguous
names deselect input and require manual selection; controllers plugged in later must be selected
manually. This is a fallback because the existing input contract exposes no durable serial identity.
MIDI preferences restore a currently available endpoint selection. A different connected output
disconnects through Core's cleanup/flush path; a missing saved endpoint produces a visible message.
Profile loading never automatically connects a MIDI output. Profiles with no device preferences
retain current selections/routes.

Loading configuration resets the persistent octave offset explicitly while retaining exact held-note
pitch/channel/velocity records and physical edges. Existing notes release correctly even after root,
scale, mapping, octave, or channel changes. Expression destinations reset through the existing Core
transition. Actual controller/output switches perform note/expression cleanup. No held notes replay
on startup or reconnect. Panic, shutdown, input failure, and MIDI failure use the existing safety path.
File errors leave musical processing usable; they do not abandon tracked notes.

## Build, run, and validation

Use the SDK setup in [README](../README.md), then:

```powershell
dotnet restore PadToMIDI.sln --locked-mode
dotnet build PadToMIDI.sln -c Release --no-restore
dotnet test --project tests/PadToMIDI.Core.Tests -c Release --no-build --no-restore
dotnet test --project tests/PadToMIDI.App.Tests -c Release --no-build --no-restore
dotnet test --project tests/PadToMIDI.Midi.Windows.Tests -c Release --no-build --no-restore
dotnet run --project tools/PadToMIDI.SettingsProbe -c Release -- --profiles-test
dotnet run --project tools/PadToMIDI.MidiProbe -c Release -- --loopback-test
dotnet run --project src/PadToMIDI.App -c Release --no-build --no-restore
```

Validated in this workspace:

- **248 Core + 29 App + 18 MIDI encoding tests = 295 passing tests.** New cases cover all-preset
  serialization, custom scales/mappings/expression/preferences, invalid JSON/schema/ranges,
  shipped-default consistency, persistent offset reset with held notes, lifecycle/restart,
  locked-file replacement failures, corrupt UTF-8/files/markers, oversized files, and Panic.
- Actual Profiles window controls/bindings verify startup restore, name entry, create/save/load,
  duplicate/rename/delete confirmation, import/export commands through an injected picker boundary,
  invalid-import errors, exact held releases, persistent octave capture, missing devices/routes,
  manual MIDI connection, and Panic. Files stay in an isolated workspace library;
  `artifacts/profiles-*.png` and `artifacts/milestone-7-main.png` record rendering.
- Real Windows MIDI Services loopback verifies JSON roundtrip/load/default reset, persistent octave
  reset, and original/new-channel releases, alongside existing expression/backend failure cleanup.
  Temporary service endpoints are removed afterward.
- The Settings UI regression fixture, Debug/Release builds, and published app startup check pass.

Core tests require no Windows hardware/API. App tests/UI fixtures require Windows but no gamepad or
MIDI service. Native loopback requires Windows MIDI Services. Physical controller compatibility,
native file-dialog interactions, DAW routing, and audible expression remain manual checks.

The existing published folder is refreshed; previous launch commands still work:

```powershell
.\.tools\dotnet\dotnet.exe artifacts\current-publish\PadToMIDI.dll
```

The pinned preview MIDI SDK still requires Windows 11 25H2+. Milestone 8 remains packaging,
installer, and release polish.
