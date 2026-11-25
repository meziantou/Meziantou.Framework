using System.ComponentModel;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The system of the units of a report item (https://hid.spec.whatwg.org/#dom-hidunitsystem).</summary>
/// <seealso href="https://hid.spec.whatwg.org/#dom-hidunitsystem"/>
[IdlInterface("HIDUnitSystem", "https://hid.spec.whatwg.org/#dom-hidunitsystem")]
public enum HidUnitSystem
{
    /// <summary>No unit.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidunitsystem-none"/>
    [IdlMember("none", "https://hid.spec.whatwg.org/#dom-hidunitsystem-none")]
    None,

    /// <summary>The SI linear units (centimeter, gram, second, kelvin, ampere, candela).</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidunitsystem-si-linear"/>
    [IdlMember("si-linear", "https://hid.spec.whatwg.org/#dom-hidunitsystem-si-linear")]
    SiLinear,

    /// <summary>The SI rotation units (radian, gram, second, kelvin, ampere, candela).</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidunitsystem-si-rotation"/>
    [IdlMember("si-rotation", "https://hid.spec.whatwg.org/#dom-hidunitsystem-si-rotation")]
    SiRotation,

    /// <summary>The English linear units (inch, slug, second, Fahrenheit, ampere, candela).</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidunitsystem-english-linear"/>
    [IdlMember("english-linear", "https://hid.spec.whatwg.org/#dom-hidunitsystem-english-linear")]
    EnglishLinear,

    /// <summary>The English rotation units (degree, slug, second, Fahrenheit, ampere, candela).</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidunitsystem-english-rotation"/>
    [IdlMember("english-rotation", "https://hid.spec.whatwg.org/#dom-hidunitsystem-english-rotation")]
    EnglishRotation,

    /// <summary>A vendor-defined unit system.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidunitsystem-vendor-defined"/>
    [IdlMember("vendor-defined", "https://hid.spec.whatwg.org/#dom-hidunitsystem-vendor-defined")]
    VendorDefined,

    /// <summary>A reserved unit system.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidunitsystem-reserved"/>
    [IdlMember("reserved", "https://hid.spec.whatwg.org/#dom-hidunitsystem-reserved")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1700:Do not name enum values 'Reserved'", Justification = "The reserved unit systems of the HID specification (the value of the IDL enum)")]
    Reserved,
}

/// <summary>A data field of a report (https://hid.spec.whatwg.org/#dom-hidreportitem), parsed from the report descriptor.</summary>
/// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem"/>
[IdlInterface("HIDReportItem", "https://hid.spec.whatwg.org/#dom-hidreportitem")]
public sealed class HidReportItem
{
    /// <summary>Gets whether the data are absolute (not relative to the previous report).</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-isabsolute"/>
    [IdlMember("isAbsolute", "https://hid.spec.whatwg.org/#dom-hidreportitem-isabsolute")]
    public bool? IsAbsolute { get; init; }

    /// <summary>Gets whether the field is an array (not a variable).</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-isarray"/>
    [IdlMember("isArray", "https://hid.spec.whatwg.org/#dom-hidreportitem-isarray")]
    public bool? IsArray { get; init; }

    /// <summary>Gets whether the field is a stream of bytes.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-isbufferedbytes"/>
    [IdlMember("isBufferedBytes", "https://hid.spec.whatwg.org/#dom-hidreportitem-isbufferedbytes")]
    public bool? IsBufferedBytes { get; init; }

    /// <summary>Gets whether the field is constant (padding).</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-isconstant"/>
    [IdlMember("isConstant", "https://hid.spec.whatwg.org/#dom-hidreportitem-isconstant")]
    public bool? IsConstant { get; init; }

    /// <summary>Gets whether the data are linear.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-islinear"/>
    [IdlMember("isLinear", "https://hid.spec.whatwg.org/#dom-hidreportitem-islinear")]
    public bool? IsLinear { get; init; }

    /// <summary>Gets whether the usages are a range (<see cref="UsageMinimum"/>, <see cref="UsageMaximum"/>).</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-isrange"/>
    [IdlMember("isRange", "https://hid.spec.whatwg.org/#dom-hidreportitem-isrange")]
    public bool? IsRange { get; init; }

    /// <summary>Gets whether the data may change without host interaction.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-isvolatile"/>
    [IdlMember("isVolatile", "https://hid.spec.whatwg.org/#dom-hidreportitem-isvolatile")]
    public bool? IsVolatile { get; init; }

    /// <summary>Gets whether the field has a null state.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-hasnull"/>
    [IdlMember("hasNull", "https://hid.spec.whatwg.org/#dom-hidreportitem-hasnull")]
    public bool? HasNull { get; init; }

    /// <summary>Gets whether the control returns to a preferred state when it is not used.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-haspreferredstate"/>
    [IdlMember("hasPreferredState", "https://hid.spec.whatwg.org/#dom-hidreportitem-haspreferredstate")]
    public bool? HasPreferredState { get; init; }

    /// <summary>Gets whether the data wrap around.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-wrap"/>
    [IdlMember("wrap", "https://hid.spec.whatwg.org/#dom-hidreportitem-wrap")]
    public bool? Wrap { get; init; }

    /// <summary>Gets the usages (the usage page in the high 16 bits, the usage ID in the low 16 bits).</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-usages"/>
    [IdlMember("usages", "https://hid.spec.whatwg.org/#dom-hidreportitem-usages")]
    public IReadOnlyList<uint>? Usages { get; init; }

    /// <summary>Gets the first usage of the range, or null.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-usageminimum"/>
    [IdlMember("usageMinimum", "https://hid.spec.whatwg.org/#dom-hidreportitem-usageminimum")]
    public uint? UsageMinimum { get; init; }

    /// <summary>Gets the last usage of the range, or null.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-usagemaximum"/>
    [IdlMember("usageMaximum", "https://hid.spec.whatwg.org/#dom-hidreportitem-usagemaximum")]
    public uint? UsageMaximum { get; init; }

    /// <summary>Gets the size of a field, in bits.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-reportsize"/>
    [IdlMember("reportSize", "https://hid.spec.whatwg.org/#dom-hidreportitem-reportsize")]
    public ushort? ReportSize { get; init; }

    /// <summary>Gets the number of fields.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-reportcount"/>
    [IdlMember("reportCount", "https://hid.spec.whatwg.org/#dom-hidreportitem-reportcount")]
    public ushort? ReportCount { get; init; }

    /// <summary>Gets the exponent of the unit (a power of 10).</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-unitexponent"/>
    [IdlMember("unitExponent", "https://hid.spec.whatwg.org/#dom-hidreportitem-unitexponent")]
    public sbyte? UnitExponent { get; init; }

    /// <summary>Gets the system of the unit.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-unitsystem"/>
    [IdlMember("unitSystem", "https://hid.spec.whatwg.org/#dom-hidreportitem-unitsystem")]
    public HidUnitSystem? UnitSystem { get; init; }

    /// <summary>Gets the exponent of the length factor of the unit.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-unitfactorlengthexponent"/>
    [IdlMember("unitFactorLengthExponent", "https://hid.spec.whatwg.org/#dom-hidreportitem-unitfactorlengthexponent")]
    public sbyte? UnitFactorLengthExponent { get; init; }

    /// <summary>Gets the exponent of the mass factor of the unit.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-unitfactormassexponent"/>
    [IdlMember("unitFactorMassExponent", "https://hid.spec.whatwg.org/#dom-hidreportitem-unitfactormassexponent")]
    public sbyte? UnitFactorMassExponent { get; init; }

    /// <summary>Gets the exponent of the time factor of the unit.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-unitfactortimeexponent"/>
    [IdlMember("unitFactorTimeExponent", "https://hid.spec.whatwg.org/#dom-hidreportitem-unitfactortimeexponent")]
    public sbyte? UnitFactorTimeExponent { get; init; }

    /// <summary>Gets the exponent of the temperature factor of the unit.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-unitfactortemperatureexponent"/>
    [IdlMember("unitFactorTemperatureExponent", "https://hid.spec.whatwg.org/#dom-hidreportitem-unitfactortemperatureexponent")]
    public sbyte? UnitFactorTemperatureExponent { get; init; }

    /// <summary>Gets the exponent of the current factor of the unit.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-unitfactorcurrentexponent"/>
    [IdlMember("unitFactorCurrentExponent", "https://hid.spec.whatwg.org/#dom-hidreportitem-unitfactorcurrentexponent")]
    public sbyte? UnitFactorCurrentExponent { get; init; }

    /// <summary>Gets the exponent of the luminous intensity factor of the unit.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-unitfactorluminousintensityexponent"/>
    [IdlMember("unitFactorLuminousIntensityExponent", "https://hid.spec.whatwg.org/#dom-hidreportitem-unitfactorluminousintensityexponent")]
    public sbyte? UnitFactorLuminousIntensityExponent { get; init; }

    /// <summary>Gets the smallest value of a field.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-logicalminimum"/>
    [IdlMember("logicalMinimum", "https://hid.spec.whatwg.org/#dom-hidreportitem-logicalminimum")]
    public int? LogicalMinimum { get; init; }

    /// <summary>Gets the largest value of a field.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-logicalmaximum"/>
    [IdlMember("logicalMaximum", "https://hid.spec.whatwg.org/#dom-hidreportitem-logicalmaximum")]
    public int? LogicalMaximum { get; init; }

    /// <summary>Gets the physical value of <see cref="LogicalMinimum"/>.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-physicalminimum"/>
    [IdlMember("physicalMinimum", "https://hid.spec.whatwg.org/#dom-hidreportitem-physicalminimum")]
    public int? PhysicalMinimum { get; init; }

    /// <summary>Gets the physical value of <see cref="LogicalMaximum"/>.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-physicalmaximum"/>
    [IdlMember("physicalMaximum", "https://hid.spec.whatwg.org/#dom-hidreportitem-physicalmaximum")]
    public int? PhysicalMaximum { get; init; }

    /// <summary>Gets the strings of the string descriptors of the field.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportitem-strings"/>
    [IdlMember("strings", "https://hid.spec.whatwg.org/#dom-hidreportitem-strings")]
    public IReadOnlyList<string>? Strings { get; init; }
}

/// <summary>A report of a collection (https://hid.spec.whatwg.org/#dom-hidreportinfo): its report ID and its fields.</summary>
/// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportinfo"/>
[IdlInterface("HIDReportInfo", "https://hid.spec.whatwg.org/#dom-hidreportinfo")]
public sealed class HidReportInfo
{
    /// <summary>Gets the report ID (0 when the interface does not use report IDs).</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportinfo-reportid"/>
    [IdlMember("reportId", "https://hid.spec.whatwg.org/#dom-hidreportinfo-reportid")]
    public byte? ReportId { get; init; }

    /// <summary>Gets the fields of the report.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidreportinfo-items"/>
    [IdlMember("items", "https://hid.spec.whatwg.org/#dom-hidreportinfo-items")]
    public IReadOnlyList<HidReportItem>? Items { get; init; }
}

/// <summary>A collection of the report descriptor (https://hid.spec.whatwg.org/#dom-hidcollectioninfo): its usage, its nested collections and its reports.</summary>
/// <seealso href="https://hid.spec.whatwg.org/#dom-hidcollectioninfo"/>
[IdlInterface("HIDCollectionInfo", "https://hid.spec.whatwg.org/#dom-hidcollectioninfo")]
public sealed class HidCollectionInfo
{
    /// <summary>Gets the usage page of the collection.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidcollectioninfo-usagepage"/>
    [IdlMember("usagePage", "https://hid.spec.whatwg.org/#dom-hidcollectioninfo-usagepage")]
    public ushort? UsagePage { get; init; }

    /// <summary>Gets the usage ID of the collection.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidcollectioninfo-usage"/>
    [IdlMember("usage", "https://hid.spec.whatwg.org/#dom-hidcollectioninfo-usage")]
    public ushort? Usage { get; init; }

    /// <summary>Gets the type of the collection (0 physical, 1 application, 2 logical...).</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidcollectioninfo-type"/>
    [IdlMember("type", "https://hid.spec.whatwg.org/#dom-hidcollectioninfo-type")]
    public byte? Type { get; init; }

    /// <summary>Gets the nested collections.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidcollectioninfo-children"/>
    [IdlMember("children", "https://hid.spec.whatwg.org/#dom-hidcollectioninfo-children")]
    public IReadOnlyList<HidCollectionInfo>? Children { get; init; }

    /// <summary>Gets the input reports of the collection and of its nested collections.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidcollectioninfo-inputreports"/>
    [IdlMember("inputReports", "https://hid.spec.whatwg.org/#dom-hidcollectioninfo-inputreports")]
    public IReadOnlyList<HidReportInfo>? InputReports { get; init; }

    /// <summary>Gets the output reports of the collection and of its nested collections.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidcollectioninfo-outputreports"/>
    [IdlMember("outputReports", "https://hid.spec.whatwg.org/#dom-hidcollectioninfo-outputreports")]
    public IReadOnlyList<HidReportInfo>? OutputReports { get; init; }

    /// <summary>Gets the feature reports of the collection and of its nested collections.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidcollectioninfo-featurereports"/>
    [IdlMember("featureReports", "https://hid.spec.whatwg.org/#dom-hidcollectioninfo-featurereports")]
    public IReadOnlyList<HidReportInfo>? FeatureReports { get; init; }
}

/// <summary>A filter of the devices of <see cref="Hid.RequestDevice"/> (https://hid.spec.whatwg.org/#dom-hiddevicefilter).</summary>
/// <seealso href="https://hid.spec.whatwg.org/#dom-hiddevicefilter"/>
[IdlInterface("HIDDeviceFilter", "https://hid.spec.whatwg.org/#dom-hiddevicefilter")]
public sealed class HidDeviceFilter
{
    /// <summary>Gets the vendor ID, or null.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hiddevicefilter-vendorid"/>
    [IdlMember("vendorId", "https://hid.spec.whatwg.org/#dom-hiddevicefilter-vendorid")]
    public ushort? VendorId { get; init; }

    /// <summary>Gets the product ID, or null (it requires <see cref="VendorId"/>).</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hiddevicefilter-productid"/>
    [IdlMember("productId", "https://hid.spec.whatwg.org/#dom-hiddevicefilter-productid")]
    public ushort? ProductId { get; init; }

    /// <summary>Gets the usage page of a top-level collection, or null.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hiddevicefilter-usagepage"/>
    [IdlMember("usagePage", "https://hid.spec.whatwg.org/#dom-hiddevicefilter-usagepage")]
    public ushort? UsagePage { get; init; }

    /// <summary>Gets the usage ID of a top-level collection, or null (it requires <see cref="UsagePage"/>).</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hiddevicefilter-usage"/>
    [IdlMember("usage", "https://hid.spec.whatwg.org/#dom-hiddevicefilter-usage")]
    public ushort? Usage { get; init; }

    /// <summary>https://hid.spec.whatwg.org/#dfn-valid-filter</summary>
    internal bool IsValid => (VendorId is not null || ProductId is not null || UsagePage is not null || Usage is not null) && (ProductId is null || VendorId is not null) && (Usage is null || UsagePage is not null);

    /// <summary>Returns whether a device matches the filter (its identifiers and one of its top-level collections, https://hid.spec.whatwg.org/#dfn-matches-any-filter).</summary>
    /// <param name="device">The device.</param>
    [NotInIdl("The matching algorithm of the filters, for the device choosers of the hosts")]
    public bool Matches(HidHostDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (VendorId is { } vendorId && (device.VendorId != vendorId || (ProductId is { } productId && device.ProductId != productId)))
            return false;

        foreach (var collection in device.Collections)
        {
            if (UsagePage is { } usagePage && (collection.UsagePage != usagePage || (Usage is { } usage && collection.Usage != usage)))
                continue;

            return true;
        }

        return false;
    }
}

/// <summary>The options of <see cref="Hid.RequestDevice"/> (https://hid.spec.whatwg.org/#dom-hiddevicerequestoptions).</summary>
/// <seealso href="https://hid.spec.whatwg.org/#dom-hiddevicerequestoptions"/>
[IdlInterface("HIDDeviceRequestOptions", "https://hid.spec.whatwg.org/#dom-hiddevicerequestoptions")]
public sealed class HidDeviceRequestOptions
{
    /// <summary>Gets the filters: a device matches one of them (every device when there is none).</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hiddevicerequestoptions-filters"/>
    [IdlMember("filters", "https://hid.spec.whatwg.org/#dom-hiddevicerequestoptions-filters")]
    public required IReadOnlyList<HidDeviceFilter> Filters { get; init; }

    /// <summary>Gets the exclusion filters: a device matches none of them (they must not be empty when present).</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hiddevicerequestoptions-exclusionfilters"/>
    [IdlMember("exclusionFilters", "https://hid.spec.whatwg.org/#dom-hiddevicerequestoptions-exclusionfilters")]
    public IReadOnlyList<HidDeviceFilter>? ExclusionFilters { get; init; }
}

/// <summary>The initialization dictionary of a <see cref="HidConnectionEvent"/> (https://hid.spec.whatwg.org/#dom-hidconnectioneventinit).</summary>
/// <seealso href="https://hid.spec.whatwg.org/#dom-hidconnectioneventinit"/>
[IdlInterface("HIDConnectionEventInit", "https://hid.spec.whatwg.org/#dom-hidconnectioneventinit")]
public sealed class HidConnectionEventInit : EventInit
{
    /// <summary>Gets the device.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidconnectioneventinit-device"/>
    [IdlMember("device", "https://hid.spec.whatwg.org/#dom-hidconnectioneventinit-device")]
    public required HidDevice Device { get; init; }
}

/// <summary>The event fired at <see cref="Hid"/> when a device is connected or disconnected (<c>connect</c>, <c>disconnect</c>, https://hid.spec.whatwg.org/#dom-hidconnectionevent).</summary>
/// <seealso href="https://hid.spec.whatwg.org/#dom-hidconnectionevent"/>
[IdlInterface("HIDConnectionEvent", "https://hid.spec.whatwg.org/#dom-hidconnectionevent")]
public sealed class HidConnectionEvent : Event
{
    /// <summary>Creates an event.</summary>
    /// <param name="type">The type of the event.</param>
    /// <param name="eventInitDict">The initialization dictionary.</param>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidconnectionevent-constructor"/>
    [IdlMember("constructor", "https://hid.spec.whatwg.org/#dom-hidconnectionevent-constructor")]
    public HidConnectionEvent(string type, HidConnectionEventInit eventInitDict)
        : base(type, eventInitDict)
    {
        ArgumentNullException.ThrowIfNull(eventInitDict);
        Device = eventInitDict.Device;
    }

    internal HidConnectionEvent(string type, HidDevice device)
        : base(type)
    {
        Device = device;
    }

    /// <summary>Gets the device.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidconnectionevent-device"/>
    [IdlMember("device", "https://hid.spec.whatwg.org/#dom-hidconnectionevent-device")]
    public HidDevice Device { get; }
}

/// <summary>The initialization dictionary of a <see cref="HidInputReportEvent"/> (https://hid.spec.whatwg.org/#dom-hidinputreporteventinit).</summary>
/// <seealso href="https://hid.spec.whatwg.org/#dom-hidinputreporteventinit"/>
[IdlInterface("HIDInputReportEventInit", "https://hid.spec.whatwg.org/#dom-hidinputreporteventinit")]
public sealed class HidInputReportEventInit : EventInit
{
    /// <summary>Gets the device.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidinputreporteventinit-device"/>
    [IdlMember("device", "https://hid.spec.whatwg.org/#dom-hidinputreporteventinit-device")]
    public required HidDevice Device { get; init; }

    /// <summary>Gets the report ID.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidinputreporteventinit-reportid"/>
    [IdlMember("reportId", "https://hid.spec.whatwg.org/#dom-hidinputreporteventinit-reportid")]
    public required byte ReportId { get; init; }

    /// <summary>Gets the data of the report.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidinputreporteventinit-data"/>
    [IdlMember("data", "https://hid.spec.whatwg.org/#dom-hidinputreporteventinit-data")]
    public required ReadOnlyMemory<byte> Data { get; init; }
}

/// <summary>The event fired at a <see cref="HidDevice"/> when the device sends an input report (<c>inputreport</c>, https://hid.spec.whatwg.org/#dom-hidinputreportevent).</summary>
/// <seealso href="https://hid.spec.whatwg.org/#dom-hidinputreportevent"/>
[IdlInterface("HIDInputReportEvent", "https://hid.spec.whatwg.org/#dom-hidinputreportevent")]
public sealed class HidInputReportEvent : Event
{
    private readonly JsBufferSource _data;

    /// <summary>Creates an event.</summary>
    /// <param name="type">The type of the event.</param>
    /// <param name="eventInitDict">The initialization dictionary.</param>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidinputreportevent-constructor"/>
    [IdlMember("constructor", "https://hid.spec.whatwg.org/#dom-hidinputreportevent-constructor")]
    public HidInputReportEvent(string type, HidInputReportEventInit eventInitDict)
        : base(type, eventInitDict)
    {
        ArgumentNullException.ThrowIfNull(eventInitDict);
        Device = eventInitDict.Device;
        ReportId = eventInitDict.ReportId;
        _data = new JsBufferSource(JsBufferKind.DataView, eventInitDict.Data.ToArray());
    }

    internal HidInputReportEvent(string type, HidDevice device, byte reportId, byte[] data)
        : base(type)
    {
        Device = device;
        ReportId = reportId;
        _data = new JsBufferSource(JsBufferKind.DataView, data);
    }

    /// <summary>Gets the device.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidinputreportevent-device"/>
    [IdlMember("device", "https://hid.spec.whatwg.org/#dom-hidinputreportevent-device")]
    public HidDevice Device { get; }

    /// <summary>Gets the report ID (0 when the interface does not use report IDs).</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidinputreportevent-reportid"/>
    [IdlMember("reportId", "https://hid.spec.whatwg.org/#dom-hidinputreportevent-reportid")]
    public byte ReportId { get; }

    /// <summary>Gets the data of the report, without the report ID.</summary>
    [NotInIdl("The bytes for the .NET code: the data attribute is DataView")]
    public ReadOnlyMemory<byte> Data => _data.Bytes;

    /// <summary>Gets the <c>DataView</c> of the data for the scripts: use <see cref="Data"/> in .NET code.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hidinputreportevent-data"/>
    [IdlMember("data", "https://hid.spec.whatwg.org/#dom-hidinputreportevent-data")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public object DataView => _data;
}

/// <summary>
/// The WebHID API of a window or a worker (https://hid.spec.whatwg.org/#hid-interface, <c>navigator.hid</c>): the devices of
/// the <see cref="HidProvider"/> of the browsing context (<see cref="BrowsingContextOptions.Hid"/>) that the origin is
/// allowed to use.
/// </summary>
/// <seealso href="https://hid.spec.whatwg.org/#dom-hid"/>
[IdlInterface("HID", "https://hid.spec.whatwg.org/#dom-hid")]
public sealed class Hid : EventTarget
{
    private readonly GlobalHid _state;

    internal Hid(GlobalHid state)
    {
        _state = state;
    }

    internal override GlobalScope? RelevantGlobal => _state.Global;

    /// <summary>Gets or sets the event handler of the <c>connect</c> event (<see cref="HidConnectionEvent"/>).</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hid-onconnect"/>
    [IdlMember("onconnect", "https://hid.spec.whatwg.org/#dom-hid-onconnect")]
    public Action<Event>? OnConnect
    {
        get => GetEventHandler("connect");
        set => SetEventHandler("connect", value);
    }

    /// <summary>Gets or sets the event handler of the <c>disconnect</c> event (<see cref="HidConnectionEvent"/>).</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hid-ondisconnect"/>
    [IdlMember("ondisconnect", "https://hid.spec.whatwg.org/#dom-hid-ondisconnect")]
    public Action<Event>? OnDisconnect
    {
        get => GetEventHandler("disconnect");
        set => SetEventHandler("disconnect", value);
    }

    /// <summary>
    /// Returns the connected devices that the origin is allowed to use (https://hid.spec.whatwg.org/#getdevices-method). The
    /// task fails with a <c>SecurityError</c> <see cref="DomException"/> when the <c>hid</c> permissions policy feature is not
    /// allowed, and in a service worker.
    /// </summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hid-getdevices"/>
    [IdlMember("getDevices", "https://hid.spec.whatwg.org/#dom-hid-getdevices")]
    public Task<IReadOnlyList<HidDevice>> GetDevices() => _state.GetDevices();

    /// <summary>
    /// Asks the user to choose a device (https://hid.spec.whatwg.org/#requestdevice-method) with
    /// <see cref="HidProvider.RequestDevice"/>: the origin is then allowed to use it. The task gives the chosen device (no
    /// device when the user cancels). It fails with a <see cref="TypeErrorException"/> for an invalid filter, a
    /// <c>SecurityError</c> <see cref="DomException"/> without user activation or when the <c>hid</c> permissions policy feature
    /// is not allowed, and a <c>NotSupportedError</c> in a worker.
    /// </summary>
    /// <param name="options">The filters of the devices.</param>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hid-requestdevice"/>
    [IdlMember("requestDevice", "https://hid.spec.whatwg.org/#dom-hid-requestdevice")]
    public Task<IReadOnlyList<HidDevice>> RequestDevice(HidDeviceRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return _state.RequestDevice(options);
    }
}

/// <summary>
/// A HID device of a window or a worker (https://hid.spec.whatwg.org/#hiddevice-interface): the identifiers and the
/// collections of a <see cref="HidHostDevice"/>, and the reports of the page, which use the <see cref="HidDeviceSession"/>
/// that <see cref="Open"/> opens. The reports of the HID blocklist are blocked.
/// </summary>
/// <seealso href="https://hid.spec.whatwg.org/#dom-hiddevice"/>
[IdlInterface("HIDDevice", "https://hid.spec.whatwg.org/#dom-hiddevice")]
public sealed class HidDevice : EventTarget
{
    private readonly GlobalHid _state;
    private readonly HashSet<TaskCompletionSource> _pendingSendReports = [];
    private readonly HashSet<TaskCompletionSource> _pendingSendFeatureReports = [];
    private readonly HashSet<TaskCompletionSource<object>> _pendingReceiveFeatureReports = [];
    private HidDeviceState _deviceState = HidDeviceState.Closed;
    private HidDeviceSession? _session;
    private CancellationTokenSource? _sessionCancellation;

    internal HidDevice(GlobalHid state, HidHostDevice device)
    {
        _state = state;
        HostDevice = device;
    }

    private enum HidDeviceState
    {
        Closed,
        Opening,
        Opened,
        Closing,
        Forgetting,
        Forgotten,
    }

    /// <summary>Gets the device of the provider.</summary>
    [NotInIdl("The device of the host, which is not exposed to scripts")]
    public HidHostDevice HostDevice { get; }

    /// <summary>Gets or sets the event handler of the <c>inputreport</c> event (<see cref="HidInputReportEvent"/>).</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hiddevice-oninputreport"/>
    [IdlMember("oninputreport", "https://hid.spec.whatwg.org/#dom-hiddevice-oninputreport")]
    public Action<Event>? OnInputReport
    {
        get => GetEventHandler("inputreport");
        set => SetEventHandler("inputreport", value);
    }

    /// <summary>Gets whether the device is open.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hiddevice-opened"/>
    [IdlMember("opened", "https://hid.spec.whatwg.org/#dom-hiddevice-opened")]
    public bool Opened => _deviceState == HidDeviceState.Opened;

    /// <summary>Gets the vendor ID of the device.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hiddevice-vendorid"/>
    [IdlMember("vendorId", "https://hid.spec.whatwg.org/#dom-hiddevice-vendorid")]
    public ushort VendorId => HostDevice.VendorId;

    /// <summary>Gets the product ID of the device.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hiddevice-productid"/>
    [IdlMember("productId", "https://hid.spec.whatwg.org/#dom-hiddevice-productid")]
    public ushort ProductId => HostDevice.ProductId;

    /// <summary>Gets the product name of the device.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hiddevice-productname"/>
    [IdlMember("productName", "https://hid.spec.whatwg.org/#dom-hiddevice-productname")]
    public string ProductName => HostDevice.ProductName;

    /// <summary>Gets the top-level collections of the report descriptor.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hiddevice-collections"/>
    [IdlMember("collections", "https://hid.spec.whatwg.org/#dom-hiddevice-collections")]
    public IReadOnlyList<HidCollectionInfo> Collections => HostDevice.Collections;

    private GlobalScope Global => _state.Global;

    internal override GlobalScope? RelevantGlobal => Global;

    /// <summary>
    /// Opens the device (https://hid.spec.whatwg.org/#open-method) with <see cref="HidHostDevice.OpenAsync"/>: the device then
    /// gets the <c>inputreport</c> events. The task fails with an <c>InvalidStateError</c> <see cref="DomException"/> when the
    /// device is not closed, and a <c>NetworkError</c> when it cannot be opened.
    /// </summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hiddevice-open"/>
    [IdlMember("open", "https://hid.spec.whatwg.org/#dom-hiddevice-open")]
    public Task Open()
    {
        if (_deviceState != HidDeviceState.Closed)
            return Task.FromException(DomException.InvalidState("The device is already open."));

        if (HostDevice.Provider is null)
            return Task.FromException(new DomException("The device is not connected.", "NetworkError"));

        _deviceState = HidDeviceState.Opening;
        var cancellation = new CancellationTokenSource();
        _sessionCancellation = cancellation;
        var result = new TaskCompletionSource();
        Task<HidDeviceSession> opening;
        try
        {
            opening = HostDevice.OpenAsync(cancellation.Token);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            opening = Task.FromException<HidDeviceSession>(exception);
        }

        _ = opening.ContinueWith(
            task => Global.QueueGlobalTask(() =>
            {
                if (!task.IsCompletedSuccessfully)
                {
                    if (_deviceState == HidDeviceState.Opening)
                    {
                        _deviceState = HidDeviceState.Closed;
                    }

                    result.TrySetException(new DomException("Failed to open the device.", "NetworkError"));
                    return;
                }

                var session = task.Result;
                if (_deviceState != HidDeviceState.Opening)
                {
                    _ = session.CloseAsync();
                    result.TrySetException(new DomException("Failed to open the device.", "NetworkError"));
                    return;
                }

                _session = session;
                session.InputReportReceived = (reportId, data) => Global.QueueGlobalTask(() => OnInputReportReceived(session, reportId, data));
                _deviceState = HidDeviceState.Opened;
                result.TrySetResult();
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return result.Task;
    }

    /// <summary>
    /// Closes the device (https://hid.spec.whatwg.org/#close-method): the pending reports fail with an <c>AbortError</c>. The
    /// task fails with an <c>InvalidStateError</c> <see cref="DomException"/> while the device is being opened, closed or forgotten.
    /// </summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hiddevice-close"/>
    [IdlMember("close", "https://hid.spec.whatwg.org/#dom-hiddevice-close")]
    public Task Close()
    {
        if (_deviceState is HidDeviceState.Forgotten or HidDeviceState.Forgetting or HidDeviceState.Opening or HidDeviceState.Closing)
            return Task.FromException(DomException.InvalidState("The device is being opened, closed or forgotten."));

        _deviceState = HidDeviceState.Closing;
        AbortPendingReports();
        var result = new TaskCompletionSource();
        _ = CloseSession().ContinueWith(
            _ => Global.QueueGlobalTask(() =>
            {
                if (_deviceState == HidDeviceState.Closing)
                {
                    _deviceState = HidDeviceState.Closed;
                }

                result.TrySetResult();
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return result.Task;
    }

    /// <summary>
    /// Revokes the permission of the origin to use the device (https://hid.spec.whatwg.org/#forget-method): the pending reports
    /// fail with an <c>AbortError</c>, the device is closed, and <see cref="Hid.GetDevices"/> does not return it anymore.
    /// </summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hiddevice-forget"/>
    [IdlMember("forget", "https://hid.spec.whatwg.org/#dom-hiddevice-forget")]
    public Task Forget()
    {
        _deviceState = HidDeviceState.Forgetting;
        AbortPendingReports();
        _state.Forget(HostDevice);
        var result = new TaskCompletionSource();
        _ = CloseSession().ContinueWith(
            _ => Global.QueueGlobalTask(() =>
            {
                _deviceState = HidDeviceState.Forgotten;
                result.TrySetResult();
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return result.Task;
    }

    /// <summary>
    /// Sends an output report (https://hid.spec.whatwg.org/#sendreport-method). The task fails with an <c>InvalidStateError</c>
    /// <see cref="DomException"/> when the device is not open, a <see cref="TypeErrorException"/> for a report ID of 0 when the
    /// interface uses report IDs (or another one when it does not), a <c>NotAllowedError</c> for a blocked report, a
    /// <c>NetworkError</c> when the report cannot be sent, and an <c>AbortError</c> when the device is closed first.
    /// </summary>
    /// <param name="reportId">The report ID, or 0.</param>
    /// <param name="data">The data of the report.</param>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hiddevice-sendreport"/>
    [IdlMember("sendReport", "https://hid.spec.whatwg.org/#dom-hiddevice-sendreport")]
    public Task SendReport(byte reportId, ReadOnlyMemory<byte> data)
    {
        var bytes = data.ToArray();
        return SendCore(_pendingSendReports, HidReportType.Output, reportId, (session, token) => session.SendReportAsync(reportId, bytes, token), "Failed to write the report.");
    }

    /// <summary>Sends a feature report (https://hid.spec.whatwg.org/#sendfeaturereport-method). The errors are the ones of <see cref="SendReport"/>.</summary>
    /// <param name="reportId">The report ID, or 0.</param>
    /// <param name="data">The data of the report.</param>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hiddevice-sendfeaturereport"/>
    [IdlMember("sendFeatureReport", "https://hid.spec.whatwg.org/#dom-hiddevice-sendfeaturereport")]
    public Task SendFeatureReport(byte reportId, ReadOnlyMemory<byte> data)
    {
        var bytes = data.ToArray();
        return SendCore(_pendingSendFeatureReports, HidReportType.Feature, reportId, (session, token) => session.SendFeatureReportAsync(reportId, bytes, token), "Failed to write the feature report.");
    }

    /// <summary>
    /// Reads a feature report (https://hid.spec.whatwg.org/#receivefeaturereport-method): the task gives a <c>DataView</c> of the
    /// bytes of the device (which may start with the report ID). The errors are the ones of <see cref="SendReport"/>.
    /// </summary>
    /// <param name="reportId">The report ID, or 0.</param>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-hiddevice-receivefeaturereport"/>
    [IdlMember("receiveFeatureReport", "https://hid.spec.whatwg.org/#dom-hiddevice-receivefeaturereport")]
    public Task<object> ReceiveFeatureReport(byte reportId)
    {
        if (GetReportError(reportId, out var session) is { } error)
            return Task.FromException<object>(error);

        var result = new TaskCompletionSource<object>();
        _pendingReceiveFeatureReports.Add(result);
        if (HostDevice.Descriptor.IsBlocked(HostDevice, HidReportType.Feature, reportId))
        {
            Global.QueueGlobalTask(() => Complete(_pendingReceiveFeatureReports, result, () => result.TrySetException(BlockedReportError())));
            return result.Task;
        }

        Task<ReadOnlyMemory<byte>> receive;
        try
        {
            receive = session.ReceiveFeatureReportAsync(reportId, _sessionCancellation?.Token ?? CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            receive = Task.FromException<ReadOnlyMemory<byte>>(exception);
        }

        _ = receive.ContinueWith(
            task => Global.QueueGlobalTask(() => Complete(_pendingReceiveFeatureReports, result, () =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    result.TrySetResult(new JsBufferSource(JsBufferKind.DataView, task.Result.ToArray()));
                }
                else
                {
                    result.TrySetException(new DomException("Failed to receive the feature report.", "NetworkError"));
                }
            })),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return result.Task;
    }

    /// <summary>The device was disconnected: the pending reports fail, and the session ends.</summary>
    internal void OnDisconnected()
    {
        if (_deviceState is HidDeviceState.Opened or HidDeviceState.Opening)
        {
            _deviceState = HidDeviceState.Closed;
            AbortPendingReports();
            _ = CloseSession();
        }
    }

    /// <summary>The window is unloaded or the worker is terminated: the session ends.</summary>
    internal void OnContextDestroyed()
    {
        AbortPendingReports();
        _ = CloseSession();
        _deviceState = HidDeviceState.Closed;
    }

    private static DomException BlockedReportError() => DomException.NotAllowed("Failed to send or receive a report: the report is blocked by the HID blocklist.");

    // The pending promise is settled once: close() and forget() reject the pending ones with an AbortError
    private static void Complete<T>(HashSet<T> pending, T promise, Action settle)
        where T : class
    {
        if (pending.Remove(promise))
        {
            settle();
        }
    }

    // https://hid.spec.whatwg.org/#dom-hiddevice-sendreport, https://hid.spec.whatwg.org/#dom-hiddevice-sendfeaturereport
    private Task SendCore(HashSet<TaskCompletionSource> pending, HidReportType type, byte reportId, Func<HidDeviceSession, CancellationToken, Task> send, string errorMessage)
    {
        if (GetReportError(reportId, out var session) is { } error)
            return Task.FromException(error);

        var result = new TaskCompletionSource();
        pending.Add(result);
        if (HostDevice.Descriptor.IsBlocked(HostDevice, type, reportId))
        {
            Global.QueueGlobalTask(() => Complete(pending, result, () => result.TrySetException(BlockedReportError())));
            return result.Task;
        }

        Task task;
        try
        {
            task = send(session, _sessionCancellation?.Token ?? CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            task = Task.FromException(exception);
        }

        _ = task.ContinueWith(
            completed => Global.QueueGlobalTask(() => Complete(pending, result, () =>
            {
                if (completed.IsCompletedSuccessfully)
                {
                    result.TrySetResult();
                }
                else
                {
                    result.TrySetException(new DomException(errorMessage, "NetworkError"));
                }
            })),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return result.Task;
    }

    private Exception? GetReportError(byte reportId, out HidDeviceSession session)
    {
        session = _session!;
        if (_deviceState != HidDeviceState.Opened || _session is null)
            return DomException.InvalidState("The device must be opened first.");

        var usesReportIds = HostDevice.UsesReportIds;
        if (reportId == 0 && usesReportIds)
            return new TypeErrorException("The report ID 0 is reserved: the interface uses report IDs.");

        if (reportId != 0 && !usesReportIds)
            return new TypeErrorException("The report ID must be 0: the interface does not use report IDs.");

        return null;
    }

    // https://hid.spec.whatwg.org/#hiddevice-interface: an input report that is not blocked fires an inputreport event
    private void OnInputReportReceived(HidDeviceSession session, byte reportId, byte[] data)
    {
        if (!ReferenceEquals(_session, session) || _deviceState != HidDeviceState.Opened || HostDevice.Descriptor.IsBlocked(HostDevice, HidReportType.Input, reportId))
            return;

        FireEvent(new HidInputReportEvent("inputreport", this, reportId, data));
    }

    private void AbortPendingReports()
    {
        var error = new DomException("The device was closed.", "AbortError");
        foreach (var pending in _pendingSendReports.ToArray())
        {
            pending.TrySetException(error);
        }

        foreach (var pending in _pendingSendFeatureReports.ToArray())
        {
            pending.TrySetException(error);
        }

        foreach (var pending in _pendingReceiveFeatureReports.ToArray())
        {
            pending.TrySetException(error);
        }

        _pendingSendReports.Clear();
        _pendingSendFeatureReports.Clear();
        _pendingReceiveFeatureReports.Clear();
    }

    private Task CloseSession()
    {
        _sessionCancellation?.Cancel();
        _sessionCancellation = null;
        var session = _session;
        _session = null;
        if (session is null)
            return Task.CompletedTask;

        session.InputReportReceived = null;
        try
        {
            return session.CloseAsync().ContinueWith(static _ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// The HID state of a window or a worker: its <see cref="Hid"/> object, and the <see cref="HidDevice"/> object of each
/// <see cref="HidHostDevice"/> it can use. The connections and the disconnections of the provider run in tasks.
/// </summary>
internal sealed class GlobalHid
{
    private readonly Dictionary<HidHostDevice, HidDevice> _devices = [];

    public GlobalHid(GlobalScope global)
    {
        Global = global;
        Hid = new Hid(this);
        Provider?.Register(this);
    }

    public GlobalScope Global { get; }

    public Hid Hid { get; }

    private HidProvider? Provider => Global.Options.Hid?.Provider;

    private string Origin => Global.OriginRecord.Serialize();

    // https://hid.spec.whatwg.org/#getdevices-method
    public Task<IReadOnlyList<HidDevice>> GetDevices()
    {
        if (GetSecurityError("getDevices") is { } error)
            return Task.FromException<IReadOnlyList<HidDevice>>(error);

        var result = new TaskCompletionSource<IReadOnlyList<HidDevice>>();
        Global.QueueGlobalTask(() =>
        {
            var devices = new List<HidDevice>();
            if (Provider is { } provider)
            {
                foreach (var device in provider.Devices)
                {
                    if (provider.IsAllowed(device, Origin))
                    {
                        devices.Add(GetOrCreate(device));
                    }
                }
            }

            result.TrySetResult(devices);
        });
        return result.Task;
    }

    // https://hid.spec.whatwg.org/#requestdevice-method
    public Task<IReadOnlyList<HidDevice>> RequestDevice(HidDeviceRequestOptions options)
    {
        if (GetSecurityError("requestDevice") is { } error)
            return Task.FromException<IReadOnlyList<HidDevice>>(error);

        if (Global is not Window window)
            return Task.FromException<IReadOnlyList<HidDevice>>(DomException.NotSupported("requestDevice() is only supported in a window."));

        if (!window.HasTransientActivation)
            return Task.FromException<IReadOnlyList<HidDevice>>(new DomException("Failed to execute 'requestDevice' on 'HID': Must be handling a user gesture to show a permission request.", "SecurityError"));

        if (options.Filters.Any(filter => !filter.IsValid))
            return Task.FromException<IReadOnlyList<HidDevice>>(new TypeErrorException("Failed to execute 'requestDevice' on 'HID': A filter must not be empty, a productId requires a vendorId, and a usage a usagePage."));

        var exclusionFilters = options.ExclusionFilters ?? [];
        if (options.ExclusionFilters is { Count: 0 })
            return Task.FromException<IReadOnlyList<HidDevice>>(new TypeErrorException("Failed to execute 'requestDevice' on 'HID': 'exclusionFilters', if present, must contain at least one filter."));

        if (exclusionFilters.Any(filter => !filter.IsValid))
            return Task.FromException<IReadOnlyList<HidDevice>>(new TypeErrorException("Failed to execute 'requestDevice' on 'HID': An exclusion filter must not be empty, a productId requires a vendorId, and a usage a usagePage."));

        var result = new TaskCompletionSource<IReadOnlyList<HidDevice>>();
        if (Provider is not { } provider)
        {
            Global.QueueGlobalTask(() => result.TrySetResult([]));
            return result.Task;
        }

        // In parallel: the connected devices that match the filters are offered to the user
        var candidates = provider.Devices
            .Where(device => options.Filters.Count == 0 || options.Filters.Any(filter => filter.Matches(device)))
            .Where(device => !exclusionFilters.Any(filter => filter.Matches(device)))
            .ToArray();
        Task<HidHostDevice?> selection;
        try
        {
            selection = provider.RequestDevice(new HidDeviceRequest(CallbackContext.For(window), options.Filters, exclusionFilters, candidates));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            selection = Task.FromException<HidHostDevice?>(exception);
        }

        _ = selection.ContinueWith(
            task => Global.QueueGlobalTask(() =>
            {
                var device = task.IsCompletedSuccessfully ? task.Result : null;
                if (device is null || device.Provider != provider)
                {
                    result.TrySetResult([]);
                    return;
                }

                provider.Allow(device, Origin);
                result.TrySetResult([GetOrCreate(device)]);
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return result.Task;
    }

    /// <summary>https://hid.spec.whatwg.org/#forget-method: the origin is not allowed to use the device anymore.</summary>
    public void Forget(HidHostDevice device) => Provider?.Revoke(device, Origin);

    /// <summary>A device was connected. Called from any thread.</summary>
    public void OnConnected(HidHostDevice device) => Global.QueueGlobalTask(() =>
    {
        if (Provider is not { } provider || !provider.IsAllowed(device, Origin) || GetSecurityError("connect") is not null)
            return;

        Hid.FireEvent(new HidConnectionEvent("connect", GetOrCreate(device)));
    });

    /// <summary>A device was disconnected. Called from any thread.</summary>
    public void OnDisconnected(HidHostDevice device) => Global.QueueGlobalTask(() =>
    {
        if (!_devices.Remove(device, out var deviceObject))
            return;

        deviceObject.OnDisconnected();
        Hid.FireEvent(new HidConnectionEvent("disconnect", deviceObject));
    });

    /// <summary>The window or the worker is destroyed: the sessions of its devices end.</summary>
    public void OnDestroyed()
    {
        foreach (var device in _devices.Values)
        {
            device.OnContextDestroyed();
        }
    }

    private HidDevice GetOrCreate(HidHostDevice device)
    {
        if (!_devices.TryGetValue(device, out var deviceObject))
        {
            deviceObject = new HidDevice(this, device);
            _devices.Add(device, deviceObject);
        }

        return deviceObject;
    }

    /// <summary>
    /// The hid permissions policy feature (default allowlist 'self'), checked on the document of the window or the documents
    /// that own a dedicated worker. A service worker has no document: the request fails.
    /// </summary>
    private DomException? GetSecurityError(string operation)
    {
        var documents = PressureObserver.GetOwningDocumentSet(Global).ToList();
        if (documents.Count == 0 && Global is not Window)
            return new DomException($"Failed to execute '{operation}' on 'HID': Access to the feature \"hid\" requires a document.", "SecurityError");

        foreach (var document in documents)
        {
            if (!PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.Hid))
                return new DomException($"Failed to execute '{operation}' on 'HID': Access to the feature \"hid\" is disallowed by permissions policy.", "SecurityError");
        }

        return null;
    }
}

// https://hid.spec.whatwg.org/#extensions-to-the-navigator-interface
public sealed partial class Navigator
{
    /// <summary>Gets the WebHID API of the window.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-navigator-hid"/>
    [IdlMember("hid", "https://hid.spec.whatwg.org/#dom-navigator-hid")]
    public Hid Hid => _window.GetHidState().Hid;
}

// https://hid.spec.whatwg.org/#extensions-to-the-workernavigator-interface
public sealed partial class WorkerNavigator
{
    /// <summary>Gets the WebHID API of the worker.</summary>
    /// <seealso href="https://hid.spec.whatwg.org/#dom-workernavigator-hid"/>
    [IdlMember("hid", "https://hid.spec.whatwg.org/#dom-workernavigator-hid")]
    public Hid Hid => _global.GetHidState().Hid;
}

public abstract partial class GlobalScope
{
    private GlobalHid? _hidState;

    /// <summary>The HID state of the global object, created by the first access to <c>navigator.hid</c>.</summary>
    internal GlobalHid GetHidState() => _hidState ??= new GlobalHid(this);

    /// <summary>The window is unloaded or the worker is terminated: the sessions of its HID devices end.</summary>
    internal void CloseHidDevices() => _hidState?.OnDestroyed();
}
