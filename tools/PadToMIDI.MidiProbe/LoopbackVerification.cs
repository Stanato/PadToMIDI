using System.Collections.Concurrent;
using PadToMIDI.Core.Input;
using PadToMIDI.Core.Midi;
using PadToMIDI.Core.Musical;
using PadToMIDI.Midi.Windows;
using Windows.Devices.Midi;
using Windows.Devices.Midi2;
using Windows.Devices.Midi2.Transports.Loopback;

namespace PadToMIDI.MidiProbe;

/// <summary>Temporary service endpoints and an optional test-only bridge into the built-in Windows synth.</summary>
internal static class LoopbackVerification
{
    public static int Run(WindowsMidiOutput output, bool useSynth, bool monitor = false)
    {
        output.GetEndpointsAsync().AsTask().GetAwaiter().GetResult();
        string identity = Guid.NewGuid().ToString("N");
        var response = MidiLoopbackManager.CreateTransientLoopback(new(
            new MidiLoopbackEndpointDefinition(useSynth ? "PadToMIDI to Windows synth" : "PadToMIDI probe send", "Temporary verification endpoint", $"pad-send-{identity}"),
            new MidiLoopbackEndpointDefinition("PadToMIDI probe receive", "Temporary verification endpoint", $"pad-receive-{identity}")));
        if (!response.Success) throw new InvalidOperationException($"Cannot create test loopback: {response.ErrorCode} {response.ErrorMessage}");
        var pair = response.CreatedLoopbackEntry;
        bool pairRemoved = false;
        try
        {
            using var session = MidiSession.Create("PadToMIDI verification receiver");
            var receiver = session.CreateEndpointConnection(pair.EndpointB.EndpointDeviceId, new(false, false));
            using var synth = useSynth ? MidiSynthesizer.CreateAsync().AsTask().GetAwaiter().GetResult() : null;
            if (useSynth && synth is null) throw new InvalidOperationException("The built-in Windows software synthesizer is unavailable.");
            var received = new ConcurrentQueue<uint>();
            var synthNotes = new ConcurrentQueue<byte>();
            receiver.MessageReceived += (_, args) =>
            {
                uint word = args.PeekFirstWord();
                received.Enqueue(word);
                if (synth is null || (word >> 28) != 2) return;
                byte channel = (byte)((word >> 16) & 15), note = (byte)((word >> 8) & 127), value = (byte)(word & 127);
                switch ((word >> 20) & 15)
                {
                    case 9: synth.SendMessage(new MidiNoteOnMessage(channel, note, value)); synthNotes.Enqueue(note); break;
                    case 8: synth.SendMessage(new MidiNoteOffMessage(channel, note, value)); break;
                    case 10: synth.SendMessage(new MidiPolyphonicKeyPressureMessage(channel, note, value)); break;
                    case 13: synth.SendMessage(new MidiChannelPressureMessage(channel, note)); break;
                    case 11: synth.SendMessage(new MidiControlChangeMessage(channel, note, value)); break;
                    case 14: synth.SendMessage(new MidiPitchBendChangeMessage(channel, (ushort)(note | (value << 7)))); break;
                }
            };
            if (!receiver.Open()) throw new InvalidOperationException("Cannot open the verification receiver.");
            if (monitor)
            {
                using var stopped = new ManualResetEventSlim();
                ConsoleCancelEventHandler stop = (_, args) => { args.Cancel = true; stopped.Set(); };
                Console.CancelKeyPress += stop;
                Console.WriteLine("Synth ready. In PadToMIDI, refresh outputs, choose 'PadToMIDI to Windows synth · Group 1', and connect. Press Ctrl+C here to stop and remove the temporary route.");
                try { stopped.Wait(); }
                finally
                {
                    Console.CancelKeyPress -= stop;
                    session.DisconnectEndpointConnection(receiver.ConnectionId);
                    for (byte channel = 0; channel < 16; channel++) synth!.SendMessage(new MidiControlChangeMessage(channel, 120, 0));
                }
                return 0;
            }
            string id = $"{pair.EndpointA.EndpointDeviceId}|group=0";
            using var instrument = new MidiInstrument(output);
            instrument.ConnectOutputAsync(id).AsTask().GetAwaiter().GetResult();
            var device = new GamepadDeviceId(42);
            instrument.Process(new(GamepadEventKind.Selected, device));
            var controls = new[] { PhysicalControl.DPadDown, PhysicalControl.DPadUp, PhysicalControl.DPadLeft,
                PhysicalControl.DPadRight, PhysicalControl.FaceSouth, PhysicalControl.FaceNorth, PhysicalControl.FaceWest, PhysicalControl.FaceEast };
            byte[] pitches = [48, 50, 52, 53, 55, 57, 59, 60];
            for (int index = 0; index < controls.Length; index++)
            {
                instrument.Process(new(GamepadEventKind.ControlChanged, device, controls[index], 1));
                output.FlushAsync().AsTask().GetAwaiter().GetResult();
                if (useSynth) Thread.Sleep(220);
                instrument.Process(new(GamepadEventKind.ControlChanged, device, controls[index], 0));
            }
            instrument.Process(new(GamepadEventKind.ControlChanged, device, PhysicalControl.FaceSouth, 1));
            instrument.DisconnectOutputAsync().AsTask().GetAwaiter().GetResult();
            SpinWait.SpinUntil(() => received.Count >= 21, TimeSpan.FromSeconds(5));
            uint[] notes = received.Where(word => ((word >> 20) & 15) is 8 or 9).ToArray();
            if (notes.Length != 18) throw new InvalidOperationException($"Expected 18 received notes, got {notes.Length}.");
            for (int index = 0; index < pitches.Length; index++)
            {
                AssertWord(notes[index * 2], new(MidiEventKind.NoteOn, 0, pitches[index], 100));
                AssertWord(notes[index * 2 + 1], new(MidiEventKind.NoteOff, 0, pitches[index], 0));
            }
            AssertWord(notes[16], new(MidiEventKind.NoteOn, 0, 55, 100));
            AssertWord(notes[17], new(MidiEventKind.NoteOff, 0, 55, 0));
            if (!received.Contains(Midi1UmpEncoder.Encode(new(MidiEventKind.PitchBend, 0, 0, 8192), 0)))
                throw new InvalidOperationException("Cleanup pitch center was not received.");
            if (useSynth && synthNotes.Count != 9) throw new InvalidOperationException("The synth did not receive every Note On.");
            Console.WriteLine($"PASS: eight default scale notes and matching Note Offs crossed Windows MIDI Services; held-note output-switch cleanup received. {received.Count} UMP messages.");
            if (useSynth) Console.WriteLine($"PASS: {synthNotes.Count} received notes forwarded into Windows' built-in software synthesizer ({synth!.DeviceId}). Listen to confirm audible output.");
            instrument.ConnectOutputAsync(id).AsTask().GetAwaiter().GetResult();
            GestureVerification.Run(instrument, output, received, device);
            ExpressionVerification.Run(instrument, output, received, device);
            ConfigurationVerification.Run(instrument, output, received, device);
            ProfileVerification.Run(instrument, output, received, device);
            ChordVerification.Run(instrument, output, received, device);
            instrument.DisconnectOutputAsync().AsTask().GetAwaiter().GetResult();
            // Exercise backend ownership independently of Core, then live removal of an output with an active note.
            output.ConnectAsync(id).AsTask().GetAwaiter().GetResult();
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                try
                {
                    output.DisconnectAsync(cancelled.Token).AsTask().GetAwaiter().GetResult();
                    throw new InvalidOperationException("Pre-cancelled disconnect was accepted.");
                }
                catch (OperationCanceledException) { }
                if (!output.IsConnected) throw new InvalidOperationException("Cancelled disconnect disabled an active output.");
            }
            Program.Send(output, new(MidiEventKind.NoteOn, 0, 72, 100));
            Program.Send(output, new(MidiEventKind.PolyphonicPressure, 0, 72, 99));
            Program.Send(output, new(MidiEventKind.ControlChange, 0, 11, 127, 64));
            output.DisconnectAsync().AsTask().GetAwaiter().GetResult();
            uint cleanup = Midi1UmpEncoder.Encode(new(MidiEventKind.NoteOff, 0, 72, 0), 0);
            if (!SpinWait.SpinUntil(() => received.Contains(cleanup), TimeSpan.FromSeconds(5)))
                throw new InvalidOperationException("Backend ledger cleanup was not received.");
            if (!received.Contains(Midi1UmpEncoder.Encode(new(MidiEventKind.PolyphonicPressure, 0, 72, 0), 0)) ||
                !received.Contains(Midi1UmpEncoder.Encode(new(MidiEventKind.ControlChange, 0, 11, 64), 0)))
                throw new InvalidOperationException("Backend pressure/custom-CC ledger cleanup was not received.");
            instrument.ConnectOutputAsync(id).AsTask().GetAwaiter().GetResult();
            instrument.Process(new(GamepadEventKind.ControlChanged, device, PhysicalControl.FaceEast, 1));
            output.FlushAsync().AsTask().GetAwaiter().GetResult();
            var removed = MidiLoopbackManager.RemoveTransientLoopback(new(pair.AssociationId));
            if (!removed.Success) throw new InvalidOperationException(removed.ErrorMessage);
            pairRemoved = true;
            if (!SpinWait.SpinUntil(() => output.CaptureSnapshot().State == MidiOutputState.Faulted, TimeSpan.FromSeconds(5)))
                throw new InvalidOperationException("Endpoint removal was not reported as a fault.");
            if (!instrument.CaptureSnapshot().HeldNotes.IsEmpty) throw new InvalidOperationException("Endpoint loss retained held notes.");
            Console.WriteLine("PASS: backend ledger reset a direct note, poly pressure and custom CC; endpoint removal reported a fault and cleared musical state.");
            session.DisconnectEndpointConnection(receiver.ConnectionId);
            if (synth is not null)
                for (byte channel = 0; channel < 16; channel++) synth.SendMessage(new MidiControlChangeMessage(channel, 120, 0));
            return 0;
        }
        finally
        {
            output.DisconnectAsync().AsTask().GetAwaiter().GetResult();
            // May already have been removed by the endpoint-loss assertion.
            if (!pairRemoved)
            {
                var removal = MidiLoopbackManager.RemoveTransientLoopback(new(pair.AssociationId));
                if (!removal.Success) Console.Error.WriteLine($"Test loopback removal failed: {removal.ErrorMessage}");
            }
        }
    }

    private static void AssertWord(uint actual, MidiEvent expected)
    {
        uint word = Midi1UmpEncoder.Encode(expected, 0);
        if (actual != word) throw new InvalidOperationException($"UMP mismatch: expected {word:X8}, received {actual:X8}.");
    }
}
