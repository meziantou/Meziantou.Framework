using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The setup packet of a control transfer (https://usb.spec.whatwg.org/#dictdef-usbcontroltransferparameters).</summary>
/// <seealso href="https://usb.spec.whatwg.org/#dictdef-usbcontroltransferparameters"/>
[IdlInterface("USBControlTransferParameters", "https://usb.spec.whatwg.org/#dictdef-usbcontroltransferparameters")]
public sealed class UsbControlTransferParameters
{
    /// <summary>Gets the type of the request.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbcontroltransferparameters-requesttype"/>
    [IdlMember("requestType", "https://usb.spec.whatwg.org/#dom-usbcontroltransferparameters-requesttype")]
    public required UsbRequestType RequestType { get; init; }

    /// <summary>Gets the recipient.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbcontroltransferparameters-recipient"/>
    [IdlMember("recipient", "https://usb.spec.whatwg.org/#dom-usbcontroltransferparameters-recipient")]
    public required UsbRecipient Recipient { get; init; }

    /// <summary>Gets the request (bRequest).</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbcontroltransferparameters-request"/>
    [IdlMember("request", "https://usb.spec.whatwg.org/#dom-usbcontroltransferparameters-request")]
    public required byte Request { get; init; }

    /// <summary>Gets the value (wValue).</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbcontroltransferparameters-value"/>
    [IdlMember("value", "https://usb.spec.whatwg.org/#dom-usbcontroltransferparameters-value")]
    public required ushort Value { get; init; }

    /// <summary>Gets the index (wIndex).</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbcontroltransferparameters-index"/>
    [IdlMember("index", "https://usb.spec.whatwg.org/#dom-usbcontroltransferparameters-index")]
    public required ushort Index { get; init; }

    internal UsbControlTransferSetup ToSetup() => new(RequestType, Recipient, Request, Value, Index);
}

/// <summary>
/// A USB device of a window or a worker (https://usb.spec.whatwg.org/#usbdevice): the descriptors of a
/// <see cref="UsbHostDevice"/>, and the operations of the page, which run on the <see cref="UsbDeviceSession"/> that
/// <see cref="Open"/> opens. As in Chromium, an operation fails with an <c>InvalidStateError</c> while the state of the
/// device (<c>open()</c>, <c>close()</c>, <c>selectConfiguration()</c>, <c>reset()</c>) or of the interface
/// (<c>claimInterface()</c>, <c>releaseInterface()</c>, <c>selectAlternateInterface()</c>) is changing.
/// </summary>
/// <seealso href="https://usb.spec.whatwg.org/#usbdevice"/>
[IdlInterface("USBDevice", "https://usb.spec.whatwg.org/#usbdevice")]
public sealed class UsbDevice
{
    // The maximum length of a transfer (kUsbTransferLengthLimit of Chromium): longer transfers fail with a DataError
    private const int TransferLengthLimit = 32 * 1024 * 1024;

    private readonly GlobalUsb _state;
    private readonly List<PendingOperation> _pendingOperations = [];
    private UsbDeviceSession? _session;
    private bool _connected = true;
    private bool _deviceStateChangeInProgress;
    private byte _configurationValue;
    private byte[] _selectedAlternateSetting = [];
    private bool[] _claimedInterface = [];
    private bool[] _interfaceStateChangeInProgress = [];

    internal UsbDevice(GlobalUsb state, UsbHostDevice device)
    {
        _state = state;
        HostDevice = device;
        var descriptor = device.Descriptor;
        Configurations = [.. descriptor.Configurations.Select(configuration => new UsbConfiguration(this, configuration))];
        SetConfigurationValue(device.ActiveConfigurationValue);
    }

    /// <summary>Gets the device of the provider.</summary>
    [NotInIdl("The device of the host, which is not exposed to scripts")]
    public UsbHostDevice HostDevice { get; }

    private UsbDeviceDescriptor Descriptor => HostDevice.Descriptor;

    private GlobalScope Global => _state.Global;

    /// <summary>Gets the major version of the USB protocol.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-usbversionmajor"/>
    [IdlMember("usbVersionMajor", "https://usb.spec.whatwg.org/#dom-usbdevice-usbversionmajor")]
    public byte UsbVersionMajor => Descriptor.UsbVersionMajor;

    /// <summary>Gets the minor version of the USB protocol.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-usbversionminor"/>
    [IdlMember("usbVersionMinor", "https://usb.spec.whatwg.org/#dom-usbdevice-usbversionminor")]
    public byte UsbVersionMinor => Descriptor.UsbVersionMinor;

    /// <summary>Gets the subminor version of the USB protocol.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-usbversionsubminor"/>
    [IdlMember("usbVersionSubminor", "https://usb.spec.whatwg.org/#dom-usbdevice-usbversionsubminor")]
    public byte UsbVersionSubminor => Descriptor.UsbVersionSubminor;

    /// <summary>Gets the class of the device.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-deviceclass"/>
    [IdlMember("deviceClass", "https://usb.spec.whatwg.org/#dom-usbdevice-deviceclass")]
    public byte DeviceClass => Descriptor.DeviceClass;

    /// <summary>Gets the subclass of the device.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-devicesubclass"/>
    [IdlMember("deviceSubclass", "https://usb.spec.whatwg.org/#dom-usbdevice-devicesubclass")]
    public byte DeviceSubclass => Descriptor.DeviceSubclass;

    /// <summary>Gets the protocol of the device.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-deviceprotocol"/>
    [IdlMember("deviceProtocol", "https://usb.spec.whatwg.org/#dom-usbdevice-deviceprotocol")]
    public byte DeviceProtocol => Descriptor.DeviceProtocol;

    /// <summary>Gets the vendor ID.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-vendorid"/>
    [IdlMember("vendorId", "https://usb.spec.whatwg.org/#dom-usbdevice-vendorid")]
    public ushort VendorId => Descriptor.VendorId;

    /// <summary>Gets the product ID.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-productid"/>
    [IdlMember("productId", "https://usb.spec.whatwg.org/#dom-usbdevice-productid")]
    public ushort ProductId => Descriptor.ProductId;

    /// <summary>Gets the major version of the device.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-deviceversionmajor"/>
    [IdlMember("deviceVersionMajor", "https://usb.spec.whatwg.org/#dom-usbdevice-deviceversionmajor")]
    public byte DeviceVersionMajor => Descriptor.DeviceVersionMajor;

    /// <summary>Gets the minor version of the device.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-deviceversionminor"/>
    [IdlMember("deviceVersionMinor", "https://usb.spec.whatwg.org/#dom-usbdevice-deviceversionminor")]
    public byte DeviceVersionMinor => Descriptor.DeviceVersionMinor;

    /// <summary>Gets the subminor version of the device.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-deviceversionsubminor"/>
    [IdlMember("deviceVersionSubminor", "https://usb.spec.whatwg.org/#dom-usbdevice-deviceversionsubminor")]
    public byte DeviceVersionSubminor => Descriptor.DeviceVersionSubminor;

    /// <summary>Gets the name of the manufacturer, or null.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-manufacturername"/>
    [IdlMember("manufacturerName", "https://usb.spec.whatwg.org/#dom-usbdevice-manufacturername")]
    public string? ManufacturerName => Descriptor.ManufacturerName;

    /// <summary>Gets the name of the product, or null.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-productname"/>
    [IdlMember("productName", "https://usb.spec.whatwg.org/#dom-usbdevice-productname")]
    public string? ProductName => Descriptor.ProductName;

    /// <summary>Gets the serial number, or null.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-serialnumber"/>
    [IdlMember("serialNumber", "https://usb.spec.whatwg.org/#dom-usbdevice-serialnumber")]
    public string? SerialNumber => Descriptor.SerialNumber;

    /// <summary>Gets the current configuration, or null when the device is not configured.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-configuration"/>
    [IdlMember("configuration", "https://usb.spec.whatwg.org/#dom-usbdevice-configuration")]
    public UsbConfiguration? Configuration => FindConfiguration(_configurationValue);

    /// <summary>Gets the configurations of the device.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-configurations"/>
    [IdlMember("configurations", "https://usb.spec.whatwg.org/#dom-usbdevice-configurations")]
    public IReadOnlyList<UsbConfiguration> Configurations { get; }

    /// <summary>Gets whether the window or the worker opened the device.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-opened"/>
    [IdlMember("opened", "https://usb.spec.whatwg.org/#dom-usbdevice-opened")]
    public bool Opened => _session is not null;

    /// <summary>Opens a session with the device (https://usb.spec.whatwg.org/#dom-usbdevice-open).</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-open"/>
    [IdlMember("open", "https://usb.spec.whatwg.org/#dom-usbdevice-open")]
    public Task Open()
    {
        if (GetDeviceError(checkInterfaces: true) is { } error)
            return Task.FromException(error);

        if (Opened)
            return Task.CompletedTask;

        _deviceStateChangeInProgress = true;
        return Start(
            interfaceNumber: null,
            HostDevice.OpenAsync,
            session =>
            {
                _session = session;
                return true;
            },
            () => _deviceStateChangeInProgress = false);
    }

    /// <summary>
    /// Ends the session (https://usb.spec.whatwg.org/#dom-usbdevice-close): the claimed interfaces are released. As in
    /// Chromium, the operations already sent to the device complete before (the specification aborts them).
    /// </summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-close"/>
    [IdlMember("close", "https://usb.spec.whatwg.org/#dom-usbdevice-close")]
    public Task Close()
    {
        if (GetDeviceError(checkInterfaces: true) is { } error)
            return Task.FromException(error);

        if (_session is not { } session)
            return Task.CompletedTask;

        _deviceStateChangeInProgress = true;
        return Start(
            interfaceNumber: null,
            async _ =>
            {
                // The session ends even when the device reports an error
                try
                {
                    await session.CloseAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                }

                return true;
            },
            _ =>
            {
                _session = null;
                Array.Fill(_claimedInterface, value: false);
                Array.Fill(_selectedAlternateSetting, value: (byte)0);
                return true;
            },
            () => _deviceStateChangeInProgress = false);
    }

    /// <summary>Revokes the permission of the origin to use the device (https://usb.spec.whatwg.org/#dom-usbdevice-forget).</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-forget"/>
    [IdlMember("forget", "https://usb.spec.whatwg.org/#dom-usbdevice-forget")]
    public Task Forget()
    {
        _state.Forget(HostDevice);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Selects a configuration (https://usb.spec.whatwg.org/#dom-usbdevice-selectconfiguration): the transfers of the
    /// interfaces of the current configuration are aborted, and the interfaces are not claimed anymore.
    /// </summary>
    /// <param name="configurationValue">The value of a configuration of <see cref="Configurations"/>.</param>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-selectconfiguration"/>
    [IdlMember("selectConfiguration", "https://usb.spec.whatwg.org/#dom-usbdevice-selectconfiguration")]
    public Task SelectConfiguration(byte configurationValue)
    {
        if (GetDeviceError(checkInterfaces: true) is { } error)
            return Task.FromException(error);

        if (_session is not { } session)
            return Task.FromException(DomException.InvalidState("The device is not opened"));

        if (FindConfiguration(configurationValue) is null)
            return Task.FromException(DomException.NotFound($"The device has no configuration {configurationValue}"));

        AbortOperations(static operation => operation.InterfaceNumber is not null, static name => new DomException($"The {name} operation was aborted: the configuration of the device changed", "AbortError"));
        _deviceStateChangeInProgress = true;
        return Start(
            interfaceNumber: null,
            cancellationToken => Complete(session.SelectConfigurationAsync(configurationValue, cancellationToken)),
            _ =>
            {
                SetConfigurationValue(configurationValue);
                return true;
            },
            () => _deviceStateChangeInProgress = false);
    }

    /// <summary>Claims an interface of the current configuration (https://usb.spec.whatwg.org/#dom-usbdevice-claiminterface).</summary>
    /// <param name="interfaceNumber">The number of the interface.</param>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-claiminterface"/>
    [IdlMember("claimInterface", "https://usb.spec.whatwg.org/#dom-usbdevice-claiminterface")]
    public Task ClaimInterface(byte interfaceNumber)
    {
        if (GetInterfaceError(interfaceNumber, out var session, out var configuration, out var index) is { } error)
            return Task.FromException(error);

        if (_claimedInterface[index])
            return Task.CompletedTask;

        // The usb-unrestricted feature, which allows the protected classes, is only enabled for the Isolated Web Apps
        if (configuration.Interfaces[index].IsProtectedClass)
            return Task.FromException(new DomException($"The interface {interfaceNumber} implements a protected class", "SecurityError"));

        _interfaceStateChangeInProgress[index] = true;
        return Start(
            interfaceNumber: null,
            cancellationToken => Complete(session.ClaimInterfaceAsync(interfaceNumber, cancellationToken)),
            _ =>
            {
                _claimedInterface[index] = true;
                return true;
            },
            () => EndInterfaceStateChange(configuration, index));
    }

    /// <summary>Releases an interface (https://usb.spec.whatwg.org/#dom-usbdevice-releaseinterface).</summary>
    /// <param name="interfaceNumber">The number of the interface.</param>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-releaseinterface"/>
    [IdlMember("releaseInterface", "https://usb.spec.whatwg.org/#dom-usbdevice-releaseinterface")]
    public Task ReleaseInterface(byte interfaceNumber)
    {
        if (GetInterfaceError(interfaceNumber, out var session, out var configuration, out var index) is { } error)
            return Task.FromException(error);

        if (!_claimedInterface[index])
            return Task.CompletedTask;

        _interfaceStateChangeInProgress[index] = true;
        return Start(
            interfaceNumber: null,
            cancellationToken => Complete(session.ReleaseInterfaceAsync(interfaceNumber, cancellationToken)),
            _ =>
            {
                if (ReferenceEquals(configuration, Configuration))
                {
                    _selectedAlternateSetting[index] = 0;
                    _claimedInterface[index] = false;
                }

                return true;
            },
            () => EndInterfaceStateChange(configuration, index));
    }

    /// <summary>
    /// Selects an alternate setting of a claimed interface (https://usb.spec.whatwg.org/#dom-usbdevice-selectalternateinterface):
    /// the transfers of the interface are aborted.
    /// </summary>
    /// <param name="interfaceNumber">The number of the interface.</param>
    /// <param name="alternateSetting">The alternate setting.</param>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-selectalternateinterface"/>
    [IdlMember("selectAlternateInterface", "https://usb.spec.whatwg.org/#dom-usbdevice-selectalternateinterface")]
    public Task SelectAlternateInterface(byte interfaceNumber, byte alternateSetting)
    {
        if (GetInterfaceError(interfaceNumber, out var session, out var configuration, out var index) is { } error)
            return Task.FromException(error);

        if (!_claimedInterface[index])
            return Task.FromException(DomException.InvalidState($"The interface {interfaceNumber} is not claimed"));

        if (configuration.Interfaces[index].FindAlternate(alternateSetting) is null)
            return Task.FromException(DomException.NotFound($"The interface {interfaceNumber} has no alternate setting {alternateSetting}"));

        AbortOperations(operation => operation.InterfaceNumber == interfaceNumber, static name => new DomException($"The {name} operation was aborted: the alternate setting of the interface changed", "AbortError"));
        _interfaceStateChangeInProgress[index] = true;
        return Start(
            interfaceNumber: null,
            cancellationToken => Complete(session.SelectAlternateInterfaceAsync(interfaceNumber, alternateSetting, cancellationToken)),
            _ =>
            {
                if (ReferenceEquals(configuration, Configuration))
                {
                    _selectedAlternateSetting[index] = alternateSetting;
                }

                return true;
            },
            () => EndInterfaceStateChange(configuration, index));
    }

    /// <summary>
    /// Issues a control transfer from the device (https://usb.spec.whatwg.org/#dom-usbdevice-controltransferin). The device
    /// does not need to be configured for the requests to the device. The task fails with a <c>SecurityError</c>
    /// <see cref="DomException"/> for a request to a protected interface, and with the error of the device.
    /// </summary>
    /// <param name="setup">The setup packet.</param>
    /// <param name="length">The number of bytes to receive.</param>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-controltransferin"/>
    [IdlMember("controlTransferIn", "https://usb.spec.whatwg.org/#dom-usbdevice-controltransferin")]
    public Task<UsbInTransferResult> ControlTransferIn(UsbControlTransferParameters setup, ushort length)
    {
        ArgumentNullException.ThrowIfNull(setup);
        if (GetControlTransferError(setup, out var session) is { } error)
            return Task.FromException<UsbInTransferResult>(error);

        var parameters = setup.ToSetup();
        return Start(
            interfaceNumber: null,
            cancellationToken => session.ControlTransferInAsync(parameters, length, cancellationToken),
            static result => new UsbInTransferResult(result.Status, result.Data));
    }

    /// <summary>
    /// Issues a control transfer to the device (https://usb.spec.whatwg.org/#dom-usbdevice-controltransferout). The task fails
    /// as the task of <see cref="ControlTransferIn"/>.
    /// </summary>
    /// <param name="setup">The setup packet.</param>
    /// <param name="data">The data stage, or null.</param>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-controltransferout"/>
    [IdlMember("controlTransferOut", "https://usb.spec.whatwg.org/#dom-usbdevice-controltransferout")]
    public Task<UsbOutTransferResult> ControlTransferOut(UsbControlTransferParameters setup, ReadOnlyMemory<byte>? data = null)
    {
        ArgumentNullException.ThrowIfNull(setup);
        if (GetControlTransferError(setup, out var session) is { } error)
            return Task.FromException<UsbOutTransferResult>(error);

        if (data is { Length: > TransferLengthLimit })
            return Task.FromException<UsbOutTransferResult>(TransferTooLong());

        var parameters = setup.ToSetup();
        var bytes = data is { } value ? value.ToArray() : [];
        return Start(
            interfaceNumber: null,
            cancellationToken => session.ControlTransferOutAsync(parameters, bytes, cancellationToken),
            static result => new UsbOutTransferResult(result.Status, result.BytesWritten));
    }

    /// <summary>Clears the halt condition of an endpoint of a claimed interface (https://usb.spec.whatwg.org/#dom-usbdevice-clearhalt).</summary>
    /// <param name="direction">The direction of the endpoint.</param>
    /// <param name="endpointNumber">The number of the endpoint.</param>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-clearhalt"/>
    [IdlMember("clearHalt", "https://usb.spec.whatwg.org/#dom-usbdevice-clearhalt")]
    public Task ClearHalt(UsbDirection direction, byte endpointNumber)
    {
        if (GetEndpointError(direction, endpointNumber, isochronous: null, out var session, out var endpoint) is { } error)
            return Task.FromException(error);

        return Start(
            endpoint.InterfaceNumber,
            cancellationToken => Complete(session.ClearHaltAsync(direction, endpointNumber, cancellationToken)),
            static _ => true);
    }

    /// <summary>
    /// Performs a bulk or interrupt IN transfer on an endpoint of a claimed interface
    /// (https://usb.spec.whatwg.org/#dom-usbdevice-transferin). The task fails with an <c>InvalidAccessError</c>
    /// <see cref="DomException"/> for an isochronous endpoint.
    /// </summary>
    /// <param name="endpointNumber">The number of the endpoint.</param>
    /// <param name="length">The number of bytes to receive.</param>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-transferin"/>
    [IdlMember("transferIn", "https://usb.spec.whatwg.org/#dom-usbdevice-transferin")]
    public Task<UsbInTransferResult> TransferIn(byte endpointNumber, uint length)
    {
        if (GetEndpointError(UsbDirection.In, endpointNumber, isochronous: false, out var session, out var endpoint, length) is { } error)
            return Task.FromException<UsbInTransferResult>(error);

        return Start(
            endpoint.InterfaceNumber,
            cancellationToken => session.TransferInAsync(endpointNumber, length, cancellationToken),
            static result => new UsbInTransferResult(result.Status, result.Data));
    }

    /// <summary>
    /// Performs a bulk or interrupt OUT transfer on an endpoint of a claimed interface
    /// (https://usb.spec.whatwg.org/#dom-usbdevice-transferout).
    /// </summary>
    /// <param name="endpointNumber">The number of the endpoint.</param>
    /// <param name="data">The data to send.</param>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-transferout"/>
    [IdlMember("transferOut", "https://usb.spec.whatwg.org/#dom-usbdevice-transferout")]
    public Task<UsbOutTransferResult> TransferOut(byte endpointNumber, ReadOnlyMemory<byte> data)
    {
        if (GetEndpointError(UsbDirection.Out, endpointNumber, isochronous: false, out var session, out var endpoint, data.Length) is { } error)
            return Task.FromException<UsbOutTransferResult>(error);

        var bytes = data.ToArray();
        return Start(
            endpoint.InterfaceNumber,
            cancellationToken => session.TransferOutAsync(endpointNumber, bytes, cancellationToken),
            static result => new UsbOutTransferResult(result.Status, result.BytesWritten));
    }

    /// <summary>
    /// Performs an isochronous IN transfer on an endpoint of a claimed interface
    /// (https://usb.spec.whatwg.org/#dom-usbdevice-isochronoustransferin): the data of the result has the total length of the
    /// packets, and the data of each packet is at the offset of its length.
    /// </summary>
    /// <param name="endpointNumber">The number of the endpoint.</param>
    /// <param name="packetLengths">The length of each packet.</param>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-isochronoustransferin"/>
    [IdlMember("isochronousTransferIn", "https://usb.spec.whatwg.org/#dom-usbdevice-isochronoustransferin")]
    public Task<UsbIsochronousInTransferResult> IsochronousTransferIn(byte endpointNumber, IReadOnlyList<uint> packetLengths)
    {
        ArgumentNullException.ThrowIfNull(packetLengths);
        if (GetEndpointError(UsbDirection.In, endpointNumber, isochronous: true, out var session, out var endpoint, GetTotalLength(packetLengths)) is { } error)
            return Task.FromException<UsbIsochronousInTransferResult>(error);

        var lengths = packetLengths.ToArray();
        return Start(
            endpoint.InterfaceNumber,
            cancellationToken => session.IsochronousTransferInAsync(endpointNumber, lengths, cancellationToken),
            packets => CreateIsochronousInResult(lengths, packets));
    }

    /// <summary>Performs an isochronous OUT transfer on an endpoint of a claimed interface (https://usb.spec.whatwg.org/#dom-usbdevice-isochronoustransferout).</summary>
    /// <param name="endpointNumber">The number of the endpoint.</param>
    /// <param name="data">The data to send.</param>
    /// <param name="packetLengths">The length of each packet.</param>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-isochronoustransferout"/>
    [IdlMember("isochronousTransferOut", "https://usb.spec.whatwg.org/#dom-usbdevice-isochronoustransferout")]
    public Task<UsbIsochronousOutTransferResult> IsochronousTransferOut(byte endpointNumber, ReadOnlyMemory<byte> data, IReadOnlyList<uint> packetLengths)
    {
        ArgumentNullException.ThrowIfNull(packetLengths);
        var totalLength = GetTotalLength(packetLengths);
        if (GetEndpointError(UsbDirection.Out, endpointNumber, isochronous: true, out var session, out var endpoint, totalLength) is { } error)
            return Task.FromException<UsbIsochronousOutTransferResult>(error);

        // A detached buffer has no bytes
        if (data.Length != totalLength)
            return Task.FromException<UsbIsochronousOutTransferResult>(new DomException("The size of the data must be the sum of the packet lengths", "DataError"));

        var bytes = data.ToArray();
        var lengths = packetLengths.ToArray();
        return Start(
            endpoint.InterfaceNumber,
            cancellationToken => session.IsochronousTransferOutAsync(endpointNumber, bytes, lengths, cancellationToken),
            packets =>
            {
                if (packets.Count != lengths.Length)
                    throw new UsbException("The device did not return a packet for each length");

                return new UsbIsochronousOutTransferResult([.. packets.Select(packet => new UsbIsochronousOutTransferPacket(packet.Status, packet.BytesWritten))]);
            });
    }

    /// <summary>Soft resets the device (https://usb.spec.whatwg.org/#dom-usbdevice-reset): the other operations fail with an <c>AbortError</c>.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevice-reset"/>
    [IdlMember("reset", "https://usb.spec.whatwg.org/#dom-usbdevice-reset")]
    public Task Reset()
    {
        if (GetDeviceError(checkInterfaces: true) is { } error)
            return Task.FromException(error);

        if (_session is not { } session)
            return Task.FromException(DomException.InvalidState("The device is not opened"));

        AbortOperations(static _ => true, static name => new DomException($"The {name} operation was aborted: the device was reset", "AbortError"));
        _deviceStateChangeInProgress = true;
        return Start(
            interfaceNumber: null,
            cancellationToken => Complete(session.ResetAsync(cancellationToken)),
            static _ => true,
            () => _deviceStateChangeInProgress = false);
    }

    /// <summary>The window or the worker is destroyed: the pending operations are aborted, and the session ends.</summary>
    internal void OnContextDestroyed()
    {
        AbortOperations(static _ => true, static name => new DomException($"The {name} operation was aborted: the context of the device was destroyed", "AbortError"));
        if (_session is { } session)
        {
            _session = null;
            _ = CloseSessionAsync(session);
        }
    }

    private static async Task CloseSessionAsync(UsbDeviceSession session)
    {
        try
        {
            await session.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
    }

    /// <summary>The device was disconnected: the pending operations fail with a <c>NotFoundError</c>, and the device is closed.</summary>
    internal void OnDisconnected()
    {
        _connected = false;
        AbortOperations(static _ => true, static name => DomException.NotFound($"The {name} operation failed: the device was disconnected"));
        _session = null;
        Array.Fill(_claimedInterface, value: false);
    }

    /// <summary>Whether an interface of a configuration is claimed (https://usb.spec.whatwg.org/#usbinterface-find-if-the-interface-is-claimed).</summary>
    /// <remarks>As in Chromium, the configuration is the current one when it has its value (an object created by the scripts too).</remarks>
    internal bool IsClaimed(UsbInterface usbInterface)
    {
        if (!ReferenceEquals(usbInterface.Configuration.Device, this) || usbInterface.Configuration.ConfigurationValue != _configurationValue || Configuration is not { } configuration)
            return false;

        var index = configuration.IndexOf(usbInterface.InterfaceNumber);
        return index >= 0 && _claimedInterface[index];
    }

    /// <summary>The alternate setting of a claimed interface, else 0 (https://usb.spec.whatwg.org/#usbinterface-find-the-alternate-interface-for-the-current-alternate-setting).</summary>
    internal byte GetSelectedAlternateSetting(UsbInterface usbInterface)
        => IsClaimed(usbInterface) ? _selectedAlternateSetting[Configuration!.IndexOf(usbInterface.InterfaceNumber)] : (byte)0;

    internal UsbConfiguration? FindConfiguration(byte configurationValue)
    {
        foreach (var configuration in Configurations)
        {
            if (configuration.ConfigurationValue == configurationValue)
                return configuration;
        }

        return null;
    }

    private static UsbIsochronousInTransferResult CreateIsochronousInResult(uint[] lengths, IReadOnlyList<UsbInTransfer> packets)
    {
        if (packets.Count != lengths.Length)
            throw new UsbException("The device did not return a packet for each length");

        // The data of the result has the total length; the data of each packet is a view of it, at the offset of the previous
        // lengths
        var data = new JsBufferSource(JsBufferKind.DataView, new byte[lengths.Sum(length => (long)length)]);
        var offset = 0;
        var resultPackets = new UsbIsochronousInTransferPacket[packets.Count];
        for (var i = 0; i < packets.Count; i++)
        {
            var packetData = packets[i].Data.Span;
            if (packetData.Length > lengths[i])
            {
                packetData = packetData[..(int)lengths[i]];
            }

            packetData.CopyTo(data.Bytes.Span[offset..]);
            resultPackets[i] = new UsbIsochronousInTransferPacket(packets[i].Status, JsBufferSource.CreateView(data, offset, packetData.Length));
            offset += (int)lengths[i];
        }

        return new UsbIsochronousInTransferResult(resultPackets, data);
    }

    // The sum of the packet lengths, or -1 when it overflows an unsigned long
    private static long GetTotalLength(IReadOnlyList<uint> packetLengths)
    {
        long total = 0;
        foreach (var length in packetLengths)
        {
            total += length;
            if (total > uint.MaxValue)
                return -1;
        }

        return total;
    }

    private static DomException TransferTooLong() => new("The length of the transfer exceeds its maximum size (32 MiB)", "DataError");

    private static async Task<bool> Complete(Task task)
    {
        await task.ConfigureAwait(false);
        return true;
    }

    private void SetConfigurationValue(byte configurationValue)
    {
        _configurationValue = configurationValue;
        var count = FindConfiguration(configurationValue)?.Interfaces.Count ?? 0;
        _selectedAlternateSetting = new byte[count];
        _claimedInterface = new bool[count];
        _interfaceStateChangeInProgress = new bool[count];
    }

    private void EndInterfaceStateChange(UsbConfiguration configuration, int index)
    {
        if (ReferenceEquals(configuration, Configuration) && index < _interfaceStateChangeInProgress.Length)
        {
            _interfaceStateChangeInProgress[index] = false;
        }
    }

    /// <summary>The device is connected, and its state is not changing.</summary>
    private DomException? GetDeviceError(bool checkInterfaces = false)
    {
        if (!_connected)
            return DomException.NotFound("The device was disconnected");

        if (_deviceStateChangeInProgress || (checkInterfaces && _interfaceStateChangeInProgress.Contains(true)))
            return DomException.InvalidState("An operation that changes the state of the device is in progress");

        return null;
    }

    /// <summary>https://usb.spec.whatwg.org/#check-if-the-device-is-configured, then the interface of the current configuration, whose state is not changing.</summary>
    private DomException? GetInterfaceError(byte interfaceNumber, out UsbDeviceSession session, out UsbConfiguration configuration, out int index)
    {
        session = null!;
        configuration = null!;
        index = -1;
        if (GetConfiguredError(out session, out configuration) is { } error)
            return error;

        index = configuration.IndexOf(interfaceNumber);
        if (index < 0)
            return DomException.NotFound($"The configuration has no interface {interfaceNumber}");

        if (_interfaceStateChangeInProgress[index])
            return DomException.InvalidState($"An operation that changes the state of the interface {interfaceNumber} is in progress");

        return null;
    }

    /// <summary>https://usb.spec.whatwg.org/#check-if-the-device-is-configured</summary>
    private DomException? GetConfiguredError(out UsbDeviceSession session, out UsbConfiguration configuration)
    {
        session = null!;
        configuration = null!;
        if (GetDeviceError() is { } error)
            return error;

        if (_session is null)
            return DomException.InvalidState("The device is not opened");

        if (Configuration is not { } current)
            return DomException.InvalidState("The device is not configured");

        session = _session;
        configuration = current;
        return null;
    }

    /// <summary>
    /// The endpoint of a claimed interface, of the type of the transfer (https://usb.spec.whatwg.org/#usbdevice-find-the-endpoint),
    /// as in Chromium: the number of an endpoint is at most 15 (an <c>IndexSizeError</c>), and the length of a transfer (-1
    /// when it overflows) is at most 32 MiB (a <c>DataError</c>).
    /// </summary>
    private DomException? GetEndpointError(UsbDirection direction, byte endpointNumber, bool? isochronous, out UsbDeviceSession session, out UsbEndpoint endpoint, long length = 0)
    {
        endpoint = null!;
        if (GetConfiguredError(out session, out var configuration) is { } error)
            return error;

        if (endpointNumber > 15)
            return new DomException($"The endpoint number {endpointNumber} is out of range", "IndexSizeError");

        if (FindEndpoint(configuration, direction, endpointNumber) is not { } found)
            return DomException.NotFound($"The claimed interfaces have no {(direction == UsbDirection.In ? "in" : "out")} endpoint {endpointNumber}");

        if (_interfaceStateChangeInProgress[configuration.IndexOf(found.InterfaceNumber)])
            return DomException.InvalidState($"An operation that changes the state of the interface {found.InterfaceNumber} is in progress");

        if (length is < 0 or > TransferLengthLimit)
            return TransferTooLong();

        if (isochronous is { } expectIsochronous && (found.Type == UsbEndpointType.Isochronous) != expectIsochronous)
            return new DomException($"The endpoint {endpointNumber} is a {found.Type.ToString().ToLowerInvariant()} endpoint", "InvalidAccessError");

        endpoint = found;
        return null;
    }

    private UsbEndpoint? FindEndpoint(UsbConfiguration configuration, UsbDirection direction, byte endpointNumber)
    {
        foreach (var usbInterface in configuration.Interfaces)
        {
            if (!IsClaimed(usbInterface))
                continue;

            foreach (var endpoint in usbInterface.Alternate.Endpoints)
            {
                if (endpoint.EndpointNumber == endpointNumber && endpoint.Direction == direction)
                    return endpoint;
            }
        }

        return null;
    }

    /// <summary>
    /// https://usb.spec.whatwg.org/#check-the-validity-of-the-control-transfer-parameters, as in Chromium: the requests to an
    /// interface or an endpoint need a configuration, the address of an endpoint is the low byte of the index, and the
    /// standard requests are not restricted.
    /// </summary>
    private DomException? GetControlTransferError(UsbControlTransferParameters setup, out UsbDeviceSession session)
    {
        session = null!;
        if (GetDeviceError(checkInterfaces: true) is { } error)
            return error;

        if (_session is null)
            return DomException.InvalidState("The device is not opened");

        session = _session;
        if (Configuration is not { } configuration)
            return setup.Recipient is UsbRecipient.Interface or UsbRecipient.Endpoint ? DomException.InvalidState("The device is not configured") : null;

        if (setup.RequestType == UsbRequestType.Class && configuration.IndexOf((byte)setup.Index) is >= 0 and var classIndex && configuration.Interfaces[classIndex].IsProtectedClass)
            return new DomException("The class requests to a protected interface are not allowed", "SecurityError");

        switch (setup.Recipient)
        {
            case UsbRecipient.Interface:
            {
                var index = configuration.IndexOf((byte)setup.Index);
                if (index < 0)
                    return DomException.NotFound($"The configuration has no interface {(byte)setup.Index}");

                var usbInterface = configuration.Interfaces[index];
                if (usbInterface.IsProtectedClass)
                    return new DomException("The requests to a protected interface are not allowed", "SecurityError");

                if (!IsClaimed(usbInterface))
                    return DomException.InvalidState($"The interface {usbInterface.InterfaceNumber} is not claimed");

                break;
            }

            case UsbRecipient.Endpoint:
            {
                var address = (byte)setup.Index;
                if (FindEndpoint(configuration, (address & 0x80) != 0 ? UsbDirection.In : UsbDirection.Out, (byte)(address & 0x0F)) is not { } endpoint)
                    return DomException.NotFound($"The claimed interfaces have no endpoint {address}");

                if (endpoint.Alternate.Interface.IsProtectedClass)
                    return new DomException("The requests to an endpoint of a protected interface are not allowed", "SecurityError");

                break;
            }
        }

        return null;
    }

    /// <summary>Rejects the pending operations that match the predicate (https://usb.spec.whatwg.org/#abort-transfers-currently-scheduled-on-an-interface).</summary>
    private void AbortOperations(Func<PendingOperation, bool> predicate, Func<string, DomException> createError)
    {
        foreach (var operation in _pendingOperations.Where(predicate).ToArray())
        {
            _pendingOperations.Remove(operation);
            operation.Abort(createError(operation.Name));
        }
    }

    /// <summary>
    /// Runs an operation of the session in parallel, then a global task settles the promise, unless the operation was aborted:
    /// <paramref name="complete"/> converts the result of the device, and <paramref name="settled"/> runs once settled (also
    /// when aborted).
    /// </summary>
    private Task<TResult> Start<THost, TResult>(byte? interfaceNumber, Func<CancellationToken, Task<THost>> operation, Func<THost, TResult> complete, Action? settled = null, [CallerMemberName] string name = "")
    {
        var pending = new PendingOperation<TResult>(char.ToLowerInvariant(name[0]) + name[1..], interfaceNumber, settled);
        _pendingOperations.Add(pending);
        Task<THost> task;
        try
        {
            task = operation(pending.CancellationToken);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            task = Task.FromException<THost>(exception);
        }

        _ = task.ContinueWith(
            completed => Global.QueueGlobalTask(() =>
            {
                if (!_pendingOperations.Remove(pending))
                    return;

                if (!completed.IsCompletedSuccessfully)
                {
                    pending.Fail(ToDomException(completed.Exception?.InnerException));
                    return;
                }

                try
                {
                    pending.Succeed(complete(completed.Result));
                }
                catch (UsbException exception)
                {
                    pending.Fail(exception.ToDomException());
                }
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return pending.Task;
    }

    private static DomException ToDomException(Exception? exception) => exception switch
    {
        UsbException usbException => usbException.ToDomException(),
        DomException domException => domException,
        OperationCanceledException or null => new DomException("The operation was aborted", "AbortError"),
        _ => new DomException(exception.Message, "NetworkError"),
    };

    private abstract class PendingOperation(string name, byte? interfaceNumber, Action? settled) : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();

        /// <summary>The name of the method of the operation (e.g. <c>transferIn</c>).</summary>
        public string Name { get; } = name;

        /// <summary>The interface of a transfer on an endpoint, or null.</summary>
        public byte? InterfaceNumber { get; } = interfaceNumber;

        public CancellationToken CancellationToken => _cancellation.Token;

        public void Abort(DomException error)
        {
            _cancellation.Cancel();
            Fail(error);
        }

        public void Fail(DomException error)
        {
            settled?.Invoke();
            SetException(error);
            Dispose();
        }

        public void Dispose() => _cancellation.Dispose();

        protected void OnSucceeded()
        {
            settled?.Invoke();
            Dispose();
        }

        protected abstract void SetException(DomException error);
    }

    private sealed class PendingOperation<TResult>(string name, byte? interfaceNumber, Action? settled) : PendingOperation(name, interfaceNumber, settled)
    {
        private readonly TaskCompletionSource<TResult> _result = new();

        public Task<TResult> Task => _result.Task;

        public void Succeed(TResult result)
        {
            OnSucceeded();
            _result.TrySetResult(result);
        }

        protected override void SetException(DomException error) => _result.TrySetException(error);
    }
}

/// <summary>A configuration of a USB device (https://usb.spec.whatwg.org/#usbconfiguration).</summary>
/// <seealso href="https://usb.spec.whatwg.org/#usbconfiguration"/>
[IdlInterface("USBConfiguration", "https://usb.spec.whatwg.org/#usbconfiguration")]
public sealed class UsbConfiguration
{
    /// <summary>Creates a configuration of a device (a new object, distinct from the ones of <see cref="UsbDevice.Configurations"/>).</summary>
    /// <param name="device">The device.</param>
    /// <param name="configurationValue">The value of a configuration of the device.</param>
    /// <exception cref="RangeErrorException">The device has no configuration with this value.</exception>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbconfiguration-usbconfiguration"/>
    [IdlMember("constructor", "https://usb.spec.whatwg.org/#dom-usbconfiguration-usbconfiguration")]
    public UsbConfiguration(UsbDevice device, byte configurationValue)
        : this(device ?? throw new ArgumentNullException(nameof(device)), device.HostDevice.Descriptor.Configurations.FirstOrDefault(configuration => configuration.ConfigurationValue == configurationValue) ?? throw new RangeErrorException($"The device has no configuration {configurationValue}"))
    {
    }

    internal UsbConfiguration(UsbDevice device, UsbConfigurationDescriptor descriptor)
    {
        Device = device;
        ConfigurationValue = descriptor.ConfigurationValue;
        ConfigurationName = descriptor.ConfigurationName;

        // The first descriptor of each interface number
        var interfaces = new List<UsbInterface>();
        foreach (var usbInterface in descriptor.Interfaces)
        {
            if (!interfaces.Exists(existing => existing.InterfaceNumber == usbInterface.InterfaceNumber))
            {
                interfaces.Add(new UsbInterface(this, usbInterface));
            }
        }

        Interfaces = interfaces;
    }

    internal UsbDevice Device { get; }

    /// <summary>Gets the value of the configuration.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbconfiguration-configurationvalue"/>
    [IdlMember("configurationValue", "https://usb.spec.whatwg.org/#dom-usbconfiguration-configurationvalue")]
    public byte ConfigurationValue { get; }

    /// <summary>Gets the name of the configuration, or null.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbconfiguration-configurationname"/>
    [IdlMember("configurationName", "https://usb.spec.whatwg.org/#dom-usbconfiguration-configurationname")]
    public string? ConfigurationName { get; }

    /// <summary>Gets the interfaces of the configuration.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbconfiguration-interfaces"/>
    [IdlMember("interfaces", "https://usb.spec.whatwg.org/#dom-usbconfiguration-interfaces")]
    public IReadOnlyList<UsbInterface> Interfaces { get; }

    /// <summary>https://usb.spec.whatwg.org/#usbconfiguration-find-the-interface-index</summary>
    internal int IndexOf(byte interfaceNumber)
    {
        for (var i = 0; i < Interfaces.Count; i++)
        {
            if (Interfaces[i].InterfaceNumber == interfaceNumber)
                return i;
        }

        return -1;
    }
}

/// <summary>An interface of a configuration of a USB device (https://usb.spec.whatwg.org/#usbinterface).</summary>
/// <seealso href="https://usb.spec.whatwg.org/#usbinterface"/>
[IdlInterface("USBInterface", "https://usb.spec.whatwg.org/#usbinterface")]
public sealed class UsbInterface
{
    // https://usb.spec.whatwg.org/#protected-interface-class: audio, HID, mass storage, hub, smart card, video, audio/video and wireless controller
    private static readonly byte[] ProtectedClasses = [0x01, 0x03, 0x08, 0x09, 0x0B, 0x0E, 0x10, 0xE0];

    /// <summary>Creates an interface of a configuration (a new object, distinct from the ones of <see cref="UsbConfiguration.Interfaces"/>).</summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="interfaceNumber">The number of an interface of the configuration.</param>
    /// <exception cref="RangeErrorException">The configuration has no interface with this number.</exception>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbinterface-usbinterface"/>
    [IdlMember("constructor", "https://usb.spec.whatwg.org/#dom-usbinterface-usbinterface")]
    public UsbInterface(UsbConfiguration configuration, byte interfaceNumber)
        : this(configuration ?? throw new ArgumentNullException(nameof(configuration)), FindDescriptor(configuration, interfaceNumber))
    {
    }

    internal UsbInterface(UsbConfiguration configuration, UsbInterfaceDescriptor descriptor)
    {
        Configuration = configuration;
        InterfaceNumber = descriptor.InterfaceNumber;
        Alternates = [.. descriptor.Alternates.Select(alternate => new UsbAlternateInterface(this, alternate))];
        IsProtectedClass = descriptor.Alternates.Any(alternate => ProtectedClasses.Contains(alternate.InterfaceClass));
    }

    internal UsbConfiguration Configuration { get; }

    /// <summary>Whether an alternate setting of the interface has a protected class ([[isProtectedClass]]).</summary>
    internal bool IsProtectedClass { get; }

    /// <summary>Gets the number of the interface.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbinterface-interfacenumber"/>
    [IdlMember("interfaceNumber", "https://usb.spec.whatwg.org/#dom-usbinterface-interfacenumber")]
    public byte InterfaceNumber { get; }

    /// <summary>Gets the alternate setting selected for the interface (the first one when the interface is not claimed).</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbinterface-alternate"/>
    [IdlMember("alternate", "https://usb.spec.whatwg.org/#dom-usbinterface-alternate")]
    public UsbAlternateInterface Alternate
    {
        get
        {
            var device = Configuration.Device;
            return device.IsClaimed(this) && FindAlternate(device.GetSelectedAlternateSetting(this)) is { } alternate ? alternate : Alternates[0];
        }
    }

    /// <summary>Gets the alternate settings of the interface.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbinterface-alternates"/>
    [IdlMember("alternates", "https://usb.spec.whatwg.org/#dom-usbinterface-alternates")]
    public IReadOnlyList<UsbAlternateInterface> Alternates { get; }

    /// <summary>Gets whether the window or the worker claimed the interface.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbinterface-claimed"/>
    [IdlMember("claimed", "https://usb.spec.whatwg.org/#dom-usbinterface-claimed")]
    public bool Claimed => Configuration.Device.IsClaimed(this);

    internal UsbAlternateInterface? FindAlternate(byte alternateSetting) => Alternates.FirstOrDefault(alternate => alternate.AlternateSetting == alternateSetting);

    private static UsbInterfaceDescriptor FindDescriptor(UsbConfiguration configuration, byte interfaceNumber)
    {
        var descriptor = configuration.Device.HostDevice.Descriptor.Configurations.FirstOrDefault(c => c.ConfigurationValue == configuration.ConfigurationValue);
        return descriptor?.Interfaces.FirstOrDefault(i => i.InterfaceNumber == interfaceNumber) ?? throw new RangeErrorException($"The configuration has no interface {interfaceNumber}");
    }
}

/// <summary>An alternate setting of an interface of a USB device (https://usb.spec.whatwg.org/#usbalternateinterface).</summary>
/// <seealso href="https://usb.spec.whatwg.org/#usbalternateinterface"/>
[IdlInterface("USBAlternateInterface", "https://usb.spec.whatwg.org/#usbalternateinterface")]
public sealed class UsbAlternateInterface
{
    /// <summary>Creates an alternate setting of an interface (a new object, distinct from the ones of <see cref="UsbInterface.Alternates"/>).</summary>
    /// <param name="deviceInterface">The interface.</param>
    /// <param name="alternateSetting">An alternate setting of the interface.</param>
    /// <exception cref="RangeErrorException">The interface has no such alternate setting.</exception>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbalternateinterface-usbalternateinterface"/>
    [IdlMember("constructor", "https://usb.spec.whatwg.org/#dom-usbalternateinterface-usbalternateinterface")]
    public UsbAlternateInterface(UsbInterface deviceInterface, byte alternateSetting)
        : this(deviceInterface ?? throw new ArgumentNullException(nameof(deviceInterface)), FindDescriptor(deviceInterface, alternateSetting))
    {
    }

    internal UsbAlternateInterface(UsbInterface deviceInterface, UsbAlternateInterfaceDescriptor descriptor)
    {
        Interface = deviceInterface;
        Descriptor = descriptor;
        Endpoints = [.. descriptor.Endpoints.Where(endpoint => endpoint.EndpointNumber is >= 1 and <= 15).Select(endpoint => new UsbEndpoint(this, endpoint))];
    }

    internal UsbInterface Interface { get; }

    internal UsbAlternateInterfaceDescriptor Descriptor { get; }

    /// <summary>Gets the alternate setting.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbalternateinterface-alternatesetting"/>
    [IdlMember("alternateSetting", "https://usb.spec.whatwg.org/#dom-usbalternateinterface-alternatesetting")]
    public byte AlternateSetting => Descriptor.AlternateSetting;

    /// <summary>Gets the class of the interface.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbalternateinterface-interfaceclass"/>
    [IdlMember("interfaceClass", "https://usb.spec.whatwg.org/#dom-usbalternateinterface-interfaceclass")]
    public byte InterfaceClass => Descriptor.InterfaceClass;

    /// <summary>Gets the subclass of the interface.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbalternateinterface-interfacesubclass"/>
    [IdlMember("interfaceSubclass", "https://usb.spec.whatwg.org/#dom-usbalternateinterface-interfacesubclass")]
    public byte InterfaceSubclass => Descriptor.InterfaceSubclass;

    /// <summary>Gets the protocol of the interface.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbalternateinterface-interfaceprotocol"/>
    [IdlMember("interfaceProtocol", "https://usb.spec.whatwg.org/#dom-usbalternateinterface-interfaceprotocol")]
    public byte InterfaceProtocol => Descriptor.InterfaceProtocol;

    /// <summary>Gets the name of the interface, or null.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbalternateinterface-interfacename"/>
    [IdlMember("interfaceName", "https://usb.spec.whatwg.org/#dom-usbalternateinterface-interfacename")]
    public string? InterfaceName => Descriptor.InterfaceName;

    /// <summary>Gets the endpoints of the alternate setting (the endpoints whose number is not between 1 and 15 are ignored).</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbalternateinterface-endpoints"/>
    [IdlMember("endpoints", "https://usb.spec.whatwg.org/#dom-usbalternateinterface-endpoints")]
    public IReadOnlyList<UsbEndpoint> Endpoints { get; }

    private static UsbAlternateInterfaceDescriptor FindDescriptor(UsbInterface deviceInterface, byte alternateSetting)
        => deviceInterface.Alternates.FirstOrDefault(alternate => alternate.AlternateSetting == alternateSetting)?.Descriptor ?? throw new RangeErrorException($"The interface has no alternate setting {alternateSetting}");
}

/// <summary>An endpoint of an alternate setting of a USB device (https://usb.spec.whatwg.org/#usbendpoint).</summary>
/// <seealso href="https://usb.spec.whatwg.org/#usbendpoint"/>
[IdlInterface("USBEndpoint", "https://usb.spec.whatwg.org/#usbendpoint")]
public sealed class UsbEndpoint
{
    /// <summary>Creates an endpoint of an alternate setting.</summary>
    /// <param name="alternate">The alternate setting.</param>
    /// <param name="endpointNumber">The number of the endpoint.</param>
    /// <param name="direction">The direction of the endpoint.</param>
    /// <exception cref="RangeErrorException">The alternate setting has no such endpoint.</exception>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbendpoint-usbendpoint"/>
    [IdlMember("constructor", "https://usb.spec.whatwg.org/#dom-usbendpoint-usbendpoint")]
    public UsbEndpoint(UsbAlternateInterface alternate, byte endpointNumber, UsbDirection direction)
        : this(alternate ?? throw new ArgumentNullException(nameof(alternate)), alternate.Endpoints.FirstOrDefault(endpoint => endpoint.EndpointNumber == endpointNumber && endpoint.Direction == direction)?.Descriptor ?? throw new RangeErrorException($"The alternate setting has no {(direction == UsbDirection.In ? "in" : "out")} endpoint {endpointNumber}"))
    {
    }

    internal UsbEndpoint(UsbAlternateInterface alternate, UsbEndpointDescriptor descriptor)
    {
        Alternate = alternate;
        Descriptor = descriptor;
    }

    internal UsbAlternateInterface Alternate { get; }

    internal UsbEndpointDescriptor Descriptor { get; }

    internal byte InterfaceNumber => Alternate.Interface.InterfaceNumber;

    /// <summary>Gets the number of the endpoint.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbendpoint-endpointnumber"/>
    [IdlMember("endpointNumber", "https://usb.spec.whatwg.org/#dom-usbendpoint-endpointnumber")]
    public byte EndpointNumber => Descriptor.EndpointNumber;

    /// <summary>Gets the direction of the endpoint.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbendpoint-direction"/>
    [IdlMember("direction", "https://usb.spec.whatwg.org/#dom-usbendpoint-direction")]
    public UsbDirection Direction => Descriptor.Direction;

    /// <summary>Gets the type of the endpoint.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbendpoint-type"/>
    [IdlMember("type", "https://usb.spec.whatwg.org/#dom-usbendpoint-type")]
    public UsbEndpointType Type => Descriptor.Type;

    /// <summary>Gets the maximum packet size of the endpoint.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbendpoint-packetsize"/>
    [IdlMember("packetSize", "https://usb.spec.whatwg.org/#dom-usbendpoint-packetsize")]
    public uint PacketSize => Descriptor.PacketSize;
}
