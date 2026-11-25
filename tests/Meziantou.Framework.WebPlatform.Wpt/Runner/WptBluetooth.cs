using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The simulated Bluetooth adapter of the tests (<see cref="BrowsingContextOptions.Bluetooth"/>), driven by the WebDriver
/// BiDi commands of the bluetooth module (https://webbluetoothcg.github.io/web-bluetooth/#automated-testing) that
/// testdriver-vendor.js implements with <c>__wpt_bluetooth</c>: the simulated peripherals, their GATT attributes, and the
/// responses of the operations of the pages. The events of the module (the device prompts, the connection attempts and the
/// operations on the attributes) are emitted to the realms that subscribed to them. There is no adapter before
/// <c>simulateAdapter</c>, as on a machine without Bluetooth.
/// </summary>
internal sealed class WptBluetoothProvider : BluetoothProvider
{
    private const string ContextId = "wpt-context";

    private readonly Lock _lock = new();
    private readonly ConcurrentDictionary<GlobalScope, Action<string, string>> _dispatchers = new();
    private bool _simulated;
    private int _nextPromptId;
    private DevicePrompt? _prompt;

    public WptBluetoothProvider()
    {
        AdapterState = BluetoothAdapterState.Absent;
    }

    // https://webbluetoothcg.github.io/web-bluetooth/#bluetooth-requestdevicepromptupdated-event: the prompt is answered by bluetooth.handleRequestDevicePrompt
    public override Task<BluetoothHostDevice?> RequestDevice(BluetoothDeviceRequest request)
    {
        var completion = new TaskCompletionSource<BluetoothHostDevice?>();
        DevicePrompt prompt;
        lock (_lock)
        {
            _prompt?.Completion.TrySetResult(null);
            prompt = new DevicePrompt("pmt-" + (++_nextPromptId).ToString(CultureInfo.InvariantCulture), request.Devices, completion);
            _prompt = prompt;
        }

        var devices = new JsonArray();
        foreach (var device in request.Devices)
        {
            devices.Add(new JsonObject { ["id"] = device.Address, ["name"] = device.Name });
        }

        Emit("request_device_prompt_updated", new JsonObject { ["context"] = ContextId, ["prompt"] = prompt.Id, ["devices"] = devices });
        return completion.Task;
    }

    /// <summary>
    /// Defines the functions of testdriver-vendor.js in a realm: <c>__wpt_bluetooth</c> runs a command after the tasks that
    /// are already queued (as a WebDriver command), and <c>__wpt_bluetooth_dispatcher</c> registers the function that emits
    /// the events in the realm.
    /// </summary>
    public void DefineTestApi<TAdapter, TValue>(WebIdlRealm<TAdapter, TValue> realm, Action<Action> queueTask)
        where TAdapter : struct, IJsAdapter<TValue>
        where TValue : class
    {
        var adapter = realm.Adapter;

        void Define(string name, int length, JsHostFunction<TValue> function)
            => adapter.DefineOwnProperty(realm.Global, adapter.FromString(name), JsPropertyDescriptor<TValue>.Data(adapter.CreateFunction(name, length, function), writable: true, enumerable: false, configurable: true));

        Define("__wpt_bluetooth", 4, (_, args) =>
        {
            var command = adapter.ToString(args[0]);
            var parameters = adapter.ToString(args[1]);
            var (resolve, reject) = (args[2], args[3]);
            queueTask(() => realm.Run(() =>
            {
                try
                {
                    Execute(command, JsonNode.Parse(parameters) as JsonObject ?? []);
                }
                catch (WptWebDriverException ex)
                {
                    return adapter.Call(reject, adapter.Undefined, [adapter.FromString(ex.Message)]);
                }

                return adapter.Call(resolve, adapter.Undefined, []);
            }));
            return adapter.Undefined;
        });
        Define("__wpt_bluetooth_dispatcher", 1, (_, args) =>
        {
            var dispatch = args[0];
            var global = realm.GlobalScope;
            _dispatchers[global] = (name, json) => global.QueueGlobalTask(() => realm.Run(() => adapter.Call(dispatch, adapter.Undefined, [adapter.FromString(name), adapter.FromString(json)])));
            return adapter.Undefined;
        });
    }

