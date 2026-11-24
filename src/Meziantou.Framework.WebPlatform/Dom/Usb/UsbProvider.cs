namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The host hooks of WebUSB (https://usb.spec.whatwg.org/, <see cref="BrowsingContextOptions.Usb"/>).</summary>
public sealed class UsbOptions
{
    /// <summary>
    /// Gets or sets the USB devices of the machine: a <see cref="UsbProvider"/> to which the application connects the
    /// <see cref="UsbHostDevice"/> objects that implement the devices. A provider can be shared by several browsing contexts.
    /// The default is null: no USB device is connected.
    /// </summary>
    public UsbProvider? Provider { get; init; }
}

/// <summary>
/// The USB devices connected to the machine, used by the windows and the workers of the browsing contexts that share a
/// <see cref="BrowsingContextOptions"/> (<see cref="UsbOptions.Provider"/>): <see cref="Connect"/> and
/// <see cref="Disconnect"/> fire the <c>connect</c> and <c>disconnect</c> events of <c>navigator.usb</c>, and the
/// permission storage gives each origin the devices it can use. Override <see cref="RequestDevice"/> to play the role of
/// the device chooser of <c>navigator.usb.requestDevice()</c>.
/// </summary>
/// <remarks>A provider can be shared by several browsing contexts, and used from any thread.</remarks>
public class UsbProvider
{
    private readonly Lock _lock = new();
    private readonly List<UsbHostDevice> _devices = [];
    private readonly List<WeakReference<GlobalUsb>> _globals = [];
    private readonly Dictionary<string, HashSet<UsbHostDevice>> _allowedDevices = new(StringComparer.Ordinal);

    /// <summary>Gets the connected devices, in the order they were connected.</summary>
    public IReadOnlyList<UsbHostDevice> Devices
    {
        get
        {
            lock (_lock)
            {
                return [.. _devices];
            }
        }
    }

    /// <summary>
    /// Connects a device: the windows and the workers of the origins that are allowed to use it get a <c>connect</c> event.
    /// Can be called from any thread.
    /// </summary>
    /// <param name="device">The device. A device can be connected to one provider at a time.</param>
    /// <exception cref="InvalidOperationException">The device is connected to a provider.</exception>
    public void Connect(UsbHostDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        lock (_lock)
        {
            if (device.Provider is not null)
                throw new InvalidOperationException("The device is already connected");

            device.Provider = this;
            _devices.Add(device);
        }

        foreach (var global in GetGlobals())
        {
            global.OnConnected(device);
        }
    }

    /// <summary>
    /// Disconnects a device: its <c>USBDevice</c> objects are closed, their pending operations fail with a
    /// <c>NotFoundError</c>, and the windows and the workers that see it get a <c>disconnect</c> event. Can be called from
    /// any thread.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <returns>Whether the device was connected to this provider.</returns>
    public bool Disconnect(UsbHostDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        lock (_lock)
        {
            if (!_devices.Remove(device))
                return false;

            device.Provider = null;
        }

        foreach (var global in GetGlobals())
        {
            global.OnDisconnected(device);
        }

        return true;
    }

    /// <summary>
    /// Returns whether an origin can use a device without asking the user (the USB permission storage,
    /// https://usb.spec.whatwg.org/#usbpermissionstorage): the default implementation returns whether the device was
    /// allowed with <see cref="Allow"/>, or chosen by the user with <see cref="RequestDevice"/>, and not forgotten since.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <param name="origin">The serialization of the origin of the window or the worker (e.g. <c>https://example.com</c>).</param>
    public virtual bool IsAllowed(UsbHostDevice device, string origin)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(origin);
        lock (_lock)
        {
            return _allowedDevices.TryGetValue(origin, out var devices) && devices.Contains(device);
        }
    }

    /// <summary>Allows an origin to use a device (https://usb.spec.whatwg.org/#add-an-allowed-usb-device).</summary>
    /// <param name="device">The device.</param>
    /// <param name="origin">The serialization of the origin.</param>
    public void Allow(UsbHostDevice device, string origin)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(origin);
        lock (_lock)
        {
            if (!_allowedDevices.TryGetValue(origin, out var devices))
            {
                devices = [];
                _allowedDevices.Add(origin, devices);
            }

            devices.Add(device);
        }
    }

    /// <summary>Revokes the permission of an origin to use a device (https://usb.spec.whatwg.org/#remove-an-allowed-usb-device), as <c>USBDevice.forget()</c> does.</summary>
    /// <param name="device">The device.</param>
    /// <param name="origin">The serialization of the origin.</param>
    public void Revoke(UsbHostDevice device, string origin)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(origin);
        lock (_lock)
        {
            if (_allowedDevices.TryGetValue(origin, out var devices))
            {
                devices.Remove(device);
            }
        }
    }

    /// <summary>
    /// Asks the user to choose a device for <c>navigator.usb.requestDevice()</c> (https://usb.spec.whatwg.org/#request-the-usb-permission):
    /// <see cref="UsbDeviceRequest.Devices"/> are the connected devices that match the filters of the page. Returns one of
    /// them, which the origin is then allowed to use, or null when the user cancels (the request fails with a
    /// <c>NotFoundError</c>). The default implementation returns null.
    /// </summary>
    /// <param name="request">The request of the window.</param>
    public virtual Task<UsbHostDevice?> RequestDevice(UsbDeviceRequest request) => Task.FromResult<UsbHostDevice?>(null);

    /// <summary>Registers the USB objects of a window or a worker, which then get the connections and the disconnections.</summary>
    internal void Register(GlobalUsb global)
    {
        lock (_lock)
        {
            _globals.RemoveAll(static reference => !reference.TryGetTarget(out _));
            _globals.Add(new WeakReference<GlobalUsb>(global));
        }
    }

    private List<GlobalUsb> GetGlobals()
    {
        lock (_lock)
        {
            var result = new List<GlobalUsb>(_globals.Count);
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

/// <summary>A request of <c>navigator.usb.requestDevice()</c>, given to <see cref="UsbProvider.RequestDevice"/>.</summary>
public sealed class UsbDeviceRequest
{
    internal UsbDeviceRequest(CallbackContext context, IReadOnlyList<UsbDeviceFilter> filters, IReadOnlyList<UsbDeviceFilter> exclusionFilters, IReadOnlyList<UsbHostDevice> devices)
    {
        Context = context;
        Filters = filters;
        ExclusionFilters = exclusionFilters;
        Devices = devices;
    }

    /// <summary>Gets the context of the window that requests the device.</summary>
    public CallbackContext Context { get; }

    /// <summary>Gets the filters of the page: a device matches one of them (all the devices when there is none).</summary>
    public IReadOnlyList<UsbDeviceFilter> Filters { get; }

    /// <summary>Gets the exclusion filters of the page: a device matches none of them.</summary>
    public IReadOnlyList<UsbDeviceFilter> ExclusionFilters { get; }

    /// <summary>Gets the devices to choose from: the connected devices that match the filters and are not blocklisted.</summary>
    public IReadOnlyList<UsbHostDevice> Devices { get; }
}

/// <summary>
/// A USB device of a <see cref="UsbProvider"/>: its descriptors, and the sessions that the <c>USBDevice</c> objects of the
/// pages open with <see cref="OpenAsync"/>. Each <c>USBDevice</c> (one per window or worker) opens its own session.
/// </summary>
public abstract class UsbHostDevice
{
    /// <summary>Creates a device.</summary>
    /// <param name="descriptor">The descriptors of the device.</param>
    protected UsbHostDevice(UsbDeviceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        Descriptor = descriptor;
    }

    /// <summary>Gets the descriptors of the device.</summary>
    public UsbDeviceDescriptor Descriptor { get; }

    /// <summary>Gets the configuration of the device when a page gets it (GET_CONFIGURATION), or 0 when it is not configured. The default implementation returns <see cref="UsbDeviceDescriptor.ActiveConfigurationValue"/>.</summary>
    public virtual byte ActiveConfigurationValue => Descriptor.ActiveConfigurationValue;

    internal UsbProvider? Provider { get; set; }

    /// <summary>
    /// Begins a session with the device (<c>USBDevice.open()</c>). Throw a <see cref="UsbException"/> to report an error
    /// (any other exception is a <c>NetworkError</c>). The token is canceled when the operation is aborted (the device is
    /// closed or disconnected).
    /// </summary>
    /// <param name="cancellationToken">The token canceled when the operation is aborted.</param>
    public abstract Task<UsbDeviceSession> OpenAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A session of a page with a <see cref="UsbHostDevice"/>, opened by <c>USBDevice.open()</c>: the operations of the page,
/// which the library validates first (the configuration, the claimed interfaces, the endpoints, the protected classes). A
/// method throws a <see cref="UsbException"/> to report an error (any other exception is a <c>NetworkError</c>); the token
/// is canceled when the operation is aborted (<c>close()</c>, <c>reset()</c>, a new configuration or alternate setting, a
/// disconnection).
/// </summary>
public abstract class UsbDeviceSession
{
    /// <summary>Ends the session (<c>USBDevice.close()</c>): the interfaces that the page claimed are released.</summary>
    public abstract Task CloseAsync();

    /// <summary>Issues a SET_CONFIGURATION request (<c>selectConfiguration()</c>).</summary>
    /// <param name="configurationValue">The value of a configuration of the device.</param>
    /// <param name="cancellationToken">The token canceled when the operation is aborted.</param>
    public abstract Task SelectConfigurationAsync(byte configurationValue, CancellationToken cancellationToken);

    /// <summary>Requests the exclusive control of an interface of the current configuration (<c>claimInterface()</c>).</summary>
    /// <param name="interfaceNumber">The number of the interface.</param>
    /// <param name="cancellationToken">The token canceled when the operation is aborted.</param>
    public abstract Task ClaimInterfaceAsync(byte interfaceNumber, CancellationToken cancellationToken);

    /// <summary>Releases an interface (<c>releaseInterface()</c>).</summary>
    /// <param name="interfaceNumber">The number of the interface.</param>
    /// <param name="cancellationToken">The token canceled when the operation is aborted.</param>
    public abstract Task ReleaseInterfaceAsync(byte interfaceNumber, CancellationToken cancellationToken);

    /// <summary>Issues a SET_INTERFACE request (<c>selectAlternateInterface()</c>).</summary>
    /// <param name="interfaceNumber">The number of a claimed interface.</param>
    /// <param name="alternateSetting">The alternate setting.</param>
    /// <param name="cancellationToken">The token canceled when the operation is aborted.</param>
    public abstract Task SelectAlternateInterfaceAsync(byte interfaceNumber, byte alternateSetting, CancellationToken cancellationToken);

    /// <summary>Soft resets the device (<c>reset()</c>).</summary>
    /// <param name="cancellationToken">The token canceled when the operation is aborted.</param>
    public abstract Task ResetAsync(CancellationToken cancellationToken);

    /// <summary>Clears the halt condition of an endpoint (<c>clearHalt()</c>).</summary>
    /// <param name="direction">The direction of the endpoint.</param>
    /// <param name="endpointNumber">The number of the endpoint.</param>
    /// <param name="cancellationToken">The token canceled when the operation is aborted.</param>
    public abstract Task ClearHaltAsync(UsbDirection direction, byte endpointNumber, CancellationToken cancellationToken);

    /// <summary>Issues a control transfer from the device (<c>controlTransferIn()</c>).</summary>
    /// <param name="setup">The setup packet.</param>
    /// <param name="length">The number of bytes to receive (wLength).</param>
    /// <param name="cancellationToken">The token canceled when the operation is aborted.</param>
    public abstract Task<UsbInTransfer> ControlTransferInAsync(UsbControlTransferSetup setup, ushort length, CancellationToken cancellationToken);

    /// <summary>Issues a control transfer to the device (<c>controlTransferOut()</c>).</summary>
    /// <param name="setup">The setup packet.</param>
    /// <param name="data">The data stage.</param>
    /// <param name="cancellationToken">The token canceled when the operation is aborted.</param>
    public abstract Task<UsbOutTransfer> ControlTransferOutAsync(UsbControlTransferSetup setup, ReadOnlyMemory<byte> data, CancellationToken cancellationToken);

    /// <summary>Performs a bulk or interrupt IN transfer (<c>transferIn()</c>).</summary>
    /// <param name="endpointNumber">The number of an IN endpoint of a claimed interface.</param>
    /// <param name="length">The number of bytes to receive.</param>
    /// <param name="cancellationToken">The token canceled when the operation is aborted.</param>
    public abstract Task<UsbInTransfer> TransferInAsync(byte endpointNumber, uint length, CancellationToken cancellationToken);

    /// <summary>Performs a bulk or interrupt OUT transfer (<c>transferOut()</c>).</summary>
    /// <param name="endpointNumber">The number of an OUT endpoint of a claimed interface.</param>
    /// <param name="data">The data to send.</param>
    /// <param name="cancellationToken">The token canceled when the operation is aborted.</param>
    public abstract Task<UsbOutTransfer> TransferOutAsync(byte endpointNumber, ReadOnlyMemory<byte> data, CancellationToken cancellationToken);

    /// <summary>Performs an isochronous IN transfer (<c>isochronousTransferIn()</c>): returns a packet for each length.</summary>
    /// <param name="endpointNumber">The number of an IN endpoint of a claimed interface.</param>
    /// <param name="packetLengths">The length of each packet.</param>
    /// <param name="cancellationToken">The token canceled when the operation is aborted.</param>
    public abstract Task<IReadOnlyList<UsbInTransfer>> IsochronousTransferInAsync(byte endpointNumber, IReadOnlyList<uint> packetLengths, CancellationToken cancellationToken);

    /// <summary>Performs an isochronous OUT transfer (<c>isochronousTransferOut()</c>): returns a packet for each length.</summary>
    /// <param name="endpointNumber">The number of an OUT endpoint of a claimed interface.</param>
    /// <param name="data">The data to send, divided into packets of the lengths.</param>
    /// <param name="packetLengths">The length of each packet.</param>
    /// <param name="cancellationToken">The token canceled when the operation is aborted.</param>
    public abstract Task<IReadOnlyList<UsbOutTransfer>> IsochronousTransferOutAsync(byte endpointNumber, ReadOnlyMemory<byte> data, IReadOnlyList<uint> packetLengths, CancellationToken cancellationToken);
}

/// <summary>The setup packet of a control transfer.</summary>
/// <param name="RequestType">The type of the request (bits 5-6 of bmRequestType).</param>
/// <param name="Recipient">The recipient of the request (bits 0-4 of bmRequestType).</param>
/// <param name="Request">The request (bRequest).</param>
/// <param name="Value">The value (wValue).</param>
/// <param name="Index">The index (wIndex).</param>
public sealed record UsbControlTransferSetup(UsbRequestType RequestType, UsbRecipient Recipient, byte Request, ushort Value, ushort Index);

/// <summary>The result of an IN transfer, or of a packet of an isochronous IN transfer.</summary>
/// <param name="Status">The status: <see cref="UsbTransferStatus.Stall"/> when the device stalled the endpoint, <see cref="UsbTransferStatus.Babble"/> when it sent more data than requested.</param>
/// <param name="Data">The data received.</param>
public sealed record UsbInTransfer(UsbTransferStatus Status, ReadOnlyMemory<byte> Data);

/// <summary>The result of an OUT transfer, or of a packet of an isochronous OUT transfer.</summary>
/// <param name="Status">The status: <see cref="UsbTransferStatus.Stall"/> when the device stalled the endpoint.</param>
/// <param name="BytesWritten">The number of bytes sent.</param>
public sealed record UsbOutTransfer(UsbTransferStatus Status, uint BytesWritten);

/// <summary>The errors of the operations of a <see cref="UsbHostDevice"/>, and the <see cref="DomException"/> names the pages get.</summary>
public enum UsbError
{
    /// <summary>The operation failed (<c>NetworkError</c>).</summary>
    Failed,

    /// <summary>The operating system denied the access to the device (<c>SecurityError</c>).</summary>
    AccessDenied,

    /// <summary>The transfer timed out (<c>TimeoutError</c>).</summary>
    Timeout,

    /// <summary>The transfer was canceled (<c>AbortError</c>).</summary>
    Cancelled,

    /// <summary>The device was disconnected (<c>NotFoundError</c>).</summary>
    Disconnected,
}

/// <summary>An error of an operation of a <see cref="UsbHostDevice"/> or a <see cref="UsbDeviceSession"/>.</summary>
public sealed class UsbException : Exception
{
    /// <summary>Creates an exception.</summary>
    public UsbException()
        : this(UsbError.Failed)
    {
    }

    /// <summary>Creates an exception.</summary>
    /// <param name="message">The message.</param>
    public UsbException(string message)
        : this(UsbError.Failed, message)
    {
    }

    /// <summary>Creates an exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public UsbException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception.</summary>
    /// <param name="error">The error.</param>
    /// <param name="message">The message, or null.</param>
    public UsbException(UsbError error, string? message = null)
        : base(message ?? $"The USB operation failed ({error})")
    {
        Error = error;
    }

    /// <summary>Gets the error.</summary>
    public UsbError Error { get; }

    internal DomException ToDomException() => new(Message, Error switch
    {
        UsbError.AccessDenied => "SecurityError",
        UsbError.Timeout => "TimeoutError",
        UsbError.Cancelled => "AbortError",
        UsbError.Disconnected => "NotFoundError",
        _ => "NetworkError",
    });
}

/// <summary>The descriptors of a USB device (https://usb.spec.whatwg.org/#appendix-intro).</summary>
public sealed class UsbDeviceDescriptor
{
    /// <summary>Gets the major version of the USB protocol (bcdUSB).</summary>
    public byte UsbVersionMajor { get; init; }

    /// <summary>Gets the minor version of the USB protocol (bcdUSB).</summary>
    public byte UsbVersionMinor { get; init; }

    /// <summary>Gets the subminor version of the USB protocol (bcdUSB).</summary>
    public byte UsbVersionSubminor { get; init; }

    /// <summary>Gets the class of the device (bDeviceClass).</summary>
    public byte DeviceClass { get; init; }

    /// <summary>Gets the subclass of the device (bDeviceSubClass).</summary>
    public byte DeviceSubclass { get; init; }

    /// <summary>Gets the protocol of the device (bDeviceProtocol).</summary>
    public byte DeviceProtocol { get; init; }

    /// <summary>Gets the vendor ID (idVendor).</summary>
    public ushort VendorId { get; init; }

    /// <summary>Gets the product ID (idProduct).</summary>
    public ushort ProductId { get; init; }

    /// <summary>Gets the major version of the device (bcdDevice).</summary>
    public byte DeviceVersionMajor { get; init; }

    /// <summary>Gets the minor version of the device (bcdDevice).</summary>
    public byte DeviceVersionMinor { get; init; }

    /// <summary>Gets the subminor version of the device (bcdDevice).</summary>
    public byte DeviceVersionSubminor { get; init; }

    /// <summary>Gets the name of the manufacturer (iManufacturer), or null.</summary>
    public string? ManufacturerName { get; init; }

    /// <summary>Gets the name of the product (iProduct), or null.</summary>
    public string? ProductName { get; init; }

    /// <summary>Gets the serial number (iSerialNumber), or null.</summary>
    public string? SerialNumber { get; init; }

    /// <summary>Gets the value of the configuration that the device is in when it is connected, or 0 (the default) when it is not configured.</summary>
    public byte ActiveConfigurationValue { get; init; }

    /// <summary>Gets the configurations of the device.</summary>
    public IReadOnlyList<UsbConfigurationDescriptor> Configurations { get; init; } = [];
}

/// <summary>A configuration descriptor of a USB device.</summary>
public sealed class UsbConfigurationDescriptor
{
    /// <summary>Gets the value of the configuration (bConfigurationValue).</summary>
    public byte ConfigurationValue { get; init; }

    /// <summary>Gets the name of the configuration (iConfiguration), or null.</summary>
    public string? ConfigurationName { get; init; }

    /// <summary>Gets the interfaces of the configuration.</summary>
    public IReadOnlyList<UsbInterfaceDescriptor> Interfaces { get; init; } = [];
}

/// <summary>An interface of a configuration of a USB device: its alternate settings.</summary>
public sealed class UsbInterfaceDescriptor
{
    /// <summary>Gets the number of the interface (bInterfaceNumber).</summary>
    public byte InterfaceNumber { get; init; }

    /// <summary>Gets the alternate settings of the interface (an interface has at least the alternate setting 0).</summary>
    public IReadOnlyList<UsbAlternateInterfaceDescriptor> Alternates { get; init; } = [];
}

/// <summary>An interface descriptor of a USB device: an alternate setting of an interface.</summary>
public sealed class UsbAlternateInterfaceDescriptor
{
    /// <summary>Gets the alternate setting (bAlternateSetting).</summary>
    public byte AlternateSetting { get; init; }

    /// <summary>Gets the class of the interface (bInterfaceClass).</summary>
    public byte InterfaceClass { get; init; }

    /// <summary>Gets the subclass of the interface (bInterfaceSubClass).</summary>
    public byte InterfaceSubclass { get; init; }

    /// <summary>Gets the protocol of the interface (bInterfaceProtocol).</summary>
    public byte InterfaceProtocol { get; init; }

    /// <summary>Gets the name of the interface (iInterface), or null.</summary>
    public string? InterfaceName { get; init; }

    /// <summary>Gets the endpoints of the alternate setting (the control endpoints are ignored).</summary>
    public IReadOnlyList<UsbEndpointDescriptor> Endpoints { get; init; } = [];
}

/// <summary>An endpoint descriptor of a USB device.</summary>
public sealed class UsbEndpointDescriptor
{
    /// <summary>Gets the number of the endpoint (the 4 low bits of bEndpointAddress, from 1 to 15).</summary>
    public byte EndpointNumber { get; init; }

    /// <summary>Gets the direction of the endpoint (the high bit of bEndpointAddress).</summary>
    public UsbDirection Direction { get; init; }

    /// <summary>Gets the type of the endpoint (bmAttributes).</summary>
    public UsbEndpointType Type { get; init; }

    /// <summary>Gets the maximum packet size (wMaxPacketSize).</summary>
    public uint PacketSize { get; init; }
}

/// <summary>
/// The USB blocklist (https://usb.spec.whatwg.org/#blocklist): the devices that the pages cannot use, from
/// https://github.com/WICG/webusb/blob/04c12b125453c17c272286f57c75a9fdc12725c2/blocklist.txt.
/// </summary>
internal static class UsbBlocklist
{
    // idVendor, idProduct and the newest blocked bcdDevice (0xFFFF for every version)
    private static readonly (ushort VendorId, ushort ProductId, ushort BcdDevice)[] Entries =
    [
        (0x096E, 0x0850, 0xFFFF), (0x096E, 0x0852, 0xFFFF), (0x096E, 0x0853, 0xFFFF), (0x096E, 0x0854, 0xFFFF),
        (0x096E, 0x0856, 0xFFFF), (0x096E, 0x0858, 0xFFFF), (0x096E, 0x085A, 0xFFFF), (0x096E, 0x085B, 0xFFFF),
        (0x096E, 0x0880, 0xFFFF), (0x09C3, 0x0023, 0xFFFF), (0x1050, 0x0010, 0xFFFF), (0x1050, 0x0018, 0xFFFF),
        (0x1050, 0x0030, 0xFFFF), (0x1050, 0x0110, 0xFFFF), (0x1050, 0x0111, 0xFFFF), (0x1050, 0x0112, 0xFFFF),
        (0x1050, 0x0113, 0xFFFF), (0x1050, 0x0114, 0xFFFF), (0x1050, 0x0115, 0xFFFF), (0x1050, 0x0116, 0xFFFF),
        (0x1050, 0x0120, 0xFFFF), (0x1050, 0x0200, 0xFFFF), (0x1050, 0x0211, 0xFFFF), (0x1050, 0x0401, 0xFFFF),
        (0x1050, 0x0402, 0xFFFF), (0x1050, 0x0403, 0xFFFF), (0x1050, 0x0404, 0xFFFF), (0x1050, 0x0405, 0xFFFF),
        (0x1050, 0x0406, 0xFFFF), (0x1050, 0x0407, 0xFFFF), (0x1050, 0x0410, 0xFFFF), (0x10C4, 0x8ACF, 0xFFFF),
        (0x18D1, 0x5026, 0xFFFF), (0x1A44, 0x00BB, 0xFFFF), (0x1D50, 0x60FC, 0xFFFF), (0x1E0D, 0xF1AE, 0xFFFF),
        (0x1E0D, 0xF1D0, 0xFFFF), (0x1EA8, 0xF025, 0xFFFF), (0x20A0, 0x4287, 0xFFFF), (0x24DC, 0x0101, 0xFFFF),
        (0x2581, 0xF1D0, 0xFFFF), (0x2ABE, 0x1002, 0xFFFF), (0x2CCF, 0x0880, 0xFFFF),
    ];

    /// <summary>https://usb.spec.whatwg.org/#blocklisted (the usb-unrestricted feature is only enabled for Isolated Web Apps).</summary>
    public static bool IsBlocklisted(UsbDeviceDescriptor descriptor)
    {
        var bcdDevice = (descriptor.DeviceVersionMajor << 8) + (descriptor.DeviceVersionMinor << 4) + descriptor.DeviceVersionSubminor;
        foreach (var entry in Entries)
        {
            if (entry.VendorId == descriptor.VendorId && entry.ProductId == descriptor.ProductId && bcdDevice <= entry.BcdDevice)
                return true;
        }

        return false;
    }
}
