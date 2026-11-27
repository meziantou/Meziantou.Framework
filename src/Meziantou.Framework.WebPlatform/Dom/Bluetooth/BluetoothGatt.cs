using System.ComponentModel;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The GATT server of a <see cref="BluetoothDevice"/> (https://bluetooth.spec.whatwg.org/#bluetoothremotegattserver): the
/// connection of the window to the device, and the services the origin is allowed to use. As in Chromium, the error messages
/// tell which check failed, and the services, the characteristics and the descriptors of a connection are invalid after a
/// disconnection.
/// </summary>
/// <seealso href="https://bluetooth.spec.whatwg.org/#bluetoothremotegattserver"/>
[IdlInterface("BluetoothRemoteGATTServer", "https://bluetooth.spec.whatwg.org/#bluetoothremotegattserver")]
public sealed class BluetoothRemoteGattServer
{
    private const string ServicesDisconnectedMessage = "GATT Server is disconnected. Cannot retrieve services. (Re)connect first with `device.gatt.connect`.";
    private const string NotAllowedToAccessAnyServiceMessage = "Origin is not allowed to access any service. Tip: Add the service UUID to 'optionalServices' in requestDevice() options. https://goo.gl/HxfxSQ";
    private const string NotAllowedToAccessServiceMessage = "Origin is not allowed to access the service. Tip: Add the service UUID to 'optionalServices' in requestDevice() options. https://goo.gl/HxfxSQ";

    // [[attributeInstanceMap]] of the connection: the objects are created once per connection
    private readonly Dictionary<BluetoothHostService, BluetoothRemoteGattService> _services = [];
    private readonly Dictionary<BluetoothHostCharacteristic, BluetoothRemoteGattCharacteristic> _characteristics = [];
    private readonly Dictionary<BluetoothHostDescriptor, BluetoothRemoteGattDescriptor> _descriptors = [];

    internal BluetoothRemoteGattServer(BluetoothDevice device)
    {
        Device = device;
    }

    /// <summary>Gets the device.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattserver-device"/>
    [IdlMember("device", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattserver-device")]
    public BluetoothDevice Device { get; }

    /// <summary>Gets whether the window is connected to the device.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattserver-connected"/>
    [IdlMember("connected", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattserver-connected")]
    public bool Connected { get; private set; }

    /// <summary>
    /// The [[activeAlgorithms]] of the server: the operations started before a disconnection (or a call to <see cref="Disconnect"/>)
    /// fail, and the attributes of a previous connection are invalid.
    /// </summary>
    internal int Generation { get; private set; }

    private GlobalBluetooth State => Device.State;

    private GlobalScope Global => State.Global;

    /// <summary>
    /// Connects the window to the device (https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattserver-connect): the
    /// device connects with <see cref="BluetoothHostDevice"/> when it is not connected yet. The task fails with a
    /// <c>SecurityError</c> <see cref="DomException"/> when the origin is not allowed to use the device anymore, a
    /// <c>NetworkError</c> when the device is out of range or the connection fails, and an <c>AbortError</c> when
    /// <see cref="Disconnect"/> is called before the connection completes.
    /// </summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattserver-connect"/>
    [IdlMember("connect", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattserver-connect")]
    public Task<BluetoothRemoteGattServer> Connect()
    {
        var device = Device.HostDevice;
        if (State.GetAllowedDevice(device) is null)
            return Task.FromException<BluetoothRemoteGattServer>(new DomException("Origin is not allowed to access the device. It was forgotten, or its permission was revoked.", "SecurityError"));

        if (device.Provider is null || device.Provider != State.Provider)
            return Task.FromException<BluetoothRemoteGattServer>(new DomException("Bluetooth Device is no longer in range.", "NetworkError"));

        if (Connected)
            return Task.FromResult(this);

        var generation = Generation;
        var result = new TaskCompletionSource<BluetoothRemoteGattServer>();
        _ = device.Connect().ContinueWith(
            task => Global.QueueGlobalTask(() =>
            {
                if (generation != Generation)
                {
                    // disconnect() was called during the connection: the connection is released if no page uses it
                    result.TrySetException(new DomException("Connection is no longer valid.", "AbortError"));
                    device.ReleaseIfUnused();
                    return;
                }

                if (!task.IsCompletedSuccessfully)
                {
                    result.TrySetException(task.Exception?.InnerException as DomException ?? new DomException("Connection failed for unknown reason.", "NetworkError"));
                    return;
                }

                if (!Connected)
                {
                    Connected = true;
                    device.AddClient();
                }

                result.TrySetResult(this);
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return result.Task;
    }

    /// <summary>
    /// Disconnects the window from the device (https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattserver-disconnect):
    /// the pending operations fail, and the device gets a <c>gattserverdisconnected</c> event. The connection of the device
    /// is released when no page uses it.
    /// </summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattserver-disconnect"/>
    [IdlMember("disconnect", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattserver-disconnect")]
    public void Disconnect()
    {
        // The active algorithms are cleared: a pending connect() fails with an AbortError
        Generation++;
        if (!Connected)
            return;

        CleanUpDisconnectedDevice();
    }

    /// <summary>
    /// Returns the first primary service with a UUID (https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattserver-getprimaryservice).
    /// The task fails with a <see cref="TypeErrorException"/> for an invalid UUID, a <c>SecurityError</c> when the origin is
    /// not allowed to use the service, a <c>NetworkError</c> when the window is not connected, and a <c>NotFoundError</c>
    /// when the device has no such service.
    /// </summary>
    /// <param name="service">The UUID, the alias (a number) or the name of the service.</param>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattserver-getprimaryservice"/>
    [IdlMember("getPrimaryService", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattserver-getprimaryservice")]
    public Task<BluetoothRemoteGattService> GetPrimaryService(BluetoothUuidName service)
    {
        return QueryServices("getPrimaryService", service, single: true, services => GetOrCreate(services[0]));
    }

    /// <summary>
    /// Returns the primary services (https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattserver-getprimaryservices)
    /// that the origin is allowed to use, with a UUID or all of them. The errors are the ones of <see cref="GetPrimaryService"/>.
    /// </summary>
    /// <param name="service">The UUID, the alias (a number) or the name of the services, or null for all the services.</param>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattserver-getprimaryservices"/>
    [IdlMember("getPrimaryServices", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattserver-getprimaryservices")]
    public Task<IReadOnlyList<BluetoothRemoteGattService>> GetPrimaryServices(BluetoothUuidName? service = null)
        => QueryServices<IReadOnlyList<BluetoothRemoteGattService>>("getPrimaryServices", service, single: false, services => [.. services.Select(GetOrCreate)]);

    internal static string FailedToExecute(string operation, string interfaceName, string message) => $"Failed to execute '{operation}' on '{interfaceName}': {message}";

    /// <summary>
    /// Runs an operation of the page with a gatt-connection-checking wrapper (https://bluetooth.spec.whatwg.org/#gatt-connection-checking-wrapper):
    /// the result is delivered by a task, and the operation fails with a <c>NetworkError</c> when the window was disconnected
    /// in the meantime. The host task completes synchronously in its continuation: a continuation on the thread pool would
    /// delay the result.
    /// </summary>
    internal Task<TResult> Run<THost, TResult>(Func<Task<THost>> start, Func<THost, TResult> complete, string disconnectedMessage, bool isWrite = false)
    {
        var generation = Generation;
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
                if (!Connected || generation != Generation)
                {
                    result.TrySetException(new DomException(disconnectedMessage, "NetworkError"));
                    return;
                }

                if (!completed.IsCompletedSuccessfully)
                {
                    result.TrySetException(MapError(completed.Exception?.InnerException, isWrite));
                    return;
                }

                try
                {
                    result.TrySetResult(complete(completed.Result));
                }
                catch (Exception exception) when (exception is DomException or TypeErrorException)
                {
                    result.TrySetException(exception);
                }
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return result.Task;
    }

    /// <summary>A task without result of the host, as a task with a result that completes synchronously.</summary>
    internal static Task<bool> AsResult(Task task) => task.ContinueWith(
        static completed =>
        {
            completed.GetAwaiter().GetResult();
            return true;
        },
        CancellationToken.None,
        TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);

    internal BluetoothRemoteGattService GetOrCreate(BluetoothHostService service)
    {
        if (!_services.TryGetValue(service, out var result))
        {
            result = new BluetoothRemoteGattService(Device, service, Generation);
            _services.Add(service, result);
        }

        return result;
    }

    internal BluetoothRemoteGattCharacteristic GetOrCreate(BluetoothRemoteGattService service, BluetoothHostCharacteristic characteristic)
    {
        if (!_characteristics.TryGetValue(characteristic, out var result))
        {
            result = new BluetoothRemoteGattCharacteristic(service, characteristic, Generation);
            _characteristics.Add(characteristic, result);
        }

        return result;
    }

    internal BluetoothRemoteGattDescriptor GetOrCreate(BluetoothRemoteGattCharacteristic characteristic, BluetoothHostDescriptor descriptor)
    {
        if (!_descriptors.TryGetValue(descriptor, out var result))
        {
            result = new BluetoothRemoteGattDescriptor(characteristic, descriptor, Generation);
            _descriptors.Add(descriptor, result);
        }

        return result;
    }

    /// <summary>Whether the origin is allowed to use a service of the device (and the service is not blocklisted).</summary>
    internal bool IsServiceAllowed(BluetoothHostService service)
        => !BluetoothBlocklist.IsBlocklisted(service.Uuid) && State.GetAllowedDevice(Device.HostDevice) is { } allowed && allowed.IsServiceAllowed(service.Uuid);

    /// <summary>
    /// Clean up the disconnected device (https://bluetooth.spec.whatwg.org/#clean-up-the-disconnected-device): the attributes
    /// of the connection are invalid, the notifications stop, and the device gets a <c>gattserverdisconnected</c> event.
    /// </summary>
    internal void CleanUpDisconnectedDevice()
    {
        Connected = false;
        Generation++;
        foreach (var characteristic in _characteristics.Keys)
        {
            characteristic.RemoveNotificationContext(State);
        }

        _services.Clear();
        _characteristics.Clear();
        _descriptors.Clear();
        Device.HostDevice.RemoveClient();
        Device.FireEvent(new Event("gattserverdisconnected", new EventInit { Bubbles = true }));
    }

    /// <summary>A notification of a characteristic (https://bluetooth.spec.whatwg.org/#notification-events): the object of the characteristic gets the value and a <c>characteristicvaluechanged</c> event.</summary>
    internal void OnCharacteristicValueChanged(BluetoothHostCharacteristic characteristic, byte[] value)
    {
        if (Connected && _characteristics.TryGetValue(characteristic, out var characteristicObject))
        {
            characteristicObject.SetValueAndFireEvent(value);
        }
    }

    // https://bluetooth.spec.whatwg.org/#error-handling: the errors of the host
    private static Exception MapError(Exception? exception, bool isWrite) => exception switch
    {
        DomException or TypeErrorException => exception,
        BluetoothAttException attException => attException.ToDomException(isWrite),
        _ => new DomException("GATT operation failed for unknown reason.", "NetworkError"),
    };

    // GetGATTChildren (https://bluetooth.spec.whatwg.org/#getgattchildren) for the primary services, with the checks of Chromium
    private Task<TResult> QueryServices<TResult>(string operation, BluetoothUuidName? service, bool single, Func<BluetoothHostService[], TResult> complete)
    {
        string? uuid = null;
        if (service is { } name)
        {
            uuid = BluetoothUuid.TryResolve(name, BluetoothUuidKind.Service);
            if (uuid is null)
                return Task.FromException<TResult>(new TypeErrorException(FailedToExecute(operation, "BluetoothRemoteGATTServer", BluetoothUuid.GetInvalidNameMessage(name, BluetoothUuidKind.Service))));
        }

        if (!Connected)
            return Task.FromException<TResult>(new DomException(ServicesDisconnectedMessage, "NetworkError"));

        var allowed = State.GetAllowedDevice(Device.HostDevice);
        if (allowed is null || allowed.AllowedServices is { Count: 0 })
            return Task.FromException<TResult>(new DomException(NotAllowedToAccessAnyServiceMessage, "SecurityError"));

        if (uuid is not null && !allowed.IsServiceAllowed(uuid))
            return Task.FromException<TResult>(new DomException(NotAllowedToAccessServiceMessage, "SecurityError"));

        if (uuid is not null && BluetoothBlocklist.IsBlocklisted(uuid))
            return Task.FromException<TResult>(new DomException("getPrimaryService(s) called with blocklisted UUID. https://goo.gl/4NeimX", "SecurityError"));

        var device = Device.HostDevice;
        return Run(
            () =>
            {
                var services = device.Services
                    .Where(candidate => candidate.IsPrimary && (uuid is null || candidate.Uuid == uuid) && !BluetoothBlocklist.IsBlocklisted(candidate.Uuid) && allowed.IsServiceAllowed(candidate.Uuid))
                    .Take(single ? 1 : int.MaxValue)
                    .ToArray();
                if (services.Length == 0)
                    throw DomException.NotFound(uuid is null ? "No Services found in device." : $"No Services matching UUID {uuid} found in Device.");

                return Task.FromResult(services);
            },
            complete,
            ServicesDisconnectedMessage);
    }
}

/// <summary>A GATT service of a device (https://bluetooth.spec.whatwg.org/#bluetoothremotegattservice), for one connection: it is invalid after a disconnection.</summary>
/// <seealso href="https://bluetooth.spec.whatwg.org/#bluetoothremotegattservice"/>
[IdlInterface("BluetoothRemoteGATTService", "https://bluetooth.spec.whatwg.org/#bluetoothremotegattservice")]
public sealed class BluetoothRemoteGattService : EventTarget
{
    private const string CharacteristicsDisconnectedMessage = "GATT Server is disconnected. Cannot retrieve characteristics. (Re)connect first with `device.gatt.connect`.";
    private const string IncludedServicesDisconnectedMessage = "GATT Server is disconnected. Cannot retrieve included services. (Re)connect first with `device.gatt.connect`.";

    private readonly int _generation;

    internal BluetoothRemoteGattService(BluetoothDevice device, BluetoothHostService service, int generation)
    {
        Device = device;
        HostService = service;
        _generation = generation;
    }

    /// <summary>Gets the service of the provider.</summary>
    [NotInIdl("The service of the host, which is not exposed to scripts")]
    public BluetoothHostService HostService { get; }

    /// <summary>Gets the device of the service.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattservice-device"/>
    [IdlMember("device", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattservice-device")]
    public BluetoothDevice Device { get; }

    /// <summary>Gets the UUID of the service.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattservice-uuid"/>
    [IdlMember("uuid", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattservice-uuid")]
    public string Uuid => HostService.Uuid;

    /// <summary>Gets whether the service is a primary service.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattservice-isprimary"/>
    [IdlMember("isPrimary", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattservice-isprimary")]
    public bool IsPrimary => HostService.IsPrimary;

    /// <summary>Gets or sets the event handler of the <c>characteristicvaluechanged</c> events of the characteristics of the service.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-characteristiceventhandlers-oncharacteristicvaluechanged"/>
    [IdlMember("oncharacteristicvaluechanged", "https://bluetooth.spec.whatwg.org/#dom-characteristiceventhandlers-oncharacteristicvaluechanged")]
    public Action<Event>? OnCharacteristicValueChanged
    {
        get => GetEventHandler("characteristicvaluechanged");
        set => SetEventHandler("characteristicvaluechanged", value);
    }

    /// <summary>Gets or sets the event handler of the <c>serviceadded</c> event.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-serviceeventhandlers-onserviceadded"/>
    [IdlMember("onserviceadded", "https://bluetooth.spec.whatwg.org/#dom-serviceeventhandlers-onserviceadded")]
    public Action<Event>? OnServiceAdded
    {
        get => GetEventHandler("serviceadded");
        set => SetEventHandler("serviceadded", value);
    }

    /// <summary>Gets or sets the event handler of the <c>servicechanged</c> event.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-serviceeventhandlers-onservicechanged"/>
    [IdlMember("onservicechanged", "https://bluetooth.spec.whatwg.org/#dom-serviceeventhandlers-onservicechanged")]
    public Action<Event>? OnServiceChanged
    {
        get => GetEventHandler("servicechanged");
        set => SetEventHandler("servicechanged", value);
    }

    /// <summary>Gets or sets the event handler of the <c>serviceremoved</c> event.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-serviceeventhandlers-onserviceremoved"/>
    [IdlMember("onserviceremoved", "https://bluetooth.spec.whatwg.org/#dom-serviceeventhandlers-onserviceremoved")]
    public Action<Event>? OnServiceRemoved
    {
        get => GetEventHandler("serviceremoved");
        set => SetEventHandler("serviceremoved", value);
    }

    internal override GlobalScope? RelevantGlobal => Device.State.Global;

    internal BluetoothRemoteGattServer Server => Device.Gatt!;

    /// <summary>
    /// Returns the first characteristic with a UUID (https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattservice-getcharacteristic).
    /// The task fails with a <see cref="TypeErrorException"/> for an invalid UUID, a <c>SecurityError</c> for a blocklisted
    /// UUID, a <c>NetworkError</c> when the window is not connected, an <c>InvalidStateError</c> when the service was removed
    /// or belongs to a previous connection, and a <c>NotFoundError</c> when the service has no such characteristic.
    /// </summary>
    /// <param name="characteristic">The UUID, the alias (a number) or the name of the characteristic.</param>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattservice-getcharacteristic"/>
    [IdlMember("getCharacteristic", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattservice-getcharacteristic")]
    public Task<BluetoothRemoteGattCharacteristic> GetCharacteristic(BluetoothUuidName characteristic)
    {
        return QueryCharacteristics("getCharacteristic", characteristic, single: true, characteristics => Server.GetOrCreate(this, characteristics[0]));
    }

    /// <summary>
    /// Returns the characteristics (https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattservice-getcharacteristics),
    /// with a UUID or all of them, except the blocklisted ones. The errors are the ones of <see cref="GetCharacteristic"/>.
    /// </summary>
    /// <param name="characteristic">The UUID, the alias (a number) or the name of the characteristics, or null for all the characteristics.</param>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattservice-getcharacteristics"/>
    [IdlMember("getCharacteristics", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattservice-getcharacteristics")]
    public Task<IReadOnlyList<BluetoothRemoteGattCharacteristic>> GetCharacteristics(BluetoothUuidName? characteristic = null)
        => QueryCharacteristics<IReadOnlyList<BluetoothRemoteGattCharacteristic>>("getCharacteristics", characteristic, single: false, characteristics => [.. characteristics.Select(c => Server.GetOrCreate(this, c))]);

    /// <summary>Returns the first included service with a UUID (https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattservice-getincludedservice) that the origin is allowed to use.</summary>
    /// <param name="service">The UUID, the alias (a number) or the name of the service.</param>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattservice-getincludedservice"/>
    [IdlMember("getIncludedService", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattservice-getincludedservice")]
    public Task<BluetoothRemoteGattService> GetIncludedService(BluetoothUuidName service)
    {
        return QueryIncludedServices("getIncludedService", service, single: true, services => Server.GetOrCreate(services[0]));
    }

    /// <summary>Returns the included services (https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattservice-getincludedservices) that the origin is allowed to use, with a UUID or all of them.</summary>
    /// <param name="service">The UUID, the alias (a number) or the name of the services, or null for all the services.</param>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattservice-getincludedservices"/>
    [IdlMember("getIncludedServices", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattservice-getincludedservices")]
    public Task<IReadOnlyList<BluetoothRemoteGattService>> GetIncludedServices(BluetoothUuidName? service = null)
        => QueryIncludedServices<IReadOnlyList<BluetoothRemoteGattService>>("getIncludedServices", service, single: false, services => [.. services.Select(Server.GetOrCreate)]);

    /// <summary>The error of an operation on a service that was removed, or that belongs to a previous connection, or null.</summary>
    internal DomException? GetStateError()
    {
        if (_generation != Server.Generation)
            return DomException.InvalidState($"Service with UUID {Uuid} is no longer valid. Remember to retrieve the service again after reconnecting.");

        return HostService.Device == Device.HostDevice ? null : DomException.InvalidState("GATT Service no longer exists.");
    }

    // https://bluetooth.spec.whatwg.org/#bluetooth-tree: the parent of a service is its device
    internal override EventTarget? GetTheParent(Event @event) => Device;

    private Task<TResult> QueryCharacteristics<TResult>(string operation, BluetoothUuidName? characteristic, bool single, Func<BluetoothHostCharacteristic[], TResult> complete)
    {
        string? uuid = null;
        if (characteristic is { } name)
        {
            uuid = BluetoothUuid.TryResolve(name, BluetoothUuidKind.Characteristic);
            if (uuid is null)
                return Task.FromException<TResult>(new TypeErrorException(BluetoothRemoteGattServer.FailedToExecute(operation, "BluetoothRemoteGATTService", BluetoothUuid.GetInvalidNameMessage(name, BluetoothUuidKind.Characteristic))));

            if (BluetoothBlocklist.IsBlocklisted(uuid))
                return Task.FromException<TResult>(new DomException("getCharacteristic(s) called with blocklisted UUID. https://goo.gl/4NeimX", "SecurityError"));
        }

        if (!Server.Connected)
            return Task.FromException<TResult>(new DomException(CharacteristicsDisconnectedMessage, "NetworkError"));

        if (_generation != Server.Generation)
            return Task.FromException<TResult>(GetStateError()!);

        return Server.Run(
            () =>
            {
                if (GetStateError() is { } error)
                    throw error;

                var characteristics = HostService.Characteristics
                    .Where(candidate => (uuid is null || candidate.Uuid == uuid) && !BluetoothBlocklist.IsBlocklisted(candidate.Uuid))
                    .Take(single ? 1 : int.MaxValue)
                    .ToArray();
                if (characteristics.Length == 0)
                    throw DomException.NotFound(uuid is null ? "No Characteristics found in service." : $"No Characteristics matching UUID {uuid} found in Service.");

                return Task.FromResult(characteristics);
            },
            complete,
            CharacteristicsDisconnectedMessage);
    }

    private Task<TResult> QueryIncludedServices<TResult>(string operation, BluetoothUuidName? service, bool single, Func<BluetoothHostService[], TResult> complete)
    {
        string? uuid = null;
        if (service is { } name)
        {
            uuid = BluetoothUuid.TryResolve(name, BluetoothUuidKind.Service);
            if (uuid is null)
                return Task.FromException<TResult>(new TypeErrorException(BluetoothRemoteGattServer.FailedToExecute(operation, "BluetoothRemoteGATTService", BluetoothUuid.GetInvalidNameMessage(name, BluetoothUuidKind.Service))));

            if (BluetoothBlocklist.IsBlocklisted(uuid))
                return Task.FromException<TResult>(new DomException("getIncludedService(s) called with blocklisted UUID. https://goo.gl/4NeimX", "SecurityError"));
        }

        if (!Server.Connected)
            return Task.FromException<TResult>(new DomException(IncludedServicesDisconnectedMessage, "NetworkError"));

        if (_generation != Server.Generation)
            return Task.FromException<TResult>(GetStateError()!);

        return Server.Run(
            () =>
            {
                if (GetStateError() is { } error)
                    throw error;

                var services = HostService.IncludedServices
                    .Where(candidate => candidate.Device == Device.HostDevice && (uuid is null || candidate.Uuid == uuid) && Server.IsServiceAllowed(candidate))
                    .Take(single ? 1 : int.MaxValue)
                    .ToArray();
                if (services.Length == 0)
                    throw DomException.NotFound(uuid is null ? "No Services found in service." : $"No Services matching UUID {uuid} found in Service.");

                return Task.FromResult(services);
            },
            complete,
            IncludedServicesDisconnectedMessage);
    }
}

/// <summary>The properties of a characteristic (https://bluetooth.spec.whatwg.org/#bluetoothcharacteristicproperties).</summary>
/// <seealso href="https://bluetooth.spec.whatwg.org/#bluetoothcharacteristicproperties"/>
[IdlInterface("BluetoothCharacteristicProperties", "https://bluetooth.spec.whatwg.org/#bluetoothcharacteristicproperties")]
public sealed class BluetoothCharacteristicProperties
{
    private readonly BluetoothCharacteristicPropertyFlags _flags;

    internal BluetoothCharacteristicProperties(BluetoothCharacteristicPropertyFlags flags)
    {
        _flags = flags;
    }

    /// <summary>Gets whether the value can be broadcast.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothcharacteristicproperties-broadcast"/>
    [IdlMember("broadcast", "https://bluetooth.spec.whatwg.org/#dom-bluetoothcharacteristicproperties-broadcast")]
    public bool Broadcast => _flags.HasFlag(BluetoothCharacteristicPropertyFlags.Broadcast);

    /// <summary>Gets whether the value can be read.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothcharacteristicproperties-read"/>
    [IdlMember("read", "https://bluetooth.spec.whatwg.org/#dom-bluetoothcharacteristicproperties-read")]
    public bool Read => _flags.HasFlag(BluetoothCharacteristicPropertyFlags.Read);

    /// <summary>Gets whether the value can be written without response.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothcharacteristicproperties-writewithoutresponse"/>
    [IdlMember("writeWithoutResponse", "https://bluetooth.spec.whatwg.org/#dom-bluetoothcharacteristicproperties-writewithoutresponse")]
    public bool WriteWithoutResponse => _flags.HasFlag(BluetoothCharacteristicPropertyFlags.WriteWithoutResponse);

    /// <summary>Gets whether the value can be written with response.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothcharacteristicproperties-write"/>
    [IdlMember("write", "https://bluetooth.spec.whatwg.org/#dom-bluetoothcharacteristicproperties-write")]
    public bool Write => _flags.HasFlag(BluetoothCharacteristicPropertyFlags.Write);

    /// <summary>Gets whether the device notifies the changes of the value.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothcharacteristicproperties-notify"/>
    [IdlMember("notify", "https://bluetooth.spec.whatwg.org/#dom-bluetoothcharacteristicproperties-notify")]
    public bool Notify => _flags.HasFlag(BluetoothCharacteristicPropertyFlags.Notify);

    /// <summary>Gets whether the device indicates the changes of the value.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothcharacteristicproperties-indicate"/>
    [IdlMember("indicate", "https://bluetooth.spec.whatwg.org/#dom-bluetoothcharacteristicproperties-indicate")]
    public bool Indicate => _flags.HasFlag(BluetoothCharacteristicPropertyFlags.Indicate);

    /// <summary>Gets whether the value can be written with an authenticated signed write.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothcharacteristicproperties-authenticatedsignedwrites"/>
    [IdlMember("authenticatedSignedWrites", "https://bluetooth.spec.whatwg.org/#dom-bluetoothcharacteristicproperties-authenticatedsignedwrites")]
    public bool AuthenticatedSignedWrites => _flags.HasFlag(BluetoothCharacteristicPropertyFlags.AuthenticatedSignedWrites);

    /// <summary>Gets the Reliable Write bit of the extended properties (false without extended properties).</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothcharacteristicproperties-reliablewrite"/>
    [IdlMember("reliableWrite", "https://bluetooth.spec.whatwg.org/#dom-bluetoothcharacteristicproperties-reliablewrite")]
    public bool ReliableWrite => _flags.HasFlag(BluetoothCharacteristicPropertyFlags.ExtendedProperties) && _flags.HasFlag(BluetoothCharacteristicPropertyFlags.ReliableWrite);

    /// <summary>Gets the Writable Auxiliaries bit of the extended properties (false without extended properties).</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothcharacteristicproperties-writableauxiliaries"/>
    [IdlMember("writableAuxiliaries", "https://bluetooth.spec.whatwg.org/#dom-bluetoothcharacteristicproperties-writableauxiliaries")]
    public bool WritableAuxiliaries => _flags.HasFlag(BluetoothCharacteristicPropertyFlags.ExtendedProperties) && _flags.HasFlag(BluetoothCharacteristicPropertyFlags.WritableAuxiliaries);
}

/// <summary>A GATT characteristic of a service (https://bluetooth.spec.whatwg.org/#bluetoothremotegattcharacteristic), for one connection: it is invalid after a disconnection.</summary>
/// <seealso href="https://bluetooth.spec.whatwg.org/#bluetoothremotegattcharacteristic"/>
[IdlInterface("BluetoothRemoteGATTCharacteristic", "https://bluetooth.spec.whatwg.org/#bluetoothremotegattcharacteristic")]
public sealed class BluetoothRemoteGattCharacteristic : EventTarget
{
    private const int MaxValueLength = 512;
    private const string DescriptorsDisconnectedMessage = "GATT Server is disconnected. Cannot retrieve descriptors. (Re)connect first with `device.gatt.connect`.";
    internal const string OperationsDisconnectedMessage = "GATT Server is disconnected. Cannot perform GATT operations. (Re)connect first with `device.gatt.connect`.";

    private readonly int _generation;
    private JsBufferSource? _value;

    internal BluetoothRemoteGattCharacteristic(BluetoothRemoteGattService service, BluetoothHostCharacteristic characteristic, int generation)
    {
        Service = service;
        HostCharacteristic = characteristic;
        _generation = generation;
        Properties = new BluetoothCharacteristicProperties(characteristic.Properties);
    }

    /// <summary>Gets the characteristic of the provider.</summary>
    [NotInIdl("The characteristic of the host, which is not exposed to scripts")]
    public BluetoothHostCharacteristic HostCharacteristic { get; }

    /// <summary>Gets the service of the characteristic.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-service"/>
    [IdlMember("service", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-service")]
    public BluetoothRemoteGattService Service { get; }

    /// <summary>Gets the UUID of the characteristic.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-uuid"/>
    [IdlMember("uuid", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-uuid")]
    public string Uuid => HostCharacteristic.Uuid;

    /// <summary>Gets the properties of the characteristic.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-properties"/>
    [IdlMember("properties", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-properties")]
    public BluetoothCharacteristicProperties Properties { get; }

    /// <summary>Gets the last value read, written or notified, or null.</summary>
    [NotInIdl("The bytes for the .NET code: the value attribute is DataView")]
    public ReadOnlyMemory<byte>? Value => _value is null ? (ReadOnlyMemory<byte>?)null : _value.Bytes;

    /// <summary>Gets the <c>DataView</c> of the last value for the scripts: use <see cref="Value"/> in .NET code.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-value"/>
    [IdlMember("value", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-value")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public object? ValueView => _value;

    /// <summary>Gets or sets the event handler of the <c>characteristicvaluechanged</c> event: the value was read or notified.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-characteristiceventhandlers-oncharacteristicvaluechanged"/>
    [IdlMember("oncharacteristicvaluechanged", "https://bluetooth.spec.whatwg.org/#dom-characteristiceventhandlers-oncharacteristicvaluechanged")]
    public Action<Event>? OnCharacteristicValueChanged
    {
        get => GetEventHandler("characteristicvaluechanged");
        set => SetEventHandler("characteristicvaluechanged", value);
    }

    internal override GlobalScope? RelevantGlobal => Service.Device.State.Global;

    private BluetoothRemoteGattServer Server => Service.Server;

    /// <summary>
    /// Returns the first descriptor with a UUID (https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-getdescriptor).
    /// The task fails with a <see cref="TypeErrorException"/> for an invalid UUID, a <c>SecurityError</c> for a blocklisted
    /// UUID, a <c>NetworkError</c> when the window is not connected, an <c>InvalidStateError</c> when the characteristic was
    /// removed or belongs to a previous connection, and a <c>NotFoundError</c> when it has no such descriptor.
    /// </summary>
    /// <param name="descriptor">The UUID, the alias (a number) or the name of the descriptor.</param>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-getdescriptor"/>
    [IdlMember("getDescriptor", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-getdescriptor")]
    public Task<BluetoothRemoteGattDescriptor> GetDescriptor(BluetoothUuidName descriptor)
    {
        return QueryDescriptors("getDescriptor", descriptor, single: true, descriptors => Server.GetOrCreate(this, descriptors[0]));
    }

    /// <summary>Returns the descriptors (https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-getdescriptors), with a UUID or all of them, except the blocklisted ones. The errors are the ones of <see cref="GetDescriptor"/>.</summary>
    /// <param name="descriptor">The UUID, the alias (a number) or the name of the descriptors, or null for all the descriptors.</param>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-getdescriptors"/>
    [IdlMember("getDescriptors", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-getdescriptors")]
    public Task<IReadOnlyList<BluetoothRemoteGattDescriptor>> GetDescriptors(BluetoothUuidName? descriptor = null)
        => QueryDescriptors<IReadOnlyList<BluetoothRemoteGattDescriptor>>("getDescriptors", descriptor, single: false, descriptors => [.. descriptors.Select(d => Server.GetOrCreate(this, d))]);

    /// <summary>
    /// Reads the value (https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-readvalue) with
    /// <see cref="BluetoothHostCharacteristic"/>: <see cref="ValueView"/> is the new value, and the characteristic gets a
    /// <c>characteristicvaluechanged</c> event before the task completes. The task (whose result is the <c>DataView</c> of
    /// the value) fails with a <c>SecurityError</c> for a blocklisted characteristic, a <c>NetworkError</c> when the window is
    /// not connected, an <c>InvalidStateError</c> when the characteristic was removed, and a <c>NotSupportedError</c> when it
    /// cannot be read.
    /// </summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-readvalue"/>
    [IdlMember("readValue", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-readvalue")]
    public Task<object> ReadValue()
    {
        if (BluetoothBlocklist.IsBlocklistedForReads(Uuid))
            return Task.FromException<object>(new DomException("readValue() called on blocklisted object marked exclude-reads. https://goo.gl/4NeimX", "SecurityError"));

        if (GetOperationError() is { } error)
            return Task.FromException<object>(error);

        if (!HostCharacteristic.Properties.HasFlag(BluetoothCharacteristicPropertyFlags.Read))
            return Task.FromException<object>(DomException.NotSupported("GATT operation not permitted."));

        var characteristic = HostCharacteristic;
        return Server.Run(
            () => characteristic.ReadValueAsync(CancellationToken.None),
            value => (object)SetValueAndFireEvent(value.ToArray()),
            OperationsDisconnectedMessage);
    }

    /// <summary>Writes the value with any procedure (https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-writevalue). Deprecated: use <see cref="WriteValueWithResponse"/> or <see cref="WriteValueWithoutResponse"/>.</summary>
    /// <param name="value">The value (at most 512 bytes).</param>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-writevalue"/>
    [IdlMember("writeValue", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-writevalue")]
    public Task WriteValue(ReadOnlyMemory<byte> value) => WriteCharacteristicValue(value, BluetoothWriteType.Default);

    /// <summary>
    /// Writes the value with the Write Characteristic Value procedure (https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-writevaluewithresponse):
    /// <see cref="ValueView"/> is the new value. The task fails with a <c>SecurityError</c> for a blocklisted characteristic,
    /// an <c>InvalidModificationError</c> for a value longer than 512 bytes, a <c>NetworkError</c> when the window is not
    /// connected, an <c>InvalidStateError</c> when the characteristic was removed, and a <c>NotSupportedError</c> when it
    /// cannot be written.
    /// </summary>
    /// <param name="value">The value (at most 512 bytes).</param>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-writevaluewithresponse"/>
    [IdlMember("writeValueWithResponse", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-writevaluewithresponse")]
    public Task WriteValueWithResponse(ReadOnlyMemory<byte> value) => WriteCharacteristicValue(value, BluetoothWriteType.WithResponse);

    /// <summary>Writes the value with the Write Without Response procedure (https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-writevaluewithoutresponse). The errors are the ones of <see cref="WriteValueWithResponse"/>.</summary>
    /// <param name="value">The value (at most 512 bytes).</param>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-writevaluewithoutresponse"/>
    [IdlMember("writeValueWithoutResponse", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-writevaluewithoutresponse")]
    public Task WriteValueWithoutResponse(ReadOnlyMemory<byte> value) => WriteCharacteristicValue(value, BluetoothWriteType.WithoutResponse);

    /// <summary>
    /// Starts the notifications of the window (https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-startnotifications):
    /// the characteristic gets a <c>characteristicvaluechanged</c> event for each <see cref="BluetoothHostCharacteristic.NotifyValueChanged"/>.
    /// The task fails as <see cref="ReadValue"/>, and with a <c>NotSupportedError</c> when the characteristic neither
    /// notifies nor indicates its changes.
    /// </summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-startnotifications"/>
    [IdlMember("startNotifications", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-startnotifications")]
    public Task<BluetoothRemoteGattCharacteristic> StartNotifications()
    {
        if (BluetoothBlocklist.IsBlocklistedForReads(Uuid))
            return Task.FromException<BluetoothRemoteGattCharacteristic>(new DomException("startNotifications() called on blocklisted object marked exclude-reads. https://goo.gl/4NeimX", "SecurityError"));

        if (GetOperationError() is { } error)
            return Task.FromException<BluetoothRemoteGattCharacteristic>(error);

        var characteristic = HostCharacteristic;
        if ((characteristic.Properties & (BluetoothCharacteristicPropertyFlags.Notify | BluetoothCharacteristicPropertyFlags.Indicate)) == BluetoothCharacteristicPropertyFlags.None)
            return Task.FromException<BluetoothRemoteGattCharacteristic>(DomException.NotSupported("GATT operation not permitted."));

        var context = Service.Device.State;
        if (characteristic.HasNotificationContext(context))
            return Server.Run(() => Task.FromResult(true), _ => this, OperationsDisconnectedMessage);

        return Server.Run(
            () => BluetoothRemoteGattServer.AsResult(characteristic.StartNotificationsAsync(CancellationToken.None)),
            _ =>
            {
                characteristic.AddNotificationContext(context);
                return this;
            },
            OperationsDisconnectedMessage);
    }

    /// <summary>
    /// Stops the notifications of the window (https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-stopnotifications).
    /// The task fails with an <c>InvalidStateError</c> when the characteristic was removed or belongs to a previous connection.
    /// </summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-stopnotifications"/>
    [IdlMember("stopNotifications", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattcharacteristic-stopnotifications")]
    public Task<BluetoothRemoteGattCharacteristic> StopNotifications()
    {
        if (GetStateError() is { } error)
            return Task.FromException<BluetoothRemoteGattCharacteristic>(error);

        var characteristic = HostCharacteristic;
        var global = Service.Device.State.Global;
        characteristic.RemoveNotificationContext(Service.Device.State);
        var result = new TaskCompletionSource<BluetoothRemoteGattCharacteristic>();
        Task task;
        try
        {
            task = characteristic.StopNotificationsAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            task = Task.FromException(exception);
        }

        _ = task.ContinueWith(
            completed => global.QueueGlobalTask(() =>
            {
                if (completed.IsCompletedSuccessfully)
                {
                    result.TrySetResult(this);
                }
                else
                {
                    result.TrySetException(completed.Exception?.InnerException as DomException ?? new DomException("GATT operation failed for unknown reason.", "NetworkError"));
                }
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return result.Task;
    }

    /// <summary>The value was read or notified: <see cref="ValueView"/> is a new <c>DataView</c>, and the characteristic gets a <c>characteristicvaluechanged</c> event.</summary>
    internal JsBufferSource SetValueAndFireEvent(byte[] value)
    {
        var view = new JsBufferSource(JsBufferKind.DataView, value);
        _value = view;
        FireEvent(new Event("characteristicvaluechanged", new EventInit { Bubbles = true }));
        return view;
    }

    /// <summary>The error of an operation on a characteristic that was removed (or whose service was removed), or that belongs to a previous connection, or null.</summary>
    internal DomException? GetStateError()
    {
        if (_generation != Server.Generation)
            return DomException.InvalidState($"Characteristic with UUID {Uuid} is no longer valid. Remember to retrieve the characteristic again after reconnecting.");

        if (Service.GetStateError() is { } serviceError)
            return serviceError;

        return HostCharacteristic.Service == Service.HostService ? null : DomException.InvalidState("GATT Characteristic no longer exists.");
    }

    // https://bluetooth.spec.whatwg.org/#bluetooth-tree: the parent of a characteristic is its service
    internal override EventTarget? GetTheParent(Event @event) => Service;

    /// <summary>The errors of the operations on the value: the window is not connected, or the characteristic is not valid.</summary>
    private DomException? GetOperationError() => Server.Connected ? GetStateError() : new DomException(OperationsDisconnectedMessage, "NetworkError");

    // WriteCharacteristicValue (https://bluetooth.spec.whatwg.org/#writecharacteristicvalue)
    private Task WriteCharacteristicValue(ReadOnlyMemory<byte> value, BluetoothWriteType writeType)
    {
        if (BluetoothBlocklist.IsBlocklistedForWrites(Uuid))
            return Task.FromException(new DomException("writeValue() called on blocklisted object marked exclude-writes. https://goo.gl/4NeimX", "SecurityError"));

        var bytes = value.ToArray();
        if (bytes.Length > MaxValueLength)
            return Task.FromException(new DomException("Value can't exceed 512 bytes.", "InvalidModificationError"));

        if (GetOperationError() is { } error)
            return Task.FromException(error);

        var characteristic = HostCharacteristic;
        var required = writeType switch
        {
            BluetoothWriteType.WithResponse => BluetoothCharacteristicPropertyFlags.Write,
            BluetoothWriteType.WithoutResponse => BluetoothCharacteristicPropertyFlags.WriteWithoutResponse,
            _ => BluetoothCharacteristicPropertyFlags.Write | BluetoothCharacteristicPropertyFlags.WriteWithoutResponse,
        };
        if ((characteristic.Properties & required) == BluetoothCharacteristicPropertyFlags.None)
            return Task.FromException(DomException.NotSupported("GATT operation not permitted."));

        return Server.Run(
            () => BluetoothRemoteGattServer.AsResult(characteristic.WriteValueAsync(bytes, writeType, CancellationToken.None)),
            _ =>
            {
                _value = new JsBufferSource(JsBufferKind.DataView, bytes);
                return true;
            },
            OperationsDisconnectedMessage,
            isWrite: true);
    }

    private Task<TResult> QueryDescriptors<TResult>(string operation, BluetoothUuidName? descriptor, bool single, Func<BluetoothHostDescriptor[], TResult> complete)
    {
        string? uuid = null;
        if (descriptor is { } name)
        {
            uuid = BluetoothUuid.TryResolve(name, BluetoothUuidKind.Descriptor);
            if (uuid is null)
                return Task.FromException<TResult>(new TypeErrorException(BluetoothRemoteGattServer.FailedToExecute(operation, "BluetoothRemoteGATTCharacteristic", BluetoothUuid.GetInvalidNameMessage(name, BluetoothUuidKind.Descriptor))));

            if (BluetoothBlocklist.IsBlocklisted(uuid))
                return Task.FromException<TResult>(new DomException("getDescriptor(s) called with blocklisted UUID. https://goo.gl/4NeimX", "SecurityError"));
        }

        if (!Server.Connected)
            return Task.FromException<TResult>(new DomException(DescriptorsDisconnectedMessage, "NetworkError"));

        if (_generation != Server.Generation)
            return Task.FromException<TResult>(GetStateError()!);

        return Server.Run(
            () =>
            {
                if (GetStateError() is { } error)
                    throw error;

                var descriptors = HostCharacteristic.Descriptors
                    .Where(candidate => (uuid is null || candidate.Uuid == uuid) && !BluetoothBlocklist.IsBlocklisted(candidate.Uuid))
                    .Take(single ? 1 : int.MaxValue)
                    .ToArray();
                if (descriptors.Length == 0)
                    throw DomException.NotFound(uuid is null ? "No Descriptors found in Characteristic." : $"No Descriptors matching UUID {uuid} found in Characteristic.");

                return Task.FromResult(descriptors);
            },
            complete,
            DescriptorsDisconnectedMessage);
    }
}

/// <summary>A GATT descriptor of a characteristic (https://bluetooth.spec.whatwg.org/#bluetoothremotegattdescriptor), for one connection: it is invalid after a disconnection.</summary>
/// <seealso href="https://bluetooth.spec.whatwg.org/#bluetoothremotegattdescriptor"/>
[IdlInterface("BluetoothRemoteGATTDescriptor", "https://bluetooth.spec.whatwg.org/#bluetoothremotegattdescriptor")]
public sealed class BluetoothRemoteGattDescriptor
{
    private const int MaxValueLength = 512;

    private readonly int _generation;
    private JsBufferSource? _value;

    internal BluetoothRemoteGattDescriptor(BluetoothRemoteGattCharacteristic characteristic, BluetoothHostDescriptor descriptor, int generation)
    {
        Characteristic = characteristic;
        HostDescriptor = descriptor;
        _generation = generation;
    }

    /// <summary>Gets the descriptor of the provider.</summary>
    [NotInIdl("The descriptor of the host, which is not exposed to scripts")]
    public BluetoothHostDescriptor HostDescriptor { get; }

    /// <summary>Gets the characteristic of the descriptor.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattdescriptor-characteristic"/>
    [IdlMember("characteristic", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattdescriptor-characteristic")]
    public BluetoothRemoteGattCharacteristic Characteristic { get; }

    /// <summary>Gets the UUID of the descriptor.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattdescriptor-uuid"/>
    [IdlMember("uuid", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattdescriptor-uuid")]
    public string Uuid => HostDescriptor.Uuid;

    /// <summary>Gets the last value read or written, or null.</summary>
    [NotInIdl("The bytes for the .NET code: the value attribute is DataView")]
    public ReadOnlyMemory<byte>? Value => _value is null ? (ReadOnlyMemory<byte>?)null : _value.Bytes;

    /// <summary>Gets the <c>DataView</c> of the last value for the scripts: use <see cref="Value"/> in .NET code.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattdescriptor-value"/>
    [IdlMember("value", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattdescriptor-value")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public object? ValueView => _value;

    private BluetoothRemoteGattServer Server => Characteristic.Service.Server;

    /// <summary>
    /// Reads the value (https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattdescriptor-readvalue) with
    /// <see cref="BluetoothHostDescriptor"/>. The task (whose result is the <c>DataView</c> of the value) fails with a
    /// <c>SecurityError</c> for a blocklisted descriptor, a <c>NetworkError</c> when the window is not connected, and an
    /// <c>InvalidStateError</c> when the descriptor was removed.
    /// </summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattdescriptor-readvalue"/>
    [IdlMember("readValue", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattdescriptor-readvalue")]
    public Task<object> ReadValue()
    {
        if (BluetoothBlocklist.IsBlocklistedForReads(Uuid))
            return Task.FromException<object>(new DomException("readValue() called on blocklisted object marked exclude-reads. https://goo.gl/4NeimX", "SecurityError"));

        if (GetOperationError() is { } error)
            return Task.FromException<object>(error);

        var descriptor = HostDescriptor;
        return Server.Run(
            () => descriptor.ReadValueAsync(CancellationToken.None),
            value =>
            {
                var view = new JsBufferSource(JsBufferKind.DataView, value.ToArray());
                _value = view;
                return (object)view;
            },
            BluetoothRemoteGattCharacteristic.OperationsDisconnectedMessage);
    }

    /// <summary>
    /// Writes the value (https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattdescriptor-writevalue). The task fails
    /// with a <c>SecurityError</c> for a blocklisted descriptor, an <c>InvalidModificationError</c> for a value longer than
    /// 512 bytes, a <c>NetworkError</c> when the window is not connected, and an <c>InvalidStateError</c> when the descriptor
    /// was removed.
    /// </summary>
    /// <param name="value">The value (at most 512 bytes).</param>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattdescriptor-writevalue"/>
    [IdlMember("writeValue", "https://bluetooth.spec.whatwg.org/#dom-bluetoothremotegattdescriptor-writevalue")]
    public Task WriteValue(ReadOnlyMemory<byte> value)
    {
        if (BluetoothBlocklist.IsBlocklistedForWrites(Uuid))
            return Task.FromException(new DomException("writeValue() called on blocklisted object marked exclude-writes. https://goo.gl/4NeimX", "SecurityError"));

        var bytes = value.ToArray();
        if (bytes.Length > MaxValueLength)
            return Task.FromException(new DomException("Value can't exceed 512 bytes.", "InvalidModificationError"));

        if (GetOperationError() is { } error)
            return Task.FromException(error);

        var descriptor = HostDescriptor;
        return Server.Run(
            () => BluetoothRemoteGattServer.AsResult(descriptor.WriteValueAsync(bytes, CancellationToken.None)),
            _ =>
            {
                _value = new JsBufferSource(JsBufferKind.DataView, bytes);
                return true;
            },
            BluetoothRemoteGattCharacteristic.OperationsDisconnectedMessage,
            isWrite: true);
    }

    /// <summary>The error of an operation on a descriptor that was removed (or whose characteristic or service was removed), or that belongs to a previous connection, or null.</summary>
    private DomException? GetStateError()
    {
        if (_generation != Server.Generation)
            return DomException.InvalidState($"Descriptor with UUID {Uuid} is no longer valid. Remember to retrieve the Descriptor again after reconnecting.");

        if (Characteristic.GetStateError() is { } characteristicError)
            return characteristicError;

        return HostDescriptor.Characteristic == Characteristic.HostCharacteristic ? null : DomException.InvalidState("GATT Descriptor no longer exists.");
    }

    private DomException? GetOperationError() => Server.Connected ? GetStateError() : new DomException(BluetoothRemoteGattCharacteristic.OperationsDisconnectedMessage, "NetworkError");
}