    /// <summary>Emits an event of the bluetooth module to the realms (testdriver-vendor.js delivers it while there is a subscription).</summary>
    internal void Emit(string name, JsonObject parameters)
    {
        var json = parameters.ToJsonString();
        foreach (var dispatcher in _dispatchers.Values)
        {
            dispatcher(name, json);
        }
    }

    internal static string ToJson(ReadOnlyMemory<byte> data)
    {
        var array = new JsonArray();
        foreach (var value in data.Span)
        {
            array.Add(value);
        }

        return array.ToJsonString();
    }

    internal static JsonObject CreateEventParameters(BluetoothHostDevice device) => new() { ["context"] = ContextId, ["address"] = device.Address };

    private static WptWebDriverException InvalidArgument(string message) => new("invalid argument: " + message);

    private static WptWebDriverException InvalidElementState(string message) => new("invalid element state: " + message);

    private static string GetString(JsonObject parameters, string name)
        => parameters[name] is JsonValue value && value.TryGetValue<string>(out var result) ? result : throw InvalidArgument($"'{name}' must be a string");

    private static int GetCode(JsonObject parameters)
        => parameters["code"] is JsonValue value && value.TryGetValue<double>(out var code) && code >= 0 ? (int)code : throw InvalidArgument("'code' must be an unsigned integer");

    private static byte[] GetData(JsonObject parameters)
    {
        if (parameters["data"] is not JsonArray array)
            return [];

        var result = new byte[array.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = array[i] is JsonValue value && value.TryGetValue<double>(out var number) ? (byte)number : throw InvalidArgument("'data' must be an array of bytes");
        }

        return result;
    }

    private static BluetoothCharacteristicPropertyFlags GetProperties(JsonObject properties)
    {
        var result = BluetoothCharacteristicPropertyFlags.None;
        foreach (var (name, flag) in new[]
        {
            ("broadcast", BluetoothCharacteristicPropertyFlags.Broadcast),
            ("read", BluetoothCharacteristicPropertyFlags.Read),
            ("writeWithoutResponse", BluetoothCharacteristicPropertyFlags.WriteWithoutResponse),
            ("write", BluetoothCharacteristicPropertyFlags.Write),
            ("notify", BluetoothCharacteristicPropertyFlags.Notify),
            ("indicate", BluetoothCharacteristicPropertyFlags.Indicate),
            ("authenticatedSignedWrites", BluetoothCharacteristicPropertyFlags.AuthenticatedSignedWrites),
            ("extendedProperties", BluetoothCharacteristicPropertyFlags.ExtendedProperties),
        })
        {
            if (properties[name] is JsonValue value && value.TryGetValue<bool>(out var set) && set)
            {
                result |= flag;
            }
        }

        return result;
    }

    // The remote end steps of the commands of https://webbluetoothcg.github.io/web-bluetooth/#bluetooth-commands
    private void Execute(string command, JsonObject parameters)
    {
        switch (command)
        {
            case "handleRequestDevicePrompt":
                HandleRequestDevicePrompt(parameters);
                break;

            case "simulateAdapter":
                SimulateAdapter(parameters);
                break;

            case "disableSimulation":
                DisableSimulation();
                break;

            case "simulatePreconnectedPeripheral":
                SimulatePreconnectedPeripheral(parameters);
                break;

            case "simulateGattConnectionResponse":
                if (!GetDevice(parameters).RespondToConnection(GetCode(parameters)))
                    throw InvalidElementState("no GATT connection attempt is expected");

                break;

            case "simulateGattDisconnection":
                GetDevice(parameters).SimulateDisconnection();
                break;

            case "simulateGattMtuChange":
                // maxWriteWithoutResponseSize is not implemented (it is not in the specification): the MTU is ignored
                GetDevice(parameters);
                break;

            case "simulateService":
                SimulateService(parameters);
                break;

            case "simulateCharacteristic":
                SimulateCharacteristic(parameters);
                break;

            case "simulateCharacteristicResponse":
                GetCharacteristic(parameters, required: true)!.Respond(GetString(parameters, "type"), GetCode(parameters), GetData(parameters));
                break;

            case "simulateDescriptor":
                SimulateDescriptor(parameters);
                break;

            case "simulateDescriptorResponse":
                GetDescriptor(parameters).Respond(GetString(parameters, "type"), GetCode(parameters), GetData(parameters));
                break;

            default:
                throw new WptWebDriverException("unknown command: bluetooth." + command);
        }
    }

