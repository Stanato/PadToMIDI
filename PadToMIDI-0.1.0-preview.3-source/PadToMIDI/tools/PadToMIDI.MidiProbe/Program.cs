using PadToMIDI.Core.Midi;
using PadToMIDI.Midi.Windows;

namespace PadToMIDI.MidiProbe;

internal static class Program
{
    [MTAThread]
    public static int Main(string[] args)
    {
        var output = new WindowsMidiOutput();
        try
        {
            if (args.Length == 1 && args[0] is "--loopback-test" or "--synth-test")
                return LoopbackVerification.Run(output, args[0] == "--synth-test");
            if (args.Length == 1 && args[0] == "--synth-monitor")
                return LoopbackVerification.Run(output, true, true);
            var endpoints = output.GetEndpointsAsync().AsTask().GetAwaiter().GetResult();
            Console.WriteLine(output.CaptureSnapshot());
            foreach (var endpoint in endpoints) Console.WriteLine($"{endpoint.Name}\n  {endpoint.Id}");
            if (args.Length == 2 && args[0] == "--play")
            {
                output.ConnectAsync(args[1]).AsTask().GetAwaiter().GetResult();
                foreach (byte pitch in new byte[] { 48, 50, 52, 53, 55, 57, 59, 60 })
                {
                    Send(output, new(MidiEventKind.NoteOn, 0, pitch, 100));
                    output.FlushAsync().AsTask().GetAwaiter().GetResult();
                    Thread.Sleep(180);
                    Send(output, new(MidiEventKind.NoteOff, 0, pitch, 0));
                }
                output.FlushAsync().AsTask().GetAwaiter().GetResult();
                Console.WriteLine(output.CaptureSnapshot());
                output.DisconnectAsync().AsTask().GetAwaiter().GetResult();
            }
            else if (args.Length != 0) throw new ArgumentException("Use no arguments, --loopback-test, --synth-test, --synth-monitor, or --play <endpoint-id>.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(output.CaptureSnapshot());
            Console.Error.WriteLine(error);
            return 1;
        }
        finally { output.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
    internal static void Send(IMidiOutput output, MidiEvent message)
    {
        if (!output.TrySend(message)) throw new InvalidOperationException("Submission rejected.");
    }
}
