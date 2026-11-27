using System.Collections;
using System.ComponentModel;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The type of a MIDI port (https://webaudio.github.io/web-midi-api/#dom-midiporttype).</summary>
/// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiporttype"/>
[IdlInterface("MIDIPortType", "https://webaudio.github.io/web-midi-api/#dom-midiporttype")]
public enum MidiPortType
{
    /// <summary>An input port.</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiporttype-input"/>
    [IdlMember("input", "https://webaudio.github.io/web-midi-api/#dom-midiporttype-input")]
    Input,

    /// <summary>An output port.</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiporttype-output"/>
    [IdlMember("output", "https://webaudio.github.io/web-midi-api/#dom-midiporttype-output")]
    Output,
}

/// <summary>Whether the device of a MIDI port is connected (https://webaudio.github.io/web-midi-api/#dom-midiportdevicestate).</summary>
/// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiportdevicestate"/>
[IdlInterface("MIDIPortDeviceState", "https://webaudio.github.io/web-midi-api/#dom-midiportdevicestate")]
public enum MidiPortDeviceState
{
    /// <summary>The device is not connected.</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiportdevicestate-disconnected"/>
    [IdlMember("disconnected", "https://webaudio.github.io/web-midi-api/#dom-midiportdevicestate-disconnected")]
    Disconnected,

    /// <summary>The device is connected.</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiportdevicestate-connected"/>
    [IdlMember("connected", "https://webaudio.github.io/web-midi-api/#dom-midiportdevicestate-connected")]
    Connected,
}

/// <summary>Whether a MIDI port is open (https://webaudio.github.io/web-midi-api/#dom-midiportconnectionstate).</summary>
/// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiportconnectionstate"/>
[IdlInterface("MIDIPortConnectionState", "https://webaudio.github.io/web-midi-api/#dom-midiportconnectionstate")]
public enum MidiPortConnectionState
{
    /// <summary>The port is open.</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiportconnectionstate-open"/>
    [IdlMember("open", "https://webaudio.github.io/web-midi-api/#dom-midiportconnectionstate-open")]
    Open,

    /// <summary>The port is closed.</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiportconnectionstate-closed"/>
    [IdlMember("closed", "https://webaudio.github.io/web-midi-api/#dom-midiportconnectionstate-closed")]
    Closed,

    /// <summary>The port was opened while its device was disconnected: it is opened when the device is connected.</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiportconnectionstate-pending"/>
    [IdlMember("pending", "https://webaudio.github.io/web-midi-api/#dom-midiportconnectionstate-pending")]
    Pending,
}

/// <summary>The options of <c>navigator.requestMIDIAccess()</c> (https://webaudio.github.io/web-midi-api/#dom-midioptions).</summary>
/// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midioptions"/>
[IdlInterface("MIDIOptions", "https://webaudio.github.io/web-midi-api/#dom-midioptions")]
public sealed class MidiOptions
{
    /// <summary>Gets whether the page sends and receives system exclusive messages.</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midioptions-sysex"/>
    [IdlMember("sysex", "https://webaudio.github.io/web-midi-api/#dom-midioptions-sysex")]
    public bool? Sysex { get; init; }

    /// <summary>Gets whether the page uses the software synthesizers (<see cref="MidiHostPort.IsSoftwareSynthesizer"/>).</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midioptions-software"/>
    [IdlMember("software", "https://webaudio.github.io/web-midi-api/#dom-midioptions-software")]
    public bool? Software { get; init; }
}

/// <summary>The initialization dictionary of a <see cref="MidiMessageEvent"/> (https://webaudio.github.io/web-midi-api/#dom-midimessageeventinit).</summary>
/// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midimessageeventinit"/>
[IdlInterface("MIDIMessageEventInit", "https://webaudio.github.io/web-midi-api/#dom-midimessageeventinit")]
public sealed class MidiMessageEventInit : EventInit
{
    /// <summary>Gets the bytes of the message.</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midimessageeventinit-data"/>
    [IdlMember("data", "https://webaudio.github.io/web-midi-api/#dom-midimessageeventinit-data")]
    public ReadOnlyMemory<byte>? Data { get; init; }
}

/// <summary>The event of a MIDI message received by a <see cref="MidiInput"/> (<c>midimessage</c>, https://webaudio.github.io/web-midi-api/#dom-midimessageevent).</summary>
/// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midimessageevent"/>
[IdlInterface("MIDIMessageEvent", "https://webaudio.github.io/web-midi-api/#dom-midimessageevent")]
public sealed class MidiMessageEvent : Event
{
    private readonly JsBufferSource? _data;

    /// <summary>Creates an event.</summary>
    /// <param name="type">The type of the event.</param>
    /// <param name="eventInitDict">The initialization dictionary.</param>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midimessageevent-constructor"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-midi-api/#dom-midimessageevent-constructor")]
    public MidiMessageEvent(string type, MidiMessageEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        if (eventInitDict?.Data is { } data)
        {
            _data = new JsBufferSource(JsBufferKind.Uint8Array, data.ToArray());
        }
    }

    internal MidiMessageEvent(byte[] data)
        : base("midimessage")
    {
        _data = new JsBufferSource(JsBufferKind.Uint8Array, data);
    }

    /// <summary>Gets the bytes of the message, or null.</summary>
    [NotInIdl("The bytes for the .NET code: the data attribute is a Uint8Array")]
    public ReadOnlyMemory<byte>? Data => _data?.Bytes;

    /// <summary>Gets the <c>Uint8Array</c> of the message for the scripts: use <see cref="Data"/> in .NET code.</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midimessageevent-data"/>
    [IdlMember("data", "https://webaudio.github.io/web-midi-api/#dom-midimessageevent-data")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public object? DataArray => _data;
}

/// <summary>The initialization dictionary of a <see cref="MidiConnectionEvent"/> (https://webaudio.github.io/web-midi-api/#dom-midiconnectioneventinit).</summary>
/// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiconnectioneventinit"/>
[IdlInterface("MIDIConnectionEventInit", "https://webaudio.github.io/web-midi-api/#dom-midiconnectioneventinit")]
public sealed class MidiConnectionEventInit : EventInit
{
    /// <summary>Gets the port.</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiconnectioneventinit-port"/>
    [IdlMember("port", "https://webaudio.github.io/web-midi-api/#dom-midiconnectioneventinit-port")]
    public MidiPort? Port { get; init; }
}

/// <summary>
/// The event fired at a <see cref="MidiAccess"/> and at a <see cref="MidiPort"/> when the <c>state</c> or the <c>connection</c>
/// of the port changes (<c>statechange</c>, https://webaudio.github.io/web-midi-api/#dom-midiconnectionevent).
/// </summary>
/// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiconnectionevent"/>
[IdlInterface("MIDIConnectionEvent", "https://webaudio.github.io/web-midi-api/#dom-midiconnectionevent")]
public sealed class MidiConnectionEvent : Event
{
    /// <summary>Creates an event.</summary>
    /// <param name="type">The type of the event.</param>
    /// <param name="eventInitDict">The initialization dictionary.</param>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiconnectionevent-constructor"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-midi-api/#dom-midiconnectionevent-constructor")]
    public MidiConnectionEvent(string type, MidiConnectionEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        Port = eventInitDict?.Port;
    }

    internal MidiConnectionEvent(MidiPort port)
        : base("statechange")
    {
        Port = port;
    }

    /// <summary>Gets the port whose state changed.</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiconnectionevent-port"/>
    [IdlMember("port", "https://webaudio.github.io/web-midi-api/#dom-midiconnectionevent-port")]
    public MidiPort? Port { get; }
}

/// <summary>
/// The access of a window to the MIDI ports (https://webaudio.github.io/web-midi-api/#dom-midiaccess), returned by
/// <c>navigator.requestMIDIAccess()</c>: the connected input and output ports of the <see cref="MidiProvider"/> of the
/// browsing context (<see cref="BrowsingContextOptions.Midi"/>). Each call returns a new object, with its own port objects.
/// </summary>
/// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiaccess"/>
[IdlInterface("MIDIAccess", "https://webaudio.github.io/web-midi-api/#dom-midiaccess")]
public sealed class MidiAccess : EventTarget
{
    private readonly Dictionary<MidiHostPort, MidiPort> _ports = [];
    private readonly List<MidiPort> _order = [];
    private readonly bool _software;

    internal MidiAccess(GlobalMidi state, bool sysexEnabled, bool software)
    {
        State = state;
        SysexEnabled = sysexEnabled;
        _software = software;
        Inputs = new MidiInputMap(this);
        Outputs = new MidiOutputMap(this);
        foreach (var port in state.Provider?.Ports ?? [])
        {
            if (IsVisible(port))
            {
                GetOrCreate(port);
            }
        }
    }

    internal GlobalMidi State { get; }

    internal override GlobalScope? RelevantGlobal => State.Global;

    /// <summary>Gets the connected input ports, by identifier.</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiaccess-inputs"/>
    [IdlMember("inputs", "https://webaudio.github.io/web-midi-api/#dom-midiaccess-inputs")]
    public MidiInputMap Inputs { get; }

    /// <summary>Gets the connected output ports, by identifier.</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiaccess-outputs"/>
    [IdlMember("outputs", "https://webaudio.github.io/web-midi-api/#dom-midiaccess-outputs")]
    public MidiOutputMap Outputs { get; }

    /// <summary>Gets or sets the event handler of the <c>statechange</c> events (a port is connected or disconnected, opened or closed).</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiaccess-onstatechange"/>
    [IdlMember("onstatechange", "https://webaudio.github.io/web-midi-api/#dom-midiaccess-onstatechange")]
    public Action<Event>? OnStateChange
    {
        get => GetEventHandler("statechange");
        set => SetEventHandler("statechange", value);
    }

    /// <summary>Gets whether the ports send and receive the system exclusive messages (the <c>sysex</c> option of <c>requestMIDIAccess()</c>).</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiaccess-sysexenabled"/>
    [IdlMember("sysexEnabled", "https://webaudio.github.io/web-midi-api/#dom-midiaccess-sysexenabled")]
    public bool SysexEnabled { get; }

    /// <summary>The connected ports of a type, in the order they were seen.</summary>
    internal IEnumerable<TPort> GetConnectedPorts<TPort>()
        where TPort : MidiPort
    {
        foreach (var port in _order)
        {
            if (port is TPort typed && port.State == MidiPortDeviceState.Connected)
                yield return typed;
        }
    }

    /// <summary>A port of the provider is connected: a new port fires a <c>statechange</c> event at the access, a known port also at itself.</summary>
    internal void OnConnected(MidiHostPort hostPort)
    {
        if (!IsVisible(hostPort))
            return;

        if (_ports.TryGetValue(hostPort, out var port))
        {
            port.OnConnected();
            return;
        }

        port = GetOrCreate(hostPort);
        FireEvent(new MidiConnectionEvent(port));
    }

    internal void OnDisconnected(MidiHostPort hostPort)
    {
        if (_ports.TryGetValue(hostPort, out var port))
        {
            port.OnDisconnected();
        }
    }

    internal void OnMessage(MidiHostInput hostPort, byte[] message)
    {
        if (_ports.TryGetValue(hostPort, out var port) && port is MidiInput input)
        {
            input.OnMessage(message);
        }
    }

    internal void OnDestroyed()
    {
        foreach (var port in _order)
        {
            port.OnDestroyed();
        }
    }

    private bool IsVisible(MidiHostPort port) => _software || !port.IsSoftwareSynthesizer;

    private MidiPort GetOrCreate(MidiHostPort hostPort)
    {
        if (!_ports.TryGetValue(hostPort, out var port))
        {
            port = hostPort switch
            {
                MidiHostInput input => new MidiInput(this, input),
                MidiHostOutput output => new MidiOutput(this, output),
                _ => throw new InvalidOperationException("Unknown MIDI port"),
            };
            _ports.Add(hostPort, port);
            _order.Add(port);
        }

        return port;
    }
}

/// <summary>The connected input ports of a <see cref="MidiAccess"/>, by identifier (https://webaudio.github.io/web-midi-api/#dom-midiinputmap): a read-only map.</summary>
/// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiinputmap"/>
[IdlInterface("MIDIInputMap", "https://webaudio.github.io/web-midi-api/#dom-midiinputmap")]
public sealed class MidiInputMap : IReadOnlyCollection<KeyValuePair<string, MidiInput>>
{
    private readonly MidiAccess _access;

    internal MidiInputMap(MidiAccess access) => _access = access;

    /// <summary>Gets the number of ports.</summary>
    [NotInIdl("The size member of maplike<DOMString, MIDIInput>")]
    public int Count => _access.GetConnectedPorts<MidiInput>().Count();

    /// <summary>Returns the port of an identifier, or null.</summary>
    /// <param name="id">The identifier of the port.</param>
    [NotInIdl("The get() member of maplike<DOMString, MIDIInput>")]
    public MidiInput? Get(string id) => _access.GetConnectedPorts<MidiInput>().FirstOrDefault(port => port.Id == id);

    /// <summary>Returns whether there is a port of an identifier.</summary>
    /// <param name="id">The identifier of the port.</param>
    [NotInIdl("The has() member of maplike<DOMString, MIDIInput>")]
    public bool Has(string id) => Get(id) is not null;

    /// <summary>Returns an enumerator over the ports, by identifier.</summary>
    [NotInIdl("The entries() member of maplike<DOMString, MIDIInput>")]
    public IEnumerator<KeyValuePair<string, MidiInput>> GetEnumerator()
        => _access.GetConnectedPorts<MidiInput>().Select(static port => KeyValuePair.Create(port.Id, port)).ToList().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>The connected output ports of a <see cref="MidiAccess"/>, by identifier (https://webaudio.github.io/web-midi-api/#dom-midioutputmap): a read-only map.</summary>
/// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midioutputmap"/>
[IdlInterface("MIDIOutputMap", "https://webaudio.github.io/web-midi-api/#dom-midioutputmap")]
public sealed class MidiOutputMap : IReadOnlyCollection<KeyValuePair<string, MidiOutput>>
{
    private readonly MidiAccess _access;

    internal MidiOutputMap(MidiAccess access) => _access = access;

    /// <summary>Gets the number of ports.</summary>
    [NotInIdl("The size member of maplike<DOMString, MIDIOutput>")]
    public int Count => _access.GetConnectedPorts<MidiOutput>().Count();

    /// <summary>Returns the port of an identifier, or null.</summary>
    /// <param name="id">The identifier of the port.</param>
    [NotInIdl("The get() member of maplike<DOMString, MIDIOutput>")]
    public MidiOutput? Get(string id) => _access.GetConnectedPorts<MidiOutput>().FirstOrDefault(port => port.Id == id);

    /// <summary>Returns whether there is a port of an identifier.</summary>
    /// <param name="id">The identifier of the port.</param>
    [NotInIdl("The has() member of maplike<DOMString, MIDIOutput>")]
    public bool Has(string id) => Get(id) is not null;

    /// <summary>Returns an enumerator over the ports, by identifier.</summary>
    [NotInIdl("The entries() member of maplike<DOMString, MIDIOutput>")]
    public IEnumerator<KeyValuePair<string, MidiOutput>> GetEnumerator()
        => _access.GetConnectedPorts<MidiOutput>().Select(static port => KeyValuePair.Create(port.Id, port)).ToList().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// A MIDI port of a <see cref="MidiAccess"/> (https://webaudio.github.io/web-midi-api/#dom-midiport): its information, whether
/// its device is connected (<see cref="State"/>), and whether it is open (<see cref="Connection"/>). Opening the port obtains
/// the access to the device with <see cref="MidiHostPort.OpenAsync"/>.
/// </summary>
/// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiport"/>
[IdlInterface("MIDIPort", "https://webaudio.github.io/web-midi-api/#dom-midiport")]
public abstract class MidiPort : EventTarget
{
    // Incremented when the port is closed, so that the access obtained by an open that was in progress is released
    private int _generation;
    private Task<bool>? _opening;

    private protected MidiPort(MidiAccess access, MidiHostPort host)
    {
        Access = access;
        Host = host;
        State = host.Provider == access.State.Provider && access.State.Provider is not null ? MidiPortDeviceState.Connected : MidiPortDeviceState.Disconnected;
    }

    internal MidiAccess Access { get; }

    internal MidiHostPort Host { get; }

    private protected GlobalScope Global => Access.State.Global;

    internal override GlobalScope? RelevantGlobal => Global;

    /// <summary>Gets the identifier of the port.</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiport-id"/>
    [IdlMember("id", "https://webaudio.github.io/web-midi-api/#dom-midiport-id")]
    public string Id => Host.Id;

    /// <summary>Gets the manufacturer of the port, or null.</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiport-manufacturer"/>
    [IdlMember("manufacturer", "https://webaudio.github.io/web-midi-api/#dom-midiport-manufacturer")]
    public string? Manufacturer => Host.Manufacturer;

    /// <summary>Gets the name of the port, or null.</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiport-name"/>
    [IdlMember("name", "https://webaudio.github.io/web-midi-api/#dom-midiport-name")]
    public string? Name => Host.Name;

    /// <summary>Gets the type of the port.</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiport-type"/>
    [IdlMember("type", "https://webaudio.github.io/web-midi-api/#dom-midiport-type")]
    public MidiPortType Type => Host.Type;

    /// <summary>Gets the version of the port, or null.</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiport-version"/>
    [IdlMember("version", "https://webaudio.github.io/web-midi-api/#dom-midiport-version")]
    public string? Version => Host.Version;

    /// <summary>Gets whether the device of the port is connected.</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiport-state"/>
    [IdlMember("state", "https://webaudio.github.io/web-midi-api/#dom-midiport-state")]
    public MidiPortDeviceState State { get; private set; }

    /// <summary>Gets whether the port is open.</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiport-connection"/>
    [IdlMember("connection", "https://webaudio.github.io/web-midi-api/#dom-midiport-connection")]
    public MidiPortConnectionState Connection { get; private set; } = MidiPortConnectionState.Closed;

    /// <summary>Gets or sets the event handler of the <c>statechange</c> events of the port.</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiport-onstatechange"/>
    [IdlMember("onstatechange", "https://webaudio.github.io/web-midi-api/#dom-midiport-onstatechange")]
    public Action<Event>? OnStateChange
    {
        get => GetEventHandler("statechange");
        set => SetEventHandler("statechange", value);
    }

    /// <summary>
    /// Opens the port (https://webaudio.github.io/web-midi-api/#dom-midiport-open): the access to the device is obtained with
    /// <see cref="MidiHostPort.OpenAsync"/>, and <see cref="Connection"/> becomes <c>open</c>. When the device is disconnected,
    /// the port becomes <c>pending</c> and is opened when the device is connected. The task fails with a
    /// <c>NotAllowedError</c> <see cref="DomException"/> when the device is not available.
    /// </summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiport-open"/>
    [IdlMember("open", "https://webaudio.github.io/web-midi-api/#dom-midiport-open")]
    public Task<MidiPort> Open()
    {
        var result = new TaskCompletionSource<MidiPort>();
        Global.QueueGlobalTask(() => OpenCore(result));
        return result.Task;
    }

    /// <summary>
    /// Closes the port (https://webaudio.github.io/web-midi-api/#dom-midiport-close): the messages of an output that are not
    /// sent yet are discarded, the access to the device is released with <see cref="MidiHostPort.CloseAsync"/>, and
    /// <see cref="Connection"/> becomes <c>closed</c>.
    /// </summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiport-close"/>
    [IdlMember("close", "https://webaudio.github.io/web-midi-api/#dom-midiport-close")]
    public Task<MidiPort> Close()
    {
        var result = new TaskCompletionSource<MidiPort>();
        Global.QueueGlobalTask(() =>
        {
            if (Connection != MidiPortConnectionState.Closed || _opening is not null)
            {
                _generation++;
                _opening = null;
                OnClosing();
                if (Connection == MidiPortConnectionState.Open)
                {
                    ReleaseAccess();
                }

                if (Connection != MidiPortConnectionState.Closed)
                {
                    SetConnection(MidiPortConnectionState.Closed);
                }
            }

            result.TrySetResult(this);
        });
        return result.Task;
    }

    /// <summary>The port is opened implicitly (a <c>midimessage</c> listener of an input, <c>send()</c> on an output).</summary>
    internal void OpenImplicitly()
    {
        if (Connection == MidiPortConnectionState.Closed && State == MidiPortDeviceState.Connected)
        {
            Global.QueueGlobalTask(() => OpenCore(result: null));
        }
    }

    /// <summary>The device is connected again: a <c>pending</c> port is opened, then the port fires a <c>statechange</c> event.</summary>
    internal void OnConnected()
    {
        State = MidiPortDeviceState.Connected;
        if (Connection != MidiPortConnectionState.Pending)
        {
            FireStateChange();
            return;
        }

        var generation = _generation;
        _ = ObtainAccess(success =>
        {
            if (generation != _generation)
                return;

            Connection = success ? MidiPortConnectionState.Open : MidiPortConnectionState.Closed;
            if (success)
            {
                OnOpened();
            }

            FireStateChange();
        });
    }

    /// <summary>The device is disconnected: an open port becomes <c>pending</c>.</summary>
    internal void OnDisconnected()
    {
        State = MidiPortDeviceState.Disconnected;
        if (Connection == MidiPortConnectionState.Open)
        {
            Connection = MidiPortConnectionState.Pending;
            OnClosing();
        }

        FireStateChange();
    }

    /// <summary>The window is destroyed: the access to the device is released.</summary>
    internal void OnDestroyed()
    {
        _generation++;
        if (Connection == MidiPortConnectionState.Open)
        {
            ReleaseAccess();
        }

        Connection = MidiPortConnectionState.Closed;
    }

    /// <summary>The port is open: an output sends the messages that wait for it.</summary>
    private protected virtual void OnOpened()
    {
    }

    /// <summary>The port is closed or disconnected: an output discards the messages that are not sent yet.</summary>
    private protected virtual void OnClosing()
    {
    }

    // https://webaudio.github.io/web-midi-api/#dom-midiport-open
    private void OpenCore(TaskCompletionSource<MidiPort>? result)
    {
        if (Connection is MidiPortConnectionState.Open or MidiPortConnectionState.Pending)
        {
            result?.TrySetResult(this);
            return;
        }

        if (State == MidiPortDeviceState.Disconnected)
        {
            SetConnection(MidiPortConnectionState.Pending);
            result?.TrySetResult(this);
            return;
        }

        var generation = _generation;
        _ = ObtainAccess(success =>
        {
            if (generation != _generation)
            {
                // The port was closed while it was opened
                result?.TrySetResult(this);
                return;
            }

            if (!success)
            {
                result?.TrySetException(DomException.NotAllowed($"Failed to open the MIDI port '{Id}': the port is not available."));
                return;
            }

            if (Connection != MidiPortConnectionState.Open)
            {
                SetConnection(MidiPortConnectionState.Open);
                OnOpened();
            }

            result?.TrySetResult(this);
        });
    }

    /// <summary>Obtains the access to the device (one open of the host at a time); the callback runs in a task of the window.</summary>
    private Task<bool> ObtainAccess(Action<bool> callback)
    {
        if (_opening is null)
        {
            Task hostTask;
            try
            {
                hostTask = Host.OpenAsync(Access.State.CancellationToken);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                hostTask = Task.FromException(exception);
            }

            var generation = _generation;
            _opening = hostTask.ContinueWith(
                static task => task.IsCompletedSuccessfully,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            var opening = _opening;
            _ = opening.ContinueWith(
                task => Global.QueueGlobalTask(() =>
                {
                    if (_opening == opening)
                    {
                        _opening = null;
                    }

                    // The access obtained after the port was closed is released at once
                    if (task.Result && generation != _generation)
                    {
                        ReleaseAccess();
                    }
                }),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        var pending = _opening;
        return pending.ContinueWith(
            task =>
            {
                Global.QueueGlobalTask(() => callback(task.Result));
                return task.Result;
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void ReleaseAccess()
    {
        try
        {
            _ = Host.CloseAsync();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The errors of the host are ignored: the port is closed anyway
        }
    }

    private void SetConnection(MidiPortConnectionState connection)
    {
        Connection = connection;
        FireStateChange();
    }

    private void FireStateChange()
    {
        FireEvent(new MidiConnectionEvent(this));
        Access.FireEvent(new MidiConnectionEvent(this));
    }
}

/// <summary>
/// A MIDI input port (https://webaudio.github.io/web-midi-api/#dom-midiinput): the messages that the device sends
/// (<see cref="MidiHostInput.Receive"/>) are fired as <c>midimessage</c> events while the port is open. Setting
/// <see cref="OnMidiMessage"/>, or adding a <c>midimessage</c> listener, opens the port.
/// </summary>
/// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiinput"/>
[IdlInterface("MIDIInput", "https://webaudio.github.io/web-midi-api/#dom-midiinput")]
public sealed class MidiInput : MidiPort
{
    internal MidiInput(MidiAccess access, MidiHostInput host)
        : base(access, host)
    {
    }

    /// <summary>Gets or sets the event handler of the <c>midimessage</c> events (<see cref="MidiMessageEvent"/>).</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midiinput-onmidimessage"/>
    [IdlMember("onmidimessage", "https://webaudio.github.io/web-midi-api/#dom-midiinput-onmidimessage")]
    public Action<Event>? OnMidiMessage
    {
        get => GetEventHandler("midimessage");
        set => SetEventHandler("midimessage", value);
    }

    internal void OnMessage(byte[] message)
    {
        if (Connection != MidiPortConnectionState.Open)
            return;

        // The system exclusive messages are masked when they are not enabled
        if (message[0] == MidiMessages.SysEx && !Access.SysexEnabled)
            return;

        FireEvent(new MidiMessageEvent(message));
    }

    private protected override void OnEventHandlerSet(string type)
    {
        if (type == "midimessage" && OnMidiMessage is not null)
        {
            OpenImplicitly();
        }
    }

    private protected override void OnEventListenerAdded(EventListenerEntry listener)
    {
        if (listener.Type == "midimessage")
        {
            OpenImplicitly();
        }
    }
}

/// <summary>
/// A MIDI output port (https://webaudio.github.io/web-midi-api/#dom-midioutput): <see cref="Send"/> validates the messages
/// and gives them to <see cref="MidiHostOutput.Send"/>, opening the port when it is closed.
/// </summary>
/// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midioutput"/>
[IdlInterface("MIDIOutput", "https://webaudio.github.io/web-midi-api/#dom-midioutput")]
public sealed class MidiOutput : MidiPort
{
    // The messages sent while the port is opened, with their timestamps
    private readonly List<(byte[] Data, double Timestamp)> _pending = [];

    internal MidiOutput(MidiAccess access, MidiHostOutput host)
        : base(access, host)
    {
    }

    private MidiHostOutput HostOutput => (MidiHostOutput)Host;

    /// <summary>
    /// Sends MIDI messages (https://webaudio.github.io/web-midi-api/#dom-midioutput-send): <paramref name="data"/> is a sequence
    /// of complete messages, without running status (the System Real Time messages can appear inside other messages). A closed
    /// port is opened, and the messages are sent when it is open.
    /// </summary>
    /// <param name="data">The bytes of the messages.</param>
    /// <param name="timestamp">The time at which the messages are sent, relative to the time origin of the window (0, or a time in the past, to send them at once).</param>
    /// <exception cref="TypeErrorException">The data is not a sequence of valid messages.</exception>
    /// <exception cref="DomException">A <c>NotAllowedError</c> when the data contains a system exclusive message and the access does not enable them, an <c>InvalidStateError</c> when the device is disconnected.</exception>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midioutput-send"/>
    [IdlMember("send", "https://webaudio.github.io/web-midi-api/#dom-midioutput-send")]
    public void Send(IReadOnlyList<byte> data, double timestamp = 0)
    {
        ArgumentNullException.ThrowIfNull(data);
        var bytes = data.ToArray();
        if (MidiMessages.Validate(bytes, out var containsSysEx) is { } error)
            throw new TypeErrorException("Failed to execute 'send' on 'MIDIOutput': " + error);

        if (containsSysEx && !Access.SysexEnabled)
            throw DomException.NotAllowed("Failed to execute 'send' on 'MIDIOutput': System exclusive message is not allowed.");

        if (State == MidiPortDeviceState.Disconnected)
            throw DomException.InvalidState("Failed to execute 'send' on 'MIDIOutput': The port is disconnected.");

        if (Connection == MidiPortConnectionState.Open)
        {
            HostOutput.Send(bytes, GetDelay(timestamp));
            return;
        }

        _pending.Add((bytes, timestamp));
        OpenImplicitly();
    }

    /// <summary>Discards the messages that are not sent yet (https://webaudio.github.io/web-midi-api/#dom-midioutput-clear), with <see cref="MidiHostOutput.Clear"/>.</summary>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-midioutput-clear"/>
    [IdlMember("clear", "https://webaudio.github.io/web-midi-api/#dom-midioutput-clear")]
    public void Clear()
    {
        _pending.Clear();
        HostOutput.Clear();
    }

    private protected override void OnOpened()
    {
        var pending = _pending.ToArray();
        _pending.Clear();
        foreach (var (data, timestamp) in pending)
        {
            HostOutput.Send(data, GetDelay(timestamp));
        }
    }

    private protected override void OnClosing()
    {
        _pending.Clear();
        HostOutput.Clear();
    }

    private TimeSpan GetDelay(double timestamp)
    {
        var delay = timestamp - Global.GetCurrentHighResolutionTime();
        return delay > 0 && double.IsFinite(delay) ? TimeSpan.FromMilliseconds(delay) : TimeSpan.Zero;
    }
}

/// <summary>
/// The MIDI state of a window: its <see cref="MidiAccess"/> objects, which get the connections, the disconnections and the
/// messages of the <see cref="MidiProvider"/> in tasks.
/// </summary>
#pragma warning disable CA1001 // The cancellation source is canceled when the window is destroyed, and the host may still observe its token: it is not disposed (it allocates no handle)
internal sealed class GlobalMidi
#pragma warning restore CA1001
{
    private readonly List<MidiAccess> _accesses = [];
    private readonly CancellationTokenSource _destroyed = new();

    public GlobalMidi(GlobalScope global)
    {
        Global = global;
        Provider?.Register(this);
    }

    public GlobalScope Global { get; }

    public MidiProvider? Provider => Global.Options.Midi?.Provider;

    public CancellationToken CancellationToken => _destroyed.Token;

    // https://webaudio.github.io/web-midi-api/#dom-navigator-requestmidiaccess; a prerendering document gets the access once it
    // is activated (https://wicg.github.io/nav-speculation/prerendering.html#web-midi-patch)
    public Task<MidiAccess> RequestAccess(Window window, MidiOptions options) => Document.DelayWhilePrerendering(window, () => RequestAccessCore(window, options));

    private Task<MidiAccess> RequestAccessCore(Window window, MidiOptions options)
    {
        var result = new TaskCompletionSource<MidiAccess>();
        if (window.BrowsingContext is null || !window.Document.IsFullyActive)
        {
            result.TrySetException(DomException.InvalidState("Failed to execute 'requestMIDIAccess' on 'Navigator': The document is not fully active."));
            return result.Task;
        }

        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(window.Document, PolicyControlledFeature.Midi))
        {
            Global.QueueGlobalTask(() => result.TrySetException(DomException.NotAllowed("Failed to execute 'requestMIDIAccess' on 'Navigator': Access to the feature \"midi\" is disallowed by permissions policy.")));
            return result.Task;
        }

        var permission = Permissions.RequestPermissionToUse(window, "midi");
        Global.QueueGlobalTask(() =>
        {
            if (permission == PermissionState.Denied)
            {
                result.TrySetException(DomException.NotAllowed("Failed to execute 'requestMIDIAccess' on 'Navigator': The midi permission is denied."));
                return;
            }

            var access = new MidiAccess(this, sysexEnabled: options.Sysex is true, software: options.Software is true);
            _accesses.Add(access);
            result.TrySetResult(access);
        });
        return result.Task;
    }

    public void OnConnected(MidiHostPort port) => Global.QueueGlobalTask(() =>
    {
        foreach (var access in _accesses.ToArray())
        {
            access.OnConnected(port);
        }
    });

    public void OnDisconnected(MidiHostPort port) => Global.QueueGlobalTask(() =>
    {
        foreach (var access in _accesses.ToArray())
        {
            access.OnDisconnected(port);
        }
    });

    public void OnMessage(MidiHostInput port, byte[] message) => Global.QueueGlobalTask(() =>
    {
        foreach (var access in _accesses.ToArray())
        {
            access.OnMessage(port, message);
        }
    });

    /// <summary>The window is destroyed: the open ports are closed.</summary>
    public void OnDestroyed()
    {
        _destroyed.Cancel();
        foreach (var access in _accesses)
        {
            access.OnDestroyed();
        }

        _accesses.Clear();
    }
}

// https://webaudio.github.io/web-midi-api/#extensions-to-the-navigator-interface
public sealed partial class Navigator
{
    /// <summary>
    /// Requests the access to the MIDI ports (https://webaudio.github.io/web-midi-api/#dom-navigator-requestmidiaccess): the
    /// <c>midi</c> permission is requested (<see cref="BrowsingContextOptions.RequestPermission"/>), and a new
    /// <see cref="MidiAccess"/> lists the ports of the <see cref="MidiProvider"/>. The task fails with a <c>NotAllowedError</c>
    /// <see cref="DomException"/> when the <c>midi</c> permissions policy feature is not allowed or the permission is denied,
    /// and with an <c>InvalidStateError</c> when the document is not fully active. A prerendering document gets the access
    /// once it is activated.
    /// </summary>
    /// <param name="options">The options: whether the page uses the system exclusive messages and the software synthesizers.</param>
    /// <remarks>The <c>midi</c> permission covers the system exclusive messages: the browsers that ask a separate permission for them (<c>{ name: "midi", sysex: true }</c>) are not emulated.</remarks>
    /// <seealso href="https://webaudio.github.io/web-midi-api/#dom-navigator-requestmidiaccess"/>
    [IdlMember("requestMIDIAccess", "https://webaudio.github.io/web-midi-api/#dom-navigator-requestmidiaccess")]
    public Task<MidiAccess> RequestMidiAccess(MidiOptions? options = null) => _window.GetMidiState().RequestAccess(_window, options ?? new MidiOptions());
}

public abstract partial class GlobalScope
{
    private GlobalMidi? _midiState;

    /// <summary>The MIDI state of the window, created by the first call to <c>navigator.requestMIDIAccess()</c>.</summary>
    internal GlobalMidi GetMidiState() => _midiState ??= new GlobalMidi(this);

    /// <summary>The window is unloaded: its MIDI ports are closed.</summary>
    internal void CloseMidiPorts() => _midiState?.OnDestroyed();
}
