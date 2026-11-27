using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The conversions of the names and the aliases of the Bluetooth UUIDs (https://bluetooth.spec.whatwg.org/#bluetoothuuid):
/// a 16- or 32-bit alias, a valid UUID (lowercase), or a name of the GATT assigned numbers (e.g. <c>heart_rate</c>).
/// </summary>
/// <seealso href="https://bluetooth.spec.whatwg.org/#bluetoothuuid"/>
[IdlInterface("BluetoothUUID", "https://bluetooth.spec.whatwg.org/#bluetoothuuid")]
public static class BluetoothUuid
{
    private const string BaseUuidSuffix = "-0000-1000-8000-00805f9b34fb";

    /// <summary>Returns the UUID of a service (https://bluetooth.spec.whatwg.org/#dom-bluetoothuuid-getservice).</summary>
    /// <param name="name">A 16- or 32-bit alias (a number), a valid UUID, or the name of a GATT assigned service (e.g. <c>heart_rate</c>).</param>
    /// <exception cref="TypeErrorException">The name is not a valid UUID, nor the name of a service.</exception>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothuuid-getservice"/>
    [IdlMember("getService", "https://bluetooth.spec.whatwg.org/#dom-bluetoothuuid-getservice")]
    public static string GetService(BluetoothUuidName name) => Resolve(name, BluetoothUuidKind.Service);

    /// <summary>Returns the UUID of a characteristic (https://bluetooth.spec.whatwg.org/#dom-bluetoothuuid-getcharacteristic).</summary>
    /// <param name="name">A 16- or 32-bit alias (a number), a valid UUID, or the name of a GATT assigned characteristic (e.g. <c>battery_level</c>).</param>
    /// <exception cref="TypeErrorException">The name is not a valid UUID, nor the name of a characteristic.</exception>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothuuid-getcharacteristic"/>
    [IdlMember("getCharacteristic", "https://bluetooth.spec.whatwg.org/#dom-bluetoothuuid-getcharacteristic")]
    public static string GetCharacteristic(BluetoothUuidName name) => Resolve(name, BluetoothUuidKind.Characteristic);

    /// <summary>Returns the UUID of a descriptor (https://bluetooth.spec.whatwg.org/#dom-bluetoothuuid-getdescriptor).</summary>
    /// <param name="name">A 16- or 32-bit alias (a number), a valid UUID, or the name of a GATT assigned descriptor (e.g. <c>gatt.client_characteristic_configuration</c>).</param>
    /// <exception cref="TypeErrorException">The name is not a valid UUID, nor the name of a descriptor.</exception>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothuuid-getdescriptor"/>
    [IdlMember("getDescriptor", "https://bluetooth.spec.whatwg.org/#dom-bluetoothuuid-getdescriptor")]
    public static string GetDescriptor(BluetoothUuidName name) => Resolve(name, BluetoothUuidKind.Descriptor);

    /// <summary>
    /// Returns the 128-bit UUID of a 16- or 32-bit alias (https://bluetooth.spec.whatwg.org/#dom-bluetoothuuid-canonicaluuid):
    /// the alias replaces the top 32 bits of the Bluetooth base UUID (e.g. <c>0x180d</c> is <c>0000180d-0000-1000-8000-00805f9b34fb</c>).
    /// </summary>
    /// <param name="alias">The alias.</param>
    /// <seealso href="https://bluetooth.spec.whatwg.org/#dom-bluetoothuuid-canonicaluuid"/>
    [IdlMember("canonicalUUID", "https://bluetooth.spec.whatwg.org/#dom-bluetoothuuid-canonicaluuid")]
    public static string CanonicalUuid(uint alias) => alias.ToString("x8", CultureInfo.InvariantCulture) + BaseUuidSuffix;

    /// <summary>Returns whether a string is a valid UUID (https://bluetooth.spec.whatwg.org/#valid-uuid): 128 bits, lowercase, with the dashes.</summary>
    /// <param name="value">The string.</param>
    [NotInIdl("The validation of the UUIDs of the hosts")]
    public static bool IsValidUuid(string? value)
    {
        if (value is not { Length: 36 })
            return false;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (i is 8 or 13 or 18 or 23)
            {
                if (c != '-')
                    return false;
            }
            else if (!char.IsAsciiDigit(c) && c is not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>ResolveUUIDName (https://bluetooth.spec.whatwg.org/#resolveuuidname), or null for an invalid name.</summary>
    internal static string? TryResolve(BluetoothUuidName name, BluetoothUuidKind kind)
    {
        switch (name.Value)
        {
            case string text:
                if (IsValidUuid(text))
                    return text;

                return IsValidName(text) && GetAssignedNumbers(kind).TryGetValue(text, out var alias) ? CanonicalUuid(alias) : null;

            default:
                // A number: the bindings converted it to an unsigned long
                return CanonicalUuid(ToAlias(name.Value));
        }
    }

    /// <summary>The message of the TypeError of an invalid name, as in Chromium (the prefix names the operation of the page).</summary>
    internal static string GetInvalidNameMessage(BluetoothUuidName name, BluetoothUuidKind kind)
    {
        var (attribute, url, example) = kind switch
        {
            BluetoothUuidKind.Service => ("Service", "https://www.bluetooth.com/specifications/gatt/services", "alert_notification"),
            BluetoothUuidKind.Characteristic => ("Characteristic", "https://www.bluetooth.com/specifications/gatt/characteristics", "aerobic_heart_rate_lower_limit"),
            _ => ("Descriptor", "https://www.bluetooth.com/specifications/gatt/descriptors", "gatt.characteristic_presentation_format"),
        };
        return $"Invalid {attribute} name: '{name.Value}'. It must be a valid UUID alias (e.g. 0x1234), UUID (lowercase hex characters e.g. '00001234-0000-1000-8000-00805f9b34fb'), or recognized standard name from {url} e.g. '{example}'.";
    }

    private static string Resolve(BluetoothUuidName name, BluetoothUuidKind kind)
        => TryResolve(name, kind) ?? throw new TypeErrorException(GetInvalidNameMessage(name, kind));

    // https://bluetooth.spec.whatwg.org/#valid-name: /^[a-z0-9_-.]+$/
    private static bool IsValidName(string value)
    {
        if (value.Length == 0)
            return false;

        foreach (var c in value)
        {
            if (!char.IsAsciiDigit(c) && c is not ((>= 'a' and <= 'z') or '_' or '-' or '.'))
                return false;
        }

        return true;
    }

    private static Dictionary<string, ushort> GetAssignedNumbers(BluetoothUuidKind kind) => kind switch
    {
        BluetoothUuidKind.Service => BluetoothGattAssignedNumbers.Services,
        BluetoothUuidKind.Characteristic => BluetoothGattAssignedNumbers.Characteristics,
        _ => BluetoothGattAssignedNumbers.Descriptors,
    };

    // The ToUint32 conversion of the unsigned long of the IDL union (an integer from .NET code is taken modulo 2^32)
    private static uint ToAlias(object value) => value switch
    {
        uint number => number,
        int number => unchecked((uint)number),
        ushort number => number,
        long number => unchecked((uint)number),
        ulong number => unchecked((uint)number),
        double number => ToUint32(number),
        float number => ToUint32(number),
        IConvertible convertible => ToUint32(convertible.ToDouble(CultureInfo.InvariantCulture)),
        _ => throw new TypeErrorException("A Bluetooth UUID must be a string or a number"),
    };

    // https://tc39.es/ecma262/#sec-touint32
    private static uint ToUint32(double value)
    {
        if (!double.IsFinite(value))
            return 0;

        var integer = Math.Truncate(value);
        var modulo = integer % 4294967296d;
        if (modulo < 0)
        {
            modulo += 4294967296d;
        }

        return (uint)modulo;
    }
}

/// <summary>
/// The <c>(DOMString or unsigned long)</c> union of the Bluetooth UUIDs (https://bluetooth.spec.whatwg.org/#typedefdef-bluetoothserviceuuid,
/// <c>BluetoothServiceUUID</c>, <c>BluetoothCharacteristicUUID</c> and <c>BluetoothDescriptorUUID</c>): a valid UUID, the
/// name of a GATT assigned number (e.g. <c>heart_rate</c>), or a 16- or 32-bit alias (e.g. <c>0x180d</c>).
/// </summary>
/// <seealso href="https://bluetooth.spec.whatwg.org/#typedefdef-bluetoothserviceuuid"/>
[IdlInterface("(DOMString or unsigned long)", "https://bluetooth.spec.whatwg.org/#typedefdef-bluetoothserviceuuid")]
public readonly struct BluetoothUuidName : IUnionValue, IEquatable<BluetoothUuidName>
{
    // Null for the default value of the struct: the alias 0
    private readonly object? _value;

    private BluetoothUuidName(object value) => _value = value;

    /// <summary>Gets the value: a <see cref="string"/> or a <see cref="uint"/>.</summary>
    [NotInIdl]
    public object Value => _value ?? 0u;

    /// <summary>Gets the UUID or the name when the value is a string, otherwise null.</summary>
    [NotInIdl]
    public string? Name => Value as string;

    /// <summary>Gets the alias when the value is a number, otherwise null.</summary>
    [NotInIdl]
    public uint? Alias => Value as uint?;

    /// <summary>Creates a union value from a UUID or a name.</summary>
    [NotInIdl]
    public static implicit operator BluetoothUuidName(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates a union value from an alias.</summary>
    [NotInIdl]
    public static implicit operator BluetoothUuidName(uint value) => new(value);

    /// <summary>Creates a union value from a UUID or a name.</summary>
    [NotInIdl]
    public static BluetoothUuidName FromString(string value) => value;

    /// <summary>Creates a union value from an alias.</summary>
    [NotInIdl]
    public static BluetoothUuidName FromUInt32(uint value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(BluetoothUuidName other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is BluetoothUuidName other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc/>
    [NotInIdl]
    public override string ToString() => Convert.ToString(Value, CultureInfo.InvariantCulture) ?? "";

    /// <summary>Returns whether the two values are equal.</summary>
    [NotInIdl]
    public static bool operator ==(BluetoothUuidName left, BluetoothUuidName right) => left.Equals(right);

    /// <summary>Returns whether the two values are different.</summary>
    [NotInIdl]
    public static bool operator !=(BluetoothUuidName left, BluetoothUuidName right) => !left.Equals(right);
}

/// <summary>The kinds of attributes of the GATT assigned numbers.</summary>
internal enum BluetoothUuidKind
{
    Service,
    Characteristic,
    Descriptor,
}

/// <summary>
/// The blocklists of Web Bluetooth (https://bluetooth.spec.whatwg.org/#the-blocklist): the GATT attributes and the
/// manufacturer data that the pages cannot use. Copied from gatt_blocklist.txt and manufacturer_data_blocklist.txt of
/// https://github.com/WebBluetoothCG/registries at commit 228b62c31c177c9b770b79896aec9ef660f62216.
/// </summary>
internal static class BluetoothBlocklist
{
    private static readonly Dictionary<string, BlocklistToken> GattBlocklist = new(StringComparer.Ordinal)
    {
        // Services: human_interface_device, the firmware update services of Nordic, TI and Cypress, and FIDO
        ["00001812-0000-1000-8000-00805f9b34fb"] = BlocklistToken.Exclude,
        ["00001530-1212-efde-1523-785feabcd123"] = BlocklistToken.Exclude,
        ["f000ffc0-0451-4000-b000-000000000000"] = BlocklistToken.Exclude,
        ["00060000-0000-1000-8000-00805f9b34fb"] = BlocklistToken.Exclude,
        ["0000fffd-0000-1000-8000-00805f9b34fb"] = BlocklistToken.Exclude,
        ["0000fff9-0000-1000-8000-00805f9b34fb"] = BlocklistToken.Exclude,
        ["0000fde2-0000-1000-8000-00805f9b34fb"] = BlocklistToken.Exclude,

        // Characteristics: gap.peripheral_privacy_flag, gap.reconnection_address and serial_number_string
        ["00002a02-0000-1000-8000-00805f9b34fb"] = BlocklistToken.ExcludeWrites,
        ["00002a03-0000-1000-8000-00805f9b34fb"] = BlocklistToken.Exclude,
        ["00002a25-0000-1000-8000-00805f9b34fb"] = BlocklistToken.Exclude,

        // Descriptors: gatt.client_characteristic_configuration and gatt.server_characteristic_configuration
        ["00002902-0000-1000-8000-00805f9b34fb"] = BlocklistToken.ExcludeWrites,
        ["00002903-0000-1000-8000-00805f9b34fb"] = BlocklistToken.ExcludeWrites,
    };

    // manufacturer 4c advdata-02/ff: the proximity UUID of the iBeacons might reveal the location of the user
    private static readonly (ushort CompanyIdentifier, byte[] DataPrefix, byte[] Mask)[] ManufacturerDataBlocklist =
    [
        (0x004c, [0x02], [0xff]),
    ];

    private enum BlocklistToken
    {
        Exclude,
        ExcludeReads,
        ExcludeWrites,
    }

    /// <summary>https://bluetooth.spec.whatwg.org/#blocklisted</summary>
    public static bool IsBlocklisted(string uuid) => GattBlocklist.TryGetValue(uuid, out var token) && token == BlocklistToken.Exclude;

    /// <summary>https://bluetooth.spec.whatwg.org/#blocklisted-for-reads</summary>
    public static bool IsBlocklistedForReads(string uuid) => GattBlocklist.TryGetValue(uuid, out var token) && token is BlocklistToken.Exclude or BlocklistToken.ExcludeReads;

    /// <summary>https://bluetooth.spec.whatwg.org/#blocklisted-for-writes</summary>
    public static bool IsBlocklistedForWrites(string uuid) => GattBlocklist.TryGetValue(uuid, out var token) && token is BlocklistToken.Exclude or BlocklistToken.ExcludeWrites;

    /// <summary>https://bluetooth.spec.whatwg.org/#blocklisted-manufacturer-data</summary>
    public static bool IsBlocklistedManufacturerData(ushort companyIdentifier, ReadOnlySpan<byte> data)
    {
        foreach (var (blockedCompany, prefix, mask) in ManufacturerDataBlocklist)
        {
            if (blockedCompany == companyIdentifier && BluetoothDataFilter.Matches(data, prefix, mask))
                return true;
        }

        return false;
    }

    /// <summary>https://bluetooth.spec.whatwg.org/#blocklisted-manufacturer-data-filter</summary>
    public static bool IsBlocklistedManufacturerDataFilter(ushort companyIdentifier, ReadOnlySpan<byte> dataPrefix, ReadOnlySpan<byte> mask)
    {
        foreach (var (blockedCompany, blockedPrefix, blockedMask) in ManufacturerDataBlocklist)
        {
            if (blockedCompany == companyIdentifier && BluetoothDataFilter.IsStrictSubset(dataPrefix, mask, blockedPrefix, blockedMask))
                return true;
        }

        return false;
    }
}
