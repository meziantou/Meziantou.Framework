using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Fuzz;

// Web Bluetooth (https://bluetooth.spec.whatwg.org/): the resolution of the names and the aliases of the UUIDs, and the
// canonicalization and the matching of the filters of requestDevice()
public static partial class WebPlatformFuzzTargets
{
    private static string[] BluetoothUuidTokens =>
    [
        "-0000-1000-8000-00805f9b34fb", "0000180d", "deadbeef", "0x", "-", ".", "_",
        "heart_rate", "alert_notification", "generic_access", "battery_service", "human_interface_device", "health_thermometer",
        "heart_rate_measurement", "aerobic_heart_rate_lower_limit", "gap.device_name", "gap.peripheral_privacy_flag", "battery_level",
        "gatt.client_characteristic_configuration", "gatt.characteristic_extended_properties", "gatt.characteristic_user_description",
    ];

    private static string[] BluetoothRequestDeviceTokens =>
    [
        "{", "}", "[", "]", ":", ",", "\"", "true", "false", "null", "0", "1", "255", "65535", "6157",
        "\"devices\"", "\"options\"", "\"name\"", "\"services\"", "\"manufacturerData\"", "\"serviceData\"",
        "\"filters\"", "\"exclusionFilters\"", "\"optionalServices\"", "\"optionalManufacturerData\"", "\"acceptAllDevices\"",
        "\"namePrefix\"", "\"companyIdentifier\"", "\"dataPrefix\"", "\"mask\"", "\"service\"",
        "\"heart_rate\"", "\"health_thermometer\"", "\"generic_access\"", "\"human_interface_device\"", "\"Health Thermometer\"", "\"Heart\"",
    ];

