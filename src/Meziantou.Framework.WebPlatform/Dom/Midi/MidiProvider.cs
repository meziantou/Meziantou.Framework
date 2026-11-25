namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The host hooks of the Web MIDI API (https://webaudio.github.io/web-midi-api/, <see cref="BrowsingContextOptions.Midi"/>).</summary>
public sealed class WebMidiOptions
{
    /// <summary>
    /// Gets or sets the MIDI ports of the machine: a <see cref="MidiProvider"/> to which the application connects the
    /// <see cref="MidiHostInput"/> and <see cref="MidiHostOutput"/> objects that implement the ports. A provider can be shared
    /// by several browsing contexts. The default is null: the machine has no MIDI port.
    /// </summary>
    public MidiProvider? Provider { get; init; }
}

/// <summary>
/// The MIDI ports of the machine, used by the windows of the browsing contexts that share a <see cref="BrowsingContextOptions"/>
/// (<see cref="WebMidiOptions.Provider"/>): <see cref="Connect"/> and <see cref="Disconnect"/> change the <c>state</c> of
/// the ports and fire the <c>statechange</c> events of the <c>MIDIAccess</c> objects and of the ports.
/// </summary>
/// <remarks>A provider can be shared by several browsing contexts, and used from any thread.</remarks>
public class MidiProvider
{
    private readonly Lock _lock = new();
    private readonly List<MidiHostPort> _ports = [];
    private readonly List<WeakReference<GlobalMidi>> _globals = [];

    /// <summary>Gets the connected ports, in the order they were connected.</summary>
    public IReadOnlyList<MidiHostPort> Ports
    {
        get
        {
            lock (_lock)
            {
                return [.. _ports];
            }
        }
    }

    /// <summary>
    /// Connects a port (it becomes available): the <c>MIDIAccess</c> objects get a <c>statechange</c> event, and a port that
    /// was open before it was disconnected (its <c>connection</c> is <c>pending</c>) is opened again. Can be called from any
    /// thread.
    /// </summary>
    /// <param name="port">The port. A port can be connected to one provider at a time.</param>
    /// <exception cref="InvalidOperationException">The port is connected to a provider.</exception>
    public void Connect(MidiHostPort port)
    {
        ArgumentNullException.ThrowIfNull(port);
        lock (_lock)
        {
            if (port.Provider is not null)
                throw new InvalidOperationException("The port is already connected");

            port.Provider = this;
            _ports.Add(port);
        }

        foreach (var global in GetGlobals())
        {
            global.OnConnected(port);
        }
    }

    /// <summary>
    /// Disconnects a port: its <c>state</c> becomes <c>disconnected</c>, an open port becomes <c>pending</c> (it is opened
    /// again when it is connected again), and the ports and the <c>MIDIAccess</c> objects get a <c>statechange</c> event.
    /// Can be called from any thread.
    /// </summary>
    /// <param name="port">The port.</param>
    /// <returns>Whether the port was connected to this provider.</returns>
    public bool Disconnect(MidiHostPort port)
    {
        ArgumentNullException.ThrowIfNull(port);
        lock (_lock)
        {
            if (!_ports.Remove(port))
                return false;

            port.Provider = null;
        }

        foreach (var global in GetGlobals())
        {
            global.OnDisconnected(port);
        }

        return true;
    }

    /// <summary>Registers the MIDI state of a window, which then gets the connections, the disconnections and the messages.</summary>
    internal void Register(GlobalMidi global)
    {
        lock (_lock)
        {
            _globals.RemoveAll(static reference => !reference.TryGetTarget(out _));
            _globals.Add(new WeakReference<GlobalMidi>(global));
        }
    }

    /// <summary>A message received by an input port, dispatched to the windows.</summary>
    internal void OnMessage(MidiHostInput port, byte[] message)
    {
        foreach (var global in GetGlobals())
        {
            global.OnMessage(port, message);
        }
    }

    private List<GlobalMidi> GetGlobals()
    {
        lock (_lock)
        {
            var result = new List<GlobalMidi>(_globals.Count);
            foreach (var reference in _globals)
            {
                if (reference.TryGetTarget(out var global))
                {
                    result.Add(global);
                }
            }

            return result;
        }
    }
}

/// <summary>
/// A MIDI port of a <see cref="MidiProvider"/> (https://webaudio.github.io/web-midi-api/#dom-midiport): its information, and
/// the access to the device that <see cref="OpenAsync"/> and <see cref="CloseAsync"/> obtain and release.
/// </summary>
/// <remarks>The members can be called from any thread.</remarks>
public abstract class MidiHostPort
{
    private protected MidiHostPort(string id, MidiPortType type)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Id = id;
        Type = type;
    }

    /// <summary>Gets the identifier of the port (<c>MIDIPort.id</c>), unique among the ports of the provider.</summary>
    public string Id { get; }

    /// <summary>Gets the type of the port.</summary>
    public MidiPortType Type { get; }

    /// <summary>Gets the name of the port (<c>MIDIPort.name</c>), or null.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the manufacturer of the port (<c>MIDIPort.manufacturer</c>), or null.</summary>
    public string? Manufacturer { get; init; }

    /// <summary>Gets the version of the port (<c>MIDIPort.version</c>), or null.</summary>
    public string? Version { get; init; }

    /// <summary>
    /// Gets whether the port is a software synthesizer: the <c>MIDIAccess</c> objects of the pages that did not request the
    /// <c>software</c> option of <c>requestMIDIAccess()</c> do not list it.
    /// </summary>
    public bool IsSoftwareSynthesizer { get; init; }

    /// <summary>Gets the provider to which the port is connected, or null when the port is disconnected.</summary>
    public MidiProvider? Provider { get; internal set; }

    /// <summary>
    /// Obtains the access to the device when a <c>MIDIPort</c> is opened (<c>open()</c>, the <c>midimessage</c> listeners of
    /// an input, or <c>send()</c> on an output): the task fails when the port is not available (e.g. it is used by another
    /// application), and <c>open()</c> then fails with a <c>NotAllowedError</c>. It is called for each <c>MIDIPort</c> object
    /// that opens the port (each <c>MIDIAccess</c> of each window has its own). The default implementation succeeds.
    /// </summary>
    /// <param name="cancellationToken">A token that is canceled when the window is destroyed.</param>
    public virtual Task OpenAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Releases the access to the device obtained by <see cref="OpenAsync"/>: the <c>MIDIPort</c> is closed with <c>close()</c>,
    /// or its window is destroyed. The default implementation does nothing.
    /// </summary>
    public virtual Task CloseAsync() => Task.CompletedTask;
}

