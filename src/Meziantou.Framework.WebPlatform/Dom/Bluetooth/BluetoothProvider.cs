using System.Security.Cryptography;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The host hooks of Web Bluetooth (https://bluetooth.spec.whatwg.org/, <see cref="BrowsingContextOptions.Bluetooth"/>).</summary>
public sealed class BluetoothOptions
{
    /// <summary>
    /// Gets or sets the Bluetooth adapter of the machine: a <see cref="BluetoothProvider"/> to which the application adds the
    /// <see cref="BluetoothHostDevice"/> objects that implement the nearby devices. A provider can be shared by several
    /// browsing contexts. The default is null: the machine has no Bluetooth adapter.
    /// </summary>
    public BluetoothProvider? Provider { get; init; }
}

/// <summary>The state of the Bluetooth adapter of a <see cref="BluetoothProvider"/>.</summary>
public enum BluetoothAdapterState
{
    /// <summary>The machine has no Bluetooth adapter: <c>getAvailability()</c> is false, and <c>requestDevice()</c> fails with a <c>NotFoundError</c>.</summary>
    Absent,

    /// <summary>The adapter is turned off: <c>getAvailability()</c> is true, but no device is found.</summary>
    PoweredOff,

    /// <summary>The adapter is turned on.</summary>
    PoweredOn,
}

/// <summary>
/// The Bluetooth adapter of the machine and its nearby devices, used by the windows of the browsing contexts that share a
/// <see cref="BrowsingContextOptions"/> (<see cref="BluetoothOptions.Provider"/>): <see cref="AddDevice"/> and
/// <see cref="RemoveDevice"/> manage the devices in range, and the permission storage gives each origin the devices it can
/// use (https://bluetooth.spec.whatwg.org/#permission-api-integration). Override <see cref="RequestDevice"/>
/// to play the role of the device chooser of <c>navigator.bluetooth.requestDevice()</c>.
/// </summary>
/// <remarks>A provider can be shared by several browsing contexts, and used from any thread.</remarks>
public class BluetoothProvider
{
    private readonly Lock _lock = new();
    private readonly List<BluetoothHostDevice> _devices = [];
    private readonly List<WeakReference<GlobalBluetooth>> _globals = [];
    private readonly Dictionary<string, List<BluetoothAllowedDevice>> _allowedDevices = new(StringComparer.Ordinal);
    private BluetoothAdapterState _adapterState = BluetoothAdapterState.PoweredOn;
    private bool _isLowEnergySupported = true;

    /// <summary>
    /// Gets or sets the state of the adapter. The default is <see cref="BluetoothAdapterState.PoweredOn"/>. A change of the
    /// availability (<see cref="IsAvailable"/>) fires the <c>availabilitychanged</c> event of <c>navigator.bluetooth</c>.
    /// </summary>
    public BluetoothAdapterState AdapterState
    {
        get
        {
            lock (_lock)
            {
                return _adapterState;
            }
        }
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));

            UpdateAvailability(() => _adapterState = value);
        }
    }

    /// <summary>Gets or sets whether the adapter supports Bluetooth Low Energy (the default is true). Web Bluetooth is not available without it.</summary>
    public bool IsLowEnergySupported
    {
        get
        {
            lock (_lock)
            {
                return _isLowEnergySupported;
            }
        }
        set => UpdateAvailability(() => _isLowEnergySupported = value);
    }

    /// <summary>Gets whether Bluetooth is available (https://bluetooth.spec.whatwg.org/#dom-bluetooth-getavailability): there is an adapter that supports Bluetooth Low Energy, even when it is turned off.</summary>
    public bool IsAvailable
    {
        get
        {
            lock (_lock)
            {
                return _adapterState != BluetoothAdapterState.Absent && _isLowEnergySupported;
            }
        }
    }

    /// <summary>Gets the devices in range, in the order they were added.</summary>
    public IReadOnlyList<BluetoothHostDevice> Devices
    {
        get
        {
            lock (_lock)
            {
                return [.. _devices];
            }
        }
    }

    /// <summary>Adds a device in range: the pages can choose it with <c>requestDevice()</c>. Can be called from any thread.</summary>
    /// <param name="device">The device. A device can be added to one provider at a time.</param>
    /// <exception cref="InvalidOperationException">The device is added to a provider.</exception>
    public void AddDevice(BluetoothHostDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        lock (_lock)
        {
            if (device.Provider is not null)
                throw new InvalidOperationException("The device is already added to a provider");

            device.Provider = this;
            _devices.Add(device);
        }
    }

    /// <summary>
    /// Removes a device: it is out of range. Its connection is lost (the pages get a <c>gattserverdisconnected</c> event), but
    /// the origins keep their permission. Can be called from any thread.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <returns>Whether the device was added to this provider.</returns>
    public bool RemoveDevice(BluetoothHostDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        lock (_lock)
        {
            if (!_devices.Remove(device))
                return false;

            device.Provider = null;
        }

        device.Disconnect();
        return true;
    }

    /// <summary>Gets the devices that an origin is allowed to use (the <c>allowedDevices</c> of its Bluetooth permission storage), in the order they were allowed.</summary>
    /// <param name="origin">The serialization of the origin of the window (e.g. <c>https://example.com</c>).</param>
    public IReadOnlyList<BluetoothAllowedDevice> GetAllowedDevices(string origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        lock (_lock)
        {
            return _allowedDevices.TryGetValue(origin, out var devices) ? [.. devices] : [];
        }
    }

    /// <summary>
    /// Allows an origin to use a device (https://bluetooth.spec.whatwg.org/#add-an-allowed-bluetooth-device): the
    /// services are added to the ones it was allowed to use before.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <param name="origin">The serialization of the origin.</param>
    /// <param name="services">The UUIDs of the services the origin can use (valid UUIDs), or null for all the services.</param>
    /// <param name="manufacturerData">The company identifiers of the manufacturer data the origin can read.</param>
    /// <returns>The permission of the origin.</returns>
    public BluetoothAllowedDevice Allow(BluetoothHostDevice device, string origin, IEnumerable<string>? services, IEnumerable<ushort>? manufacturerData = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(origin);
        var serviceSet = services is null ? null : new HashSet<string>(services, StringComparer.Ordinal);
        if (serviceSet?.FirstOrDefault(uuid => !BluetoothUuid.IsValidUuid(uuid)) is { } invalid)
            throw new ArgumentException($"'{invalid}' is not a valid UUID", nameof(services));

        lock (_lock)
        {
            if (!_allowedDevices.TryGetValue(origin, out var devices))
            {
                devices = [];
                _allowedDevices.Add(origin, devices);
            }

            var index = devices.FindIndex(allowed => allowed.Device == device);
            if (index < 0)
            {
                var allowedDevice = new BluetoothAllowedDevice(Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)), device, serviceSet, [.. manufacturerData ?? []]);
                devices.Add(allowedDevice);
                return allowedDevice;
            }

            var existing = devices[index];
            var mergedServices = existing.AllowedServices is null || serviceSet is null ? null : new HashSet<string>(existing.AllowedServices.Concat(serviceSet), StringComparer.Ordinal);
            var merged = new BluetoothAllowedDevice(existing.DeviceId, device, mergedServices, [.. existing.AllowedManufacturerData.Union(manufacturerData ?? [])]);
            devices[index] = merged;
            return merged;
        }
    }

    /// <summary>Revokes the permission of an origin to use a device (https://bluetooth.spec.whatwg.org/#remove-an-allowed-bluetooth-device), as <c>BluetoothDevice.forget()</c> does.</summary>
    /// <param name="device">The device.</param>
    /// <param name="origin">The serialization of the origin.</param>
    /// <returns>Whether the origin was allowed to use the device.</returns>
    public bool Revoke(BluetoothHostDevice device, string origin)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(origin);
        lock (_lock)
        {
            return _allowedDevices.TryGetValue(origin, out var devices) && devices.RemoveAll(allowed => allowed.Device == device) > 0;
        }
    }

    /// <summary>
    /// Asks the user to choose a device for <c>navigator.bluetooth.requestDevice()</c> (https://bluetooth.spec.whatwg.org/#request-bluetooth-devices):
    /// <see cref="BluetoothDeviceRequest.Devices"/> are the devices in range that match the filters of the page. Returns one
    /// of them, which the origin is then allowed to use, or null when the user cancels (the request fails with a
    /// <c>NotFoundError</c>). The default implementation returns null.
    /// </summary>
    /// <param name="request">The request of the window.</param>
    public virtual Task<BluetoothHostDevice?> RequestDevice(BluetoothDeviceRequest request) => Task.FromResult<BluetoothHostDevice?>(null);

    internal BluetoothAllowedDevice? GetAllowedDevice(BluetoothHostDevice device, string origin)
    {
        lock (_lock)
        {
            return _allowedDevices.TryGetValue(origin, out var devices) ? devices.Find(allowed => allowed.Device == device) : null;
        }
    }

    /// <summary>Registers the Bluetooth objects of a window, which then get the changes of availability.</summary>
    internal void Register(GlobalBluetooth global)
    {
        lock (_lock)
        {
            _globals.RemoveAll(static reference => !reference.TryGetTarget(out _));
            _globals.Add(new WeakReference<GlobalBluetooth>(global));
        }
    }

    internal List<GlobalBluetooth> GetGlobals()
    {
        lock (_lock)
        {
            var result = new List<GlobalBluetooth>(_globals.Count);
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

    // https://bluetooth.spec.whatwg.org/#availability-changed: the windows get the event when the availability changes
    private void UpdateAvailability(Action update)
    {
        bool oldAvailability;
        bool newAvailability;
        lock (_lock)
        {
            oldAvailability = _adapterState != BluetoothAdapterState.Absent && _isLowEnergySupported;
            update();
            newAvailability = _adapterState != BluetoothAdapterState.Absent && _isLowEnergySupported;
        }

        if (oldAvailability == newAvailability)
            return;

        foreach (var global in GetGlobals())
        {
            global.OnAvailabilityChanged(newAvailability);
        }
    }
}

/// <summary>A device that an origin is allowed to use (https://bluetooth.spec.whatwg.org/#dictdef-allowedbluetoothdevice).</summary>
public sealed class BluetoothAllowedDevice
{
    internal BluetoothAllowedDevice(string deviceId, BluetoothHostDevice device, IReadOnlySet<string>? allowedServices, IReadOnlyList<ushort> allowedManufacturerData)
    {
        DeviceId = deviceId;
        Device = device;
        AllowedServices = allowedServices;
        AllowedManufacturerData = allowedManufacturerData;
    }

    /// <summary>Gets the identifier of the device for the origin (<c>BluetoothDevice.id</c>): another origin gets another identifier.</summary>
    public string DeviceId { get; }

    /// <summary>Gets the device.</summary>
    public BluetoothHostDevice Device { get; }

    /// <summary>Gets the UUIDs of the services the origin can use, or null for all the services.</summary>
    public IReadOnlySet<string>? AllowedServices { get; }

    /// <summary>Gets the company identifiers of the manufacturer data the origin can read.</summary>
    public IReadOnlyList<ushort> AllowedManufacturerData { get; }

    internal bool IsServiceAllowed(string uuid) => AllowedServices is null || AllowedServices.Contains(uuid);
}

/// <summary>A request of <c>navigator.bluetooth.requestDevice()</c>, given to <see cref="BluetoothProvider.RequestDevice"/>.</summary>
public sealed class BluetoothDeviceRequest
{
    internal BluetoothDeviceRequest(CallbackContext context, RequestDeviceOptions options, IReadOnlyList<string> optionalServices, IReadOnlyList<BluetoothHostDevice> devices)
    {
        Context = context;
        Options = options;
        OptionalServices = optionalServices;
        Devices = devices;
    }

    /// <summary>Gets the context of the window that requests the device.</summary>
    public CallbackContext Context { get; }

    /// <summary>Gets the options of the page.</summary>
    public RequestDeviceOptions Options { get; }

    /// <summary>Gets the UUIDs of the optional services of the page, without the blocklisted ones.</summary>
    public IReadOnlyList<string> OptionalServices { get; }

    /// <summary>Gets the devices to choose from: the devices in range that match the filters of the page.</summary>
    public IReadOnlyList<BluetoothHostDevice> Devices { get; }
}

/// <summary>
/// A Bluetooth Low Energy device of a <see cref="BluetoothProvider"/>: its advertised data, and its GATT server (the tree of
/// <see cref="Services"/>). The pages share its connection (the ATT bearer), which <see cref="ConnectAsync"/> creates; the
/// application calls <see cref="Disconnect"/> when it is lost.
/// </summary>
/// <remarks>The members can be used from any thread.</remarks>
public class BluetoothHostDevice
{
    private readonly Lock _lock = new();
    private readonly List<BluetoothHostService> _services = [];
    private Task? _pendingConnection;
    private bool _isConnected;
    private int _connectedClients;

    /// <summary>Creates a device.</summary>
    /// <param name="address">The address of the device (e.g. <c>09:09:09:09:09:09</c>).</param>
    /// <param name="name">The name of the device, or null when it is unknown.</param>
    public BluetoothHostDevice(string address, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(address);
        Address = address;
        Name = name;
    }

    /// <summary>Gets the address of the device. It is not exposed to the pages, which see an identifier per origin.</summary>
    public string Address { get; }

    /// <summary>Gets the name of the device, or null when it is unknown.</summary>
    public string? Name { get; }

    /// <summary>Gets the UUIDs of the services the device advertises (valid UUIDs): the filters of <c>requestDevice()</c> match them, and the primary services of <see cref="Services"/>.</summary>
    public IReadOnlyList<string> ServiceUuids { get; init; } = [];

    /// <summary>Gets the manufacturer specific data the device advertises, by company identifier.</summary>
    public IReadOnlyDictionary<ushort, ReadOnlyMemory<byte>> ManufacturerData { get; init; } = new Dictionary<ushort, ReadOnlyMemory<byte>>();

    /// <summary>Gets the service data the device advertises, by service UUID (valid UUIDs).</summary>
    public IReadOnlyDictionary<string, ReadOnlyMemory<byte>> ServiceData { get; init; } = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);

    /// <summary>Gets the services of the GATT server, in the order they were added.</summary>
    public IReadOnlyList<BluetoothHostService> Services
    {
        get
        {
            lock (_lock)
            {
                return [.. _services];
            }
        }
    }

    /// <summary>Gets whether the device is connected (it has an ATT bearer).</summary>
    public bool IsConnected
    {
        get
        {
            lock (_lock)
            {
                return _isConnected;
            }
        }
    }

    internal BluetoothProvider? Provider { get; set; }

    /// <summary>Adds a service to the GATT server.</summary>
    /// <param name="service">The service. A service can be added to one device at a time.</param>
    /// <returns>The service.</returns>
    /// <exception cref="InvalidOperationException">The service is added to a device.</exception>
    public T AddService<T>(T service)
        where T : BluetoothHostService
    {
        ArgumentNullException.ThrowIfNull(service);
        lock (_lock)
        {
            if (service.Device is not null)
                throw new InvalidOperationException("The service is already added to a device");

            service.Device = this;
            _services.Add(service);
        }

        return service;
    }

    /// <summary>Removes a service from the GATT server: the pages that use it get an <c>InvalidStateError</c>.</summary>
    /// <param name="service">The service.</param>
    /// <returns>Whether the service was a service of the device.</returns>
    public bool RemoveService(BluetoothHostService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        lock (_lock)
        {
            if (!_services.Remove(service))
                return false;

            service.Device = null;
            return true;
        }
    }

    /// <summary>
    /// The connection of the device is lost (https://bluetooth.spec.whatwg.org/#disconnection-events): the
    /// pages connected to it get a <c>gattserverdisconnected</c> event. Can be called from any thread.
    /// </summary>
    public void Disconnect()
    {
        lock (_lock)
        {
            _isConnected = false;
        }

        if (Provider is not { } provider)
            return;

        foreach (var global in provider.GetGlobals())
        {
            global.OnDeviceDisconnected(this);
        }
    }

    /// <summary>
    /// Creates the connection of the device (the ATT bearer), when a page connects to it while it is not connected. The
    /// default implementation completes. Throw a <see cref="DomException"/> to choose the error of the page (any other
    /// exception is a <c>NetworkError</c>).
    /// </summary>
    /// <param name="cancellationToken">The token, which is not canceled by the pages.</param>
    protected virtual Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// No page is connected to the device anymore (https://bluetooth.spec.whatwg.org/#garbage-collect-the-connection).
    /// The default implementation disconnects it (<see cref="Disconnect"/>).
    /// </summary>
    protected virtual void OnLastClientDisconnected() => Disconnect();

    /// <summary>Connects the device, or returns the pending connection (the result is completed synchronously by the continuation of <see cref="ConnectAsync"/>).</summary>
    internal Task Connect()
    {
        TaskCompletionSource? completion = null;
        Task result;
        lock (_lock)
        {
            if (_isConnected)
                return Task.CompletedTask;

            if (_pendingConnection is null)
            {
                completion = new TaskCompletionSource();
                _pendingConnection = completion.Task;
            }

            result = _pendingConnection;
        }

        if (completion is not null)
        {
            Task attempt;
            try
            {
                attempt = ConnectAsync(CancellationToken.None);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                attempt = Task.FromException(exception);
            }

            _ = attempt.ContinueWith(
                task =>
                {
                    lock (_lock)
                    {
                        _pendingConnection = null;
                        _isConnected = task.IsCompletedSuccessfully;
                    }

                    if (task.IsCompletedSuccessfully)
                    {
                        completion.TrySetResult();
                    }
                    else
                    {
                        completion.TrySetException(task.Exception?.InnerException ?? new OperationCanceledException());
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return result;
    }

    /// <summary>A page is connected to the device.</summary>
    internal void AddClient()
    {
        lock (_lock)
        {
            _connectedClients++;
        }
    }

    /// <summary>A page is disconnected from the device: the connection is released when no page uses it.</summary>
    internal void RemoveClient()
    {
        lock (_lock)
        {
            if (_connectedClients > 0)
            {
                _connectedClients--;
            }
        }

        ReleaseIfUnused();
    }

    /// <summary>Garbage-collect the connection (https://bluetooth.spec.whatwg.org/#garbage-collect-the-connection): no page uses it, and no page is connecting.</summary>
    internal void ReleaseIfUnused()
    {
        lock (_lock)
        {
            if (_connectedClients > 0 || !_isConnected || _pendingConnection is not null)
                return;
        }

        OnLastClientDisconnected();
    }

    internal bool ContainsService(BluetoothHostService service)
    {
        lock (_lock)
        {
            return _services.Contains(service);
        }
    }
}

/// <summary>A service of the GATT server of a <see cref="BluetoothHostDevice"/> (https://bluetooth.spec.whatwg.org/#bluetoothremotegattservice).</summary>
/// <remarks>The members can be used from any thread.</remarks>
public class BluetoothHostService
{
    private readonly Lock _lock = new();
    private readonly List<BluetoothHostCharacteristic> _characteristics = [];
    private readonly List<BluetoothHostService> _includedServices = [];

    /// <summary>Creates a service.</summary>
    /// <param name="uuid">The UUID of the service (a valid UUID, see <see cref="BluetoothUuid.GetService"/>).</param>
    /// <param name="isPrimary">Whether the service is a primary service (a secondary service is only an included service).</param>
    public BluetoothHostService(string uuid, bool isPrimary = true)
    {
        Uuid = ValidateUuid(uuid);
        IsPrimary = isPrimary;
    }

    /// <summary>Gets the UUID of the service.</summary>
    public string Uuid { get; }

    /// <summary>Gets whether the service is a primary service.</summary>
    public bool IsPrimary { get; }

    /// <summary>Gets the device of the service, or null when it is not added to a device (or was removed).</summary>
    public BluetoothHostDevice? Device { get; internal set; }

    /// <summary>Gets the characteristics of the service, in the order they were added.</summary>
    public IReadOnlyList<BluetoothHostCharacteristic> Characteristics
    {
        get
        {
            lock (_lock)
            {
                return [.. _characteristics];
            }
        }
    }

    /// <summary>Gets the services included by the service, in the order they were added.</summary>
    public IReadOnlyList<BluetoothHostService> IncludedServices
    {
        get
        {
            lock (_lock)
            {
                return [.. _includedServices];
            }
        }
    }

    /// <summary>Adds a characteristic to the service.</summary>
    /// <param name="characteristic">The characteristic. A characteristic can be added to one service at a time.</param>
    /// <returns>The characteristic.</returns>
    /// <exception cref="InvalidOperationException">The characteristic is added to a service.</exception>
    public T AddCharacteristic<T>(T characteristic)
        where T : BluetoothHostCharacteristic
    {
        ArgumentNullException.ThrowIfNull(characteristic);
        lock (_lock)
        {
            if (characteristic.Service is not null)
                throw new InvalidOperationException("The characteristic is already added to a service");

            characteristic.Service = this;
            _characteristics.Add(characteristic);
        }

        return characteristic;
    }

    /// <summary>Removes a characteristic from the service: the pages that use it get an <c>InvalidStateError</c>.</summary>
    /// <param name="characteristic">The characteristic.</param>
    /// <returns>Whether the characteristic was a characteristic of the service.</returns>
    public bool RemoveCharacteristic(BluetoothHostCharacteristic characteristic)
    {
        ArgumentNullException.ThrowIfNull(characteristic);
        lock (_lock)
        {
            if (!_characteristics.Remove(characteristic))
                return false;

            characteristic.Service = null;
            return true;
        }
    }

    /// <summary>Adds an included service (a service of the same device, primary or secondary).</summary>
    /// <param name="service">The included service.</param>
    public void AddIncludedService(BluetoothHostService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        lock (_lock)
        {
            _includedServices.Add(service);
        }
    }

    /// <summary>Removes an included service.</summary>
    /// <param name="service">The included service.</param>
    /// <returns>Whether the service was included.</returns>
    public bool RemoveIncludedService(BluetoothHostService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        lock (_lock)
        {
            return _includedServices.Remove(service);
        }
    }

    internal static string ValidateUuid(string uuid)
    {
        ArgumentNullException.ThrowIfNull(uuid);
        if (!BluetoothUuid.IsValidUuid(uuid))
            throw new ArgumentException($"'{uuid}' is not a valid UUID: use BluetoothUuid to convert the aliases and the names", nameof(uuid));

        return uuid;
    }
}

/// <summary>The properties of a characteristic (https://bluetooth.spec.whatwg.org/#characteristic-properties).</summary>
[Flags]
public enum BluetoothCharacteristicPropertyFlags
{
    /// <summary>No property.</summary>
    None = 0,

    /// <summary>The value can be broadcast.</summary>
    Broadcast = 0x01,

    /// <summary>The value can be read.</summary>
    Read = 0x02,

    /// <summary>The value can be written without response.</summary>
    WriteWithoutResponse = 0x04,

    /// <summary>The value can be written with response.</summary>
    Write = 0x08,

    /// <summary>The device notifies the changes of the value.</summary>
    Notify = 0x10,

    /// <summary>The device indicates the changes of the value.</summary>
    Indicate = 0x20,

    /// <summary>The value can be written with an authenticated signed write.</summary>
    AuthenticatedSignedWrites = 0x40,

    /// <summary>The characteristic has extended properties (<see cref="ReliableWrite"/>, <see cref="WritableAuxiliaries"/>).</summary>
    ExtendedProperties = 0x80,

    /// <summary>The extended property Reliable Write (the value of the Characteristic Extended Properties descriptor).</summary>
    ReliableWrite = 0x100,

    /// <summary>The extended property Writable Auxiliaries (the value of the Characteristic Extended Properties descriptor).</summary>
    WritableAuxiliaries = 0x200,
}

/// <summary>The procedure used to write the value of a characteristic.</summary>
public enum BluetoothWriteType
{
    /// <summary>Any procedure (the deprecated <c>writeValue()</c>).</summary>
    Default,

    /// <summary>The Write Characteristic Value procedure (<c>writeValueWithResponse()</c>).</summary>
    WithResponse,

    /// <summary>The Write Without Response procedure (<c>writeValueWithoutResponse()</c>).</summary>
    WithoutResponse,
}

/// <summary>
/// A characteristic of a <see cref="BluetoothHostService"/> (https://bluetooth.spec.whatwg.org/#bluetoothremotegattcharacteristic).
/// The default implementation stores the value in <see cref="Value"/>: override the operations to implement the device.
/// Throw a <see cref="BluetoothAttException"/> or a <see cref="DomException"/> from an operation to report an error (any
/// other exception is a <c>NetworkError</c>).
/// </summary>
/// <remarks>The members can be used from any thread.</remarks>
public class BluetoothHostCharacteristic
{
    private readonly Lock _lock = new();
    private readonly List<BluetoothHostDescriptor> _descriptors = [];
    private readonly HashSet<GlobalBluetooth> _notificationContexts = [];
    private ReadOnlyMemory<byte> _value;

    /// <summary>Creates a characteristic.</summary>
    /// <param name="uuid">The UUID of the characteristic (a valid UUID, see <see cref="BluetoothUuid.GetCharacteristic"/>).</param>
    /// <param name="properties">The properties of the characteristic.</param>
    public BluetoothHostCharacteristic(string uuid, BluetoothCharacteristicPropertyFlags properties)
    {
        Uuid = BluetoothHostService.ValidateUuid(uuid);
        Properties = properties;
    }

    /// <summary>Gets the UUID of the characteristic.</summary>
    public string Uuid { get; }

    /// <summary>Gets the properties of the characteristic.</summary>
    public BluetoothCharacteristicPropertyFlags Properties { get; }

    /// <summary>Gets the service of the characteristic, or null when it is not added to a service (or was removed).</summary>
    public BluetoothHostService? Service { get; internal set; }

    /// <summary>Gets or sets the value read and written by the default implementation of the operations.</summary>
    public ReadOnlyMemory<byte> Value
    {
        get
        {
            lock (_lock)
            {
                return _value;
            }
        }
        set
        {
            lock (_lock)
            {
                _value = value.ToArray();
            }
        }
    }

    /// <summary>Gets the descriptors of the characteristic, in the order they were added.</summary>
    public IReadOnlyList<BluetoothHostDescriptor> Descriptors
    {
        get
        {
            lock (_lock)
            {
                return [.. _descriptors];
            }
        }
    }

    /// <summary>Adds a descriptor to the characteristic.</summary>
    /// <param name="descriptor">The descriptor. A descriptor can be added to one characteristic at a time.</param>
    /// <returns>The descriptor.</returns>
    /// <exception cref="InvalidOperationException">The descriptor is added to a characteristic.</exception>
    public T AddDescriptor<T>(T descriptor)
        where T : BluetoothHostDescriptor
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        lock (_lock)
        {
            if (descriptor.Characteristic is not null)
                throw new InvalidOperationException("The descriptor is already added to a characteristic");

            descriptor.Characteristic = this;
            _descriptors.Add(descriptor);
        }

        return descriptor;
    }

    /// <summary>Removes a descriptor from the characteristic: the pages that use it get an <c>InvalidStateError</c>.</summary>
    /// <param name="descriptor">The descriptor.</param>
    /// <returns>Whether the descriptor was a descriptor of the characteristic.</returns>
    public bool RemoveDescriptor(BluetoothHostDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        lock (_lock)
        {
            if (!_descriptors.Remove(descriptor))
                return false;

            descriptor.Characteristic = null;
            return true;
        }
    }

    /// <summary>
    /// The device notified or indicated a new value (https://bluetooth.spec.whatwg.org/#notification-events):
    /// the pages that started the notifications get a <c>characteristicvaluechanged</c> event. Can be called from any thread.
    /// </summary>
    /// <param name="value">The new value.</param>
    public void NotifyValueChanged(ReadOnlyMemory<byte> value)
    {
        GlobalBluetooth[] contexts;
        lock (_lock)
        {
            contexts = [.. _notificationContexts];
        }

        var bytes = value.ToArray();
        foreach (var context in contexts)
        {
            context.OnCharacteristicValueChanged(this, bytes);
        }
    }

    /// <summary>Reads the value (<c>readValue()</c>). The default implementation returns <see cref="Value"/>.</summary>
    /// <param name="cancellationToken">The token, which is not canceled by the pages.</param>
    protected internal virtual Task<ReadOnlyMemory<byte>> ReadValueAsync(CancellationToken cancellationToken) => Task.FromResult(Value);

    /// <summary>Writes the value (<c>writeValue()</c>, <c>writeValueWithResponse()</c>, <c>writeValueWithoutResponse()</c>). The default implementation sets <see cref="Value"/>.</summary>
    /// <param name="value">The value (at most 512 bytes).</param>
    /// <param name="writeType">The procedure of the write.</param>
    /// <param name="cancellationToken">The token, which is not canceled by the pages.</param>
    protected internal virtual Task WriteValueAsync(ReadOnlyMemory<byte> value, BluetoothWriteType writeType, CancellationToken cancellationToken)
    {
        Value = value;
        return Task.CompletedTask;
    }

    /// <summary>Enables the notifications or the indications of the device (<c>startNotifications()</c> of the first page). The default implementation completes.</summary>
    /// <param name="cancellationToken">The token, which is not canceled by the pages.</param>
    protected internal virtual Task StartNotificationsAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Disables the notifications of the device (<c>stopNotifications()</c>). The default implementation completes.</summary>
    /// <param name="cancellationToken">The token, which is not canceled by the pages.</param>
    protected internal virtual Task StopNotificationsAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal bool HasNotificationContext(GlobalBluetooth context)
    {
        lock (_lock)
        {
            return _notificationContexts.Contains(context);
        }
    }

    internal void AddNotificationContext(GlobalBluetooth context)
    {
        lock (_lock)
        {
            _notificationContexts.Add(context);
        }
    }

    internal void RemoveNotificationContext(GlobalBluetooth context)
    {
        lock (_lock)
        {
            _notificationContexts.Remove(context);
        }
    }

    internal bool ContainsDescriptor(BluetoothHostDescriptor descriptor)
    {
        lock (_lock)
        {
            return _descriptors.Contains(descriptor);
        }
    }
}

/// <summary>
/// A descriptor of a <see cref="BluetoothHostCharacteristic"/> (https://bluetooth.spec.whatwg.org/#bluetoothremotegattdescriptor).
/// The default implementation stores the value in <see cref="Value"/>: override the operations to implement the device.
/// Throw a <see cref="BluetoothAttException"/> or a <see cref="DomException"/> from an operation to report an error (any
/// other exception is a <c>NetworkError</c>).
/// </summary>
/// <remarks>The members can be used from any thread.</remarks>
public class BluetoothHostDescriptor
{
    private readonly Lock _lock = new();
    private ReadOnlyMemory<byte> _value;

    /// <summary>Creates a descriptor.</summary>
    /// <param name="uuid">The UUID of the descriptor (a valid UUID, see <see cref="BluetoothUuid.GetDescriptor"/>).</param>
    public BluetoothHostDescriptor(string uuid)
    {
        Uuid = BluetoothHostService.ValidateUuid(uuid);
    }

    /// <summary>Gets the UUID of the descriptor.</summary>
    public string Uuid { get; }

    /// <summary>Gets the characteristic of the descriptor, or null when it is not added to a characteristic (or was removed).</summary>
    public BluetoothHostCharacteristic? Characteristic { get; internal set; }

    /// <summary>Gets or sets the value read and written by the default implementation of the operations.</summary>
    public ReadOnlyMemory<byte> Value
    {
        get
        {
            lock (_lock)
            {
                return _value;
            }
        }
        set
        {
            lock (_lock)
            {
                _value = value.ToArray();
            }
        }
    }

    /// <summary>Reads the value (<c>readValue()</c>). The default implementation returns <see cref="Value"/>.</summary>
    /// <param name="cancellationToken">The token, which is not canceled by the pages.</param>
    protected internal virtual Task<ReadOnlyMemory<byte>> ReadValueAsync(CancellationToken cancellationToken) => Task.FromResult(Value);

    /// <summary>Writes the value (<c>writeValue()</c>). The default implementation sets <see cref="Value"/>.</summary>
    /// <param name="value">The value (at most 512 bytes).</param>
    /// <param name="cancellationToken">The token, which is not canceled by the pages.</param>
    protected internal virtual Task WriteValueAsync(ReadOnlyMemory<byte> value, CancellationToken cancellationToken)
    {
        Value = value;
        return Task.CompletedTask;
    }
}

/// <summary>
/// An Error Response of a GATT procedure (an ATT error code, BT 4.2 Vol 3 Part F 3.4.1.1), thrown by the operations of the
/// host attributes: the page gets the error of https://bluetooth.spec.whatwg.org/#error-handling.
/// </summary>
public sealed class BluetoothAttException : Exception
{
    /// <summary>Creates an exception.</summary>
    public BluetoothAttException()
        : this(0x0E)
    {
    }

    /// <summary>Creates an exception.</summary>
    /// <param name="message">The message.</param>
    public BluetoothAttException(string? message)
        : base(message)
    {
        ErrorCode = 0x0E;
    }

    /// <summary>Creates an exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public BluetoothAttException(string? message, Exception? innerException)
        : base(message, innerException)
    {
        ErrorCode = 0x0E;
    }

    /// <summary>Creates an exception.</summary>
    /// <param name="errorCode">The ATT error code (e.g. 0x02 Read Not Permitted, 0x0D Invalid Attribute Value Length).</param>
    public BluetoothAttException(byte errorCode)
        : base($"GATT Error Response 0x{errorCode:x2}")
    {
        ErrorCode = errorCode;
    }

    /// <summary>Gets the ATT error code.</summary>
    public byte ErrorCode { get; }

    /// <summary>The error of the page (https://bluetooth.spec.whatwg.org/#error-handling).</summary>
    internal DomException ToDomException(bool isWrite) => ErrorCode switch
    {
        // Invalid Handle
        0x01 => new DomException("GATT Error: invalid handle.", "InvalidStateError"),

        // Invalid Attribute Value Length
        0x0D => new DomException("GATT Error: invalid attribute length.", "InvalidModificationError"),

        // Insufficient Authentication, Insufficient Authorization, Insufficient Encryption Key Size, Insufficient Encryption
        0x05 or 0x08 or 0x0C or 0x0F => new DomException("GATT Error: Not paired.", "SecurityError"),

        // Application Error
        >= 0x80 and <= 0x9F when isWrite => new DomException("GATT Error: invalid attribute length.", "InvalidModificationError"),

        // Read Not Permitted, Write Not Permitted, Request Not Supported, and the others
        _ => new DomException("GATT Error: Not supported.", "NotSupportedError"),
    };
}

/// <summary>The algorithms of the data filters (https://bluetooth.spec.whatwg.org/#bluetoothdatafilterinit-matches).</summary>
internal static class BluetoothDataFilter
{
    /// <summary>Whether an array of bytes matches a canonicalized data filter.</summary>
    public static bool Matches(ReadOnlySpan<byte> data, ReadOnlySpan<byte> dataPrefix, ReadOnlySpan<byte> mask)
    {
        if (data.Length < dataPrefix.Length)
            return false;

        for (var i = 0; i < dataPrefix.Length; i++)
        {
            if (((data[i] ^ dataPrefix[i]) & mask[i]) != 0)
                return false;
        }

        return true;
    }

    /// <summary>https://bluetooth.spec.whatwg.org/#bluetoothdatafilterinit-strict-subset: whether filter1 matches a subset of the data matched by filter2.</summary>
    public static bool IsStrictSubset(ReadOnlySpan<byte> dataPrefix1, ReadOnlySpan<byte> mask1, ReadOnlySpan<byte> dataPrefix2, ReadOnlySpan<byte> mask2)
    {
        if (dataPrefix1.Length < dataPrefix2.Length)
            return false;

        for (var i = 0; i < dataPrefix2.Length; i++)
        {
            if ((mask1[i] & mask2[i]) != mask2[i])
                return false;

            if ((dataPrefix1[i] & mask2[i]) != (dataPrefix2[i] & mask2[i]))
                return false;
        }

        return true;
    }
}
