namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The host hooks of the Web Serial API (https://serial.spec.whatwg.org/, <see cref="BrowsingContextOptions.Serial"/>).</summary>
public sealed class WebSerialOptions
{
    /// <summary>
    /// Gets or sets the serial ports of the machine: a <see cref="SerialProvider"/> to which the application connects the
    /// <see cref="SerialHostPort"/> objects that implement the ports. A provider can be shared by several browsing contexts.
    /// The default is null: the machine has no serial port.
    /// </summary>
    public SerialProvider? Provider { get; init; }
}

/// <summary>
/// The serial ports of the machine, used by the windows and the dedicated workers of the browsing contexts that share a
/// <see cref="BrowsingContextOptions"/> (<see cref="WebSerialOptions.Provider"/>): <see cref="Connect"/> and
/// <see cref="Disconnect"/> fire the <c>connect</c> and <c>disconnect</c> events of the ports, and the permission storage
/// gives each origin the ports it can use. Override <see cref="RequestPort"/> to play the role of the port chooser of
/// <c>navigator.serial.requestPort()</c>.
/// </summary>
/// <remarks>A provider can be shared by several browsing contexts, and used from any thread.</remarks>
public class SerialProvider
{
    private readonly Lock _lock = new();
    private readonly List<SerialHostPort> _ports = [];
    private readonly List<WeakReference<GlobalSerial>> _globals = [];
    private readonly Dictionary<string, HashSet<SerialHostPort>> _allowedPorts = new(StringComparer.Ordinal);

    /// <summary>Gets the available ports, in the order they were connected.</summary>
    public IReadOnlyList<SerialHostPort> Ports
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
    /// Connects a port (it becomes available, https://serial.spec.whatwg.org/#serialport-onconnect-attribute): the windows and
    /// the workers of the origins that are allowed to use it get a <c>connect</c> event. Can be called from any thread.
    /// </summary>
    /// <param name="port">The port. A port can be connected to one provider at a time.</param>
    /// <exception cref="InvalidOperationException">The port is connected to a provider.</exception>
    public void Connect(SerialHostPort port)
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
    /// Disconnects a port (https://serial.spec.whatwg.org/#serialport-ondisconnect-attribute): the streams of an open port
    /// fail with a <c>NetworkError</c>, and the windows and the workers that see it get a <c>disconnect</c> event. Can be
    /// called from any thread.
    /// </summary>
    /// <param name="port">The port.</param>
    /// <returns>Whether the port was connected to this provider.</returns>
    public bool Disconnect(SerialHostPort port)
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

    /// <summary>
    /// Returns whether an origin can use a port without asking the user: the default implementation returns whether the port
    /// was allowed with <see cref="Allow"/>, or chosen by the user with <see cref="RequestPort"/>, and not forgotten since.
    /// </summary>
    /// <param name="port">The port.</param>
    /// <param name="origin">The serialization of the origin of the window or the worker (e.g. <c>https://example.com</c>).</param>
    public virtual bool IsAllowed(SerialHostPort port, string origin)
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(origin);
        lock (_lock)
        {
            return _allowedPorts.TryGetValue(origin, out var ports) && ports.Contains(port);
        }
    }

    /// <summary>Allows an origin to use a port.</summary>
    /// <param name="port">The port.</param>
    /// <param name="origin">The serialization of the origin.</param>
    public void Allow(SerialHostPort port, string origin)
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(origin);
        lock (_lock)
        {
            if (!_allowedPorts.TryGetValue(origin, out var ports))
            {
                ports = [];
                _allowedPorts.Add(origin, ports);
            }

            ports.Add(port);
        }
    }

    /// <summary>Revokes the permission of an origin to use a port, as <c>SerialPort.forget()</c> does.</summary>
    /// <param name="port">The port.</param>
    /// <param name="origin">The serialization of the origin.</param>
    public void Revoke(SerialHostPort port, string origin)
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(origin);
        lock (_lock)
        {
            if (_allowedPorts.TryGetValue(origin, out var ports))
            {
                ports.Remove(port);
            }
        }
    }

    /// <summary>
    /// Asks the user to choose a port for <c>navigator.serial.requestPort()</c> (https://serial.spec.whatwg.org/#requestport-method):
    /// <see cref="SerialPortRequest.Ports"/> are the available ports that match the filters of the page. Returns one of them,
    /// which the origin is then allowed to use, or null when the user cancels (the request fails with a <c>NotFoundError</c>).
    /// The default implementation returns null.
    /// </summary>
    /// <param name="request">The request of the window.</param>
    public virtual Task<SerialHostPort?> RequestPort(SerialPortRequest request) => Task.FromResult<SerialHostPort?>(null);

    /// <summary>Registers the serial objects of a window or a worker, which then get the connections and the disconnections.</summary>
    internal void Register(GlobalSerial global)
    {
        lock (_lock)
        {
            _globals.RemoveAll(static reference => !reference.TryGetTarget(out _));
            _globals.Add(new WeakReference<GlobalSerial>(global));
        }
    }

    private List<GlobalSerial> GetGlobals()
    {
        lock (_lock)
        {
            var result = new List<GlobalSerial>(_globals.Count);
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

/// <summary>A request of <c>navigator.serial.requestPort()</c>, given to <see cref="SerialProvider.RequestPort"/>.</summary>
public sealed class SerialPortRequest
{
    internal SerialPortRequest(CallbackContext context, IReadOnlyList<SerialPortFilter> filters, IReadOnlyList<SerialHostPort> ports)
    {
        Context = context;
        Filters = filters;
        Ports = ports;
    }

    /// <summary>Gets the context of the window that requests the port.</summary>
    public CallbackContext Context { get; }

    /// <summary>Gets the filters of the page: a port matches one of them (all the ports when there is none).</summary>
    public IReadOnlyList<SerialPortFilter> Filters { get; }

    /// <summary>Gets the ports to choose from: the available ports that match the filters (the Bluetooth ports of a blocked service class are excluded).</summary>
    public IReadOnlyList<SerialHostPort> Ports { get; }
}

/// <summary>
/// A serial port of a <see cref="SerialProvider"/>: the information of the port, and the connections that the
/// <c>SerialPort</c> objects of the pages open with <see cref="OpenAsync"/>.
/// </summary>
public abstract class SerialHostPort
{
    /// <summary>Creates a port.</summary>
    /// <param name="usbVendorId">The vendor ID of the USB device of the port, or null when the port is not part of a USB device.</param>
    /// <param name="usbProductId">The product ID of the USB device of the port, or null.</param>
    /// <param name="bluetoothServiceClassId">The service class UUID of the Bluetooth service of the port (a valid UUID), or null when the port is not a Bluetooth service.</param>
    protected SerialHostPort(ushort? usbVendorId = null, ushort? usbProductId = null, string? bluetoothServiceClassId = null)
    {
        if (bluetoothServiceClassId is not null && !BluetoothUuid.IsValidUuid(bluetoothServiceClassId))
            throw new ArgumentException($"'{bluetoothServiceClassId}' is not a valid UUID", nameof(bluetoothServiceClassId));

        UsbVendorId = usbVendorId;
        UsbProductId = usbProductId;
        BluetoothServiceClassId = bluetoothServiceClassId;
    }

    /// <summary>Gets the vendor ID of the USB device of the port, or null.</summary>
    public ushort? UsbVendorId { get; }

    /// <summary>Gets the product ID of the USB device of the port, or null.</summary>
    public ushort? UsbProductId { get; }

    /// <summary>Gets the service class UUID of the Bluetooth service of the port, or null.</summary>
    public string? BluetoothServiceClassId { get; }

    internal SerialProvider? Provider { get; set; }

    /// <summary>
    /// Opens the port with the parameters of the page (<c>SerialPort.open()</c>). Throw any exception to fail the opening
    /// (the page gets a <c>NetworkError</c>).
    /// </summary>
    /// <param name="options">The connection parameters, validated by the library.</param>
    /// <param name="cancellationToken">The token, canceled when the page forgets the port during the opening.</param>
    public abstract Task<SerialConnection> OpenAsync(SerialConnectionOptions options, CancellationToken cancellationToken);
}

/// <summary>The connection parameters of <c>SerialPort.open()</c> (https://serial.spec.whatwg.org/#dom-serialoptions), validated by the library.</summary>
/// <param name="BaudRate">The baud rate (not 0).</param>
/// <param name="DataBits">The number of data bits per frame: 7 or 8.</param>
/// <param name="StopBits">The number of stop bits at the end of a frame: 1 or 2.</param>
/// <param name="Parity">The parity mode.</param>
/// <param name="BufferSize">The size of the read and write buffers (not 0, at most 16 MiB).</param>
/// <param name="FlowControl">The flow control mode.</param>
public sealed record SerialConnectionOptions(uint BaudRate, byte DataBits, byte StopBits, ParityType Parity, uint BufferSize, FlowControlType FlowControl);

/// <summary>
/// An open serial port (the connection of a <c>SerialPort</c> object): the reads of its <c>readable</c> stream, the writes of
/// its <c>writable</c> stream, and its control signals. Throw a <see cref="SerialException"/> to report an error of the port.
/// </summary>
public abstract class SerialConnection
{
    /// <summary>
    /// Reads at least one byte (<c>port.readable</c>): the task completes when data is received. Throw a
    /// <see cref="SerialException"/> for an error of the port (a <see cref="SerialError.Disconnected"/> error, or a result of 0,
    /// makes the readable stream fail with a <c>NetworkError</c>, and the next one is null).
    /// </summary>
    /// <param name="buffer">The buffer, of the buffer size of the port.</param>
    /// <param name="cancellationToken">The token, canceled when the page cancels the readable stream or closes the port.</param>
    /// <returns>The number of bytes read.</returns>
    public abstract Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);

    /// <summary>
    /// Writes bytes (<c>port.writable</c>): the task completes when the bytes are written or queued for transmission. Throw a
    /// <see cref="SerialException"/> for an error of the port.
    /// </summary>
    /// <param name="data">The bytes.</param>
    /// <param name="cancellationToken">The token, canceled when the page aborts the writable stream or closes the port.</param>
    public abstract Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken);

    /// <summary>Flushes the transmit buffers (the writable stream is closed). The default implementation completes.</summary>
    /// <param name="cancellationToken">The token.</param>
    public virtual Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Discards the receive buffers (the readable stream is canceled). The default implementation completes.</summary>
    public virtual Task DiscardReceiveBuffersAsync() => Task.CompletedTask;

    /// <summary>Discards the transmit buffers (the writable stream is aborted). The default implementation completes.</summary>
    public virtual Task DiscardTransmitBuffersAsync() => Task.CompletedTask;

    /// <summary>Asserts or deasserts the output signals that are present (<c>setSignals()</c>). The default implementation completes.</summary>
    /// <param name="signals">The signals to change: DTR, then RTS, then break.</param>
    /// <param name="cancellationToken">The token.</param>
    public virtual Task SetSignalsAsync(SerialOutputSignals signals, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Returns the input signals asserted by the device (<c>getSignals()</c>). The default implementation returns no signal.</summary>
    /// <param name="cancellationToken">The token.</param>
    public virtual Task<SerialInputSignals> GetSignalsAsync(CancellationToken cancellationToken)
        => Task.FromResult(new SerialInputSignals { DataCarrierDetect = false, ClearToSend = false, RingIndicator = false, DataSetReady = false });

    /// <summary>Closes the port and releases its resources (<c>close()</c>, <c>forget()</c>, or the end of the window or the worker).</summary>
    public abstract Task CloseAsync();
}

/// <summary>The errors of a serial port (https://serial.spec.whatwg.org/#readable-attribute).</summary>
public enum SerialError
{
    /// <summary>An error of the operating system: an <c>UnknownError</c>.</summary>
    Unknown,

    /// <summary>The port was disconnected: a <c>NetworkError</c>, and the stream cannot be read or written anymore.</summary>
    Disconnected,

    /// <summary>A buffer overrun: a <c>BufferOverrunError</c>.</summary>
    BufferOverrun,

    /// <summary>A break condition: a <c>BreakError</c>.</summary>
    Break,

    /// <summary>A framing error: a <c>FramingError</c>.</summary>
    Framing,

    /// <summary>A parity error: a <c>ParityError</c>.</summary>
    Parity,
}

/// <summary>An error of a serial port, thrown by a <see cref="SerialConnection"/>: its <see cref="Error"/> chooses the <c>DOMException</c> of the page.</summary>
public sealed class SerialException : Exception
{
    /// <summary>Creates an exception.</summary>
    public SerialException()
        : this(SerialError.Unknown)
    {
    }

    /// <summary>Creates an exception.</summary>
    /// <param name="message">The message.</param>
    public SerialException(string? message)
        : base(message)
    {
    }

    /// <summary>Creates an exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public SerialException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception.</summary>
    /// <param name="error">The error.</param>
    /// <param name="message">The message, or null.</param>
    public SerialException(SerialError error, string? message = null)
        : base(message ?? $"Serial port error: {error}")
    {
        Error = error;
    }

    /// <summary>Gets the error.</summary>
    public SerialError Error { get; }

    internal DomException ToDomException() => new(Message, Error switch
    {
        SerialError.Disconnected => "NetworkError",
        SerialError.BufferOverrun => "BufferOverrunError",
        SerialError.Break => "BreakError",
        SerialError.Framing => "FramingError",
        SerialError.Parity => "ParityError",
        _ => "UnknownError",
    });
}