/// <summary>
/// A MIDI input port (https://webaudio.github.io/web-midi-api/#dom-midiinput): the application gives it the bytes received
/// from the device with <see cref="Receive"/>, which the open <c>MIDIInput</c> objects get as <c>midimessage</c> events, one
/// message per event.
/// </summary>
public class MidiHostInput : MidiHostPort
{
    private readonly Lock _lock = new();
    private readonly MidiMessageSplitter _splitter = new();

    /// <summary>Creates an input port.</summary>
    /// <param name="id">The identifier of the port.</param>
    public MidiHostInput(string id)
        : base(id, MidiPortType.Input)
    {
    }

    /// <summary>
    /// Receives bytes from the device: a stream of MIDI messages, which may be split over several calls (a message, often a
    /// system exclusive message, can continue in the next call), use the running status, or contain System Real Time
    /// messages, which are dispatched at once. Each complete message is dispatched to the open <c>MIDIInput</c> objects of the
    /// port (the system exclusive messages only to the <c>MIDIAccess</c> objects with <c>sysexEnabled</c>), the invalid bytes
    /// are ignored. The bytes are ignored when the port is not connected to a provider. Can be called from any thread.
    /// </summary>
    /// <param name="data">The bytes.</param>
    public void Receive(ReadOnlySpan<byte> data)
    {
        List<byte[]> messages = [];
        lock (_lock)
        {
            _splitter.Feed(data, messages);
        }

        if (Provider is not { } provider)
            return;

        foreach (var message in messages)
        {
            provider.OnMessage(this, message);
        }
    }
}

