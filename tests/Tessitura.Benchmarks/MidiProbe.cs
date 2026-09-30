using System.Text.Json;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;

namespace Tessitura.Benchmarks;

internal static class MidiProbe
{
    public static int Run(string mode)
    {
        if (mode != "list" && mode != "loopback")
        {
            Console.Error.WriteLine("MIDI probe mode must be list or loopback.");
            return 2;
        }

        try
        {
            List<string> inputs = [];
            List<string> outputs = [];
            foreach (InputDevice device in InputDevice.GetAll())
            {
                using (device)
                {
                    inputs.Add(device.Name);
                }
            }

            foreach (OutputDevice device in OutputDevice.GetAll())
            {
                using (device)
                {
                    outputs.Add(device.Name);
                }
            }

            if (mode == "list")
            {
                Console.WriteLine(JsonSerializer.Serialize(new { Platform = Environment.OSVersion.ToString(), Inputs = inputs, Outputs = outputs }));
                return 0;
            }

            using VirtualDevice loopback = VirtualDevice.Create($"Tessitura F0.12 {Environment.ProcessId}");
            using ManualResetEventSlim noteReceived = new(false);
            bool matchedNote = false;
            loopback.InputDevice.EventReceived += (_, args) =>
            {
                if (args.Event is NoteOnEvent note &&
                    (int)note.NoteNumber == 60 &&
                    (int)note.Velocity == 100 &&
                    (int)note.Channel == 2)
                {
                    matchedNote = true;
                    noteReceived.Set();
                }
            };
            loopback.InputDevice.StartEventsListening();
            loopback.OutputDevice.PrepareForEventsSending();
            loopback.OutputDevice.SendEvent(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100)
            {
                Channel = (FourBitNumber)2,
            });
            bool received = noteReceived.Wait(TimeSpan.FromSeconds(2));
            loopback.OutputDevice.SendEvent(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0)
            {
                Channel = (FourBitNumber)2,
            });
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Platform = Environment.OSVersion.ToString(),
                Inputs = inputs,
                Outputs = outputs,
                LoopbackName = loopback.Name,
                ReceivedMatchingNote = received && matchedNote,
            }));
            return received && matchedNote ? 0 : 1;
        }
        catch (NotSupportedException exception) when (mode == "list")
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Platform = Environment.OSVersion.ToString(),
                Supported = false,
                Error = exception.GetType().Name,
            }));
            return 0;
        }
        catch (DllNotFoundException exception) when (mode == "list" && OperatingSystem.IsLinux())
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Platform = Environment.OSVersion.ToString(),
                Supported = false,
                Error = exception.GetType().Name,
                Reason = "DryWetMIDI does not provide a Linux device backend",
            }));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"MIDI probe failed: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }
}