    /// <summary>
    /// Resolves a name with <c>BluetoothUUID.getService()</c>, <c>getCharacteristic()</c> and <c>getDescriptor()</c>, and
    /// checks the properties of the results: the resolution fails only with a <c>TypeError</c>, a result is a valid UUID that
    /// resolves to itself, a valid UUID resolves to itself, and an invalid name fails for the three kinds of attributes.
    /// </summary>
    public static void BluetoothUuidTarget(string name)
    {
        var isValidUuid = BluetoothUuid.IsValidUuid(name);
        var resolved = 0;
        foreach (var resolve in new Func<BluetoothUuidName, string>[] { BluetoothUuid.GetService, BluetoothUuid.GetCharacteristic, BluetoothUuid.GetDescriptor })
        {
            string uuid;
            try
            {
                uuid = resolve(name);
            }
            catch (TypeErrorException)
            {
                FuzzAssert.False(isValidUuid, $"The valid UUID {FuzzAssert.Format(name)} was rejected");
                continue;
            }

            resolved++;
            FuzzAssert.True(BluetoothUuid.IsValidUuid(uuid), $"{FuzzAssert.Format(name)} resolved to the invalid UUID {FuzzAssert.Format(uuid)}");
            FuzzAssert.Equal(uuid, resolve(uuid), "A UUID does not resolve to itself");
            if (isValidUuid)
            {
                FuzzAssert.Equal(name, uuid, "A valid UUID does not resolve to itself");
            }
        }

        if (isValidUuid)
        {
            FuzzAssert.Equal(3, resolved, "A valid UUID is not a UUID of every kind of attribute");
        }

        // An alias is the top 32 bits of the Bluetooth base UUID
        if (uint.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var alias))
        {
            var canonical = BluetoothUuid.CanonicalUuid(alias);
            FuzzAssert.Equal(canonical, BluetoothUuid.GetService(alias), "getService() of an alias is not its canonical UUID");
            FuzzAssert.Equal(alias.ToString("x8", CultureInfo.InvariantCulture), canonical[..8], "The canonical UUID does not start with the alias");
        }
    }

    /// <summary>
    /// Calls <c>requestDevice()</c> with the options of the input (a JSON object with the devices in range and the options),
    /// with a chooser that picks the first device it is offered, and checks the properties of the result: it fails only
    /// with a <c>TypeError</c>, a <c>SecurityError</c> or a <c>NotFoundError</c>, the offered devices are devices in range,
    /// all of them when <c>acceptAllDevices</c> is true, a chosen device matches the names of a filter, and the origin is
    /// allowed to use valid UUIDs that are not blocklisted.
    /// </summary>
    public static void BluetoothRequestDeviceTarget(ReadOnlySpan<byte> data)
    {
        // A JSON node decodes its strings when they are read: invalid UTF-8 would throw then, in the description of the devices
        if (!System.Text.Unicode.Utf8.IsValid(data))
            return;

        JsonObject? json;
        try
        {
            json = JsonNode.Parse(data, documentOptions: new JsonDocumentOptions { MaxDepth = 64 }) as JsonObject;
        }
        catch (JsonException)
        {
            return;
        }

        if (json is null)
            return;

        var provider = new FuzzBluetoothChooser();
        var devices = new List<BluetoothHostDevice>();
        if (json["devices"] is JsonArray deviceArray)
        {
            foreach (var deviceJson in deviceArray.OfType<JsonObject>())
            {
                var device = CreateFuzzBluetoothDevice(deviceJson, devices.Count);
                devices.Add(device);
                provider.AddDevice(device);
            }
        }

        var options = json["options"] is JsonObject optionsJson ? ToRequestDeviceOptions(optionsJson) : new RequestDeviceOptions();
        using var context = new BrowsingContext(new BrowsingContextOptions { Bluetooth = new BluetoothOptions { Provider = provider } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html>");
        context.EventLoop.RunUntilIdle();
        Window.NotifyActivation(document);
        var request = document.DefaultView!.Navigator.Bluetooth.RequestDevice(options);
        context.EventLoop.RunUntilIdle();
        FuzzAssert.True(request.IsCompleted, "requestDevice() did not complete");

        if (provider.Request is { } offered)
        {
            FuzzAssert.True(offered.Devices.All(devices.Contains), "requestDevice() offered a device that is not in range");
            if (options.AcceptAllDevices)
            {
                FuzzAssert.Equal(devices.Count, offered.Devices.Count, "acceptAllDevices did not offer all the devices");
            }
        }

        if (request.IsFaulted)
        {
            var exception = request.Exception!.InnerException;
            FuzzAssert.True(exception is TypeErrorException or DomException { Name: "SecurityError" or "NotFoundError" }, $"requestDevice() failed with {FuzzAssert.Format(exception)}");
            return;
        }

        var chosen = request.Result.HostDevice;
        FuzzAssert.True(provider.Request is not null && provider.Request.Devices.Count > 0 && provider.Request.Devices[0] == chosen, "requestDevice() returned a device that was not chosen");
        if (!options.AcceptAllDevices)
        {
            FuzzAssert.True(
                options.Filters!.Any(filter => (filter.Name is null || filter.Name == chosen.Name) && (filter.NamePrefix is null || (chosen.Name?.StartsWith(filter.NamePrefix, StringComparison.Ordinal) ?? false))),
                "The chosen device does not match the names of any filter");
        }

        var allowed = provider.GetAllowedDevices("https://example.com").Single();
        FuzzAssert.True(allowed.Device == chosen, "The origin is not allowed to use the chosen device");
        foreach (var uuid in allowed.AllowedServices ?? Enumerable.Empty<string>())
        {
            FuzzAssert.True(BluetoothUuid.IsValidUuid(uuid), $"The origin is allowed to use the invalid UUID {FuzzAssert.Format(uuid)}");
            FuzzAssert.True(BluetoothUuid.GetService(uuid) is { } service && !IsBlocklistedService(service), $"The origin is allowed to use the blocklisted service {FuzzAssert.Format(uuid)}");
        }
    }

    // The services of the GATT blocklist (https://github.com/WebBluetoothCG/registries/blob/master/gatt_blocklist.txt)
    private static bool IsBlocklistedService(string uuid) => uuid is "00001812-0000-1000-8000-00805f9b34fb" or "00001530-1212-efde-1523-785feabcd123" or "f000ffc0-0451-4000-b000-000000000000"
        or "00060000-0000-1000-8000-00805f9b34fb" or "0000fffd-0000-1000-8000-00805f9b34fb" or "0000fff9-0000-1000-8000-00805f9b34fb" or "0000fde2-0000-1000-8000-00805f9b34fb"
        or "00002a03-0000-1000-8000-00805f9b34fb" or "00002a25-0000-1000-8000-00805f9b34fb";

    private static BluetoothHostDevice CreateFuzzBluetoothDevice(JsonObject json, int index)
    {
        var services = new List<string>();
        foreach (var serviceName in ToUuidNames(json["services"]))
        {
            try
            {
                services.Add(BluetoothUuid.GetService(serviceName));
            }
            catch (TypeErrorException)
            {
            }
        }

        var manufacturerData = new Dictionary<ushort, ReadOnlyMemory<byte>>();
        if (json["manufacturerData"] is JsonObject manufacturerJson)
        {
            foreach (var (key, value) in manufacturerJson)
            {
                if (ushort.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var companyIdentifier) && ToBytes(value) is { } bytes)
                {
                    manufacturerData[companyIdentifier] = bytes;
                }
            }
        }

        var serviceData = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        if (json["serviceData"] is JsonObject serviceJson)
        {
            foreach (var (key, value) in serviceJson)
            {
                if (BluetoothUuid.IsValidUuid(key) && ToBytes(value) is { } bytes)
                {
                    serviceData[key] = bytes;
                }
            }
        }

        var name = json["name"] is JsonValue nameValue && nameValue.TryGetValue<string>(out var text) ? text : null;
        var device = new BluetoothHostDevice("00:00:00:00:00:" + index.ToString("x2", CultureInfo.InvariantCulture), name)
        {
            ServiceUuids = services,
            ManufacturerData = manufacturerData,
            ServiceData = serviceData,
        };
        foreach (var service in services.Distinct(StringComparer.Ordinal))
        {
            device.AddService(new BluetoothHostService(service));
        }

        return device;
    }

    private static RequestDeviceOptions ToRequestDeviceOptions(JsonObject json) => new()
    {
        Filters = json["filters"] is JsonArray filters ? [.. filters.OfType<JsonObject>().Select(ToScanFilter)] : null,
        ExclusionFilters = json["exclusionFilters"] is JsonArray exclusionFilters ? [.. exclusionFilters.OfType<JsonObject>().Select(ToScanFilter)] : null,
        OptionalServices = [.. ToUuidNames(json["optionalServices"])],
        OptionalManufacturerData = json["optionalManufacturerData"] is JsonArray codes ? [.. codes.Select(ToCompanyIdentifier).OfType<ushort>()] : [],
        AcceptAllDevices = json["acceptAllDevices"] is JsonValue value && value.TryGetValue<bool>(out var accept) && accept,
    };

    private static BluetoothLEScanFilterInit ToScanFilter(JsonObject json) => new()
    {
        Services = json["services"] is JsonArray ? [.. ToUuidNames(json["services"])] : null,
        Name = json["name"] is JsonValue name && name.TryGetValue<string>(out var text) ? text : null,
        NamePrefix = json["namePrefix"] is JsonValue prefix && prefix.TryGetValue<string>(out var prefixText) ? prefixText : null,
        ManufacturerData = json["manufacturerData"] is JsonArray manufacturerData
            ? [.. manufacturerData.OfType<JsonObject>().Where(data => ToCompanyIdentifier(data["companyIdentifier"]) is not null).Select(data => new BluetoothManufacturerDataFilterInit
            {
                CompanyIdentifier = ToCompanyIdentifier(data["companyIdentifier"])!.Value,
                DataPrefix = ToBytes(data["dataPrefix"]),
                Mask = ToBytes(data["mask"]),
            })]
            : null,
        ServiceData = json["serviceData"] is JsonArray serviceData
            ? [.. serviceData.OfType<JsonObject>().Where(data => ToUuidNames(data["service"]).Any()).Select(data => new BluetoothServiceDataFilterInit
            {
                Service = ToUuidNames(data["service"]).First(),
                DataPrefix = ToBytes(data["dataPrefix"]),
                Mask = ToBytes(data["mask"]),
            })]
            : null,
    };

    // A string, or a number converted to an unsigned long by the bindings (ToUint32)
    private static IEnumerable<BluetoothUuidName> ToUuidNames(JsonNode? node)
    {
        IEnumerable<JsonNode?> items = node is JsonArray array ? array : new[] { node };
        foreach (var item in items)
        {
            if (item is not JsonValue value)
                continue;

            if (value.TryGetValue<string>(out var text))
            {
                yield return text;
            }
            else if (value.TryGetValue<double>(out var number) && double.IsFinite(number))
            {
                yield return (uint)(long)(Math.Truncate(number) % 4294967296d);
            }
        }
    }

    private static ushort? ToCompanyIdentifier(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<double>(out var number) && number is >= 0 and <= ushort.MaxValue ? (ushort)number : null;

    private static byte[]? ToBytes(JsonNode? node)
        => node is JsonArray array ? [.. array.Select(item => item is JsonValue value && value.TryGetValue<double>(out var number) ? (byte)number : (byte)0)] : null;

    private static IEnumerable<FuzzSeed> GetBluetoothUuidSeeds()
    {
        string[] seeds =
        [
            "heart_rate", "alert_notification", "aerobic_heart_rate_lower_limit", "gatt.characteristic_extended_properties",
            "00001811-0000-1000-8000-00805f9b34fb", "1a2b3c4d-5e6f-7a8b-9c0d-1e2f3a4b5c6d", "1A2B3C4D-5E6F-7A8B-9C0D-1E2F3A4B5C6D",
            "deadbeef", "0000000g-0000-1000-8000-00805f9b34fb", "wrong_name", "undefined", "[object Object]", "", "6157", "4294967295",
        ];

        for (var i = 0; i < seeds.Length; i++)
        {
            yield return FuzzSeed.FromText("bluetooth-uuid-" + i.ToString(CultureInfo.InvariantCulture), seeds[i]);
        }
    }

    private static IEnumerable<FuzzSeed> GetBluetoothRequestDeviceSeeds()
    {
        const string Devices = """
            "devices":[
              {"name":"Health Thermometer","services":["generic_access","health_thermometer"],"manufacturerData":{"1":[1,2],"2":[3,4]}},
              {"name":"Heart Rate","services":["generic_access","heart_rate"]},
              {"name":"","services":[],"serviceData":{"00001234-0000-1000-8000-00805f9b34fb":[5,6]}}]
            """;
        string[] options =
        [
            """{"filters":[{"services":["health_thermometer"]}]}""",
            """{"filters":[{"services":[6153]}],"optionalServices":["generic_access"]}""",
            """{"filters":[{"name":"Health Thermometer","namePrefix":"Health","manufacturerData":[{"companyIdentifier":1}]}],"optionalServices":["health_thermometer"]}""",
            """{"filters":[{"namePrefix":"Heart"}],"exclusionFilters":[{"name":"Heart Rate"}]}""",
            """{"filters":[{"name":""}]}""",
            """{"acceptAllDevices":true,"optionalServices":["heart_rate","human_interface_device"]}""",
            """{"filters":[{"services":["human_interface_device"]}]}""",
            """{"filters":[{"manufacturerData":[{"companyIdentifier":76,"dataPrefix":[2,21,179]}]}]}""",
            """{"filters":[{"manufacturerData":[{"companyIdentifier":1,"dataPrefix":[1,2,3,4],"mask":[255]}]}]}""",
            """{"filters":[{"manufacturerData":[{"companyIdentifier":1,"dataPrefix":[]}]}]}""",
            """{"filters":[{"manufacturerData":[{"companyIdentifier":1},{"companyIdentifier":1}]}]}""",
            """{"filters":[{"serviceData":[{"service":4660,"dataPrefix":[5],"mask":[255]}]}]}""",
            """{"filters":[{"services":["wrong_service"]}]}""",
            """{"filters":[{"services":["heart_rate"]}],"optionalServices":["wrong_service"]}""",
            """{"filters":[{}]}""",
            """{"filters":[]}""",
            """{"exclusionFilters":[{"name":"a"}],"acceptAllDevices":true}""",
            """{"filters":[{"name":"a"}],"acceptAllDevices":true}""",
            """{"filters":[{"namePrefix":""}]}""",
            """{"filters":[{"name":"❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤❤"}]}""",
            """{}""",
        ];

        for (var i = 0; i < options.Length; i++)
        {
            yield return FuzzSeed.FromText("bluetooth-request-device-" + i.ToString(CultureInfo.InvariantCulture), "{" + Devices + ",\"options\":" + options[i] + "}");
        }
    }

    /// <summary>The device chooser of the requestDevice() target: the user picks the first device it is offered.</summary>
    private sealed class FuzzBluetoothChooser : BluetoothProvider
    {
        public BluetoothDeviceRequest? Request { get; private set; }

        public override Task<BluetoothHostDevice?> RequestDevice(BluetoothDeviceRequest request)
        {
            Request = request;
            return Task.FromResult(request.Devices.Count > 0 ? request.Devices[0] : null);
        }
    }
}
