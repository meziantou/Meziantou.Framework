using System.Text;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>A filter of the advertised data of the devices (https://bluetooth.spec.whatwg.org/#dictdef-bluetoothdatafilterinit): a value matches when its bytes masked by <see cref="Mask"/> start with the masked <see cref="DataPrefix"/>.</summary>
/// <seealso href="https://bluetooth.spec.whatwg.org/#dictdef-bluetoothdatafilterinit"/>
[IdlInterface("BluetoothDataFilterInit", "https://bluetooth.spec.whatwg.org/#dictdef-bluetoothdatafilterinit")]
public class BluetoothDataFilterInit
{
    /// <summary>Gets the prefix of the data, or null for any data.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothdatafilterinit-dataprefix"/>
    [IdlMember("dataPrefix", "https://bluetooth.spec.whatwg.org/#dom-bluetoothdatafilterinit-dataprefix")]
    public ReadOnlyMemory<byte>? DataPrefix { get; init; }

    /// <summary>Gets the mask of the prefix, or null to compare all the bits of the prefix.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothdatafilterinit-mask"/>
    [IdlMember("mask", "https://bluetooth.spec.whatwg.org/#dom-bluetoothdatafilterinit-mask")]
    public ReadOnlyMemory<byte>? Mask { get; init; }
}

/// <summary>A filter of the manufacturer specific data of the devices (https://bluetooth.spec.whatwg.org/#dictdef-bluetoothmanufacturerdatafilterinit).</summary>
/// <seealso href="https://bluetooth.spec.whatwg.org/#dictdef-bluetoothmanufacturerdatafilterinit"/>
[IdlInterface("BluetoothManufacturerDataFilterInit", "https://bluetooth.spec.whatwg.org/#dictdef-bluetoothmanufacturerdatafilterinit")]
public sealed class BluetoothManufacturerDataFilterInit : BluetoothDataFilterInit
{
    /// <summary>Gets the company identifier of the data.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothmanufacturerdatafilterinit-companyidentifier"/>
    [IdlMember("companyIdentifier", "https://bluetooth.spec.whatwg.org/#dom-bluetoothmanufacturerdatafilterinit-companyidentifier")]
    public required ushort CompanyIdentifier { get; init; }
}

/// <summary>A filter of the service data of the devices (https://bluetooth.spec.whatwg.org/#dictdef-bluetoothservicedatafilterinit).</summary>
/// <seealso href="https://bluetooth.spec.whatwg.org/#dictdef-bluetoothservicedatafilterinit"/>
[IdlInterface("BluetoothServiceDataFilterInit", "https://bluetooth.spec.whatwg.org/#dictdef-bluetoothservicedatafilterinit")]
public sealed class BluetoothServiceDataFilterInit : BluetoothDataFilterInit
{
    /// <summary>Gets the service of the data: a UUID, an alias (a number) or a name (see <see cref="BluetoothUuid.GetService"/>).</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothservicedatafilterinit-service"/>
    [IdlMember("service", "https://bluetooth.spec.whatwg.org/#dom-bluetoothservicedatafilterinit-service")]
    public required BluetoothUuidName Service { get; init; }
}

/// <summary>A filter of the devices of <see cref="Bluetooth.RequestDevice"/> (https://bluetooth.spec.whatwg.org/#dictdef-bluetoothlescanfilterinit): a device matches when it matches all the present members.</summary>
/// <seealso href="https://bluetooth.spec.whatwg.org/#dictdef-bluetoothlescanfilterinit"/>
[IdlInterface("BluetoothLEScanFilterInit", "https://bluetooth.spec.whatwg.org/#dictdef-bluetoothlescanfilterinit")]
public sealed class BluetoothLEScanFilterInit
{
    /// <summary>Gets the services the device must support (UUIDs, aliases or names), or null.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothlescanfilterinit-services"/>
    [IdlMember("services", "https://bluetooth.spec.whatwg.org/#dom-bluetoothlescanfilterinit-services")]
    public IReadOnlyList<BluetoothUuidName>? Services { get; init; }

    /// <summary>Gets the complete name of the device, or null.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothlescanfilterinit-name"/>
    [IdlMember("name", "https://bluetooth.spec.whatwg.org/#dom-bluetoothlescanfilterinit-name")]
    public string? Name { get; init; }

    /// <summary>Gets the prefix of the name of the device, or null.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothlescanfilterinit-nameprefix"/>
    [IdlMember("namePrefix", "https://bluetooth.spec.whatwg.org/#dom-bluetoothlescanfilterinit-nameprefix")]
    public string? NamePrefix { get; init; }

    /// <summary>Gets the filters of the manufacturer data the device must advertise, or null.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothlescanfilterinit-manufacturerdata"/>
    [IdlMember("manufacturerData", "https://bluetooth.spec.whatwg.org/#dom-bluetoothlescanfilterinit-manufacturerdata")]
    public IReadOnlyList<BluetoothManufacturerDataFilterInit>? ManufacturerData { get; init; }

    /// <summary>Gets the filters of the service data the device must advertise, or null.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothlescanfilterinit-servicedata"/>
    [IdlMember("serviceData", "https://bluetooth.spec.whatwg.org/#dom-bluetoothlescanfilterinit-servicedata")]
    public IReadOnlyList<BluetoothServiceDataFilterInit>? ServiceData { get; init; }
}

/// <summary>The options of <see cref="Bluetooth.RequestDevice"/> (https://bluetooth.spec.whatwg.org/#dictdef-requestdeviceoptions): exactly one of <see cref="Filters"/> and <see cref="AcceptAllDevices"/>.</summary>
/// <seealso href="https://bluetooth.spec.whatwg.org/#dictdef-requestdeviceoptions"/>
[IdlInterface("RequestDeviceOptions", "https://bluetooth.spec.whatwg.org/#dictdef-requestdeviceoptions")]
public sealed class RequestDeviceOptions
{
    /// <summary>Gets the filters: a device matches one of them.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-requestdeviceoptions-filters"/>
    [IdlMember("filters", "https://bluetooth.spec.whatwg.org/#dom-requestdeviceoptions-filters")]
    public IReadOnlyList<BluetoothLEScanFilterInit>? Filters { get; init; }

    /// <summary>Gets the exclusion filters: a device matches none of them. They require <see cref="Filters"/>.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-requestdeviceoptions-exclusionfilters"/>
    [IdlMember("exclusionFilters", "https://bluetooth.spec.whatwg.org/#dom-requestdeviceoptions-exclusionfilters")]
    public IReadOnlyList<BluetoothLEScanFilterInit>? ExclusionFilters { get; init; }

    /// <summary>Gets the services the origin can use in addition to the services of the filters.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-requestdeviceoptions-optionalservices"/>
    [IdlMember("optionalServices", "https://bluetooth.spec.whatwg.org/#dom-requestdeviceoptions-optionalservices")]
    public IReadOnlyList<BluetoothUuidName> OptionalServices { get; init; } = [];

    /// <summary>Gets the company identifiers of the manufacturer data the origin can read.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-requestdeviceoptions-optionalmanufacturerdata"/>
    [IdlMember("optionalManufacturerData", "https://bluetooth.spec.whatwg.org/#dom-requestdeviceoptions-optionalmanufacturerdata")]
    public IReadOnlyList<ushort> OptionalManufacturerData { get; init; } = [];

    /// <summary>Gets whether all the devices match (the origin can then use the <see cref="OptionalServices"/> only).</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-requestdeviceoptions-acceptalldevices"/>
    [IdlMember("acceptAllDevices", "https://bluetooth.spec.whatwg.org/#dom-requestdeviceoptions-acceptalldevices")]
    public bool AcceptAllDevices { get; init; }
}

/// <summary>The initialization dictionary of a <see cref="ValueEvent"/> (https://bluetooth.spec.whatwg.org/#dictdef-valueeventinit).</summary>
/// <seealso href="https://bluetooth.spec.whatwg.org/#dictdef-valueeventinit"/>
[IdlInterface("ValueEventInit", "https://bluetooth.spec.whatwg.org/#dictdef-valueeventinit")]
public sealed class ValueEventInit : EventInit
{
    /// <summary>Gets the value.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-valueeventinit-value"/>
    [IdlMember("value", "https://bluetooth.spec.whatwg.org/#dom-valueeventinit-value")]
    public object? Value { get; init; }
}

/// <summary>An event that carries a value (https://bluetooth.spec.whatwg.org/#valueevent): the <c>availabilitychanged</c> event of <see cref="Bluetooth"/>.</summary>
/// <seealso href="https://bluetooth.spec.whatwg.org/#valueevent"/>
[IdlInterface("ValueEvent", "https://bluetooth.spec.whatwg.org/#valueevent")]
public sealed class ValueEvent : Event
{
    /// <summary>Creates an event.</summary>
    /// <param name="type">The type of the event.</param>
    /// <param name="initDict">The initialization dictionary.</param>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-valueevent-valueevent"/>
    [IdlMember("constructor", "https://bluetooth.spec.whatwg.org/#dom-valueevent-valueevent")]
    public ValueEvent(string type, ValueEventInit? initDict = null)
        : base(type, initDict)
    {
        Value = initDict?.Value;
    }

    /// <summary>Gets the value.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-valueevent-value"/>
    [IdlMember("value", "https://bluetooth.spec.whatwg.org/#dom-valueevent-value")]
    public object? Value { get; }
}

/// <summary>The options of <see cref="BluetoothDevice.WatchAdvertisements"/> (https://bluetooth.spec.whatwg.org/#dictdef-watchadvertisementsoptions).</summary>
/// <seealso href="https://bluetooth.spec.whatwg.org/#dictdef-watchadvertisementsoptions"/>
[IdlInterface("WatchAdvertisementsOptions", "https://bluetooth.spec.whatwg.org/#dictdef-watchadvertisementsoptions")]
public sealed class WatchAdvertisementsOptions
{
    /// <summary>Gets the signal that stops watching the advertisements.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-watchadvertisementsoptions-signal"/>
    [IdlMember("signal", "https://bluetooth.spec.whatwg.org/#dom-watchadvertisementsoptions-signal")]
    public AbortSignal? Signal { get; init; }
}

/// <summary>
/// The Web Bluetooth API of a window (https://bluetooth.spec.whatwg.org/#bluetooth, <c>navigator.bluetooth</c>): the
/// devices of the <see cref="BluetoothProvider"/> of the browsing context (<see cref="BrowsingContextOptions.Bluetooth"/>)
/// that the origin is allowed to use. It is the root of the Bluetooth tree: the events of the devices and of their
/// attributes bubble to it.
/// </summary>
/// <seealso href="https://bluetooth.spec.whatwg.org/#bluetooth"/>
[IdlInterface("Bluetooth", "https://bluetooth.spec.whatwg.org/#bluetooth")]
public sealed class Bluetooth : EventTarget
{
    private readonly GlobalBluetooth _state;

    internal Bluetooth(GlobalBluetooth state)
    {
        _state = state;
    }

    internal override GlobalScope? RelevantGlobal => _state.Global;

    /// <summary>Gets or sets the event handler of the <c>availabilitychanged</c> event (<see cref="ValueEvent"/>).</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetooth-onavailabilitychanged"/>
    [IdlMember("onavailabilitychanged", "https://bluetooth.spec.whatwg.org/#dom-bluetooth-onavailabilitychanged")]
    public Action<Event>? OnAvailabilityChanged
    {
        get => GetEventHandler("availabilitychanged");
        set => SetEventHandler("availabilitychanged", value);
    }

    /// <summary>Gets the device from which the user opened the page, or null. It is always null: the hosts do not open pages from devices.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetooth-referringdevice"/>
    [IdlMember("referringDevice", "https://bluetooth.spec.whatwg.org/#dom-bluetooth-referringdevice")]
    public BluetoothDevice? ReferringDevice => null;

    /// <summary>Gets or sets the event handler of the <c>advertisementreceived</c> events of the devices.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothdeviceeventhandlers-onadvertisementreceived"/>
    [IdlMember("onadvertisementreceived", "https://bluetooth.spec.whatwg.org/#dom-bluetoothdeviceeventhandlers-onadvertisementreceived")]
    public Action<Event>? OnAdvertisementReceived
    {
        get => GetEventHandler("advertisementreceived");
        set => SetEventHandler("advertisementreceived", value);
    }

    /// <summary>Gets or sets the event handler of the <c>gattserverdisconnected</c> events of the devices.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothdeviceeventhandlers-ongattserverdisconnected"/>
    [IdlMember("ongattserverdisconnected", "https://bluetooth.spec.whatwg.org/#dom-bluetoothdeviceeventhandlers-ongattserverdisconnected")]
    public Action<Event>? OnGattServerDisconnected
    {
        get => GetEventHandler("gattserverdisconnected");
        set => SetEventHandler("gattserverdisconnected", value);
    }

    /// <summary>Gets or sets the event handler of the <c>characteristicvaluechanged</c> events of the characteristics.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-characteristiceventhandlers-oncharacteristicvaluechanged"/>
    [IdlMember("oncharacteristicvaluechanged", "https://bluetooth.spec.whatwg.org/#dom-characteristiceventhandlers-oncharacteristicvaluechanged")]
    public Action<Event>? OnCharacteristicValueChanged
    {
        get => GetEventHandler("characteristicvaluechanged");
        set => SetEventHandler("characteristicvaluechanged", value);
    }

    /// <summary>Gets or sets the event handler of the <c>serviceadded</c> events of the services.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-serviceeventhandlers-onserviceadded"/>
    [IdlMember("onserviceadded", "https://bluetooth.spec.whatwg.org/#dom-serviceeventhandlers-onserviceadded")]
    public Action<Event>? OnServiceAdded
    {
        get => GetEventHandler("serviceadded");
        set => SetEventHandler("serviceadded", value);
    }

    /// <summary>Gets or sets the event handler of the <c>servicechanged</c> events of the services.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-serviceeventhandlers-onservicechanged"/>
    [IdlMember("onservicechanged", "https://bluetooth.spec.whatwg.org/#dom-serviceeventhandlers-onservicechanged")]
    public Action<Event>? OnServiceChanged
    {
        get => GetEventHandler("servicechanged");
        set => SetEventHandler("servicechanged", value);
    }

    /// <summary>Gets or sets the event handler of the <c>serviceremoved</c> events of the services.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-serviceeventhandlers-onserviceremoved"/>
    [IdlMember("onserviceremoved", "https://bluetooth.spec.whatwg.org/#dom-serviceeventhandlers-onserviceremoved")]
    public Action<Event>? OnServiceRemoved
    {
        get => GetEventHandler("serviceremoved");
        set => SetEventHandler("serviceremoved", value);
    }

    /// <summary>
    /// Returns whether Bluetooth is available (https://bluetooth.spec.whatwg.org/#dom-bluetooth-getavailability): the
    /// provider has an adapter that supports Bluetooth Low Energy (<see cref="BluetoothProvider.IsAvailable"/>), even when
    /// it is turned off. It is false when the <c>bluetooth</c> permissions policy feature is not allowed.
    /// </summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetooth-getavailability"/>
    [IdlMember("getAvailability", "https://bluetooth.spec.whatwg.org/#dom-bluetooth-getavailability")]
    public Task<bool> GetAvailability() => _state.GetAvailability();

    /// <summary>
    /// Returns the devices that the origin is allowed to use (https://bluetooth.spec.whatwg.org/#dom-bluetooth-getdevices),
    /// in range or not. The task fails with a <c>SecurityError</c> <see cref="DomException"/> when the <c>bluetooth</c>
    /// permissions policy feature is not allowed, or the top-level origin is opaque.
    /// </summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetooth-getdevices"/>
    [IdlMember("getDevices", "https://bluetooth.spec.whatwg.org/#dom-bluetooth-getdevices")]
    public Task<IReadOnlyList<BluetoothDevice>> GetDevices() => _state.GetDevices();

    /// <summary>
    /// Asks the user to choose a device (https://bluetooth.spec.whatwg.org/#dom-bluetooth-requestdevice) with
    /// <see cref="BluetoothProvider.RequestDevice"/>: the origin is then allowed to use it, and the services of the filters
    /// and of <see cref="RequestDeviceOptions.OptionalServices"/>. The window must have transient activation (it is not
    /// consumed). The task fails with a <see cref="TypeErrorException"/> for invalid options, a <c>SecurityError</c>
    /// <see cref="DomException"/> without user activation, for a blocklisted filter, when the <c>bluetooth</c> permissions
    /// policy feature is not allowed or the top-level origin is opaque, and a <c>NotFoundError</c> when there is no adapter or
    /// the user does not choose a device.
    /// </summary>
    /// <param name="options">The filters of the devices.</param>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetooth-requestdevice"/>
    [IdlMember("requestDevice", "https://bluetooth.spec.whatwg.org/#dom-bluetooth-requestdevice")]
    public Task<BluetoothDevice> RequestDevice(RequestDeviceOptions? options = null) => _state.RequestDevice(options ?? new RequestDeviceOptions());
}

/// <summary>
/// A Bluetooth device of a window (https://bluetooth.spec.whatwg.org/#bluetoothdevice): a <see cref="BluetoothHostDevice"/>
/// that the origin is allowed to use, and its GATT server (<see cref="Gatt"/>).
/// </summary>
/// <seealso href="https://bluetooth.spec.whatwg.org/#bluetoothdevice"/>
[IdlInterface("BluetoothDevice", "https://bluetooth.spec.whatwg.org/#bluetoothdevice")]
public sealed class BluetoothDevice : EventTarget
{
    internal BluetoothDevice(GlobalBluetooth state, BluetoothHostDevice device, string id)
    {
        State = state;
        HostDevice = device;
        Id = id;
        Gatt = new BluetoothRemoteGattServer(this);
    }

    /// <summary>Gets the device of the provider.</summary>
    [NotInIdl("The device of the host, which is not exposed to scripts")]
    public BluetoothHostDevice HostDevice { get; }

    /// <summary>Gets the identifier of the device for the origin.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothdevice-id"/>
    [IdlMember("id", "https://bluetooth.spec.whatwg.org/#dom-bluetoothdevice-id")]
    public string Id { get; }

    /// <summary>Gets the name of the device, or null when it is unknown.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothdevice-name"/>
    [IdlMember("name", "https://bluetooth.spec.whatwg.org/#dom-bluetoothdevice-name")]
    public string? Name => HostDevice.Name;

    /// <summary>
    /// Gets the GATT server of the device. As in Chromium, it is not null after <see cref="Forget"/>: its operations then
    /// fail with a <c>SecurityError</c>.
    /// </summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothdevice-gatt"/>
    [IdlMember("gatt", "https://bluetooth.spec.whatwg.org/#dom-bluetoothdevice-gatt")]
    public BluetoothRemoteGattServer? Gatt { get; }

    /// <summary>Gets whether the advertisements of the device are watched. It is always false: watching the advertisements is not supported.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothdevice-watchingadvertisements"/>
    [IdlMember("watchingAdvertisements", "https://bluetooth.spec.whatwg.org/#dom-bluetoothdevice-watchingadvertisements")]
    public bool WatchingAdvertisements => false;

    /// <summary>Gets or sets the event handler of the <c>advertisementreceived</c> event.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothdeviceeventhandlers-onadvertisementreceived"/>
    [IdlMember("onadvertisementreceived", "https://bluetooth.spec.whatwg.org/#dom-bluetoothdeviceeventhandlers-onadvertisementreceived")]
    public Action<Event>? OnAdvertisementReceived
    {
        get => GetEventHandler("advertisementreceived");
        set => SetEventHandler("advertisementreceived", value);
    }

    /// <summary>Gets or sets the event handler of the <c>gattserverdisconnected</c> event: the connection to the GATT server was lost, or closed by <see cref="BluetoothRemoteGattServer.Disconnect"/>.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothdeviceeventhandlers-ongattserverdisconnected"/>
    [IdlMember("ongattserverdisconnected", "https://bluetooth.spec.whatwg.org/#dom-bluetoothdeviceeventhandlers-ongattserverdisconnected")]
    public Action<Event>? OnGattServerDisconnected
    {
        get => GetEventHandler("gattserverdisconnected");
        set => SetEventHandler("gattserverdisconnected", value);
    }

    /// <summary>Gets or sets the event handler of the <c>characteristicvaluechanged</c> events of the characteristics of the device.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-characteristiceventhandlers-oncharacteristicvaluechanged"/>
    [IdlMember("oncharacteristicvaluechanged", "https://bluetooth.spec.whatwg.org/#dom-characteristiceventhandlers-oncharacteristicvaluechanged")]
    public Action<Event>? OnCharacteristicValueChanged
    {
        get => GetEventHandler("characteristicvaluechanged");
        set => SetEventHandler("characteristicvaluechanged", value);
    }

    /// <summary>Gets or sets the event handler of the <c>serviceadded</c> events of the services of the device.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-serviceeventhandlers-onserviceadded"/>
    [IdlMember("onserviceadded", "https://bluetooth.spec.whatwg.org/#dom-serviceeventhandlers-onserviceadded")]
    public Action<Event>? OnServiceAdded
    {
        get => GetEventHandler("serviceadded");
        set => SetEventHandler("serviceadded", value);
    }

    /// <summary>Gets or sets the event handler of the <c>servicechanged</c> events of the services of the device.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-serviceeventhandlers-onservicechanged"/>
    [IdlMember("onservicechanged", "https://bluetooth.spec.whatwg.org/#dom-serviceeventhandlers-onservicechanged")]
    public Action<Event>? OnServiceChanged
    {
        get => GetEventHandler("servicechanged");
        set => SetEventHandler("servicechanged", value);
    }

    /// <summary>Gets or sets the event handler of the <c>serviceremoved</c> events of the services of the device.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-serviceeventhandlers-onserviceremoved"/>
    [IdlMember("onserviceremoved", "https://bluetooth.spec.whatwg.org/#dom-serviceeventhandlers-onserviceremoved")]
    public Action<Event>? OnServiceRemoved
    {
        get => GetEventHandler("serviceremoved");
        set => SetEventHandler("serviceremoved", value);
    }

    internal GlobalBluetooth State { get; }

    internal override GlobalScope? RelevantGlobal => State.Global;

    /// <summary>
    /// Revokes the permission of the origin to use the device (https://bluetooth.spec.whatwg.org/#dom-bluetoothdevice-forget):
    /// it is disconnected, and <see cref="Bluetooth.GetDevices"/> does not return it anymore.
    /// </summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothdevice-forget"/>
    [IdlMember("forget", "https://bluetooth.spec.whatwg.org/#dom-bluetoothdevice-forget")]
    public Task Forget()
    {
        Gatt!.Disconnect();
        State.Forget(HostDevice);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Watches the advertisements of the device (https://bluetooth.spec.whatwg.org/#dom-bluetoothdevice-watchadvertisements).
    /// Scanning for advertisements is not supported: the task fails with a <c>NotSupportedError</c> <see cref="DomException"/>
    /// (an <c>AbortError</c> when the signal is aborted).
    /// </summary>
    /// <param name="options">The options.</param>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothdevice-watchadvertisements"/>
    [IdlMember("watchAdvertisements", "https://bluetooth.spec.whatwg.org/#dom-bluetoothdevice-watchadvertisements")]
    public Task WatchAdvertisements(WatchAdvertisementsOptions? options = null)
    {
        if (options?.Signal is { Aborted: true })
            return Task.FromException(new DomException("The operation was aborted", "AbortError"));

        return Task.FromException(DomException.NotSupported("Scanning for the advertisements of the devices is not supported"));
    }

    // https://bluetooth.spec.whatwg.org/#bluetooth-tree: the parent of a device is navigator.bluetooth
    internal override EventTarget? GetTheParent(Event @event) => State.Bluetooth;
}

/// <summary>
/// The Bluetooth state of a window: its <see cref="Bluetooth"/> object, and the <see cref="BluetoothDevice"/> object of each
/// <see cref="BluetoothHostDevice"/> it can use ([[deviceInstanceMap]]). The events of the provider run in tasks.
/// </summary>
internal sealed class GlobalBluetooth
{
    private const int MaxDeviceNameLength = 248;

    private readonly Dictionary<BluetoothHostDevice, BluetoothDevice> _devices = [];

    public GlobalBluetooth(Window window)
    {
        Global = window;
        Bluetooth = new Bluetooth(this);
        Provider?.Register(this);
    }

    public Window Global { get; }

    public Bluetooth Bluetooth { get; }

    public BluetoothProvider? Provider => Global.Options.Bluetooth?.Provider;

    public string Origin => Global.OriginRecord.Serialize();

    // https://wicg.github.io/nav-speculation/prerendering.html#web-bluetooth-patch: a prerendering document waits for its activation
    public Task<bool> GetAvailability() => Document.DelayWhilePrerendering(Global, GetAvailabilityCore);

    public Task<IReadOnlyList<BluetoothDevice>> GetDevices() => Document.DelayWhilePrerendering(Global, GetDevicesCore);

    public Task<BluetoothDevice> RequestDevice(RequestDeviceOptions options) => Document.DelayWhilePrerendering(Global, () => RequestDeviceCore(options));

    public BluetoothAllowedDevice? GetAllowedDevice(BluetoothHostDevice device) => Provider?.GetAllowedDevice(device, Origin);

    public void Forget(BluetoothHostDevice device) => Provider?.Revoke(device, Origin);

    /// <summary>The availability changed (https://bluetooth.spec.whatwg.org/#availability-changed). Called from any thread.</summary>
    public void OnAvailabilityChanged(bool availability) => Global.QueueGlobalTask(() =>
    {
        if (!IsAllowedToUse(report: false))
            return;

        Bluetooth.FireEvent(new ValueEvent("availabilitychanged", new ValueEventInit { Value = availability }));
    });

    /// <summary>The connection of a device was lost (https://bluetooth.spec.whatwg.org/#disconnection-events). Called from any thread.</summary>
    public void OnDeviceDisconnected(BluetoothHostDevice device) => Global.QueueGlobalTask(() =>
    {
        if (_devices.TryGetValue(device, out var deviceObject) && deviceObject.Gatt!.Connected)
        {
            deviceObject.Gatt.CleanUpDisconnectedDevice();
        }
    });

    /// <summary>A notification or an indication of a characteristic (https://bluetooth.spec.whatwg.org/#notification-events). Called from any thread.</summary>
    public void OnCharacteristicValueChanged(BluetoothHostCharacteristic characteristic, byte[] value) => Global.QueueGlobalTask(() =>
    {
        if (characteristic.Service?.Device is not { } device || !_devices.TryGetValue(device, out var deviceObject))
            return;

        deviceObject.Gatt!.OnCharacteristicValueChanged(characteristic, value);
    });

    /// <summary>The document of the window is unloaded: its devices are disconnected.</summary>
    public void OnDestroyed()
    {
        foreach (var device in _devices.Values)
        {
            device.Gatt!.Disconnect();
        }
    }

    public BluetoothDevice GetOrCreate(BluetoothAllowedDevice allowedDevice)
    {
        if (!_devices.TryGetValue(allowedDevice.Device, out var deviceObject))
        {
            deviceObject = new BluetoothDevice(this, allowedDevice.Device, allowedDevice.DeviceId);
            _devices.Add(allowedDevice.Device, deviceObject);
        }

        return deviceObject;
    }

    private Task<bool> GetAvailabilityCore()
    {
        var result = new TaskCompletionSource<bool>();
        var availability = IsAllowedToUse() && Provider is { IsAvailable: true };
        Global.QueueGlobalTask(() => result.TrySetResult(availability));
        return result.Task;
    }

    private Task<IReadOnlyList<BluetoothDevice>> GetDevicesCore()
    {
        if (GetSecurityError("getDevices") is { } error)
            return Task.FromException<IReadOnlyList<BluetoothDevice>>(error);

        var result = new TaskCompletionSource<IReadOnlyList<BluetoothDevice>>();
        Global.QueueGlobalTask(() =>
        {
            var devices = new List<BluetoothDevice>();
            if (Provider is { } provider)
            {
                foreach (var allowedDevice in provider.GetAllowedDevices(Origin))
                {
                    devices.Add(GetOrCreate(allowedDevice));
                }
            }

            result.TrySetResult(devices);
        });
        return result.Task;
    }

    // https://bluetooth.spec.whatwg.org/#dom-bluetooth-requestdevice, with the order of the checks of Chromium: the security
    // checks, the user activation, the options (TypeError), then the blocklist
    private Task<BluetoothDevice> RequestDeviceCore(RequestDeviceOptions options)
    {
        if (GetSecurityError("requestDevice") is { } error)
            return Task.FromException<BluetoothDevice>(error);

        if (!Global.HasTransientActivation)
            return Task.FromException<BluetoothDevice>(new DomException(FailedToExecute("requestDevice", "Must be handling a user gesture to show a permission request."), "SecurityError"));

        CanonicalRequest request;
        try
        {
            request = Canonicalize(options);
        }
        catch (Exception exception) when (exception is TypeErrorException or DomException)
        {
            return Task.FromException<BluetoothDevice>(exception);
        }

        var result = new TaskCompletionSource<BluetoothDevice>();
        if (Provider is not { IsAvailable: true } provider)
        {
            Global.QueueGlobalTask(() => result.TrySetException(DomException.NotFound("Bluetooth adapter not available.")));
            return result.Task;
        }

        // In parallel: the devices in range that match the filters are offered to the user (none when the adapter is off)
        var candidates = provider.AdapterState == BluetoothAdapterState.PoweredOn ? provider.Devices.Where(request.Matches).ToArray() : [];
        Task<BluetoothHostDevice?> selection;
        try
        {
            selection = provider.RequestDevice(new BluetoothDeviceRequest(CallbackContext.For(Global), options, request.OptionalServices, candidates));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            selection = Task.FromException<BluetoothHostDevice?>(exception);
        }

        _ = selection.ContinueWith(
            task => Global.QueueGlobalTask(() =>
            {
                var device = task.IsCompletedSuccessfully ? task.Result : null;
                if (device is null || device.Provider != provider)
                {
                    result.TrySetException(DomException.NotFound("User cancelled the requestDevice() chooser."));
                    return;
                }

                // https://bluetooth.spec.whatwg.org/#add-an-allowed-bluetooth-device: the services of the filters and the optional services
                var allowedDevice = provider.Allow(device, Origin, request.RequiredServices.Concat(request.OptionalServices), options.OptionalManufacturerData);
                result.TrySetResult(GetOrCreate(allowedDevice));
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return result.Task;
    }

    private static CanonicalRequest Canonicalize(RequestDeviceOptions options)
    {
        if (options.ExclusionFilters is not null && options.Filters is null)
            throw new TypeErrorException(FailedToExecute("requestDevice", "'filters' member must be present if 'exclusionFilters' is present."));

        if ((options.Filters is not null) == options.AcceptAllDevices)
            throw new TypeErrorException(FailedToExecute("requestDevice", "Either 'filters' should be present or 'acceptAllDevices' should be true, but not both."));

        if (options.Filters is { Count: 0 })
            throw new TypeErrorException(FailedToExecute("requestDevice", "'filters' member must be non-empty to find any devices."));

        if (options.ExclusionFilters is { Count: 0 })
            throw new TypeErrorException(FailedToExecute("requestDevice", "'exclusionFilters' member must be non-empty to exclude any device."));

        var filters = options.Filters?.Select(CanonicalFilter.Create).ToArray();
        var exclusionFilters = options.ExclusionFilters?.Select(CanonicalFilter.Create).ToArray() ?? [];
        var optionalServices = new List<string>();
        foreach (var service in options.OptionalServices)
        {
            var uuid = BluetoothUuid.TryResolve(service, BluetoothUuidKind.Service) ?? throw new TypeErrorException(FailedToExecute("requestDevice", BluetoothUuid.GetInvalidNameMessage(service, BluetoothUuidKind.Service)));

            // The blocklisted optional services are removed: the origin cannot use them
            if (!BluetoothBlocklist.IsBlocklisted(uuid))
            {
                optionalServices.Add(uuid);
            }
        }

        // The blocklist is checked once the options are valid, as in Chromium (which checks it in the browser process)
        if (filters is not null && filters.Any(filter => filter.IsBlocklisted))
            throw new DomException("requestDevice() called with a filter containing a blocklisted UUID or manufacturer data. https://goo.gl/4NeimX", "SecurityError");

        return new CanonicalRequest(filters, exclusionFilters, optionalServices);
    }

    /// <summary>
    /// The bluetooth permissions policy feature (default allowlist 'self'), and the top-level origin, which must not be opaque
    /// (as in Chromium).
    /// </summary>
    private DomException? GetSecurityError(string operation)
    {
        var document = Global.Document;
        if (document.TopLevelOrigin.IsOpaque)
            return new DomException(FailedToExecute(operation, "Access to the Web Bluetooth API is denied from contexts where the top-level document has an opaque origin."), "SecurityError");

        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(document, BluetoothFeature))
            return new DomException(FailedToExecute(operation, "Access to the feature \"bluetooth\" is disallowed by permissions policy."), "SecurityError");

        return null;
    }

    // The bluetooth permissions policy feature, whose violation is reported by getAvailability()
    private bool IsAllowedToUse(bool report = true) => !Global.Document.TopLevelOrigin.IsOpaque && PermissionsPolicyAlgorithms.IsAllowedToUse(Global.Document, BluetoothFeature, report);

    private static string FailedToExecute(string operation, string message) => $"Failed to execute '{operation}' on 'Bluetooth': {message}";

    private static PolicyControlledFeature BluetoothFeature => PolicyControlledFeature.Get("bluetooth")!;

    /// <summary>The canonicalized options of <c>requestDevice()</c>.</summary>
    private sealed class CanonicalRequest(CanonicalFilter[]? filters, CanonicalFilter[] exclusionFilters, List<string> optionalServices)
    {
        public IReadOnlyList<string> OptionalServices => optionalServices;

        /// <summary>The services of the filters (requiredServiceUUIDs), which the origin can use.</summary>
        public IEnumerable<string> RequiredServices => filters?.SelectMany(filter => filter.Services ?? []) ?? [];

        public bool Matches(BluetoothHostDevice device)
            => (filters is null || filters.Any(filter => filter.Matches(device))) && !exclusionFilters.Any(filter => filter.Matches(device));
    }

    /// <summary>A canonicalized filter (https://bluetooth.spec.whatwg.org/#canonicalizing-the-bluetoothlescanfilterinit).</summary>
    private sealed class CanonicalFilter
    {
        public string[]? Services { get; private init; }

        public string? Name { get; private init; }

        public string? NamePrefix { get; private init; }

        public (ushort CompanyIdentifier, byte[] DataPrefix, byte[] Mask)[] ManufacturerData { get; private init; } = [];

        public (string Service, byte[] DataPrefix, byte[] Mask)[] ServiceData { get; private init; } = [];

        public bool IsBlocklisted
            => (Services?.Any(BluetoothBlocklist.IsBlocklisted) ?? false)
            || ServiceData.Any(data => BluetoothBlocklist.IsBlocklisted(data.Service))
            || ManufacturerData.Any(data => BluetoothBlocklist.IsBlocklistedManufacturerDataFilter(data.CompanyIdentifier, data.DataPrefix, data.Mask));

        public static CanonicalFilter Create(BluetoothLEScanFilterInit filter)
        {
            if (filter.Services is null && filter.Name is null && filter.NamePrefix is null && filter.ManufacturerData is null && filter.ServiceData is null)
                throw new TypeErrorException(FailedToExecute("requestDevice", "A filter must restrict the devices in some way."));

            string[]? services = null;
            if (filter.Services is { } filterServices)
            {
                if (filterServices.Count == 0)
                    throw new TypeErrorException(FailedToExecute("requestDevice", "'services', if present, must contain at least one service."));

                services = [.. filterServices.Select(service => BluetoothUuid.TryResolve(service, BluetoothUuidKind.Service) ?? throw new TypeErrorException(FailedToExecute("requestDevice", BluetoothUuid.GetInvalidNameMessage(service, BluetoothUuidKind.Service))))];
            }

            if (filter.Name is { } name && Encoding.UTF8.GetByteCount(name) > MaxDeviceNameLength)
                throw new TypeErrorException(FailedToExecute("requestDevice", "A device name can't be longer than 248 bytes."));

            if (filter.NamePrefix is { } namePrefix)
            {
                if (namePrefix.Length == 0)
                    throw new TypeErrorException(FailedToExecute("requestDevice", "'namePrefix', if present, must be non-empty."));

                if (Encoding.UTF8.GetByteCount(namePrefix) > MaxDeviceNameLength)
                    throw new TypeErrorException(FailedToExecute("requestDevice", "A device name can't be longer than 248 bytes."));
            }

            var manufacturerData = new List<(ushort, byte[], byte[])>();
            if (filter.ManufacturerData is { } manufacturerDataFilters)
            {
                if (manufacturerDataFilters.Count == 0)
                    throw new TypeErrorException(FailedToExecute("requestDevice", "'manufacturerData', if present, must be non-empty."));

                foreach (var data in manufacturerDataFilters)
                {
                    if (manufacturerData.Exists(existing => existing.Item1 == data.CompanyIdentifier))
                        throw new TypeErrorException(FailedToExecute("requestDevice", "'companyIdentifier' must be unique in 'manufacturerData'."));

                    var (prefix, mask) = CanonicalizeDataFilter(data);
                    manufacturerData.Add((data.CompanyIdentifier, prefix, mask));
                }
            }

            var serviceData = new List<(string, byte[], byte[])>();
            if (filter.ServiceData is { } serviceDataFilters)
            {
                if (serviceDataFilters.Count == 0)
                    throw new TypeErrorException(FailedToExecute("requestDevice", "'serviceData', if present, must be non-empty."));

                foreach (var data in serviceDataFilters)
                {
                    var service = BluetoothUuid.TryResolve(data.Service, BluetoothUuidKind.Service) ?? throw new TypeErrorException(FailedToExecute("requestDevice", BluetoothUuid.GetInvalidNameMessage(data.Service, BluetoothUuidKind.Service)));
                    var (prefix, mask) = CanonicalizeDataFilter(data);
                    serviceData.Add((service, prefix, mask));
                }
            }

            return new CanonicalFilter
            {
                Services = services,
                Name = filter.Name,
                NamePrefix = filter.NamePrefix,
                ManufacturerData = [.. manufacturerData],
                ServiceData = [.. serviceData],
            };
        }

        // https://bluetooth.spec.whatwg.org/#matches-a-filter
        public bool Matches(BluetoothHostDevice device)
        {
            if (Name is not null && !string.Equals(device.Name, Name, StringComparison.Ordinal))
                return false;

            if (NamePrefix is not null && (device.Name is null || !device.Name.StartsWith(NamePrefix, StringComparison.Ordinal)))
                return false;

            if (Services is not null)
            {
                var deviceServices = device.ServiceUuids.Concat(device.Services.Where(service => service.IsPrimary).Select(service => service.Uuid)).ToHashSet(StringComparer.Ordinal);
                if (!Services.All(deviceServices.Contains))
                    return false;
            }

            foreach (var (companyIdentifier, prefix, mask) in ManufacturerData)
            {
                if (!device.ManufacturerData.TryGetValue(companyIdentifier, out var data) || !BluetoothDataFilter.Matches(data.Span, prefix, mask))
                    return false;
            }

            foreach (var (service, prefix, mask) in ServiceData)
            {
                if (!device.ServiceData.TryGetValue(service, out var data) || !BluetoothDataFilter.Matches(data.Span, prefix, mask))
                    return false;
            }

            return true;
        }

        // https://bluetooth.spec.whatwg.org/#canonicalizing-the-bluetoothdatafilterinit (a detached buffer is empty)
        private static (byte[] DataPrefix, byte[] Mask) CanonicalizeDataFilter(BluetoothDataFilterInit filter)
        {
            byte[] dataPrefix = [];
            if (filter.DataPrefix is { } prefix)
            {
                dataPrefix = prefix.ToArray();
                if (dataPrefix.Length == 0)
                    throw new TypeErrorException(FailedToExecute("requestDevice", "'dataPrefix', if present, must be non-empty."));
            }

            var mask = filter.Mask is { } filterMask ? filterMask.ToArray() : [.. Enumerable.Repeat((byte)0xFF, dataPrefix.Length)];
            if (mask.Length != dataPrefix.Length)
                throw new TypeErrorException(FailedToExecute("requestDevice", "'mask' size must be equal to 'dataPrefix' size."));

            return (dataPrefix, mask);
        }
    }
}

// https://bluetooth.spec.whatwg.org/#navigator-extensions
public sealed partial class Navigator
{
    /// <summary>Gets the Web Bluetooth API of the window.</summary>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-navigator-bluetooth"/>
    [IdlMember("bluetooth", "https://bluetooth.spec.whatwg.org/#dom-navigator-bluetooth")]
    public Bluetooth Bluetooth => _window.GetBluetoothState().Bluetooth;
}

public partial class Window
{
    private GlobalBluetooth? _bluetoothState;

    /// <summary>The Bluetooth state of the window, created by the first access to <c>navigator.bluetooth</c>.</summary>
    internal GlobalBluetooth GetBluetoothState() => _bluetoothState ??= new GlobalBluetooth(this);

    /// <summary>The document of the window is unloaded: its Bluetooth devices are disconnected.</summary>
    internal void CloseBluetoothDevices() => _bluetoothState?.OnDestroyed();
}