/// <summary>
/// A MIDI output port (https://webaudio.github.io/web-midi-api/#dom-midioutput): <see cref="Send"/> gets the messages that
/// the scripts send with <c>MIDIOutput.send()</c>.
/// </summary>
public abstract class MidiHostOutput : MidiHostPort
{
    /// <summary>Creates an output port.</summary>
    /// <param name="id">The identifier of the port.</param>
    protected MidiHostOutput(string id)
        : base(id, MidiPortType.Output)
    {
    }

    /// <summary>
    /// Sends messages to the device. <paramref name="data"/> is a sequence of complete and valid MIDI messages (the library
    /// rejects the other ones), and they must be sent after <paramref name="delay"/> (the timestamp of <c>send()</c>; zero to
    /// send them as soon as possible), in the order of the calls for the same time. Called on the thread of the window.
    /// </summary>
    /// <param name="data">The messages.</param>
    /// <param name="delay">The time to wait before the messages are sent.</param>
    public abstract void Send(ReadOnlyMemory<byte> data, TimeSpan delay);

    /// <summary>
    /// Discards the messages that are not sent yet (<c>MIDIOutput.clear()</c>, https://webaudio.github.io/web-midi-api/#dom-midioutput-clear):
    /// when the device is in the middle of a system exclusive message, the implementation should send <c>0xF7</c> to end it.
    /// Also called when the port is closed. Called on the thread of the window. The default implementation does nothing.
    /// </summary>
    public virtual void Clear()
    {
    }
}

/// <summary>The MIDI messages (https://webaudio.github.io/web-midi-api/#terminology, MIDI 1.0): their lengths and their validation.</summary>
internal static class MidiMessages
{
    public const byte SysEx = 0xF0;
    public const byte EndOfSysEx = 0xF7;

    /// <summary>Whether a byte is a status byte (its high bit is set).</summary>
    public static bool IsStatus(byte value) => value >= 0x80;

    /// <summary>The undefined status bytes, which no valid message uses (F4, F5, F9 and FD).</summary>
    public static bool IsReserved(byte value) => value is 0xF4 or 0xF5 or 0xF9 or 0xFD;

    /// <summary>Whether a byte is a System Real Time message (F8 to FF, except the reserved ones), which can appear inside other messages.</summary>
    public static bool IsRealTime(byte value) => value >= 0xF8 && !IsReserved(value);

    /// <summary>The length of the message of a status byte, including the status byte (0 for the system exclusive messages, whose length is variable).</summary>
    public static int GetLength(byte status) => status switch
    {
        < 0x80 => 0,
        < 0xC0 => 3,
        < 0xE0 => 2,
        < 0xF0 => 3,
        SysEx => 0,
        0xF1 or 0xF3 => 2,
        0xF2 => 3,
        _ => 1,
    };

