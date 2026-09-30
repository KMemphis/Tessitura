using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;

namespace Tessitura.Playback.Midi;

/// <summary>Identifies a MIDI device.</summary>
/// <param name="Id">A stable identifier, the device name.</param>
/// <param name="Name">The name shown to the user.</param>
public readonly record struct MidiPortInfo(string Id, string Name);

/// <summary>Names the kinds of MIDI messages Tessitura reacts to.</summary>
public enum MidiMessageKind
{
    /// <summary>A key was pressed; velocity zero is delivered as <see cref="NoteOff"/>.</summary>
    NoteOn,
    /// <summary>A key was released.</summary>
    NoteOff,
    /// <summary>A controller changed.</summary>
    ControlChange,
    /// <summary>A program was selected.</summary>
    ProgramChange,
}

/// <summary>A channel MIDI message.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Channel">The channel from 0 to 15.</param>
/// <param name="Data1">The key, controller or program.</param>
/// <param name="Data2">The velocity or controller value.</param>
public readonly record struct MidiMessage(MidiMessageKind Kind, int Channel, int Data1, int Data2);

/// <summary>Reads and writes MIDI on physical or virtual devices.</summary>
public interface IMidiPort : IDisposable
{
    /// <summary>Gets whether this platform can use MIDI devices through this port.</summary>
    bool IsSupported { get; }

    /// <summary>Lists the input devices currently connected.</summary>
    /// <returns>The inputs.</returns>
    IReadOnlyList<MidiPortInfo> GetInputs();

    /// <summary>Lists the output devices currently connected.</summary>
    /// <returns>The outputs.</returns>
    IReadOnlyList<MidiPortInfo> GetOutputs();

    /// <summary>Starts receiving messages from an input.</summary>
    /// <param name="id">The device identifier.</param>
    void OpenInput(string id);

    /// <summary>Prepares an output for sending.</summary>
    /// <param name="id">The device identifier.</param>
    void OpenOutput(string id);

    /// <summary>Sends a message to the open output.</summary>
    /// <param name="message">The message.</param>
    void Send(MidiMessage message);

    /// <summary>Closes the open input and output.</summary>
    void Close();

    /// <summary>Raised on a device thread for each message from the open input.</summary>
    event Action<MidiMessage>? MessageReceived;

    /// <summary>Raised when the open device disappears, for example when its USB cable is unplugged.</summary>
    event Action<string>? DeviceDisconnected;
}

/// <summary>The port for systems without a working backend (Linux until the ALSA adapter exists).</summary>
public sealed class UnsupportedMidiPort : IMidiPort
{
    /// <inheritdoc />
    public bool IsSupported => false;

    /// <inheritdoc />
    public event Action<MidiMessage>? MessageReceived
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public event Action<string>? DeviceDisconnected
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public IReadOnlyList<MidiPortInfo> GetInputs() => [];

    /// <inheritdoc />
    public IReadOnlyList<MidiPortInfo> GetOutputs() => [];

    /// <inheritdoc />
    public void OpenInput(string id) => throw new NotSupportedException("MIDI devices are not supported on this system yet.");

    /// <inheritdoc />
    public void OpenOutput(string id) => throw new NotSupportedException("MIDI devices are not supported on this system yet.");

    /// <inheritdoc />
    public void Send(MidiMessage message) => throw new NotSupportedException("MIDI devices are not supported on this system yet.");

    /// <inheritdoc />
    public void Close()
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}

/// <summary>Uses DryWetMIDI (CoreMIDI on macOS, WinMM on Windows), as decided in F0.12.</summary>
public sealed class DryWetMidiPort : IMidiPort
{
    private InputDevice? _input;
    private OutputDevice? _output;
    private string? _inputName;

    /// <inheritdoc />
    public event Action<MidiMessage>? MessageReceived;

    /// <inheritdoc />
    public event Action<string>? DeviceDisconnected;

    /// <inheritdoc />
    public bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    /// <inheritdoc />
    public IReadOnlyList<MidiPortInfo> GetInputs()
    {
        List<MidiPortInfo> list = [];
        foreach (InputDevice device in InputDevice.GetAll())
        {
            using (device)
            {
                list.Add(new MidiPortInfo(device.Name, device.Name));
            }
        }

        return list;
    }