    private void HandleRequestDevicePrompt(JsonObject parameters)
    {
        var promptId = GetString(parameters, "prompt");
        DevicePrompt prompt;
        lock (_lock)
        {
            if (_prompt is null || _prompt.Id != promptId)
                throw new WptWebDriverException("no such prompt: " + promptId);

            prompt = _prompt;
        }

        BluetoothHostDevice? device = null;
        if (parameters["accept"] is JsonValue accept && accept.TryGetValue<bool>(out var isAccepted) && isAccepted)
        {
            var deviceId = GetString(parameters, "device");
            device = prompt.Devices.FirstOrDefault(candidate => candidate.Address == deviceId) ?? throw new WptWebDriverException("no such device: " + deviceId);
        }

        lock (_lock)
        {
            _prompt = null;
        }

        prompt.Completion.TrySetResult(device);
    }

    private void SimulateAdapter(JsonObject parameters)
    {
        var state = GetString(parameters, "state") switch
        {
            "absent" => BluetoothAdapterState.Absent,
            "powered-off" => BluetoothAdapterState.PoweredOff,
            "powered-on" => BluetoothAdapterState.PoweredOn,
            var other => throw InvalidArgument($"unknown adapter state '{other}'"),
        };
        bool? leSupported = parameters["leSupported"] is JsonValue value && value.TryGetValue<bool>(out var supported) ? supported : null;
        if (_simulated && leSupported is not null)
            throw InvalidArgument("the LE support of an existing adapter cannot change");

        if (!_simulated)
        {
            IsLowEnergySupported = leSupported ?? true;
            _simulated = true;
        }

        AdapterState = state;
    }

    private void DisableSimulation()
    {
        foreach (var device in Devices)
        {
            RemoveDevice(device);
        }

        DevicePrompt? prompt;
        lock (_lock)
        {
            prompt = _prompt;
            _prompt = null;
        }

        prompt?.Completion.TrySetResult(null);
        _simulated = false;
        AdapterState = BluetoothAdapterState.Absent;
    }

    private void SimulatePreconnectedPeripheral(JsonObject parameters)
    {
        if (!_simulated)
            throw InvalidArgument("there is no simulated adapter");

        var address = GetString(parameters, "address");
        if (Devices.Any(device => device.Address == address))
            throw InvalidArgument($"the peripheral {address} already exists");

        var manufacturerData = new Dictionary<ushort, ReadOnlyMemory<byte>>();
        if (parameters["manufacturerData"] is JsonArray entries)
        {
            foreach (var entry in entries.OfType<JsonObject>())
            {
                var key = entry["key"] is JsonValue keyValue && keyValue.TryGetValue<double>(out var number) ? (ushort)number : throw InvalidArgument("'key' must be a company identifier");
                manufacturerData[key] = Convert.FromBase64String(GetString(entry, "data"));
            }
        }

        var serviceUuids = parameters["knownServiceUuids"] is JsonArray uuids ? uuids.Select(uuid => uuid?.GetValue<string>() ?? "").ToArray() : [];
        AddDevice(new WptBluetoothDevice(this, address, GetString(parameters, "name"))
        {
            ManufacturerData = manufacturerData,
            ServiceUuids = serviceUuids,
        });
    }

