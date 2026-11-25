using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The USB devices of the tests (<see cref="BrowsingContextOptions.Usb"/>), driven by the WebUSB Testing API that
/// Runner/webusb-test.js defines in each realm (<c>navigator.usb.test</c>), as the polyfill of Chromium
/// (/resources/chromium/webusb-test.js) on its Mojo service: every origin is allowed to use the fake devices, and the
/// chooser of <c>requestDevice()</c> is the <c>onrequestdevice</c> handler of the test.
/// </summary>
internal sealed class WptUsbProvider : UsbProvider
{
    private readonly ConcurrentDictionary<int, WptFakeUsbDevice> _devices = new();
    private readonly ConcurrentDictionary<GlobalScope, Func<string, string, Task<int>>> _requestHandlers = new();
    private readonly ConcurrentDictionary<GlobalScope, Action<int>> _closeHandlers = new();
    private int _nextId;

    /// <summary>The permissions of the fake devices are not tested by the WebUSB tests: they are allowed everywhere, as in Chromium.</summary>
    public override bool IsAllowed(UsbHostDevice device, string origin) => true;

    // The answer of the handler completes the request synchronously, in the task of the event loop that runs it: a
    // continuation on the thread pool would race the virtual clock of the test
    public override Task<UsbHostDevice?> RequestDevice(UsbDeviceRequest request)
    {
        if (request.Context.Global is not { } global || !_requestHandlers.TryGetValue(global, out var handler))
            return Task.FromResult<UsbHostDevice?>(null);

        return handler(SerializeFilters(request.Filters), SerializeFilters(request.ExclusionFilters)).ContinueWith(
            task => _devices.TryGetValue(task.Result, out var device) ? device : (UsbHostDevice?)null,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>Defines the functions of Runner/webusb-test.js in a realm, then runs it: it defines <c>navigator.usb.test</c>.</summary>
    public void DefineTestApi<TAdapter, TValue>(WebIdlRealm<TAdapter, TValue> realm, string script)
        where TAdapter : struct, IJsAdapter<TValue>
        where TValue : class
    {
        var adapter = realm.Adapter;

        void Define(string name, int length, JsHostFunction<TValue> function)
            => adapter.DefineOwnProperty(realm.Global, adapter.FromString(name), JsPropertyDescriptor<TValue>.Data(adapter.CreateFunction(name, length, function), writable: true, enumerable: false, configurable: true));

        WptFakeUsbDevice? GetDevice(TValue id) => _devices.TryGetValue((int)adapter.ToNumber(id), out var device) ? device : null;

        Define("__wpt_usb_create", 1, (_, args) =>
        {
            var id = Interlocked.Increment(ref _nextId);
            var onClose = _closeHandlers.GetValueOrDefault(realm.GlobalScope);
            _devices[id] = new WptFakeUsbDevice(ParseDeviceInit(adapter.ToString(args[0])), onClose is null ? null : () => onClose(id));
            return adapter.FromNumber(id);
        });
        Define("__wpt_usb_connect", 1, (_, args) =>
        {
            if (GetDevice(args[0]) is { } device)
            {
                Connect(device);
            }

            return adapter.Undefined;
        });
        Define("__wpt_usb_disconnect", 1, (_, args) =>
        {
            if (GetDevice(args[0]) is { } device && !Disconnect(device))
                throw new InvalidOperationException("Cannot remove unknown device.");

            return adapter.Undefined;
        });
        Define("__wpt_usb_fail", 2, (_, args) =>
        {
            GetDevice(args[0])?.FailNextOperation(adapter.ToString(args[1]));
            return adapter.Undefined;
        });
        Define("__wpt_usb_stall", 2, (_, args) =>
        {
            GetDevice(args[0])?.StallNextOperation(adapter.ToString(args[1]));
            return adapter.Undefined;
        });
        Define("__wpt_usb_status", 2, (_, args) =>
        {
            GetDevice(args[0])?.SetNextTransferStatus(adapter.ToString(args[1]));
            return adapter.Undefined;
        });
        Define("__wpt_usb_reset", 0, (_, _) =>
        {
            foreach (var device in Devices)
            {
                Disconnect(device);
            }

            return adapter.Undefined;
        });
        Define("__wpt_usb_set_request_handler", 1, (_, args) =>
        {
            var handler = args[0];
            _requestHandlers[realm.GlobalScope] = (filters, exclusionFilters) =>
            {
                var completion = new TaskCompletionSource<int>();
                var respond = adapter.CreateFunction("respond", 1, (_, responseArgs) =>
                {
                    completion.TrySetResult(responseArgs.Length > 0 ? (int)adapter.ToNumber(responseArgs[0]) : -1);
                    return adapter.Undefined;
                });
                realm.Run(() => adapter.Call(handler, adapter.Undefined, [adapter.FromString(filters), adapter.FromString(exclusionFilters), respond]));
                return completion.Task;
            };
            return adapter.Undefined;
        });

        // FakeUSBDevice.onclose of the realm that created the device: a session of the device ended (its window or worker
        // was destroyed, or the page closed it), as the connection error of the Mojo pipe of Chromium
        Define("__wpt_usb_set_close_handler", 1, (_, args) =>
        {
            var handler = args[0];
            var global = realm.GlobalScope;
            _closeHandlers[global] = id => global.QueueGlobalTask(() => realm.Run(() => adapter.Call(handler, adapter.Undefined, [adapter.FromNumber(id)])));
            return adapter.Undefined;
        });

        realm.EvaluateBuiltIn(script);
    }

    // The filters of USBDeviceRequestEvent, with their present members only (convertMojoDeviceFilter of the polyfill of Chromium)
    private static string SerializeFilters(IReadOnlyList<UsbDeviceFilter> filters)
    {
        var array = new JsonArray();
        foreach (var filter in filters)
        {
            var value = new JsonObject();
            if (filter.VendorId is { } vendorId)
            {
                value["vendorId"] = vendorId;
            }

            if (filter.ProductId is { } productId)
            {
                value["productId"] = productId;
            }

            if (filter.ClassCode is { } classCode)
            {
                value["classCode"] = classCode;
            }

            if (filter.SubclassCode is { } subclassCode)
            {
                value["subclassCode"] = subclassCode;
            }

            if (filter.ProtocolCode is { } protocolCode)
            {
                value["protocolCode"] = protocolCode;
            }

            if (filter.SerialNumber is { } serialNumber)
            {
                value["serialNumber"] = serialNumber;
            }

            array.Add(value);
        }

        return array.ToJsonString();
    }

    /// <summary>The descriptors of a FakeUSBDeviceInit (https://wicg.github.io/webusb/test/#dictdef-fakeusbdeviceinit).</summary>
    private static UsbDeviceDescriptor ParseDeviceInit(string json)
    {
        var init = JsonNode.Parse(json)!.AsObject();
        return new UsbDeviceDescriptor
        {
            UsbVersionMajor = GetByte(init, "usbVersionMajor"),
            UsbVersionMinor = GetByte(init, "usbVersionMinor"),
            UsbVersionSubminor = GetByte(init, "usbVersionSubminor"),
            DeviceClass = GetByte(init, "deviceClass"),
            DeviceSubclass = GetByte(init, "deviceSubclass"),
            DeviceProtocol = GetByte(init, "deviceProtocol"),
            VendorId = (ushort)GetNumber(init, "vendorId"),
            ProductId = (ushort)GetNumber(init, "productId"),
            DeviceVersionMajor = GetByte(init, "deviceVersionMajor"),
            DeviceVersionMinor = GetByte(init, "deviceVersionMinor"),
            DeviceVersionSubminor = GetByte(init, "deviceVersionSubminor"),
            ManufacturerName = GetString(init, "manufacturerName"),
            ProductName = GetString(init, "productName"),
            SerialNumber = GetString(init, "serialNumber"),
            ActiveConfigurationValue = GetByte(init, "activeConfigurationValue"),
            Configurations = [.. GetArray(init, "configurations").Select(configuration => new UsbConfigurationDescriptor
            {
                ConfigurationValue = GetByte(configuration, "configurationValue"),
                ConfigurationName = GetString(configuration, "configurationName"),
                Interfaces = [.. GetArray(configuration, "interfaces").Select(usbInterface => new UsbInterfaceDescriptor
                {
                    InterfaceNumber = GetByte(usbInterface, "interfaceNumber"),
                    Alternates = [.. GetArray(usbInterface, "alternates").Select(alternate => new UsbAlternateInterfaceDescriptor
                    {
                        AlternateSetting = GetByte(alternate, "alternateSetting"),
                        InterfaceClass = GetByte(alternate, "interfaceClass"),
                        InterfaceSubclass = GetByte(alternate, "interfaceSubclass"),
                        InterfaceProtocol = GetByte(alternate, "interfaceProtocol"),
                        InterfaceName = GetString(alternate, "interfaceName"),
                        Endpoints = [.. GetArray(alternate, "endpoints").Select(endpoint => new UsbEndpointDescriptor
                        {
                            EndpointNumber = GetByte(endpoint, "endpointNumber"),
                            Direction = GetString(endpoint, "direction") == "in" ? UsbDirection.In : UsbDirection.Out,
                            Type = GetString(endpoint, "type") switch
                            {
                                "interrupt" => UsbEndpointType.Interrupt,
                                "isochronous" => UsbEndpointType.Isochronous,
                                _ => UsbEndpointType.Bulk,
                            },
                            PacketSize = (uint)GetNumber(endpoint, "packetSize"),
                        })],
                    })],
                })],
            })],
        };

        static double GetNumber(JsonObject value, string name) => value[name] is JsonValue number && number.TryGetValue<double>(out var result) ? result : 0;

        static byte GetByte(JsonObject value, string name) => (byte)GetNumber(value, name);

        static string? GetString(JsonObject value, string name) => value[name] is JsonValue text && text.TryGetValue<string>(out var result) ? result : null;

        static IEnumerable<JsonObject> GetArray(JsonObject value, string name) => value[name] is JsonArray array ? array.OfType<JsonObject>() : [];
    }
}

/// <summary>
/// A FakeUSBDevice of the WebUSB Testing API: the operations to fail or to stall, and the statuses of the next transfers,
/// shared by the sessions of the device (as the FakeDevice of the polyfill of Chromium).
/// </summary>
internal sealed class WptFakeUsbDevice(UsbDeviceDescriptor descriptor, Action? onClose) : UsbHostDevice(descriptor)
{
    /// <summary>A session of the device ended: FakeUSBDevice.onclose is called.</summary>
    public void OnSessionClosed() => onClose?.Invoke();

    private readonly Lock _lock = new();
    private readonly HashSet<string> _operationsToFail = new(StringComparer.Ordinal);
    private readonly HashSet<string> _operationsToStall = new(StringComparer.Ordinal);
    private readonly Queue<string> _transferStatuses = new();

    public void FailNextOperation(string operation)
    {
        lock (_lock)
        {
            _operationsToFail.Add(operation);
        }
    }

    public void StallNextOperation(string operation)
    {
        lock (_lock)
        {
            _operationsToStall.Add(operation);
        }
    }

    public void SetNextTransferStatus(string status)
    {
        if (status is not ("transfer-error" or "permission-denied" or "timeout" or "cancelled" or "disconnect" or "stall" or "babble"))
            throw new ArgumentException("unknown transfer status", nameof(status));

        lock (_lock)
        {
            _transferStatuses.Enqueue(status);
        }
    }

    public override Task<UsbDeviceSession> OpenAsync(CancellationToken cancellationToken)
    {
        if (TakeStall("open"))
            return Stall<UsbDeviceSession>(cancellationToken);

        // ACCESS_DENIED, which Chromium reports as a SecurityError
        if (TakeFailure("open"))
            throw new UsbException(UsbError.AccessDenied, "Access denied.");

        return Task.FromResult<UsbDeviceSession>(new WptFakeUsbSession(this));
    }

    public bool TakeFailure(string operation)
    {
        lock (_lock)
        {
            return _operationsToFail.Remove(operation);
        }
    }

    public bool TakeStall(string operation)
    {
        lock (_lock)
        {
            return _operationsToStall.Remove(operation);
        }
    }

    /// <summary>The status of the next transfer: throws the error of a failed transfer, else returns ok, stall or babble.</summary>
    public UsbTransferStatus TakeTransferStatus()
    {
        string? status;
        lock (_lock)
        {
            _transferStatuses.TryDequeue(out status);
        }

        return status switch
        {
            null => UsbTransferStatus.Ok,
            "stall" => UsbTransferStatus.Stall,
            "babble" => UsbTransferStatus.Babble,
            "permission-denied" => throw new UsbException(UsbError.AccessDenied, "The transfer was not allowed."),
            "timeout" => throw new UsbException(UsbError.Timeout, "The transfer timed out."),
            "cancelled" => throw new UsbException(UsbError.Cancelled, "The transfer was cancelled."),
            "disconnect" => throw new UsbException(UsbError.Disconnected, "The device was disconnected."),
            _ => throw new UsbException(UsbError.Failed, "A transfer error has occurred."),
        };
    }

    /// <summary>An operation that never completes, until it is aborted.</summary>
    public static Task<T> Stall<T>(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        return completion.Task;
    }
}

/// <summary>A session of a <see cref="WptFakeUsbDevice"/>: the FakeDevice of the polyfill of Chromium (the data of the transfers depend on their parameters).</summary>
internal sealed class WptFakeUsbSession(WptFakeUsbDevice device) : UsbDeviceSession
{
    private byte? _configurationValue = device.ActiveConfigurationValue is 0 ? null : device.ActiveConfigurationValue;

    public override Task CloseAsync()
    {
        device.OnSessionClosed();
        return Task.CompletedTask;
    }

    public override Task SelectConfigurationAsync(byte configurationValue, CancellationToken cancellationToken)
    {
        _configurationValue = configurationValue;
        return Run("selectConfiguration");
    }

    public override Task ClaimInterfaceAsync(byte interfaceNumber, CancellationToken cancellationToken) => Run("claimInterface");

    public override Task ReleaseInterfaceAsync(byte interfaceNumber, CancellationToken cancellationToken) => Run("releaseInterface");

    public override Task SelectAlternateInterfaceAsync(byte interfaceNumber, byte alternateSetting, CancellationToken cancellationToken) => Run("selectAlternateInterface");

    public override Task ResetAsync(CancellationToken cancellationToken) => Run("reset");

    public override Task ClearHaltAsync(UsbDirection direction, byte endpointNumber, CancellationToken cancellationToken) => Run("clearHalt");

    // The data is the length, the request, the value and the index of the setup packet, in big endian
    public override Task<UsbInTransfer> ControlTransferInAsync(UsbControlTransferSetup setup, ushort length, CancellationToken cancellationToken)
    {
        if (setup.Recipient is UsbRecipient.Interface or UsbRecipient.Endpoint && _configurationValue is null)
            throw new UsbException(UsbError.AccessDenied, "The transfer was not allowed.");

        var status = device.TakeTransferStatus();
        byte[] data = [(byte)(length >> 8), (byte)length, setup.Request, (byte)(setup.Value >> 8), (byte)setup.Value, (byte)(setup.Index >> 8), (byte)setup.Index];
        return Task.FromResult(new UsbInTransfer(status, data));
    }

    public override Task<UsbOutTransfer> ControlTransferOutAsync(UsbControlTransferSetup setup, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        if (setup.Recipient is UsbRecipient.Interface or UsbRecipient.Endpoint && _configurationValue is null)
            throw new UsbException(UsbError.AccessDenied, "The transfer was not allowed.");

        return Task.FromResult(new UsbOutTransfer(device.TakeTransferStatus(), (uint)data.Length));
    }

    // The data is the sequence of the byte values 0, 1, 2...
    public override Task<UsbInTransfer> TransferInAsync(byte endpointNumber, uint length, CancellationToken cancellationToken)
    {
        var status = device.TakeTransferStatus();
        var data = new byte[length];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)i;
        }

        return Task.FromResult(new UsbInTransfer(status, data));
    }

