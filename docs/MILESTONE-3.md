# Milestone 3 — Windows MIDI Services

The application now sends the musical engine's MIDI events to a selected Windows MIDI Services
endpoint/group. Output selection, MIDI channel 1–16, connection status, accepted send counts,
rejection counts, refresh, disconnect, and Panic are available in the Avalonia UI.
Octave modifiers, stick actions, aftertouch, continuous expression, and profiles remain later milestones.

## Platform and deployment

- Windows x64, Windows 11 **25H2 or newer**; .NET 10 SDK for development.
- Official `Windows.Devices.Midi2` **0.99.88-preview.10**, obtained from Microsoft's GitHub release,
  pinned in the local NuGet feed `vendor/nuget`. This release is a preview, not a stable API promise.
- `Microsoft.Windows.CsWinRT` 2.2.0 generates the C# projection in the Windows backend.
  NuGet Windows metadata replaces a machine-installed Windows SDK requirement.
- Windows MIDI Services must be enabled. Activation can demand-start the installed `midisrv` service.
  Legacy API mode is reported as unavailable; the app does not silently choose another MIDI backend.
- App-local `Windows.Devices.Midi2.dll` and its `.pri` are copied to build/publish output.
  C#/WinRT first attempts system activation, then its documented namespace DLL fallback.
  No global API registration, transport installation, or registry changes are required by this app.
- No DAW port is implicitly created by opening an existing endpoint. Use a configured loopback pair,
  a device endpoint, or the temporary synth helper described below.

Before shipping a release, review the preview's go-live terms and replace app-local API deployment
when the supported in-box implementation becomes available. See [dependencies](DEPENDENCIES.md).

## Event path and safety

`SDL owner → normalized input → Core MidiInstrument / MappingEngine → IMidiOutput.TrySend →`
`bounded queue → dedicated MIDI MTA thread → MIDI 1 channel voice UMP → Windows MIDI Services`.

Core has no package references. `MidiInstrument` serializes engine mutations using a short lock;
it handles output transitions through the platform interface. A transition suspends new note input,
releases held notes on their original channel/pitch, sends channel cleanup, drains accepted messages,
closes the old output, then opens the selected one. Notes pressed during a transition are not replayed.
Held buttons require a fresh press after a transition or Panic. Channel changes affect future notes only.

The Windows backend owns one session and one active connection on its MTA thread. Live submission
never waits for native calls or queue capacity. The 4096-entry queue preserves FIFO ordering;
a full queue faults the output instead of silently dropping Note Offs and continuing playback.
The native ledger records successfully sent note identities; closing/faulting additionally releases
any remaining ledger notes. Cleanup includes CC123, channel pressure zero, and pitch bend 8192 on used
channels. SDK send failure, endpoint removal, and queue overflow disable forwarding and clear Core
state on the next input/observer tick. Reconnection is explicit; stale notes are not retriggered.
If a physical destination is unreachable, cleanup is best-effort: software cannot deliver a Note Off
to disconnected hardware. Local ownership still clears, and notes are not replayed after reconnection.

Endpoint discovery handles Added, Removed, Updated, and EnumerationCompleted notifications. Native
callbacks publish lightweight flags/revisions only. GUI enumeration is asynchronous and independent
of input delivery. The preview connection's disconnection callback was unreliable with AutoReconnect
disabled in testing, so the endpoint watcher's Removed notification also detects selected-output loss.
Function blocks take precedence over group terminal blocks. Only active receiving/bidirectional groups
are offered. MIDI group/channel numbers are zero-based internally and one-based in the UI.
Diagnostic endpoints are excluded. Endpoints with no usable group metadata are not guessed as outputs.

## Try it with a software synth

Run the application in one VS Code terminal:

```powershell
dotnet restore PadToMIDI.sln --locked-mode
dotnet build PadToMIDI.sln -c Release --no-restore
dotnet run --project src/PadToMIDI.App -c Release --no-build --no-restore
```

In a second terminal, start the optional development receiver:

```powershell
dotnet run --project tools/PadToMIDI.MidiProbe -c Release -- --synth-monitor
```

In the app, refresh outputs, choose **PadToMIDI to Windows synth · Group 1**, and connect.
The helper creates a transient service loopback, receives the app's UMP messages, and forwards them
to Windows' built-in `MidiSynthesizer` on the default audio device. This test-only receiver uses the
older synth API; the application output itself always uses Windows MIDI Services. Press Ctrl+C in the
helper terminal to stop and remove the route. Abruptly killing the helper may leave a transient pair
until service restart; close it normally. No synth assets or transport installers are bundled.

For a DAW, create a loopback pair in Windows MIDI Services Settings, choose one sending side in the
app, and enable the opposite side as a DAW input on a monitored software-instrument track. A DAW using
legacy MIDI 1 ports may require MIDI 1 loopback/group port exposure supported by the installed tools
and transports. Verify input visibility in your particular DAW before playing. Channel filtering must
match the application's channel. This machine did not have a DAW installed, so DAW-specific port
visibility and physical-controller latency remain manual checks.

## Verification

```powershell
dotnet test --project tests/PadToMIDI.Core.Tests -c Release
dotnet test --project tests/PadToMIDI.Midi.Windows.Tests -c Release
dotnet run --project tools/PadToMIDI.MidiProbe -c Release -- --loopback-test
dotnet run --project tools/PadToMIDI.MidiProbe -c Release -- --synth-test
dotnet run --project src/PadToMIDI.App -c Release -- --smoke-test
dotnet run --project tools/PadToMIDI.InputProbe -c Release
```

Verified on Windows build 26200.9457:

- Release build with warnings treated as errors.
- 82 Core tests, including output transition ordering, channel-safe release, input failure cleanup,
  endpoint/send failure recovery, Panic/disposal, and no replay of input during switching.
- 14 UMP tests: note, poly/channel pressure, CC, pitch minimum/center/maximum, group/channel packing,
  byte order, and invalid values. These tests do not activate the service or require hardware.
- Real service loopback: eight expected C3–C4 notes, exact matching Note Offs, and output-switch cleanup
  received as UMP (24 messages in the first sequence). A directly submitted held note is also released
  by the backend ledger. Removing a live selected endpoint reports a fault and clears musical state.
  Cancelling a disconnect before admission leaves the existing destination usable.
- The same received notes were forwarded successfully into Windows' built-in software synth.
  API acceptance is verified; audible playback needs a listening check on the user's audio setup.
- Avalonia/SDL/MIDI discovery smoke test and rendered layout. Physical gamepads and an installed DAW
  were unavailable; the SDL native virtual-controller probe covers normalization separately.

The probe uses temporary endpoints and removes them in `finally`. Run it on a test machine where
starting MIDI Services and creating temporary routes is appropriate. With no arguments it lists
outputs; `--play '<endpoint-id>'` sends the scale directly to an explicitly chosen destination.

Primary references: [Preview 10 release](https://github.com/microsoft/MIDI/releases/tag/inbox-preview-10),
[MIDI library integration guidance](https://microsoft.github.io/MIDI/kb/porting-midi-libraries/),
[MidiApi](https://microsoft.github.io/MIDI/sdk-reference/MidiApi/),
[C#/WinRT activation fallback](https://github.com/microsoft/CsWinRT/blob/2.2.0/src/WinRT.Runtime/ActivationFactory.cs),
[Windows software synth](https://learn.microsoft.com/en-us/uwp/api/windows.devices.midi.midisynthesizer.createasync).