    private void SimulateService(JsonObject parameters)
    {
        var device = GetDevice(parameters);
        var uuid = GetString(parameters, "uuid");
        var existing = device.Services.FirstOrDefault(service => service.Uuid == uuid);
        switch (GetString(parameters, "type"))
        {
            case "add":
                if (existing is not null)
                    throw InvalidElementState($"the service {uuid} already exists");

                device.AddService(new BluetoothHostService(ValidateUuid(uuid)));
                break;

            case "remove":
                if (existing is null)
                    throw InvalidElementState($"the service {uuid} does not exist");

                device.RemoveService(existing);
                break;

            default:
                throw InvalidArgument("'type' must be 'add' or 'remove'");
        }
    }

    private void SimulateCharacteristic(JsonObject parameters)
    {
        var service = GetService(parameters);
        var uuid = GetString(parameters, "characteristicUuid");
        var existing = GetCharacteristic(parameters, required: false);
        switch (GetString(parameters, "type"))
        {
            case "add":
                if (existing is not null)
                    throw InvalidElementState($"the characteristic {uuid} already exists");

                if (parameters["characteristicProperties"] is not JsonObject properties)
                    throw InvalidArgument("'characteristicProperties' is required to add a characteristic");

                service.AddCharacteristic(new WptBluetoothCharacteristic(this, ValidateUuid(uuid), GetProperties(properties)));
                break;

            case "remove":
                if (parameters["characteristicProperties"] is not null)
                    throw InvalidArgument("'characteristicProperties' cannot be given to remove a characteristic");

                if (existing is null)
                    throw InvalidElementState($"the characteristic {uuid} does not exist");

                service.RemoveCharacteristic(existing);
                break;

            default:
                throw InvalidArgument("'type' must be 'add' or 'remove'");
        }
    }

    private void SimulateDescriptor(JsonObject parameters)
    {
        var characteristic = GetCharacteristic(parameters, required: true)!;
        var uuid = GetString(parameters, "descriptorUuid");
        var existing = characteristic.Descriptors.FirstOrDefault(descriptor => descriptor.Uuid == uuid);
        switch (GetString(parameters, "type"))
        {
            case "add":
                if (existing is not null)
                    throw InvalidElementState($"the descriptor {uuid} already exists");

                characteristic.AddDescriptor(new WptBluetoothDescriptor(this, ValidateUuid(uuid)));
                break;

            case "remove":
                if (existing is null)
                    throw InvalidElementState($"the descriptor {uuid} does not exist");

                characteristic.RemoveDescriptor(existing);
                break;

            default:
                throw InvalidArgument("'type' must be 'add' or 'remove'");
        }
    }

    private static string ValidateUuid(string uuid) => BluetoothUuid.IsValidUuid(uuid) ? uuid : throw InvalidArgument($"'{uuid}' is not a valid UUID");

    private WptBluetoothDevice GetDevice(JsonObject parameters)
    {
        if (!_simulated)
            throw InvalidArgument("there is no simulated adapter");

        var address = GetString(parameters, "address");
        return Devices.OfType<WptBluetoothDevice>().FirstOrDefault(device => device.Address == address) ?? throw InvalidArgument($"the peripheral {address} does not exist");
    }

    private BluetoothHostService GetService(JsonObject parameters)
    {
        var uuid = GetString(parameters, "serviceUuid");
        return GetDevice(parameters).Services.FirstOrDefault(service => service.Uuid == uuid) ?? throw InvalidArgument($"the service {uuid} does not exist");
    }

    private WptBluetoothCharacteristic? GetCharacteristic(JsonObject parameters, bool required)
    {
        var uuid = GetString(parameters, "characteristicUuid");
        var characteristic = GetService(parameters).Characteristics.OfType<WptBluetoothCharacteristic>().FirstOrDefault(characteristic => characteristic.Uuid == uuid);
        if (characteristic is null && required)
            throw InvalidElementState($"the characteristic {uuid} does not exist");

        return characteristic;
    }