    public override Task<UsbOutTransfer> TransferOutAsync(byte endpointNumber, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        => Task.FromResult(new UsbOutTransfer(device.TakeTransferStatus(), (uint)data.Length));

    public override Task<IReadOnlyList<UsbInTransfer>> IsochronousTransferInAsync(byte endpointNumber, IReadOnlyList<uint> packetLengths, CancellationToken cancellationToken)
    {
        var packets = new UsbInTransfer[packetLengths.Count];
        for (var i = 0; i < packets.Length; i++)
        {
            var data = new byte[packetLengths[i]];
            for (var j = 0; j < data.Length; j++)
            {
                data[j] = (byte)j;
            }

            packets[i] = new UsbInTransfer(device.TakeTransferStatus(), data);
        }

        return Task.FromResult<IReadOnlyList<UsbInTransfer>>(packets);
    }

    public override Task<IReadOnlyList<UsbOutTransfer>> IsochronousTransferOutAsync(byte endpointNumber, ReadOnlyMemory<byte> data, IReadOnlyList<uint> packetLengths, CancellationToken cancellationToken)
    {
        var packets = new UsbOutTransfer[packetLengths.Count];
        for (var i = 0; i < packets.Length; i++)
        {
            packets[i] = new UsbOutTransfer(device.TakeTransferStatus(), packetLengths[i]);
        }

        return Task.FromResult<IReadOnlyList<UsbOutTransfer>>(packets);
    }

    private Task Run(string operation) => device.TakeFailure(operation) ? Task.FromException(new UsbException(UsbError.Failed, $"The {operation} operation failed.")) : Task.CompletedTask;
}