    /// <inheritdoc />
    public IReadOnlyList<MidiPortInfo> GetOutputs()
    {
        List<MidiPortInfo> list = [];
        foreach (OutputDevice device in OutputDevice.GetAll())
        {
            using (device)
            {
                list.Add(new MidiPortInfo(device.Name, device.Name));
            }
        }

        return list;
    }

    /// <inheritdoc />
    public void OpenInput(string id)
    {
        _input?.Dispose();
        _input = InputDevice.GetByName(id);
        _inputName = id;
        _input.EventReceived += OnEvent;
        _input.StartEventsListening();
    }

    /// <inheritdoc />
    public void OpenOutput(string id)
    {
        _output?.Dispose();
        _output = OutputDevice.GetByName(id);
        _output.PrepareForEventsSending();
    }

    /// <inheritdoc />
    public void Send(MidiMessage message)
    {
        OutputDevice output = _output ?? throw new InvalidOperationException("No MIDI output is open.");
        MidiEvent midiEvent = message.Kind switch
        {
            MidiMessageKind.NoteOn => new NoteOnEvent((SevenBitNumber)message.Data1, (SevenBitNumber)message.Data2) { Channel = (FourBitNumber)message.Channel },
            MidiMessageKind.NoteOff => new NoteOffEvent((SevenBitNumber)message.Data1, (SevenBitNumber)message.Data2) { Channel = (FourBitNumber)message.Channel },
            MidiMessageKind.ControlChange => new ControlChangeEvent((SevenBitNumber)message.Data1, (SevenBitNumber)message.Data2) { Channel = (FourBitNumber)message.Channel },
            _ => new ProgramChangeEvent((SevenBitNumber)message.Data1) { Channel = (FourBitNumber)message.Channel },
        };
        try
        {
            output.SendEvent(midiEvent);
        }
        catch (Exception exception) when (exception is MidiDeviceException or InvalidOperationException)
        {
            DeviceDisconnected?.Invoke(output.Name);
            throw;
        }
    }

    /// <inheritdoc />
    public void Close()
    {
        if (_input is not null)
        {
            _input.EventReceived -= OnEvent;
            _input.Dispose();
            _input = null;
        }

        _output?.Dispose();
        _output = null;
    }

    /// <inheritdoc />
    public void Dispose() => Close();

    /// <summary>Checks whether the open input is still listed by the system; call periodically to detect unplugging.</summary>
    /// <returns>Whether the input is still connected, or true when none is open.</returns>
    public bool PollConnection()
    {
        if (_inputName is null)
        {
            return true;
        }

        bool present = GetInputs().Any(i => i.Id == _inputName);
        if (!present)
        {
            string lost = _inputName;
            Close();
            _inputName = null;
            DeviceDisconnected?.Invoke(lost);
        }

        return present;
    }

    private void OnEvent(object? sender, MidiEventReceivedEventArgs args)
    {
        MidiMessage? message = args.Event switch
        {
            NoteOnEvent on when on.Velocity == 0 => new MidiMessage(MidiMessageKind.NoteOff, on.Channel, on.NoteNumber, 0),
            NoteOnEvent on => new MidiMessage(MidiMessageKind.NoteOn, on.Channel, on.NoteNumber, on.Velocity),
            NoteOffEvent off => new MidiMessage(MidiMessageKind.NoteOff, off.Channel, off.NoteNumber, off.Velocity),
            ControlChangeEvent cc => new MidiMessage(MidiMessageKind.ControlChange, cc.Channel, cc.ControlNumber, cc.ControlValue),
            ProgramChangeEvent pc => new MidiMessage(MidiMessageKind.ProgramChange, pc.Channel, pc.ProgramNumber, 0),
            _ => null,
        };
        if (message is MidiMessage value)
        {
            MessageReceived?.Invoke(value);
        }
    }

    /// <summary>Creates the port that fits the current operating system.</summary>
    /// <returns>A DryWetMIDI port on Windows and macOS, an unsupported port elsewhere.</returns>
    public static IMidiPort CreateForThisSystem() =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? new DryWetMidiPort() : new UnsupportedMidiPort();
}