    private WptBluetoothDescriptor GetDescriptor(JsonObject parameters)
    {
        var uuid = GetString(parameters, "descriptorUuid");
        return GetCharacteristic(parameters, required: true)!.Descriptors.OfType<WptBluetoothDescriptor>().FirstOrDefault(descriptor => descriptor.Uuid == uuid)
            ?? throw InvalidElementState($"the descriptor {uuid} does not exist");
    }

    private sealed record DevicePrompt(string Id, IReadOnlyList<BluetoothHostDevice> Devices, TaskCompletionSource<BluetoothHostDevice?> Completion);
}

/// <summary>
/// A simulated peripheral: a connection attempt emits <c>bluetooth.gattConnectionAttempted</c>, and waits for
/// <c>bluetooth.simulateGattConnectionResponse</c>. As in Chromium, the simulated connection stays when the pages disconnect:
/// only <c>bluetooth.simulateGattDisconnection</c> ends it.
/// </summary>
internal sealed class WptBluetoothDevice(WptBluetoothProvider provider, string address, string name) : BluetoothHostDevice(address, name)
{
    // 0x15: Remote Device Terminated Connection due to Power Off
    private const int RemoteDeviceTerminatedConnectionDueToPowerOff = 0x15;

    private TaskCompletionSource? _pendingConnection;

    /// <summary>bluetooth.simulateGattConnectionResponse: returns false when no connection attempt is expected.</summary>
    public bool RespondToConnection(int code)
    {
        var pending = Interlocked.Exchange(ref _pendingConnection, null);
        if (pending is null)
            return false;

        if (code == 0)
        {
            pending.TrySetResult();
        }
        else
        {
            pending.TrySetException(new DomException($"Connection failed (HCI error 0x{code:x2}).", "NetworkError"));
        }

        return true;
    }

    /// <summary>bluetooth.simulateGattDisconnection: a pending connection attempt fails, otherwise the connection is lost.</summary>
    public void SimulateDisconnection()
    {
        if (!RespondToConnection(RemoteDeviceTerminatedConnectionDueToPowerOff))
        {
            Disconnect();
        }
    }

    // The answer completes the connection synchronously, in the task that runs the command: a continuation on the thread pool
    // would race the virtual clock of the test
    protected override Task ConnectAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource();
        _pendingConnection = completion;
        provider.Emit("gatt_connection_attempted", WptBluetoothProvider.CreateEventParameters(this));
        return completion.Task;
    }

    protected override void OnLastClientDisconnected()
    {
    }
}

