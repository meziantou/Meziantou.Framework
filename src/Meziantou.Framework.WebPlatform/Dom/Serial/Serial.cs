using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The parity mode of a serial port (https://serial.spec.whatwg.org/#dom-paritytype).</summary>
/// <seealso href="https://serial.spec.whatwg.org/#dom-paritytype"/>
[IdlInterface("ParityType", "https://serial.spec.whatwg.org/#dom-paritytype")]
public enum ParityType
{
    /// <summary>No parity bit is sent.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-paritytype-none"/>
    [IdlMember("none", "https://serial.spec.whatwg.org/#dom-paritytype-none")]
    None,

    /// <summary>The data word plus the parity bit have even parity.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-paritytype-even"/>
    [IdlMember("even", "https://serial.spec.whatwg.org/#dom-paritytype-even")]
    Even,

    /// <summary>The data word plus the parity bit have odd parity.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-paritytype-odd"/>
    [IdlMember("odd", "https://serial.spec.whatwg.org/#dom-paritytype-odd")]
    Odd,
}

/// <summary>The flow control mode of a serial port (https://serial.spec.whatwg.org/#dom-flowcontroltype).</summary>
/// <seealso href="https://serial.spec.whatwg.org/#dom-flowcontroltype"/>
[IdlInterface("FlowControlType", "https://serial.spec.whatwg.org/#dom-flowcontroltype")]
public enum FlowControlType
{
    /// <summary>No flow control.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-flowcontroltype-none"/>
    [IdlMember("none", "https://serial.spec.whatwg.org/#dom-flowcontroltype-none")]
    None,

    /// <summary>Hardware flow control with the RTS and CTS signals.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-flowcontroltype-hardware"/>
    [IdlMember("hardware", "https://serial.spec.whatwg.org/#dom-flowcontroltype-hardware")]
    Hardware,
}

/// <summary>A filter of the ports of <see cref="Serial.RequestPort"/> (https://serial.spec.whatwg.org/#serialportfilter-dictionary).</summary>
/// <seealso href="https://serial.spec.whatwg.org/#dom-serialportfilter"/>
[IdlInterface("SerialPortFilter", "https://serial.spec.whatwg.org/#dom-serialportfilter")]
public sealed class SerialPortFilter
{
    /// <summary>Gets the USB vendor ID, or null.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialportfilter-usbvendorid"/>
    [IdlMember("usbVendorId", "https://serial.spec.whatwg.org/#dom-serialportfilter-usbvendorid")]
    public ushort? UsbVendorId { get; init; }

    /// <summary>Gets the USB product ID, or null (it requires <see cref="UsbVendorId"/>).</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialportfilter-usbproductid"/>
    [IdlMember("usbProductId", "https://serial.spec.whatwg.org/#dom-serialportfilter-usbproductid")]
    public ushort? UsbProductId { get; init; }

    /// <summary>Gets the Bluetooth service class ID (a UUID, an alias or a name), or null.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialportfilter-bluetoothserviceclassid"/>
    [IdlMember("bluetoothServiceClassId", "https://serial.spec.whatwg.org/#dom-serialportfilter-bluetoothserviceclassid")]
    public BluetoothUuidName? BluetoothServiceClassId { get; init; }

    /// <summary>Returns whether a port matches the filter (https://serial.spec.whatwg.org/#dfn-matches-the-filter).</summary>
    /// <param name="port">The port.</param>
    [NotInIdl("The matching algorithm of the filters, for the port choosers of the hosts")]
    public bool Matches(SerialHostPort port)
    {
        ArgumentNullException.ThrowIfNull(port);
        if (BluetoothServiceClassId is { } serviceClassId)
            return port.BluetoothServiceClassId is { } portServiceClassId && BluetoothUuid.TryResolve(serviceClassId, BluetoothUuidKind.Service) == portServiceClassId;

        if (UsbVendorId is not { } vendorId)
            return true;

        if (port.UsbVendorId != vendorId)
            return false;

        return UsbProductId is not { } productId || port.UsbProductId == productId;
    }
}

/// <summary>The options of <see cref="Serial.RequestPort"/> (https://serial.spec.whatwg.org/#serialportrequestoptions-dictionary).</summary>
/// <seealso href="https://serial.spec.whatwg.org/#dom-serialportrequestoptions"/>
[IdlInterface("SerialPortRequestOptions", "https://serial.spec.whatwg.org/#dom-serialportrequestoptions")]
public sealed class SerialPortRequestOptions
{
    /// <summary>Gets the filters: a port matches one of them (every port when there is none).</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialportrequestoptions-filters"/>
    [IdlMember("filters", "https://serial.spec.whatwg.org/#dom-serialportrequestoptions-filters")]
    public IReadOnlyList<SerialPortFilter>? Filters { get; init; }

    /// <summary>Gets the custom service class IDs of the Bluetooth ports the page can use (the other ones are excluded, except the Serial Port Profile).</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialportrequestoptions-allowedbluetoothserviceclassids"/>
    [IdlMember("allowedBluetoothServiceClassIds", "https://serial.spec.whatwg.org/#dom-serialportrequestoptions-allowedbluetoothserviceclassids")]
    public IReadOnlyList<BluetoothUuidName>? AllowedBluetoothServiceClassIds { get; init; }
}

/// <summary>The information of a port (https://serial.spec.whatwg.org/#serialportinfo-dictionary).</summary>
/// <seealso href="https://serial.spec.whatwg.org/#dom-serialportinfo"/>
[IdlInterface("SerialPortInfo", "https://serial.spec.whatwg.org/#dom-serialportinfo")]
public sealed class SerialPortInfo
{
    /// <summary>Gets the vendor ID of the USB device of the port, or null.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialportinfo-usbvendorid"/>
    [IdlMember("usbVendorId", "https://serial.spec.whatwg.org/#dom-serialportinfo-usbvendorid")]
    public ushort? UsbVendorId { get; init; }

    /// <summary>Gets the product ID of the USB device of the port, or null.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialportinfo-usbproductid"/>
    [IdlMember("usbProductId", "https://serial.spec.whatwg.org/#dom-serialportinfo-usbproductid")]
    public ushort? UsbProductId { get; init; }

    /// <summary>Gets the service class UUID of the Bluetooth service of the port, or null.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialportinfo-bluetoothserviceclassid"/>
    [IdlMember("bluetoothServiceClassId", "https://serial.spec.whatwg.org/#dom-serialportinfo-bluetoothserviceclassid")]
    public BluetoothUuidName? BluetoothServiceClassId { get; init; }
}

/// <summary>The connection parameters of <see cref="SerialPort.Open"/> (https://serial.spec.whatwg.org/#serialoptions-dictionary).</summary>
/// <seealso href="https://serial.spec.whatwg.org/#dom-serialoptions"/>
[IdlInterface("SerialOptions", "https://serial.spec.whatwg.org/#dom-serialoptions")]
public sealed class SerialOptions
{
    /// <summary>Gets the baud rate (not 0).</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialoptions-baudrate"/>
    [IdlMember("baudRate", "https://serial.spec.whatwg.org/#dom-serialoptions-baudrate")]
    public required uint BaudRate { get; init; }

    /// <summary>Gets the number of data bits per frame: 7 or 8.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialoptions-databits"/>
    [IdlMember("dataBits", "https://serial.spec.whatwg.org/#dom-serialoptions-databits")]
    public byte DataBits { get; init; } = 8;

    /// <summary>Gets the number of stop bits at the end of a frame: 1 or 2.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialoptions-stopbits"/>
    [IdlMember("stopBits", "https://serial.spec.whatwg.org/#dom-serialoptions-stopbits")]
    public byte StopBits { get; init; } = 1;

    /// <summary>Gets the parity mode.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialoptions-parity"/>
    [IdlMember("parity", "https://serial.spec.whatwg.org/#dom-serialoptions-parity")]
    public ParityType Parity { get; init; } = ParityType.None;

    /// <summary>Gets the size of the read and write buffers (not 0, at most 16 MiB as in Chromium).</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialoptions-buffersize"/>
    [IdlMember("bufferSize", "https://serial.spec.whatwg.org/#dom-serialoptions-buffersize")]
    public uint BufferSize { get; init; } = 255;

    /// <summary>Gets the flow control mode.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialoptions-flowcontrol"/>
    [IdlMember("flowControl", "https://serial.spec.whatwg.org/#dom-serialoptions-flowcontrol")]
    public FlowControlType FlowControl { get; init; } = FlowControlType.None;
}

/// <summary>The output signals of <see cref="SerialPort.SetSignals"/> (https://serial.spec.whatwg.org/#serialoutputsignals-dictionary): the present members are changed.</summary>
/// <seealso href="https://serial.spec.whatwg.org/#dom-serialoutputsignals"/>
[IdlInterface("SerialOutputSignals", "https://serial.spec.whatwg.org/#dom-serialoutputsignals")]
public sealed class SerialOutputSignals
{
    /// <summary>Gets whether the Data Terminal Ready (DTR) signal is asserted, or null to keep it.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialoutputsignals-dataterminalready"/>
    [IdlMember("dataTerminalReady", "https://serial.spec.whatwg.org/#dom-serialoutputsignals-dataterminalready")]
    public bool? DataTerminalReady { get; init; }

    /// <summary>Gets whether the Request To Send (RTS) signal is asserted, or null to keep it.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialoutputsignals-requesttosend"/>
    [IdlMember("requestToSend", "https://serial.spec.whatwg.org/#dom-serialoutputsignals-requesttosend")]
    public bool? RequestToSend { get; init; }

    /// <summary>Gets whether the break signal is asserted, or null to keep it.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialoutputsignals-break"/>
    [IdlMember("break", "https://serial.spec.whatwg.org/#dom-serialoutputsignals-break")]
    public bool? Break { get; init; }
}

/// <summary>The input signals of <see cref="SerialPort.GetSignals"/> (https://serial.spec.whatwg.org/#serialinputsignals-dictionary).</summary>
/// <seealso href="https://serial.spec.whatwg.org/#dom-serialinputsignals"/>
[IdlInterface("SerialInputSignals", "https://serial.spec.whatwg.org/#dom-serialinputsignals")]
public sealed class SerialInputSignals
{
    /// <summary>Gets whether the Data Carrier Detect (DCD) signal is asserted.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialinputsignals-datacarrierdetect"/>
    [IdlMember("dataCarrierDetect", "https://serial.spec.whatwg.org/#dom-serialinputsignals-datacarrierdetect")]
    public required bool DataCarrierDetect { get; init; }

    /// <summary>Gets whether the Clear To Send (CTS) signal is asserted.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialinputsignals-cleartosend"/>
    [IdlMember("clearToSend", "https://serial.spec.whatwg.org/#dom-serialinputsignals-cleartosend")]
    public required bool ClearToSend { get; init; }

    /// <summary>Gets whether the Ring Indicator (RI) signal is asserted.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialinputsignals-ringindicator"/>
    [IdlMember("ringIndicator", "https://serial.spec.whatwg.org/#dom-serialinputsignals-ringindicator")]
    public required bool RingIndicator { get; init; }

    /// <summary>Gets whether the Data Set Ready (DSR) signal is asserted.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialinputsignals-datasetready"/>
    [IdlMember("dataSetReady", "https://serial.spec.whatwg.org/#dom-serialinputsignals-datasetready")]
    public required bool DataSetReady { get; init; }
}

/// <summary>
/// The Web Serial API of a window or a dedicated worker (https://serial.spec.whatwg.org/#serial-interface,
/// <c>navigator.serial</c>): the ports of the <see cref="SerialProvider"/> of the browsing context
/// (<see cref="BrowsingContextOptions.Serial"/>) that the origin is allowed to use. The <c>connect</c> and <c>disconnect</c>
/// events of the ports bubble to it.
/// </summary>
/// <seealso href="https://serial.spec.whatwg.org/#dom-serial"/>
[IdlInterface("Serial", "https://serial.spec.whatwg.org/#dom-serial")]
public sealed class Serial : EventTarget
{
    private readonly GlobalSerial _state;

    internal Serial(GlobalSerial state)
    {
        _state = state;
    }

    internal override GlobalScope? RelevantGlobal => _state.Global;

    /// <summary>Gets or sets the event handler of the <c>connect</c> events of the ports.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serial-onconnect"/>
    [IdlMember("onconnect", "https://serial.spec.whatwg.org/#dom-serial-onconnect")]
    public Action<Event>? OnConnect
    {
        get => GetEventHandler("connect");
        set => SetEventHandler("connect", value);
    }

    /// <summary>Gets or sets the event handler of the <c>disconnect</c> events of the ports.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serial-ondisconnect"/>
    [IdlMember("ondisconnect", "https://serial.spec.whatwg.org/#dom-serial-ondisconnect")]
    public Action<Event>? OnDisconnect
    {
        get => GetEventHandler("disconnect");
        set => SetEventHandler("disconnect", value);
    }

    /// <summary>
    /// Returns the available ports that the origin is allowed to use (https://serial.spec.whatwg.org/#getports-method). The task
    /// fails with a <c>SecurityError</c> <see cref="DomException"/> when the <c>serial</c> permissions policy feature is not
    /// allowed, or the top-level origin is opaque.
    /// </summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serial-getports"/>
    [IdlMember("getPorts", "https://serial.spec.whatwg.org/#dom-serial-getports")]
    public Task<IReadOnlyList<SerialPort>> GetPorts() => _state.GetPorts();

    /// <summary>
    /// Asks the user to choose a port (https://serial.spec.whatwg.org/#requestport-method) with
    /// <see cref="SerialProvider.RequestPort"/>: the origin is then allowed to use it. The window must have transient
    /// activation. The task fails with a <see cref="TypeErrorException"/> for an invalid filter, a <c>SecurityError</c>
    /// <see cref="DomException"/> without user activation or when the <c>serial</c> permissions policy feature is not allowed,
    /// and a <c>NotFoundError</c> when the user does not choose a port.
    /// </summary>
    /// <param name="options">The filters of the ports.</param>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serial-requestport"/>
    [IdlMember("requestPort", "https://serial.spec.whatwg.org/#dom-serial-requestport")]
    public Task<SerialPort> RequestPort(SerialPortRequestOptions? options = null) => _state.RequestPort(options ?? new SerialPortRequestOptions());
}

/// <summary>
/// A serial port of a window or a dedicated worker (https://serial.spec.whatwg.org/#serialport-interface): its information,
/// and the connection that <see cref="Open"/> opens with <see cref="SerialHostPort.OpenAsync"/>. The scripts read and
/// write the data with the <c>readable</c> (a readable byte stream of <c>Uint8Array</c> chunks) and <c>writable</c>
/// (<c>BufferSource</c> chunks) streams of the open port.
/// </summary>
/// <seealso href="https://serial.spec.whatwg.org/#dom-serialport"/>
[IdlInterface("SerialPort", "https://serial.spec.whatwg.org/#dom-serialport")]
public sealed class SerialPort : EventTarget
{
    // The largest buffer size (as in Chromium)
    private const uint MaxBufferSize = 16 * 1024 * 1024;

    private readonly GlobalSerial _state;
    private SerialPortState _portState = SerialPortState.Closed;
    private SerialConnection? _connection;
    private uint _bufferSize;
    private PlatformReadableStream? _readable;
    private PlatformWritableStream? _writable;
    private CancellationTokenSource? _readCancellation;
    private CancellationTokenSource? _writeCancellation;
    private bool _readFatal;
    private bool _writeFatal;
    private TaskCompletionSource? _pendingClose;

    internal SerialPort(GlobalSerial state, SerialHostPort port)
    {
        _state = state;
        HostPort = port;
        Connected = port.Provider is not null;
    }

    private enum SerialPortState
    {
        Closed,
        Opening,
        Opened,
        Closing,
        Forgetting,
        Forgotten,
    }

    /// <summary>Gets the port of the provider.</summary>
    [NotInIdl("The port of the host, which is not exposed to scripts")]
    public SerialHostPort HostPort { get; }

    /// <summary>Gets or sets the event handler of the <c>connect</c> event: the port became available.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialport-onconnect"/>
    [IdlMember("onconnect", "https://serial.spec.whatwg.org/#dom-serialport-onconnect")]
    public Action<Event>? OnConnect
    {
        get => GetEventHandler("connect");
        set => SetEventHandler("connect", value);
    }

    /// <summary>Gets or sets the event handler of the <c>disconnect</c> event: the port is no longer available.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialport-ondisconnect"/>
    [IdlMember("ondisconnect", "https://serial.spec.whatwg.org/#dom-serialport-ondisconnect")]
    public Action<Event>? OnDisconnect
    {
        get => GetEventHandler("disconnect");
        set => SetEventHandler("disconnect", value);
    }

    /// <summary>Gets whether the port is logically connected.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialport-connected"/>
    [IdlMember("connected", "https://serial.spec.whatwg.org/#dom-serialport-connected")]
    public bool Connected { get; private set; }

    /// <summary>Gets whether the port is open (<see cref="Open"/> completed, and <see cref="Close"/> was not called).</summary>
    [NotInIdl("The state of the port for the .NET code")]
    public bool IsOpen => _portState == SerialPortState.Opened;

    /// <summary>
    /// Gets the <c>ReadableStream</c> of the received bytes (https://serial.spec.whatwg.org/#readable-attribute), an object of
    /// the realm of the scripts, or null when the port is not open or the port was disconnected. An error of the port errors the
    /// stream: the next read creates a new stream (unless the port was disconnected).
    /// </summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialport-readable"/>
    [IdlMember("readable", "https://serial.spec.whatwg.org/#dom-serialport-readable")]
    public object? Readable => GetReadable();

    /// <summary>Gets the <c>WritableStream</c> of the sent bytes (https://serial.spec.whatwg.org/#writable-attribute), an object of the realm of the scripts, or null.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialport-writable"/>
    [IdlMember("writable", "https://serial.spec.whatwg.org/#dom-serialport-writable")]
    public object? Writable => GetWritable();

    private GlobalScope Global => _state.Global;

    internal override GlobalScope? RelevantGlobal => Global;

    /// <summary>Returns the information of the port (https://serial.spec.whatwg.org/#getinfo-method).</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialport-getinfo"/>
    [IdlMember("getInfo", "https://serial.spec.whatwg.org/#dom-serialport-getinfo")]
    public SerialPortInfo GetInfo() => new()
    {
        UsbVendorId = HostPort.UsbVendorId,
        UsbProductId = HostPort.UsbProductId,
        BluetoothServiceClassId = HostPort.BluetoothServiceClassId is { } uuid ? BluetoothUuidName.FromString(uuid) : (BluetoothUuidName?)null,
    };

    /// <summary>
    /// Opens the port (https://serial.spec.whatwg.org/#open-method) with <see cref="SerialHostPort.OpenAsync"/>. The task fails
    /// with an <c>InvalidStateError</c> <see cref="DomException"/> when the port is not closed, a
    /// <see cref="TypeErrorException"/> for invalid options, and a <c>NetworkError</c> when the port cannot be opened.
    /// </summary>
    /// <param name="options">The connection parameters.</param>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialport-open"/>
    [IdlMember("open", "https://serial.spec.whatwg.org/#dom-serialport-open")]
    public Task Open(SerialOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (_portState != SerialPortState.Closed)
            return Task.FromException(DomException.InvalidState("The port is already open"));

        if (options.BaudRate == 0)
            return Task.FromException(new TypeErrorException("Requested baud rate must be greater than zero."));

        if (options.DataBits is not (7 or 8))
            return Task.FromException(new TypeErrorException("Requested number of data bits must be 7 or 8."));

        if (options.StopBits is not (1 or 2))
            return Task.FromException(new TypeErrorException("Requested number of stop bits must be 1 or 2."));

        if (options.BufferSize == 0)
            return Task.FromException(new TypeErrorException("Requested buffer size must be greater than zero."));

        if (options.BufferSize > MaxBufferSize)
            return Task.FromException(new TypeErrorException($"Requested buffer size ({options.BufferSize} bytes) is greater than the maximum allowed ({MaxBufferSize} bytes)."));

        if (HostPort.Provider is null)
            return Task.FromException(new DomException("The port is not available", "NetworkError"));

        _portState = SerialPortState.Opening;
        var result = new TaskCompletionSource();
        var parameters = new SerialConnectionOptions(options.BaudRate, options.DataBits, options.StopBits, options.Parity, options.BufferSize, options.FlowControl);
        Task<SerialConnection> opening;
        try
        {
            opening = HostPort.OpenAsync(parameters, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            opening = Task.FromException<SerialConnection>(exception);
        }

        _ = opening.ContinueWith(
            task => Global.QueueGlobalTask(() =>
            {
                if (!task.IsCompletedSuccessfully)
                {
                    if (_portState == SerialPortState.Opening)
                    {
                        _portState = SerialPortState.Closed;
                    }

                    result.TrySetException(new DomException("Failed to open serial port.", "NetworkError"));
                    return;
                }

                // The port was forgotten during the opening
                if (_portState != SerialPortState.Opening)
                {
                    _ = task.Result.CloseAsync();
                    result.TrySetException(new DomException("The port was forgotten", "NetworkError"));
                    return;
                }

                _connection = task.Result;
                _bufferSize = options.BufferSize;
                _portState = SerialPortState.Opened;
                result.TrySetResult();
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return result.Task;
    }

    /// <summary>
    /// Changes the output signals of the port (https://serial.spec.whatwg.org/#setsignals-method). The task fails with an
    /// <c>InvalidStateError</c> <see cref="DomException"/> when the port is not open, a <see cref="TypeErrorException"/> when no
    /// signal is given, and a <c>NetworkError</c> when the signals cannot be changed.
    /// </summary>
    /// <param name="signals">The signals.</param>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialport-setsignals"/>
    [IdlMember("setSignals", "https://serial.spec.whatwg.org/#dom-serialport-setsignals")]
    public Task SetSignals(SerialOutputSignals? signals = null)
    {
        signals ??= new SerialOutputSignals();
        if (_portState != SerialPortState.Opened || _connection is not { } connection)
            return Task.FromException(DomException.InvalidState("The port is closed."));

        if (signals.DataTerminalReady is null && signals.RequestToSend is null && signals.Break is null)
            return Task.FromException(new TypeErrorException("Signals dictionary is empty."));

        return Run(() => GlobalSerial.AsResult(connection.SetSignalsAsync(signals, CancellationToken.None)), _ => (object?)null, "Failed to set control signals.");
    }

    /// <summary>
    /// Returns the input signals of the port (https://serial.spec.whatwg.org/#getsignals-method). The task fails with an
    /// <c>InvalidStateError</c> <see cref="DomException"/> when the port is not open, and a <c>NetworkError</c> when the
    /// signals cannot be read.
    /// </summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialport-getsignals"/>
    [IdlMember("getSignals", "https://serial.spec.whatwg.org/#dom-serialport-getsignals")]
    public Task<SerialInputSignals> GetSignals()
    {
        if (_portState != SerialPortState.Opened || _connection is not { } connection)
            return Task.FromException<SerialInputSignals>(DomException.InvalidState("The port is closed."));

        return Run(() => connection.GetSignalsAsync(CancellationToken.None), signals => signals, "Failed to get control signals.");
    }

    /// <summary>
    /// Closes the port (https://serial.spec.whatwg.org/#close-method): the readable stream is canceled and the writable stream
    /// aborted, then the connection is closed. The task fails with an <c>InvalidStateError</c> <see cref="DomException"/>
    /// when the port is not open, and with a <see cref="TypeErrorException"/> when a stream is locked (the port stays open).
    /// </summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialport-close"/>
    [IdlMember("close", "https://serial.spec.whatwg.org/#dom-serialport-close")]
    public Task Close()
    {
        if (_portState != SerialPortState.Opened)
            return Task.FromException(DomException.InvalidState("The port is already closed."));

        var cancelTask = _readable?.CancelStream(null) ?? Task.CompletedTask;
        var abortTask = _writable?.AbortStream(null) ?? Task.CompletedTask;
        var pendingClose = new TaskCompletionSource();
        if (_readable is null && _writable is null)
        {
            pendingClose.TrySetResult();
        }

        _pendingClose = pendingClose;
        _portState = SerialPortState.Closing;
        var result = new TaskCompletionSource();
        // https://webidl.spec.whatwg.org/#wait-for-all: the combined promise is rejected by the first rejection (a locked
        // stream), the pending close promise never completes then
        _ = WaitForAll(cancelTask, abortTask, pendingClose.Task).ContinueWith(
            combined =>
            {
                if (!combined.IsCompletedSuccessfully)
                {
                    Global.QueueGlobalTask(() =>
                    {
                        // As in Chromium, the port stays open when a stream cannot be canceled (it is locked)
                        _pendingClose = null;
                        if (_portState == SerialPortState.Closing)
                        {
                            _portState = SerialPortState.Opened;
                        }

                        result.TrySetException(combined.Exception?.InnerException ?? new DomException("Failed to close the port", "UnknownError"));
                    });
                    return;
                }

                _ = CloseConnection().ContinueWith(
                    _ => Global.QueueGlobalTask(() =>
                    {
                        _pendingClose = null;
                        result.TrySetResult();
                    }),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return result.Task;
    }

    /// <summary>
    /// Revokes the permission of the origin to use the port (https://serial.spec.whatwg.org/#forget-method): an open port is
    /// closed, and <see cref="Serial.GetPorts"/> does not return it anymore.
    /// </summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-serialport-forget"/>
    [IdlMember("forget", "https://serial.spec.whatwg.org/#dom-serialport-forget")]
    public Task Forget()
    {
        var result = new TaskCompletionSource();
        _portState = SerialPortState.Forgetting;
        _state.Forget(HostPort);
        ReleaseStreams(new DomException("The port was forgotten", "NetworkError"));
        _ = CloseConnection();
        _portState = SerialPortState.Forgotten;
        Global.QueueGlobalTask(() => result.TrySetResult());
        return result.Task;
    }

    // https://serial.spec.whatwg.org/#serialport-onconnect-attribute
    internal void OnConnected()
    {
        Connected = true;
        FireEvent(new Event("connect", new EventInit { Bubbles = true }));
    }

    // https://serial.spec.whatwg.org/#serialport-ondisconnect-attribute: the streams of an open port fail with a NetworkError
    internal void OnDisconnected()
    {
        Connected = false;
        if (_portState == SerialPortState.Opened)
        {
            _readFatal = true;
            _writeFatal = true;
            ReleaseStreams(new DomException("The device has been lost.", "NetworkError"));
        }

        FireEvent(new Event("disconnect", new EventInit { Bubbles = true }));
    }

    /// <summary>The window is unloaded or the worker is terminated: the connection is closed.</summary>
    internal void OnContextDestroyed()
    {
        ReleaseStreams(new DomException("The context was destroyed", "AbortError"));
        _ = CloseConnection();
        _portState = SerialPortState.Closed;
    }

    // https://serial.spec.whatwg.org/#dom-serialport: the parent of a port is navigator.serial
    internal override EventTarget? GetTheParent(Event @event) => _state.Serial;

    private PlatformReadableStream? GetReadable()
    {
        if (_readable is not null)
            return _readable;

        if (_portState != SerialPortState.Opened || _readFatal || _connection is not { } connection)
            return null;

        PlatformReadableStream? stream = null;
        stream = new PlatformReadableStream(() => Pull(stream!, connection), _ => CancelReadable(stream!, connection), isBytes: true);
        _readable = stream;
        return stream;
    }

    private PlatformWritableStream? GetWritable()
    {
        if (_writable is not null)
            return _writable;

        if (_portState != SerialPortState.Opened || _writeFatal || _connection is not { } connection)
            return null;

        PlatformWritableStream? stream = null;
        stream = new PlatformWritableStream(chunk => Write(stream!, connection, chunk), () => CloseWritable(stream!, connection), _ => AbortWritable(stream!, connection));
        _writable = stream;
        return stream;
    }

    // The pull algorithm of https://serial.spec.whatwg.org/#readable-attribute: the bytes are enqueued by a task
    private Task Pull(PlatformReadableStream stream, SerialConnection connection)
    {
        var buffer = new byte[_bufferSize];
        var cancellation = new CancellationTokenSource();
        _readCancellation = cancellation;
        var result = new TaskCompletionSource();
        Task<int> read;
        try
        {
            read = connection.ReadAsync(buffer, cancellation.Token);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            read = Task.FromException<int>(exception);
        }

        _ = read.ContinueWith(
            task => Global.QueueGlobalTask(() =>
            {
                result.TrySetResult();
                if (cancellation.IsCancellationRequested || !ReferenceEquals(_readable, stream))
                    return;

                if (task.IsCompletedSuccessfully && task.Result > 0)
                {
                    stream.Enqueue(new JsBufferSource(JsBufferKind.Uint8Array, buffer.AsMemory(0, Math.Min(task.Result, buffer.Length)).ToArray()));
                    return;
                }

                var error = task.IsCompletedSuccessfully ? new SerialException(SerialError.Disconnected, "The device has been lost.") : task.Exception?.InnerException as SerialException ?? new SerialException(SerialError.Unknown, "An unknown system error has occurred.");
                if (error.Error == SerialError.Disconnected)
                {
                    _readFatal = true;
                }

                stream.Error(error.ToDomException());
                HandleClosingReadable();
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return result.Task;
    }

    // The cancel algorithm: the receive buffers are discarded
    private Task CancelReadable(PlatformReadableStream stream, SerialConnection connection)
    {
        _readCancellation?.Cancel();
        var result = new TaskCompletionSource();
        _ = SafeRun(connection.DiscardReceiveBuffersAsync).ContinueWith(
            _ => Global.QueueGlobalTask(() =>
            {
                if (ReferenceEquals(_readable, stream))
                {
                    HandleClosingReadable();
                }

                result.TrySetResult();
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return result.Task;
    }

    // The write algorithm of https://serial.spec.whatwg.org/#writable-attribute: a BufferSource is written
    private Task Write(PlatformWritableStream stream, SerialConnection connection, object? chunk)
    {
        var bytes = ToBytes(chunk) ?? throw new TypeErrorException("The chunk is not a BufferSource");
        var cancellation = new CancellationTokenSource();
        _writeCancellation = cancellation;
        var result = new TaskCompletionSource();
        Task write;
        try
        {
            write = connection.WriteAsync(bytes, cancellation.Token);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            write = Task.FromException(exception);
        }

        _ = write.ContinueWith(
            task => Global.QueueGlobalTask(() =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    result.TrySetResult();
                    return;
                }

                if (cancellation.IsCancellationRequested)
                {
                    result.TrySetException(new DomException("The write was aborted", "AbortError"));
                    return;
                }

                var error = task.Exception?.InnerException as SerialException ?? new SerialException(SerialError.Unknown, "An unknown system error has occurred.");
                if (error.Error == SerialError.Disconnected)
                {
                    _writeFatal = true;
                    result.TrySetException(error.ToDomException());
                    if (ReferenceEquals(_writable, stream))
                    {
                        HandleClosingWritable();
                    }

                    return;
                }

                result.TrySetException(new DomException(error.Message, "UnknownError"));
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return result.Task;
    }

    // The close algorithm: the transmit buffers are flushed
    private Task CloseWritable(PlatformWritableStream stream, SerialConnection connection) => CompleteWritable(stream, SafeRun(() => connection.FlushAsync(CancellationToken.None)));

    // The abort algorithm: the pending write returns, and the transmit buffers are discarded
    private Task AbortWritable(PlatformWritableStream stream, SerialConnection connection)
    {
        _writeCancellation?.Cancel();
        return CompleteWritable(stream, SafeRun(connection.DiscardTransmitBuffersAsync));
    }

    private Task CompleteWritable(PlatformWritableStream stream, Task operation)
    {
        var result = new TaskCompletionSource();
        _ = operation.ContinueWith(
            _ => Global.QueueGlobalTask(() =>
            {
                if (ReferenceEquals(_writable, stream))
                {
                    HandleClosingWritable();
                }

                result.TrySetResult();
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return result.Task;
    }

    // https://serial.spec.whatwg.org/#dfn-handle-closing-the-readable-stream
    private void HandleClosingReadable()
    {
        _readable = null;
        if (_writable is null)
        {
            _pendingClose?.TrySetResult();
        }
    }

    // https://serial.spec.whatwg.org/#dfn-handle-closing-the-writable-stream
    private void HandleClosingWritable()
    {
        _writable = null;
        if (_readable is null)
        {
            _pendingClose?.TrySetResult();
        }
    }

    // The streams fail (the port was disconnected or forgotten)
    private void ReleaseStreams(DomException error)
    {
        _readCancellation?.Cancel();
        _writeCancellation?.Cancel();
        _readable?.Error(error);
        _writable?.Error(error);
        _readable = null;
        _writable = null;
        _pendingClose?.TrySetResult();
    }

    private Task CloseConnection()
    {
        var connection = _connection;
        _connection = null;
        _readFatal = false;
        _writeFatal = false;
        if (_portState is SerialPortState.Closing or SerialPortState.Opened)
        {
            _portState = SerialPortState.Closed;
        }

        return connection is null ? Task.CompletedTask : SafeRun(connection.CloseAsync);
    }

    private Task<TResult> Run<THost, TResult>(Func<Task<THost>> start, Func<THost, TResult> complete, string errorMessage)
    {
        var result = new TaskCompletionSource<TResult>();
        Task<THost> task;
        try
        {
            task = start();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            task = Task.FromException<THost>(exception);
        }

        _ = task.ContinueWith(
            completed => Global.QueueGlobalTask(() =>
            {
                if (completed.IsCompletedSuccessfully)
                {
                    result.TrySetResult(complete(completed.Result));
                }
                else
                {
                    result.TrySetException(new DomException(errorMessage, "NetworkError"));
                }
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return result.Task;
    }

    private static Task WaitForAll(params Task[] tasks)
    {
        var result = new TaskCompletionSource();
        var remaining = tasks.Length;
        foreach (var task in tasks)
        {
            _ = task.ContinueWith(
                completed =>
                {
                    if (!completed.IsCompletedSuccessfully)
                    {
                        result.TrySetException(completed.Exception?.InnerException ?? new OperationCanceledException());
                    }
                    else if (Interlocked.Decrement(ref remaining) == 0)
                    {
                        result.TrySetResult();
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return result.Task;
    }

    // An operation of the host, whose failure is ignored (the cleanup steps of the streams and the port)
    private static Task SafeRun(Func<Task> operation)
    {
        try
        {
            return operation().ContinueWith(static _ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Task.CompletedTask;
        }
    }

    // A chunk of the writable stream: a BufferSource of the scripts, or bytes of .NET
    private byte[]? ToBytes(object? chunk) => chunk switch
    {
        byte[] bytes => bytes.ToArray(),
        JsBufferSource buffer => buffer.ToArray(),
        ReadOnlyMemory<byte> memory => memory.ToArray(),
        _ => (Global.ScriptEngine?.GetScriptValueOperations(Global) ?? DotNetScriptValueOperations.Instance).GetBufferSourceCopy(chunk),
    };
}

/// <summary>
/// The serial state of a window or a dedicated worker: its <see cref="Serial"/> object, and the <see cref="SerialPort"/> object
/// of each <see cref="SerialHostPort"/> it can use. The connections and the disconnections of the provider run in tasks.
/// </summary>
internal sealed class GlobalSerial
{
    // https://serial.spec.whatwg.org/#dfn-serial-port-profile-service-class-id
    private const string SerialPortProfileServiceClassId = "00001101-0000-1000-8000-00805f9b34fb";

    private readonly Dictionary<SerialHostPort, SerialPort> _ports = [];

    public GlobalSerial(GlobalScope global)
    {
        Global = global;
        Serial = new Serial(this);
        Provider?.Register(this);
    }

    public GlobalScope Global { get; }

    public Serial Serial { get; }

    private SerialProvider? Provider => Global.Options.Serial?.Provider;

    private string Origin => Global.OriginRecord.Serialize();

    /// <summary>The documents of the window, or the documents that own the worker.</summary>
    private IEnumerable<Document> Documents => PressureObserver.GetOwningDocumentSet(Global);

    /// <summary>A task without result of the host, as a task with a result that completes synchronously.</summary>
    public static Task<bool> AsResult(Task task) => task.ContinueWith(
        static completed =>
        {
            completed.GetAwaiter().GetResult();
            return true;
        },
        CancellationToken.None,
        TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);

    // https://serial.spec.whatwg.org/#getports-method
    public Task<IReadOnlyList<SerialPort>> GetPorts()
    {
        if (GetSecurityError("getPorts") is { } error)
            return Task.FromException<IReadOnlyList<SerialPort>>(error);

        var result = new TaskCompletionSource<IReadOnlyList<SerialPort>>();
        Global.QueueGlobalTask(() =>
        {
            var ports = new List<SerialPort>();
            if (Provider is { } provider)
            {
                foreach (var port in provider.Ports)
                {
                    if (provider.IsAllowed(port, Origin))
                    {
                        ports.Add(GetOrCreate(port));
                    }
                }
            }

            result.TrySetResult(ports);
        });
        return result.Task;
    }

    // https://serial.spec.whatwg.org/#requestport-method
    public Task<SerialPort> RequestPort(SerialPortRequestOptions options)
    {
        if (GetSecurityError("requestPort") is { } error)
            return Task.FromException<SerialPort>(error);

        if (Global is not Window { HasTransientActivation: true } window)
            return Task.FromException<SerialPort>(new DomException("Failed to execute 'requestPort' on 'Serial': Must be handling a user gesture to show a permission request.", "SecurityError"));

        var filters = options.Filters ?? [];
        foreach (var filter in filters)
        {
            if (filter.BluetoothServiceClassId is not null)
            {
                if (filter.UsbVendorId is not null || filter.UsbProductId is not null)
                    return Task.FromException<SerialPort>(new TypeErrorException("Failed to execute 'requestPort' on 'Serial': A filter cannot specify both bluetoothServiceClassId and usbVendorId or usbProductId."));

                if (BluetoothUuid.TryResolve(filter.BluetoothServiceClassId.Value, BluetoothUuidKind.Service) is null)
                    return Task.FromException<SerialPort>(new TypeErrorException("Failed to execute 'requestPort' on 'Serial': " + BluetoothUuid.GetInvalidNameMessage(filter.BluetoothServiceClassId.Value, BluetoothUuidKind.Service)));
            }
            else if (filter.UsbVendorId is null)
            {
                return Task.FromException<SerialPort>(new TypeErrorException("Failed to execute 'requestPort' on 'Serial': A filter containing a usbProductId must also specify a usbVendorId."));
            }
        }

        var allowedServiceClassIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in options.AllowedBluetoothServiceClassIds ?? [])
        {
            var uuid = BluetoothUuid.TryResolve(name, BluetoothUuidKind.Service);
            if (uuid is null)
                return Task.FromException<SerialPort>(new TypeErrorException("Failed to execute 'requestPort' on 'Serial': " + BluetoothUuid.GetInvalidNameMessage(name, BluetoothUuidKind.Service)));

            allowedServiceClassIds.Add(uuid);
        }

        var result = new TaskCompletionSource<SerialPort>();
        if (Provider is not { } provider)
        {
            Global.QueueGlobalTask(() => result.TrySetException(DomException.NotFound("No port selected by the user.")));
            return result.Task;
        }

        // In parallel: the available ports (the Bluetooth ports of the Serial Port Profile or of an allowed custom service
        // class) that match the filters are offered to the user
        var candidates = provider.Ports
            .Where(port => port.BluetoothServiceClassId is not { } serviceClassId || (!IsBlockedServiceClass(serviceClassId) && (serviceClassId == SerialPortProfileServiceClassId || allowedServiceClassIds.Contains(serviceClassId))))
            .Where(port => filters.Count == 0 || filters.Any(filter => filter.Matches(port)))
            .ToArray();
        Task<SerialHostPort?> selection;
        try
        {
            selection = provider.RequestPort(new SerialPortRequest(CallbackContext.For(window), filters, candidates));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            selection = Task.FromException<SerialHostPort?>(exception);
        }

        _ = selection.ContinueWith(
            task => Global.QueueGlobalTask(() =>
            {
                var port = task.IsCompletedSuccessfully ? task.Result : null;
                if (port is null || port.Provider != provider)
                {
                    result.TrySetException(DomException.NotFound("No port selected by the user."));
                    return;
                }

                provider.Allow(port, Origin);
                result.TrySetResult(GetOrCreate(port));
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return result.Task;
    }

    /// <summary>https://serial.spec.whatwg.org/#forget-method: the origin is not allowed to use the port anymore.</summary>
    public void Forget(SerialHostPort port) => Provider?.Revoke(port, Origin);

    /// <summary>A port became available. Called from any thread.</summary>
    public void OnConnected(SerialHostPort port) => Global.QueueGlobalTask(() =>
    {
        if (Provider is not { } provider || !provider.IsAllowed(port, Origin) || GetSecurityError("connect") is not null)
            return;

        GetOrCreate(port).OnConnected();
    });

    /// <summary>A port is no longer available. Called from any thread.</summary>
    public void OnDisconnected(SerialHostPort port) => Global.QueueGlobalTask(() =>
    {
        if (_ports.TryGetValue(port, out var portObject))
        {
            portObject.OnDisconnected();
        }
    });

    /// <summary>The window or the worker is destroyed: the open ports are closed.</summary>
    public void OnDestroyed()
    {
        foreach (var port in _ports.Values)
        {
            port.OnContextDestroyed();
        }
    }

    // https://serial.spec.whatwg.org/#dfn-blocked-bluetooth-service-class-uuid: the service classes of the Bluetooth SIG base
    // UUID, except the Serial Port Profile (the blocklist of the specification has no custom service class)
    private static bool IsBlockedServiceClass(string uuid)
        => uuid != SerialPortProfileServiceClassId && uuid.EndsWith("-0000-1000-8000-00805f9b34fb", StringComparison.Ordinal);

    private SerialPort GetOrCreate(SerialHostPort port)
    {
        if (!_ports.TryGetValue(port, out var portObject))
        {
            portObject = new SerialPort(this, port);
            _ports.Add(port, portObject);
        }

        return portObject;
    }

    /// <summary>The serial permissions policy feature (default allowlist 'self'), checked on the documents that own a worker.</summary>
    private DomException? GetSecurityError(string operation)
    {
        foreach (var document in Documents)
        {
            if (!PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.Serial))
                return new DomException($"Failed to execute '{operation}' on 'Serial': Access to the feature \"serial\" is disallowed by permissions policy.", "SecurityError");
        }

        return null;
    }
}

// https://serial.spec.whatwg.org/#extensions-to-the-navigator-interface
public sealed partial class Navigator
{
    /// <summary>Gets the Web Serial API of the window.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-navigator-serial"/>
    [IdlMember("serial", "https://serial.spec.whatwg.org/#dom-navigator-serial")]
    public Serial Serial => _window.GetSerialState().Serial;
}

// https://serial.spec.whatwg.org/#extensions-to-the-workernavigator-interface
public sealed partial class WorkerNavigator
{
    /// <summary>Gets the Web Serial API of the dedicated worker.</summary>
    /// <seealso href="https://serial.spec.whatwg.org/#dom-workernavigator-serial"/>
    [IdlMember("serial", "https://serial.spec.whatwg.org/#dom-workernavigator-serial")]
    public Serial Serial => _global.GetSerialState().Serial;
}

public abstract partial class GlobalScope
{
    private GlobalSerial? _serialState;

    /// <summary>The serial state of the global object, created by the first access to <c>navigator.serial</c>.</summary>
    internal GlobalSerial GetSerialState() => _serialState ??= new GlobalSerial(this);

    /// <summary>The window is unloaded or the worker is terminated: its serial ports are closed.</summary>
    internal void CloseSerialPorts() => _serialState?.OnDestroyed();
}
