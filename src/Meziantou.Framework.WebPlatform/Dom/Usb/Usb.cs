using System.ComponentModel;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The status of a USB transfer (https://usb.spec.whatwg.org/#enumdef-usbtransferstatus).</summary>
/// <seealso href="https://usb.spec.whatwg.org/#enumdef-usbtransferstatus"/>
[IdlInterface("USBTransferStatus", "https://usb.spec.whatwg.org/#enumdef-usbtransferstatus")]
public enum UsbTransferStatus
{
    /// <summary>The transfer completed.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbtransferstatus-ok"/>
    [IdlMember("ok", "https://usb.spec.whatwg.org/#dom-usbtransferstatus-ok")]
    Ok,

    /// <summary>The device stalled the endpoint.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbtransferstatus-stall"/>
    [IdlMember("stall", "https://usb.spec.whatwg.org/#dom-usbtransferstatus-stall")]
    Stall,

    /// <summary>The device sent more data than requested.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbtransferstatus-babble"/>
    [IdlMember("babble", "https://usb.spec.whatwg.org/#dom-usbtransferstatus-babble")]
    Babble,
}

/// <summary>The type of a control transfer request (https://usb.spec.whatwg.org/#enumdef-usbrequesttype).</summary>
/// <seealso href="https://usb.spec.whatwg.org/#enumdef-usbrequesttype"/>
[IdlInterface("USBRequestType", "https://usb.spec.whatwg.org/#enumdef-usbrequesttype")]
public enum UsbRequestType
{
    /// <summary>A standard request of the USB specification.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbrequesttype-standard"/>
    [IdlMember("standard", "https://usb.spec.whatwg.org/#dom-usbrequesttype-standard")]
    Standard,

    /// <summary>A request of a USB device class.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbrequesttype-class"/>
    [IdlMember("class", "https://usb.spec.whatwg.org/#dom-usbrequesttype-class")]
    Class,

    /// <summary>A request of the vendor of the device.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbrequesttype-vendor"/>
    [IdlMember("vendor", "https://usb.spec.whatwg.org/#dom-usbrequesttype-vendor")]
    Vendor,
}

/// <summary>The recipient of a control transfer (https://usb.spec.whatwg.org/#enumdef-usbrecipient).</summary>
/// <seealso href="https://usb.spec.whatwg.org/#enumdef-usbrecipient"/>
[IdlInterface("USBRecipient", "https://usb.spec.whatwg.org/#enumdef-usbrecipient")]
public enum UsbRecipient
{
    /// <summary>The device.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbrecipient-device"/>
    [IdlMember("device", "https://usb.spec.whatwg.org/#dom-usbrecipient-device")]
    Device,

    /// <summary>An interface (the low byte of <c>index</c>).</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbrecipient-interface"/>
    [IdlMember("interface", "https://usb.spec.whatwg.org/#dom-usbrecipient-interface")]
    Interface,

    /// <summary>An endpoint (the address of the endpoint is <c>index</c>).</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbrecipient-endpoint"/>
    [IdlMember("endpoint", "https://usb.spec.whatwg.org/#dom-usbrecipient-endpoint")]
    Endpoint,

    /// <summary>Another recipient.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbrecipient-other"/>
    [IdlMember("other", "https://usb.spec.whatwg.org/#dom-usbrecipient-other")]
    Other,
}

/// <summary>The direction of a USB endpoint (https://usb.spec.whatwg.org/#enumdef-usbdirection).</summary>
/// <seealso href="https://usb.spec.whatwg.org/#enumdef-usbdirection"/>
[IdlInterface("USBDirection", "https://usb.spec.whatwg.org/#enumdef-usbdirection")]
public enum UsbDirection
{
    /// <summary>From the device to the host.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdirection-in"/>
    [IdlMember("in", "https://usb.spec.whatwg.org/#dom-usbdirection-in")]
    In,

    /// <summary>From the host to the device.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdirection-out"/>
    [IdlMember("out", "https://usb.spec.whatwg.org/#dom-usbdirection-out")]
    Out,
}

/// <summary>The type of a USB endpoint (https://usb.spec.whatwg.org/#enumdef-usbendpointtype).</summary>
/// <seealso href="https://usb.spec.whatwg.org/#enumdef-usbendpointtype"/>
[IdlInterface("USBEndpointType", "https://usb.spec.whatwg.org/#enumdef-usbendpointtype")]
public enum UsbEndpointType
{
    /// <summary>A bulk endpoint.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbendpointtype-bulk"/>
    [IdlMember("bulk", "https://usb.spec.whatwg.org/#dom-usbendpointtype-bulk")]
    Bulk,

    /// <summary>An interrupt endpoint.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbendpointtype-interrupt"/>
    [IdlMember("interrupt", "https://usb.spec.whatwg.org/#dom-usbendpointtype-interrupt")]
    Interrupt,

    /// <summary>An isochronous endpoint.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbendpointtype-isochronous"/>
    [IdlMember("isochronous", "https://usb.spec.whatwg.org/#dom-usbendpointtype-isochronous")]
    Isochronous,
}

/// <summary>A filter of the devices of <see cref="Usb.RequestDevice"/> (https://usb.spec.whatwg.org/#dictdef-usbdevicefilter).</summary>
/// <seealso href="https://usb.spec.whatwg.org/#dictdef-usbdevicefilter"/>
[IdlInterface("USBDeviceFilter", "https://usb.spec.whatwg.org/#dictdef-usbdevicefilter")]
public sealed class UsbDeviceFilter
{
    /// <summary>Gets the vendor ID, or null.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevicefilter-vendorid"/>
    [IdlMember("vendorId", "https://usb.spec.whatwg.org/#dom-usbdevicefilter-vendorid")]
    public ushort? VendorId { get; init; }

    /// <summary>Gets the product ID, or null (it requires <see cref="VendorId"/>).</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevicefilter-productid"/>
    [IdlMember("productId", "https://usb.spec.whatwg.org/#dom-usbdevicefilter-productid")]
    public ushort? ProductId { get; init; }

    /// <summary>Gets the class of the device or of one of its interfaces, or null.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevicefilter-classcode"/>
    [IdlMember("classCode", "https://usb.spec.whatwg.org/#dom-usbdevicefilter-classcode")]
    public byte? ClassCode { get; init; }

    /// <summary>Gets the subclass, or null (it requires <see cref="ClassCode"/>).</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevicefilter-subclasscode"/>
    [IdlMember("subclassCode", "https://usb.spec.whatwg.org/#dom-usbdevicefilter-subclasscode")]
    public byte? SubclassCode { get; init; }

    /// <summary>Gets the protocol, or null (it requires <see cref="SubclassCode"/>).</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevicefilter-protocolcode"/>
    [IdlMember("protocolCode", "https://usb.spec.whatwg.org/#dom-usbdevicefilter-protocolcode")]
    public byte? ProtocolCode { get; init; }

    /// <summary>Gets the serial number, or null.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevicefilter-serialnumber"/>
    [IdlMember("serialNumber", "https://usb.spec.whatwg.org/#dom-usbdevicefilter-serialnumber")]
    public string? SerialNumber { get; init; }

    /// <summary>https://usb.spec.whatwg.org/#usbdevicefilter-valid</summary>
    internal bool IsValid => (ProductId is null || VendorId is not null) && (SubclassCode is null || ClassCode is not null) && (ProtocolCode is null || SubclassCode is not null);

    /// <summary>Returns whether a device matches the filter (https://usb.spec.whatwg.org/#usbdevicefilter-match).</summary>
    /// <param name="device">The descriptors of the device.</param>
    [NotInIdl("The matching algorithm of the filters, for the device choosers of the hosts")]
    public bool Matches(UsbDeviceDescriptor device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (VendorId is { } vendorId && device.VendorId != vendorId)
            return false;

        if (ProductId is { } productId && device.ProductId != productId)
            return false;

        if (SerialNumber is { } serialNumber && !string.Equals(device.SerialNumber, serialNumber, StringComparison.Ordinal))
            return false;

        if (ClassCode is not null && device.Configurations.Any(configuration => configuration.Interfaces.Any(i => i.Alternates.Any(MatchesInterface))))
            return true;

        return (ClassCode is null || device.DeviceClass == ClassCode)
            && (SubclassCode is null || device.DeviceSubclass == SubclassCode)
            && (ProtocolCode is null || device.DeviceProtocol == ProtocolCode);
    }

    // https://usb.spec.whatwg.org/#usbinterface-match-an-interface-filter
    private bool MatchesInterface(UsbAlternateInterfaceDescriptor descriptor)
        => (ClassCode is null || descriptor.InterfaceClass == ClassCode)
        && (SubclassCode is null || descriptor.InterfaceSubclass == SubclassCode)
        && (ProtocolCode is null || descriptor.InterfaceProtocol == ProtocolCode);
}

/// <summary>The options of <see cref="Usb.RequestDevice"/> (https://usb.spec.whatwg.org/#dictdef-usbdevicerequestoptions).</summary>
/// <seealso href="https://usb.spec.whatwg.org/#dictdef-usbdevicerequestoptions"/>
[IdlInterface("USBDeviceRequestOptions", "https://usb.spec.whatwg.org/#dictdef-usbdevicerequestoptions")]
public sealed class UsbDeviceRequestOptions
{
    /// <summary>Gets the filters: a device matches one of them (every device when there is none).</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevicerequestoptions-filters"/>
    [IdlMember("filters", "https://usb.spec.whatwg.org/#dom-usbdevicerequestoptions-filters")]
    public required IReadOnlyList<UsbDeviceFilter> Filters { get; init; }

    /// <summary>Gets the exclusion filters: a device matches none of them.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbdevicerequestoptions-exclusionfilters"/>
    [IdlMember("exclusionFilters", "https://usb.spec.whatwg.org/#dom-usbdevicerequestoptions-exclusionfilters")]
    public IReadOnlyList<UsbDeviceFilter> ExclusionFilters { get; init; } = [];
}

/// <summary>The initialization dictionary of a <see cref="UsbConnectionEvent"/> (https://usb.spec.whatwg.org/#dictdef-usbconnectioneventinit).</summary>
/// <seealso href="https://usb.spec.whatwg.org/#dictdef-usbconnectioneventinit"/>
[IdlInterface("USBConnectionEventInit", "https://usb.spec.whatwg.org/#dictdef-usbconnectioneventinit")]
public sealed class UsbConnectionEventInit : EventInit
{
    /// <summary>Gets the device.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbconnectioneventinit-device"/>
    [IdlMember("device", "https://usb.spec.whatwg.org/#dom-usbconnectioneventinit-device")]
    public required UsbDevice Device { get; init; }
}

/// <summary>The event fired at <see cref="Usb"/> when a device is connected or disconnected (<c>connect</c>, <c>disconnect</c>, https://usb.spec.whatwg.org/#usbconnectionevent).</summary>
/// <seealso href="https://usb.spec.whatwg.org/#usbconnectionevent"/>
[IdlInterface("USBConnectionEvent", "https://usb.spec.whatwg.org/#usbconnectionevent")]
public sealed class UsbConnectionEvent : Event
{
    /// <summary>Creates an event.</summary>
    /// <param name="type">The type of the event.</param>
    /// <param name="eventInitDict">The initialization dictionary.</param>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbconnectionevent-usbconnectionevent"/>
    [IdlMember("constructor", "https://usb.spec.whatwg.org/#dom-usbconnectionevent-usbconnectionevent")]
    public UsbConnectionEvent(string type, UsbConnectionEventInit eventInitDict)
        : base(type, eventInitDict)
    {
        ArgumentNullException.ThrowIfNull(eventInitDict);
        Device = eventInitDict.Device;
    }

    internal UsbConnectionEvent(string type, UsbDevice device)
        : base(type)
    {
        Device = device;
    }

    /// <summary>Gets the device.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbconnectionevent-device"/>
    [IdlMember("device", "https://usb.spec.whatwg.org/#dom-usbconnectionevent-device")]
    public UsbDevice Device { get; }
}

/// <summary>The result of an IN transfer (https://usb.spec.whatwg.org/#usbintransferresult).</summary>
/// <seealso href="https://usb.spec.whatwg.org/#usbintransferresult"/>
[IdlInterface("USBInTransferResult", "https://usb.spec.whatwg.org/#usbintransferresult")]
public sealed class UsbInTransferResult
{
    /// <summary>Creates a result.</summary>
    /// <param name="status">The status.</param>
    /// <param name="data">The data received, or null.</param>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbintransferresult-usbintransferresult"/>
    [IdlMember("constructor", "https://usb.spec.whatwg.org/#dom-usbintransferresult-usbintransferresult")]
    public UsbInTransferResult(UsbTransferStatus status, ReadOnlyMemory<byte>? data = null)
    {
        Status = status;
        Data = data is { } bytes ? new ReadOnlyMemory<byte>(bytes.ToArray()) : (ReadOnlyMemory<byte>?)null;
    }

    /// <summary>Gets the data received (a new <c>DataView</c> for each read of the scripts), or null.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbintransferresult-data"/>
    [IdlMember("data", "https://usb.spec.whatwg.org/#dom-usbintransferresult-data")]
    public ReadOnlyMemory<byte>? Data { get; }

    /// <summary>Gets the status.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbintransferresult-status"/>
    [IdlMember("status", "https://usb.spec.whatwg.org/#dom-usbintransferresult-status")]
    public UsbTransferStatus Status { get; }
}

/// <summary>The result of an OUT transfer (https://usb.spec.whatwg.org/#usbouttransferresult).</summary>
/// <seealso href="https://usb.spec.whatwg.org/#usbouttransferresult"/>
[IdlInterface("USBOutTransferResult", "https://usb.spec.whatwg.org/#usbouttransferresult")]
public sealed class UsbOutTransferResult
{
    /// <summary>Creates a result.</summary>
    /// <param name="status">The status.</param>
    /// <param name="bytesWritten">The number of bytes sent.</param>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbouttransferresult-usbouttransferresult"/>
    [IdlMember("constructor", "https://usb.spec.whatwg.org/#dom-usbouttransferresult-usbouttransferresult")]
    public UsbOutTransferResult(UsbTransferStatus status, uint bytesWritten = 0)
    {
        Status = status;
        BytesWritten = bytesWritten;
    }

    /// <summary>Gets the number of bytes sent.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbouttransferresult-byteswritten"/>
    [IdlMember("bytesWritten", "https://usb.spec.whatwg.org/#dom-usbouttransferresult-byteswritten")]
    public uint BytesWritten { get; }

    /// <summary>Gets the status.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbouttransferresult-status"/>
    [IdlMember("status", "https://usb.spec.whatwg.org/#dom-usbouttransferresult-status")]
    public UsbTransferStatus Status { get; }
}

/// <summary>A packet of an isochronous IN transfer (https://usb.spec.whatwg.org/#usbisochronousintransferpacket).</summary>
/// <seealso href="https://usb.spec.whatwg.org/#usbisochronousintransferpacket"/>
[IdlInterface("USBIsochronousInTransferPacket", "https://usb.spec.whatwg.org/#usbisochronousintransferpacket")]
public sealed class UsbIsochronousInTransferPacket
{
    /// <summary>Creates a packet.</summary>
    /// <param name="status">The status.</param>
    /// <param name="data">The data received, or null.</param>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbisochronousintransferpacket-usbisochronousintransferpacket"/>
    [IdlMember("constructor", "https://usb.spec.whatwg.org/#dom-usbisochronousintransferpacket-usbisochronousintransferpacket")]
    public UsbIsochronousInTransferPacket(UsbTransferStatus status, ReadOnlyMemory<byte>? data = null)
        : this(status, data is { } bytes ? new JsBufferSource(JsBufferKind.DataView, bytes.ToArray()) : null)
    {
    }

    internal UsbIsochronousInTransferPacket(UsbTransferStatus status, JsBufferSource? data)
    {
        Status = status;
        _data = data;
    }

    private readonly JsBufferSource? _data;

    /// <summary>Gets the data received, or null.</summary>
    [NotInIdl("The bytes for the .NET code: the data attribute is DataView")]
    public ReadOnlyMemory<byte>? Data => _data is null ? (ReadOnlyMemory<byte>?)null : _data.Bytes;

    /// <summary>Gets the <c>DataView</c> of the data for the scripts (a view of the buffer of the data of the result of a transfer): use <see cref="Data"/> in .NET code.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbisochronousintransferpacket-data"/>
    [IdlMember("data", "https://usb.spec.whatwg.org/#dom-usbisochronousintransferpacket-data")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public object? DataView => _data;

    /// <summary>Gets the status.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbisochronousintransferpacket-status"/>
    [IdlMember("status", "https://usb.spec.whatwg.org/#dom-usbisochronousintransferpacket-status")]
    public UsbTransferStatus Status { get; }
}

/// <summary>The result of an isochronous IN transfer (https://usb.spec.whatwg.org/#usbisochronousintransferresult).</summary>
/// <seealso href="https://usb.spec.whatwg.org/#usbisochronousintransferresult"/>
[IdlInterface("USBIsochronousInTransferResult", "https://usb.spec.whatwg.org/#usbisochronousintransferresult")]
public sealed class UsbIsochronousInTransferResult
{
    /// <summary>Creates a result.</summary>
    /// <param name="packets">The packets.</param>
    /// <param name="data">The data of all the packets, or null.</param>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbisochronousintransferresult-usbisochronousintransferresult"/>
    [IdlMember("constructor", "https://usb.spec.whatwg.org/#dom-usbisochronousintransferresult-usbisochronousintransferresult")]
    public UsbIsochronousInTransferResult(IReadOnlyList<UsbIsochronousInTransferPacket> packets, ReadOnlyMemory<byte>? data = null)
        : this(packets, data is { } bytes ? new JsBufferSource(JsBufferKind.DataView, bytes.ToArray()) : null)
    {
    }

    internal UsbIsochronousInTransferResult(IReadOnlyList<UsbIsochronousInTransferPacket> packets, JsBufferSource? data)
    {
        ArgumentNullException.ThrowIfNull(packets);
        Packets = [.. packets];
        _data = data;
    }

    private readonly JsBufferSource? _data;

    /// <summary>Gets the data of all the packets, or null.</summary>
    [NotInIdl("The bytes for the .NET code: the data attribute is DataView")]
    public ReadOnlyMemory<byte>? Data => _data is null ? (ReadOnlyMemory<byte>?)null : _data.Bytes;

    /// <summary>Gets the <c>DataView</c> of the data for the scripts (the same object at each read): use <see cref="Data"/> in .NET code.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbisochronousintransferresult-data"/>
    [IdlMember("data", "https://usb.spec.whatwg.org/#dom-usbisochronousintransferresult-data")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public object? DataView => _data;

    /// <summary>Gets the packets.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbisochronousintransferresult-packets"/>
    [IdlMember("packets", "https://usb.spec.whatwg.org/#dom-usbisochronousintransferresult-packets")]
    public IReadOnlyList<UsbIsochronousInTransferPacket> Packets { get; }
}

/// <summary>A packet of an isochronous OUT transfer (https://usb.spec.whatwg.org/#usbisochronousouttransferpacket).</summary>
/// <seealso href="https://usb.spec.whatwg.org/#usbisochronousouttransferpacket"/>
[IdlInterface("USBIsochronousOutTransferPacket", "https://usb.spec.whatwg.org/#usbisochronousouttransferpacket")]
public sealed class UsbIsochronousOutTransferPacket
{
    /// <summary>Creates a packet.</summary>
    /// <param name="status">The status.</param>
    /// <param name="bytesWritten">The number of bytes sent.</param>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbisochronousouttransferpacket-usbisochronousouttransferpacket"/>
    [IdlMember("constructor", "https://usb.spec.whatwg.org/#dom-usbisochronousouttransferpacket-usbisochronousouttransferpacket")]
    public UsbIsochronousOutTransferPacket(UsbTransferStatus status, uint bytesWritten = 0)
    {
        Status = status;
        BytesWritten = bytesWritten;
    }

    /// <summary>Gets the number of bytes sent.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbisochronousouttransferpacket-byteswritten"/>
    [IdlMember("bytesWritten", "https://usb.spec.whatwg.org/#dom-usbisochronousouttransferpacket-byteswritten")]
    public uint BytesWritten { get; }

    /// <summary>Gets the status.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbisochronousouttransferpacket-status"/>
    [IdlMember("status", "https://usb.spec.whatwg.org/#dom-usbisochronousouttransferpacket-status")]
    public UsbTransferStatus Status { get; }
}

/// <summary>The result of an isochronous OUT transfer (https://usb.spec.whatwg.org/#usbisochronousouttransferresult).</summary>
/// <seealso href="https://usb.spec.whatwg.org/#usbisochronousouttransferresult"/>
[IdlInterface("USBIsochronousOutTransferResult", "https://usb.spec.whatwg.org/#usbisochronousouttransferresult")]
public sealed class UsbIsochronousOutTransferResult
{
    /// <summary>Creates a result.</summary>
    /// <param name="packets">The packets.</param>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbisochronousouttransferresult-usbisochronousouttransferresult"/>
    [IdlMember("constructor", "https://usb.spec.whatwg.org/#dom-usbisochronousouttransferresult-usbisochronousouttransferresult")]
    public UsbIsochronousOutTransferResult(IReadOnlyList<UsbIsochronousOutTransferPacket> packets)
    {
        ArgumentNullException.ThrowIfNull(packets);
        Packets = [.. packets];
    }

    /// <summary>Gets the packets.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usbisochronousouttransferresult-packets"/>
    [IdlMember("packets", "https://usb.spec.whatwg.org/#dom-usbisochronousouttransferresult-packets")]
    public IReadOnlyList<UsbIsochronousOutTransferPacket> Packets { get; }
}

/// <summary>
/// The WebUSB API of a window or a worker (https://usb.spec.whatwg.org/#usb, <c>navigator.usb</c>): the devices of the
/// <see cref="UsbProvider"/> of the browsing context (<see cref="BrowsingContextOptions.Usb"/>) that the origin is allowed
/// to use.
/// </summary>
/// <seealso href="https://usb.spec.whatwg.org/#usb"/>
[IdlInterface("USB", "https://usb.spec.whatwg.org/#usb")]
public sealed class Usb : EventTarget
{
    private readonly GlobalUsb _state;

    internal Usb(GlobalUsb state)
    {
        _state = state;
    }

    internal override GlobalScope? RelevantGlobal => _state.Global;

    /// <summary>Gets or sets the event handler of the <c>connect</c> event (<see cref="UsbConnectionEvent"/>).</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usb-onconnect"/>
    [IdlMember("onconnect", "https://usb.spec.whatwg.org/#dom-usb-onconnect")]
    public Action<Event>? OnConnect
    {
        get => GetEventHandler("connect");
        set => SetEventHandler("connect", value);
    }

    /// <summary>Gets or sets the event handler of the <c>disconnect</c> event (<see cref="UsbConnectionEvent"/>).</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usb-ondisconnect"/>
    [IdlMember("ondisconnect", "https://usb.spec.whatwg.org/#dom-usb-ondisconnect")]
    public Action<Event>? OnDisconnect
    {
        get => GetEventHandler("disconnect");
        set => SetEventHandler("disconnect", value);
    }

    /// <summary>
    /// Returns the connected devices that the origin is allowed to use (https://usb.spec.whatwg.org/#dom-usb-getdevices),
    /// except the blocklisted ones. The task fails with a <c>SecurityError</c> <see cref="DomException"/> when the
    /// <c>usb</c> permissions policy feature is not allowed, or the top-level origin is opaque. A prerendering document gets
    /// the devices once it is activated.
    /// </summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usb-getdevices"/>
    [IdlMember("getDevices", "https://usb.spec.whatwg.org/#dom-usb-getdevices")]
    public Task<IReadOnlyList<UsbDevice>> GetDevices() => _state.GetDevices();

    /// <summary>
    /// Asks the user to choose a device (https://usb.spec.whatwg.org/#dom-usb-requestdevice) with
    /// <see cref="UsbProvider.RequestDevice"/>: the origin is then allowed to use it. The window must have transient
    /// activation. The task fails with a <see cref="TypeErrorException"/> for an invalid filter, a <c>SecurityError</c>
    /// <see cref="DomException"/> without user activation, when the <c>usb</c> permissions policy feature is not allowed or
    /// the top-level origin is opaque, and a <c>NotFoundError</c> when the user does not choose a device. A prerendering
    /// document asks once it is activated.
    /// </summary>
    /// <param name="options">The filters of the devices.</param>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-usb-requestdevice"/>
    [IdlMember("requestDevice", "https://usb.spec.whatwg.org/#dom-usb-requestdevice")]
    public Task<UsbDevice> RequestDevice(UsbDeviceRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return _state.RequestDevice(options);
    }
}

/// <summary>
/// The USB state of a window or a worker: its <see cref="Usb"/> object, and the <see cref="UsbDevice"/> object of each
/// <see cref="UsbHostDevice"/> it can use. The connections and the disconnections of the provider run in tasks.
/// </summary>
internal sealed class GlobalUsb
{
    private readonly Dictionary<UsbHostDevice, UsbDevice> _devices = [];

    public GlobalUsb(GlobalScope global)
    {
        Global = global;
        Usb = new Usb(this);
        Provider?.Register(this);
    }

    public GlobalScope Global { get; }

    public Usb Usb { get; }

    private UsbProvider? Provider => Global.Options.Usb?.Provider;

    private string Origin => Global.OriginRecord.Serialize();

    /// <summary>The devices of the window, or of the documents that own the worker.</summary>
    private IEnumerable<Document> Documents => PressureObserver.GetOwningDocumentSet(Global);

    // https://wicg.github.io/nav-speculation/prerendering.html#webusb-patch: a prerendering document gets the devices once it is activated
    public Task<IReadOnlyList<UsbDevice>> GetDevices() => Document.DelayWhilePrerendering(Global, GetDevicesCore);

    public Task<UsbDevice> RequestDevice(UsbDeviceRequestOptions options) => Document.DelayWhilePrerendering(Global, () => RequestDeviceCore(options));

    private Task<IReadOnlyList<UsbDevice>> GetDevicesCore()
    {
        if (GetSecurityError() is { } error)
            return Task.FromException<IReadOnlyList<UsbDevice>>(error);

        var result = new TaskCompletionSource<IReadOnlyList<UsbDevice>>();
        Global.QueueGlobalTask(() =>
        {
            var devices = new List<UsbDevice>();
            if (Provider is { } provider)
            {
                foreach (var device in provider.Devices)
                {
                    if (IsAllowed(provider, device))
                    {
                        devices.Add(GetOrCreate(device));
                    }
                }
            }

            result.TrySetResult(devices);
        });
        return result.Task;
    }

    private Task<UsbDevice> RequestDeviceCore(UsbDeviceRequestOptions options)
    {
        if (GetSecurityError() is { } error)
            return Task.FromException<UsbDevice>(error);

        if (options.Filters.Any(filter => !filter.IsValid) || options.ExclusionFilters.Any(filter => !filter.IsValid))
            return Task.FromException<UsbDevice>(new TypeErrorException("A filter is invalid: a productId requires a vendorId, a subclassCode a classCode, and a protocolCode a subclassCode"));

        if (Global is not Window { HasTransientActivation: true } window)
            return Task.FromException<UsbDevice>(new DomException("requestDevice() requires a user activation", "SecurityError"));

        var result = new TaskCompletionSource<UsbDevice>();
        if (Provider is not { } provider)
        {
            Global.QueueGlobalTask(() => result.TrySetException(DomException.NotFound("No device was selected")));
            return result.Task;
        }

        // In parallel: the devices that match the filters are offered to the user
        var candidates = provider.Devices
            .Where(device => !UsbBlocklist.IsBlocklisted(device.Descriptor))
            .Where(device => options.Filters.Count == 0 || options.Filters.Any(filter => filter.Matches(device.Descriptor)))
            .Where(device => !options.ExclusionFilters.Any(filter => filter.Matches(device.Descriptor)))
            .ToArray();
        Task<UsbHostDevice?> selection;
        try
        {
            selection = provider.RequestDevice(new UsbDeviceRequest(CallbackContext.For(window), options.Filters, options.ExclusionFilters, candidates));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            selection = Task.FromException<UsbHostDevice?>(exception);
        }

        _ = selection.ContinueWith(
            task => Global.QueueGlobalTask(() =>
            {
                var device = task.IsCompletedSuccessfully ? task.Result : null;
                if (device is null || !candidates.Contains(device) || device.Provider != provider)
                {
                    result.TrySetException(DomException.NotFound("No device was selected"));
                    return;
                }

                provider.Allow(device, Origin);
                result.TrySetResult(GetOrCreate(device));
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return result.Task;
    }

    /// <summary>https://usb.spec.whatwg.org/#dom-usbdevice-forget: the origin is not allowed to use the device anymore.</summary>
    public void Forget(UsbHostDevice device) => Provider?.Revoke(device, Origin);

    /// <summary>A device was connected (https://usb.spec.whatwg.org/#events). Called from any thread.</summary>
    public void OnConnected(UsbHostDevice device) => Global.QueueGlobalTask(() =>
    {
        if (Provider is not { } provider || !IsAllowed(provider, device) || GetSecurityError() is not null)
            return;

        Usb.FireEvent(new UsbConnectionEvent("connect", GetOrCreate(device)));
    });

    /// <summary>The window or the worker is destroyed: the sessions of its devices end (as when their objects are collected).</summary>
    public void OnDestroyed()
    {
        foreach (var device in _devices.Values)
        {
            device.OnContextDestroyed();
        }
    }

    /// <summary>A device was disconnected: its object is closed, and the window or the worker that sees it gets a <c>disconnect</c> event. Called from any thread.</summary>
    public void OnDisconnected(UsbHostDevice device) => Global.QueueGlobalTask(() =>
    {
        if (!_devices.Remove(device, out var deviceObject))
            return;

        deviceObject.OnDisconnected();
        Usb.FireEvent(new UsbConnectionEvent("disconnect", deviceObject));
    });

    private bool IsAllowed(UsbProvider provider, UsbHostDevice device) => !UsbBlocklist.IsBlocklisted(device.Descriptor) && provider.IsAllowed(device, Origin);

    private UsbDevice GetOrCreate(UsbHostDevice device)
    {
        if (!_devices.TryGetValue(device, out var deviceObject))
        {
            deviceObject = new UsbDevice(this, device);
            _devices.Add(device, deviceObject);
        }

        return deviceObject;
    }

    /// <summary>
    /// The usb permissions policy feature (default allowlist 'self'), checked on the documents that own a worker, and the
    /// top-level origin, which must not be opaque (as in Chromium).
    /// </summary>
    private DomException? GetSecurityError()
    {
        foreach (var document in Documents)
        {
            if (document.TopLevelOrigin.IsOpaque)
                return new DomException("WebUSB cannot be used by a document whose top-level origin is opaque", "SecurityError");

            if (!PermissionsPolicyAlgorithms.IsAllowedToUse(document, UsbFeature))
                return new DomException("The document is not allowed to use the usb feature", "SecurityError");
        }

        return null;
    }

    private static PolicyControlledFeature UsbFeature => PolicyControlledFeature.Get("usb")!;
}

// https://usb.spec.whatwg.org/#navigator-usb
public sealed partial class Navigator
{
    /// <summary>Gets the WebUSB API of the window.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-navigator-usb"/>
    [IdlMember("usb", "https://usb.spec.whatwg.org/#dom-navigator-usb")]
    public Usb Usb => _window.GetUsbState().Usb;
}

// https://usb.spec.whatwg.org/#navigator-usb
public sealed partial class WorkerNavigator
{
    /// <summary>Gets the WebUSB API of the worker.</summary>
    /// <seealso href="https://usb.spec.whatwg.org/#dom-workernavigator-usb"/>
    [IdlMember("usb", "https://usb.spec.whatwg.org/#dom-workernavigator-usb")]
    public Usb Usb => _global.GetUsbState().Usb;
}

public abstract partial class GlobalScope
{
    private GlobalUsb? _usbState;

    /// <summary>The USB state of the global object, created by the first access to <c>navigator.usb</c>.</summary>
    internal GlobalUsb GetUsbState() => _usbState ??= new GlobalUsb(this);

    /// <summary>The window is unloaded or the worker is terminated: the sessions of its USB devices end.</summary>
    internal void CloseUsbDevices() => _usbState?.OnDestroyed();
}