/// <summary>A simulated characteristic: each operation emits <c>bluetooth.characteristicEventGenerated</c>, and waits for <c>bluetooth.simulateCharacteristicResponse</c>.</summary>
internal sealed class WptBluetoothCharacteristic(WptBluetoothProvider provider, string uuid, BluetoothCharacteristicPropertyFlags properties) : BluetoothHostCharacteristic(uuid, properties)
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> _pendingResponses = new(StringComparer.Ordinal);

    /// <summary>bluetooth.simulateCharacteristicResponse.</summary>
    public void Respond(string type, int code, byte[] data)
    {
        if (type is not ("read" or "write" or "subscribe-to-notifications" or "unsubscribe-from-notifications"))
            throw new WptWebDriverException("invalid argument: unknown response type " + type);

        if (!_pendingResponses.TryRemove(type, out var pending))
            throw new WptWebDriverException($"invalid element state: no {type} response is expected");

        Complete(pending, code, data);
    }

    protected internal override Task<ReadOnlyMemory<byte>> ReadValueAsync(CancellationToken cancellationToken)
        => Wait("read", "read", data: null).ContinueWith(static task => (ReadOnlyMemory<byte>)task.GetAwaiter().GetResult(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    protected internal override Task WriteValueAsync(ReadOnlyMemory<byte> value, BluetoothWriteType writeType, CancellationToken cancellationToken)
    {
        // A write without response does not wait for a response (as in Chromium)
        if (writeType == BluetoothWriteType.WithoutResponse)
        {
            Emit("write-without-response", value);
            return Task.CompletedTask;
        }

        return Wait("write", "write-with-response", value);
    }

    protected internal override Task StartNotificationsAsync(CancellationToken cancellationToken) => Wait("subscribe-to-notifications", "subscribe-to-notifications", data: null);

    protected internal override Task StopNotificationsAsync(CancellationToken cancellationToken) => Wait("unsubscribe-from-notifications", "unsubscribe-from-notifications", data: null);

    internal static void Complete(TaskCompletionSource<byte[]> pending, int code, byte[] data)
    {
        if (code == 0)
        {
            pending.TrySetResult(data);
        }
        else
        {
            pending.TrySetException(new DomException($"GATT operation failed (error 0x{code:x2}).", "NetworkError"));
        }
    }

    // https://webbluetoothcg.github.io/web-bluetooth/#trigger-a-simulated-characteristic-event: an operation is pending until its response
    private Task<byte[]> Wait(string responseType, string eventType, ReadOnlyMemory<byte>? data)
    {
        var completion = new TaskCompletionSource<byte[]>();
        if (!_pendingResponses.TryAdd(responseType, completion))
            return Task.FromException<byte[]>(DomException.InvalidState("A GATT operation is already in progress."));

        Emit(eventType, data);
        return completion.Task;
    }

    private void Emit(string type, ReadOnlyMemory<byte>? data)
    {
        if (Service?.Device is not { } device)
            return;

        var parameters = WptBluetoothProvider.CreateEventParameters(device);
        parameters["serviceUuid"] = Service.Uuid;
        parameters["characteristicUuid"] = Uuid;
        parameters["type"] = type;
        if (data is { } bytes)
        {
            parameters["data"] = JsonNode.Parse(WptBluetoothProvider.ToJson(bytes));
        }

        provider.Emit("characteristic_event_generated", parameters);
    }
}

/// <summary>A simulated descriptor: each operation emits <c>bluetooth.descriptorEventGenerated</c>, and waits for <c>bluetooth.simulateDescriptorResponse</c>.</summary>
internal sealed class WptBluetoothDescriptor(WptBluetoothProvider provider, string uuid) : BluetoothHostDescriptor(uuid)
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> _pendingResponses = new(StringComparer.Ordinal);

    /// <summary>bluetooth.simulateDescriptorResponse.</summary>
    public void Respond(string type, int code, byte[] data)
    {
        if (type is not ("read" or "write"))
            throw new WptWebDriverException("invalid argument: unknown response type " + type);

        if (!_pendingResponses.TryRemove(type, out var pending))
            throw new WptWebDriverException($"invalid element state: no {type} response is expected");

        WptBluetoothCharacteristic.Complete(pending, code, data);
    }

    protected internal override Task<ReadOnlyMemory<byte>> ReadValueAsync(CancellationToken cancellationToken)
        => Wait("read", data: null).ContinueWith(static task => (ReadOnlyMemory<byte>)task.GetAwaiter().GetResult(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    protected internal override Task WriteValueAsync(ReadOnlyMemory<byte> value, CancellationToken cancellationToken) => Wait("write", value);

    // https://webbluetoothcg.github.io/web-bluetooth/#trigger-a-simulated-descriptor-event
    private Task<byte[]> Wait(string type, ReadOnlyMemory<byte>? data)
    {
        var completion = new TaskCompletionSource<byte[]>();
        if (!_pendingResponses.TryAdd(type, completion))
            return Task.FromException<byte[]>(DomException.InvalidState("A GATT operation is already in progress."));

        if (Characteristic is { Service.Device: { } device } characteristic)
        {
            var parameters = WptBluetoothProvider.CreateEventParameters(device);
            parameters["serviceUuid"] = characteristic.Service.Uuid;
            parameters["characteristicUuid"] = characteristic.Uuid;
            parameters["descriptorUuid"] = Uuid;
            parameters["type"] = type;
            if (data is { } bytes)
            {
                parameters["data"] = JsonNode.Parse(WptBluetoothProvider.ToJson(bytes));
            }

            provider.Emit("descriptor_event_generated", parameters);
        }

        return completion.Task;
    }
}