    /// <summary>
    /// Validates the data of <c>MIDIOutput.send()</c> (https://webaudio.github.io/web-midi-api/#dom-midioutput-send): a
    /// sequence of complete messages without running status, in which the System Real Time messages can appear anywhere (as
    /// in Chromium). Returns the description of the first error (a TypeError), or null when the data is valid, and whether it
    /// contains a system exclusive message.
    /// </summary>
    public static string? Validate(ReadOnlySpan<byte> data, out bool containsSysEx)
    {
        containsSysEx = false;
        if (data.IsEmpty)
            return "The data does not contain a MIDI message.";

        var index = 0;
        while (index < data.Length)
        {
            var status = data[index];
            if (!IsStatus(status))
                return $"Running status is not allowed at index {index} ({status}).";

            if (status == EndOfSysEx)
                return $"Unexpected end of system exclusive message at index {index}.";

            if (IsReserved(status))
                return $"Unexpected reserved status {status} at index {index}.";

            if (status == SysEx)
            {
                containsSysEx = true;
                index++;
                while (true)
                {
                    if (index >= data.Length)
                        return "System exclusive message is not ended by end of system exclusive message.";

                    var value = data[index];
                    if (value == EndOfSysEx)
                    {
                        index++;
                        break;
                    }

                    if (IsReserved(value))
                        return $"Unexpected reserved status {value} at index {index}.";

                    if (IsStatus(value) && !IsRealTime(value))
                        return $"System exclusive message contains a status byte at index {index}.";

                    index++;
                }

                continue;
            }

            var remaining = GetLength(status) - 1;
            index++;
            while (remaining > 0)
            {
                if (index >= data.Length)
                    return "Message is incomplete.";

                var value = data[index];
                if (IsReserved(value))
                    return $"Unexpected reserved status {value} at index {index}.";

                if (IsStatus(value) && !IsRealTime(value))
                    return $"Unexpected status byte {value} at index {index}.";

                if (!IsStatus(value))
                {
                    remaining--;
                }

                index++;
            }
        }

        return null;
    }
}

/// <summary>
/// Splits the bytes received from a MIDI input port into messages (MIDI 1.0): the running status of the channel messages,
/// the system exclusive messages split over several calls, and the System Real Time messages inside other messages, which
/// are returned at once. A status byte that interrupts a system exclusive message discards it, and the data bytes without
/// status and the reserved status bytes are ignored.
/// </summary>
internal sealed class MidiMessageSplitter
{
    // The largest system exclusive message that is kept (as in Chromium, the larger ones are discarded)
    private const int MaxSysExLength = 64 * 1024;

    private readonly List<byte> _message = [];
    private byte _runningStatus;
    private int _expectedLength;
    private bool _inSysEx;
    private bool _sysExTooLong;

    public void Feed(ReadOnlySpan<byte> data, List<byte[]> messages)
    {
        foreach (var value in data)
        {
            if (MidiMessages.IsStatus(value))
            {
                if (MidiMessages.IsRealTime(value))
                {
                    messages.Add([value]);
                    continue;
                }

                if (MidiMessages.IsReserved(value))
                    continue;

                if (_inSysEx)
                {
                    if (value == MidiMessages.EndOfSysEx)
                    {
                        _message.Add(value);
                        if (!_sysExTooLong)
                        {
                            messages.Add([.. _message]);
                        }

                        Reset();
                        continue;
                    }

                    // Another status byte ends the system exclusive message, which is discarded
                    Reset();
                }

                if (value == MidiMessages.EndOfSysEx)
                    continue;

                _message.Clear();
                _message.Add(value);
                if (value == MidiMessages.SysEx)
                {
                    _inSysEx = true;
                    _runningStatus = 0;
                    continue;
                }

                // The channel messages set the running status, the System Common messages clear it
                _runningStatus = value < 0xF0 ? value : (byte)0;
                _expectedLength = MidiMessages.GetLength(value);
                if (_expectedLength == 1)
                {
                    messages.Add([value]);
                    _message.Clear();
                }

                continue;
            }

            if (_inSysEx)
            {
                if (_message.Count < MaxSysExLength)
                {
                    _message.Add(value);
                }
                else
                {
                    _sysExTooLong = true;
                }

                continue;
            }

            if (_message.Count == 0)
            {
                // A data byte after a complete message: the running status starts a new message
                if (_runningStatus == 0)
                    continue;

                _message.Add(_runningStatus);
                _expectedLength = MidiMessages.GetLength(_runningStatus);
            }

            _message.Add(value);
            if (_message.Count == _expectedLength)
            {
                messages.Add([.. _message]);
                _message.Clear();
            }
        }
    }

    private void Reset()
    {
        _message.Clear();
        _inSysEx = false;
        _sysExTooLong = false;
    }
}
