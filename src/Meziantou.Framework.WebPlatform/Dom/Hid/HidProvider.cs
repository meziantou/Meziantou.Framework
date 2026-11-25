namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The host hooks of WebHID (https://hid.spec.whatwg.org/, <see cref="BrowsingContextOptions.Hid"/>).</summary>
public sealed class HidOptions
{
    /// <summary>
    /// Gets or sets the HID devices of the machine: a <see cref="HidProvider"/> to which the application connects the
    /// <see cref="HidHostDevice"/> objects that implement the devices. A provider can be shared by several browsing contexts.
    /// The default is null: no HID device is connected.
    /// </summary>
    public HidProvider? Provider { get; init; }
}

/// <summary>
/// The HID devices connected to the machine (each one a HID interface), used by the windows and the workers of the browsing
/// contexts that share a <see cref="BrowsingContextOptions"/> (<see cref="HidOptions.Provider"/>): <see cref="Connect"/> and
/// <see cref="Disconnect"/> fire the <c>connect</c> and <c>disconnect</c> events of <c>navigator.hid</c>, and the permission
/// storage gives each origin the devices it can use. Override <see cref="RequestDevice"/> to play the role of the device
/// chooser of <c>navigator.hid.requestDevice()</c>.
/// </summary>
/// <remarks>A provider can be shared by several browsing contexts, and used from any thread.</remarks>
public class HidProvider
{
    private readonly Lock _lock = new();
    private readonly List<HidHostDevice> _devices = [];
    private readonly List<WeakReference<GlobalHid>> _globals = [];
    private readonly Dictionary<string, HashSet<HidHostDevice>> _allowedDevices = new(StringComparer.Ordinal);

    /// <summary>Gets the connected devices, in the order they were connected.</summary>
    public IReadOnlyList<HidHostDevice> Devices
    {
        get
        {
            lock (_lock)
            {
                return [.. _devices];
            }
        }
    }

    /// <summary>Connects a device: the windows and the workers of the origins that are allowed to use it get a <c>connect</c> event. Can be called from any thread.</summary>
    /// <param name="device">The device. A device can be connected to one provider at a time.</param>
    /// <exception cref="InvalidOperationException">The device is connected to a provider.</exception>
    public void Connect(HidHostDevice device)
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

    /// <summary>Disconnects a device: its <c>HIDDevice</c> objects are closed, and the windows and the workers that see it get a <c>disconnect</c> event. Can be called from any thread.</summary>
    /// <param name="device">The device.</param>
    /// <returns>Whether the device was connected to this provider.</returns>
    public bool Disconnect(HidHostDevice device)
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
    /// Returns whether an origin can use a device without asking the user: the default implementation returns whether the
    /// device was allowed with <see cref="Allow"/>, or chosen by the user with <see cref="RequestDevice"/>, and not forgotten since.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <param name="origin">The serialization of the origin of the window or the worker (e.g. <c>https://example.com</c>).</param>
    public virtual bool IsAllowed(HidHostDevice device, string origin)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(origin);
        lock (_lock)
        {
            return _allowedDevices.TryGetValue(origin, out var devices) && devices.Contains(device);
        }
    }

    /// <summary>Allows an origin to use a device.</summary>
    /// <param name="device">The device.</param>
    /// <param name="origin">The serialization of the origin.</param>
    public void Allow(HidHostDevice device, string origin)
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

    /// <summary>Revokes the permission of an origin to use a device, as <c>HIDDevice.forget()</c> does.</summary>
    /// <param name="device">The device.</param>
    /// <param name="origin">The serialization of the origin.</param>
    public void Revoke(HidHostDevice device, string origin)
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
    /// Asks the user to choose a device for <c>navigator.hid.requestDevice()</c> (https://hid.spec.whatwg.org/#requestdevice-method):
    /// <see cref="HidDeviceRequest.Devices"/> are the connected devices that match the filters of the page. Returns one of
    /// them, which the origin is then allowed to use, or null when the user cancels (the page gets no device). The default
    /// implementation returns null.
    /// </summary>
    /// <param name="request">The request of the window.</param>
    public virtual Task<HidHostDevice?> RequestDevice(HidDeviceRequest request) => Task.FromResult<HidHostDevice?>(null);

    /// <summary>Registers the HID objects of a window or a worker, which then get the connections and the disconnections.</summary>
    internal void Register(GlobalHid global)
    {
        lock (_lock)
        {
            _globals.RemoveAll(static reference => !reference.TryGetTarget(out _));
            _globals.Add(new WeakReference<GlobalHid>(global));
        }
    }

    private List<GlobalHid> GetGlobals()
    {
        lock (_lock)
        {
            var result = new List<GlobalHid>(_globals.Count);
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

/// <summary>A request of <c>navigator.hid.requestDevice()</c>, given to <see cref="HidProvider.RequestDevice"/>.</summary>
public sealed class HidDeviceRequest
{
    internal HidDeviceRequest(CallbackContext context, IReadOnlyList<HidDeviceFilter> filters, IReadOnlyList<HidDeviceFilter> exclusionFilters, IReadOnlyList<HidHostDevice> devices)
    {
        Context = context;
        Filters = filters;
        ExclusionFilters = exclusionFilters;
        Devices = devices;
    }

    /// <summary>Gets the context of the window that requests the device.</summary>
    public CallbackContext Context { get; }

    /// <summary>Gets the filters of the page: a device matches one of them (all the devices when there is none).</summary>
    public IReadOnlyList<HidDeviceFilter> Filters { get; }

    /// <summary>Gets the exclusion filters of the page: a device matches none of them.</summary>
    public IReadOnlyList<HidDeviceFilter> ExclusionFilters { get; }

    /// <summary>Gets the devices to choose from: the connected devices that match the filters.</summary>
    public IReadOnlyList<HidHostDevice> Devices { get; }
}

/// <summary>
/// A HID interface of a <see cref="HidProvider"/>: the identifiers and the report descriptor of the device (the library parses
/// the collections from it), and the sessions that the <c>HIDDevice</c> objects of the pages open with <see cref="OpenAsync"/>.
/// </summary>
public abstract class HidHostDevice
{
    private readonly Lock _lock = new();
    private HidReportDescriptor? _descriptor;

    /// <summary>Creates a device.</summary>
    /// <param name="vendorId">The vendor ID of the device.</param>
    /// <param name="productId">The product ID of the device.</param>
    /// <param name="productName">The product name of the device (no serial number).</param>
    /// <param name="reportDescriptor">The report descriptor of the HID interface (USB HID 1.11, section 6.2.2).</param>
    protected HidHostDevice(ushort vendorId, ushort productId, string productName, ReadOnlyMemory<byte> reportDescriptor)
    {
        ArgumentNullException.ThrowIfNull(productName);
        VendorId = vendorId;
        ProductId = productId;
        ProductName = productName;
        ReportDescriptor = reportDescriptor.ToArray();
    }

    /// <summary>Gets the vendor ID of the device.</summary>
    public ushort VendorId { get; }

    /// <summary>Gets the product ID of the device.</summary>
    public ushort ProductId { get; }

    /// <summary>Gets the product name of the device.</summary>
    public string ProductName { get; }

    /// <summary>Gets the report descriptor of the HID interface.</summary>
    public ReadOnlyMemory<byte> ReportDescriptor { get; }

    /// <summary>Gets the top-level collections of the report descriptor.</summary>
    public IReadOnlyList<HidCollectionInfo> Collections => Descriptor.Collections;

    /// <summary>Gets whether the reports of the interface have a report ID (the report descriptor has a Report ID item).</summary>
    public bool UsesReportIds => Descriptor.UsesReportIds;

    internal HidProvider? Provider { get; set; }

    internal HidReportDescriptor Descriptor
    {
        get
        {
            lock (_lock)
            {
                return _descriptor ??= HidReportDescriptor.Parse(ReportDescriptor.Span, GetString);
            }
        }
    }

    /// <summary>Opens a session with the device (<c>HIDDevice.open()</c>). Throw any exception to fail the opening (the page gets a <c>NetworkError</c>).</summary>
    /// <param name="cancellationToken">The token, canceled when the page forgets the device during the opening.</param>
    public abstract Task<HidDeviceSession> OpenAsync(CancellationToken cancellationToken);

    /// <summary>Returns the string descriptor of an index of the report descriptor (the <c>strings</c> of the report items), or null. The default implementation returns null.</summary>
    /// <param name="index">The index of the string descriptor.</param>
    protected virtual string? GetString(uint index) => null;
}

/// <summary>
/// A session of a <c>HIDDevice</c> object with a <see cref="HidHostDevice"/>: the output and feature reports of the page,
/// and the input reports of the device (<see cref="OnInputReport"/>). Throw any exception from an operation to fail it (the
/// page gets a <c>NetworkError</c>).
/// </summary>
public abstract class HidDeviceSession
{
    internal Action<byte, byte[]>? InputReportReceived { get; set; }

    /// <summary>Sends an output report (<c>sendReport()</c>).</summary>
    /// <param name="reportId">The report ID, or 0 when the interface does not use report IDs.</param>
    /// <param name="data">The data of the report, without the report ID.</param>
    /// <param name="cancellationToken">The token, canceled when the page closes or forgets the device.</param>
    public abstract Task SendReportAsync(byte reportId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken);

    /// <summary>Sends a feature report (<c>sendFeatureReport()</c>).</summary>
    /// <param name="reportId">The report ID, or 0 when the interface does not use report IDs.</param>
    /// <param name="data">The data of the report, without the report ID.</param>
    /// <param name="cancellationToken">The token, canceled when the page closes or forgets the device.</param>
    public abstract Task SendFeatureReportAsync(byte reportId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken);

    /// <summary>Reads a feature report (<c>receiveFeatureReport()</c>): the bytes returned by the device, which may start with the report ID.</summary>
    /// <param name="reportId">The report ID, or 0 when the interface does not use report IDs.</param>
    /// <param name="cancellationToken">The token, canceled when the page closes or forgets the device.</param>
    public abstract Task<ReadOnlyMemory<byte>> ReceiveFeatureReportAsync(byte reportId, CancellationToken cancellationToken);

    /// <summary>Closes the session (<c>close()</c>, <c>forget()</c>, or the end of the window or the worker).</summary>
    public abstract Task CloseAsync();

    /// <summary>
    /// The device sent an input report: the page gets an <c>inputreport</c> event (unless the report is blocked). Can be
    /// called from any thread.
    /// </summary>
    /// <param name="reportId">The report ID, or 0 when the interface does not use report IDs.</param>
    /// <param name="data">The data of the report, without the report ID.</param>
    protected void OnInputReport(byte reportId, ReadOnlyMemory<byte> data) => InputReportReceived?.Invoke(reportId, data.ToArray());
}

/// <summary>The kinds of HID reports.</summary>
internal enum HidReportType
{
    Input,
    Output,
    Feature,
}

/// <summary>
/// A parsed report descriptor (https://hid.spec.whatwg.org/#dfn-parse-the-report-descriptor): its top-level collections, whether
/// it uses report IDs, and the top-level collections of each report (for the blocklist).
/// </summary>
internal sealed class HidReportDescriptor
{
    // The limits of the parser, which protect the host from the descriptors of malicious devices: the nesting of the
    // collections (the deeper collections are ignored) and the strings of a range of string indices
    private const int MaxCollectionDepth = 32;
    private const int MaxStringsPerItem = 256;

    private HidReportDescriptor(List<HidCollectionInfo> collections, bool usesReportIds, Dictionary<(HidReportType, byte), List<HidCollectionInfo>> reportCollections)
    {
        Collections = collections;
        UsesReportIds = usesReportIds;
        ReportCollections = reportCollections;
    }

    public IReadOnlyList<HidCollectionInfo> Collections { get; }

    public bool UsesReportIds { get; }

    /// <summary>The top-level collections that contain each report.</summary>
    public Dictionary<(HidReportType Type, byte ReportId), List<HidCollectionInfo>> ReportCollections { get; }

    public static HidReportDescriptor Parse(ReadOnlySpan<byte> descriptor, Func<uint, string?>? getString = null)
    {
        var collections = new List<HidCollectionInfo>();
        var ancestors = new List<MutableCollection>();
        var topLevelCollections = new List<HidCollectionInfo>();
        var reportCollections = new Dictionary<(HidReportType, byte), List<HidCollectionInfo>>();
        var usesReportIds = false;
        var skippedDepth = 0;
        var local = new LocalState();
        var globalStack = new List<GlobalState> { new() };
        byte reportId = 0;
        var position = 0;
        while (position < descriptor.Length)
        {
            var prefix = descriptor[position++];

            // A long item (USB HID 1.11 6.2.2.3): its data are skipped
            if (prefix == 0xFE)
            {
                if (position + 2 > descriptor.Length)
                    break;

                position += 2 + descriptor[position];
                continue;
            }

            var size = (prefix & 0x03) == 3 ? 4 : prefix & 0x03;
            if (position + size > descriptor.Length)
                break;

            var data = descriptor.Slice(position, size);
            position += size;
            var value = 0u;
            for (var i = 0; i < size; i++)
            {
                value |= (uint)data[i] << (8 * i);
            }

            var type = (prefix >> 2) & 0x03;
            var tag = prefix >> 4;
            var global = globalStack[^1];
            switch (type)
            {
                // Main items
                case 0:
                    switch (tag)
                    {
                        case 0x8 or 0x9 or 0xB:
                            var reportType = tag switch
                            {
                                0x8 => HidReportType.Input,
                                0x9 => HidReportType.Output,
                                _ => HidReportType.Feature,
                            };
                            var item = CreateReportItem(value, local, global, getString);
                            foreach (var collection in ancestors)
                            {
                                collection.AddItem(reportType, reportId, item);
                            }

                            if (ancestors.Count > 0)
                            {
                                if (!reportCollections.TryGetValue((reportType, reportId), out var containing))
                                {
                                    containing = [];
                                    reportCollections.Add((reportType, reportId), containing);
                                }

                                var topLevel = topLevelCollections[^1];
                                if (!containing.Contains(topLevel))
                                {
                                    containing.Add(topLevel);
                                }
                            }

                            break;

                        // Collection
                        case 0xA:
                            if (ancestors.Count >= MaxCollectionDepth || skippedDepth > 0)
                            {
                                skippedDepth++;
                                break;
                            }

                            var usage = local.Usages.Count > 0 ? local.Usages[0] : 0;
                            if (local.Usages.Count > 1)
                            {
                                local.Usages.RemoveAt(0);
                            }

                            var newCollection = new MutableCollection((ushort)global.UsagePage, (ushort)usage, (byte)value);
                            if (ancestors.Count == 0)
                            {
                                collections.Add(newCollection.Info);
                                topLevelCollections.Add(newCollection.Info);
                            }
                            else
                            {
                                ancestors[^1].Children.Add(newCollection.Info);
                            }

                            ancestors.Add(newCollection);
                            break;

                        // End Collection
                        case 0xC:
                            if (skippedDepth > 0)
                            {
                                skippedDepth--;
                            }
                            else if (ancestors.Count > 0)
                            {
                                ancestors.RemoveAt(ancestors.Count - 1);
                            }

                            break;
                    }

                    local = new LocalState();
                    break;

                // Global items
                case 1:
                    switch (tag)
                    {
                        case 0x0:
                            global.UsagePage = value;
                            break;

                        // The minimums and the maximums are signed (as in Chromium)
                        case 0x1:
                            global.LogicalMinimum = SignExtend(value, size);
                            break;

                        case 0x2:
                            global.LogicalMaximum = SignExtend(value, size);
                            break;

                        case 0x3:
                            global.PhysicalMinimum = SignExtend(value, size);
                            break;

                        case 0x4:
                            global.PhysicalMaximum = SignExtend(value, size);
                            break;

                        case 0x5:
                            global.UnitExponent = Nibble(value, 0);
                            break;

                        case 0x6:
                            global.UnitSystem = Nibble(value, 0) switch
                            {
                                0 => HidUnitSystem.None,
                                1 => HidUnitSystem.SiLinear,
                                2 => HidUnitSystem.SiRotation,
                                3 => HidUnitSystem.EnglishLinear,
                                4 => HidUnitSystem.EnglishRotation,
                                -1 => HidUnitSystem.VendorDefined,
                                _ => HidUnitSystem.Reserved,
                            };
                            global.UnitFactorLengthExponent = Nibble(value, 1);
                            global.UnitFactorMassExponent = Nibble(value, 2);
                            global.UnitFactorTimeExponent = Nibble(value, 3);
                            global.UnitFactorTemperatureExponent = Nibble(value, 4);
                            global.UnitFactorCurrentExponent = Nibble(value, 5);
                            global.UnitFactorLuminousIntensityExponent = Nibble(value, 6);
                            break;

                        case 0x7:
                            global.ReportSize = value;
                            break;

                        // The report ID is not affected by Push and Pop
                        case 0x8:
                            reportId = (byte)value;
                            usesReportIds = true;
                            break;

                        case 0x9:
                            global.ReportCount = value;
                            break;

                        case 0xA:
                            globalStack.Add(global.Clone());
                            break;

                        case 0xB:
                            if (globalStack.Count > 1)
                            {
                                globalStack.RemoveAt(globalStack.Count - 1);
                            }

                            break;
                    }

                    break;

                // Local items
                case 2:
                    // A 1- or 2-byte usage is relative to the usage page
                    if (tag is 0x0 or 0x1 or 0x2 && size is 1 or 2)
                    {
                        value = (global.UsagePage << 16) | (value & 0xFFFF);
                    }

                    switch (tag)
                    {
                        case 0x0:
                            local.Usages.Add(value);
                            break;

                        case 0x1:
                            local.UsageMinimum = value;
                            break;

                        case 0x2:
                            local.UsageMaximum = value;
                            break;

                        case 0x7:
                            local.StringIndices.Add(value);
                            break;

                        case 0x8:
                            local.StringMinimum = value;
                            break;

                        case 0x9:
                            local.StringMaximum = value;
                            break;
                    }

                    break;
            }
        }

        return new HidReportDescriptor(collections, usesReportIds, reportCollections);
    }

    /// <summary>
    /// https://hid.spec.whatwg.org/#dfn-blocked-report: a report of a device is blocked when a rule of the HID blocklist matches
    /// the device and one of the top-level collections that contain the report (any collection when no collection contains it).
    /// </summary>
    public bool IsBlocked(HidHostDevice device, HidReportType type, byte reportId)
    {
        var containing = ReportCollections.TryGetValue((type, reportId), out var collections) ? collections : null;
        foreach (var rule in HidBlocklist.Rules)
        {
            if (rule.Vendor is { } vendor && vendor != device.VendorId)
                continue;

            if (rule.Product is { } product && product != device.ProductId)
                continue;

            if (rule.ReportId is { } ruleReportId && ruleReportId != reportId)
                continue;

            if (rule.ReportType is { } ruleType && ruleType != type)
                continue;

            if (rule.UsagePage is null && rule.Usage is null)
                return true;

            if (containing is not null && containing.Exists(collection => (rule.UsagePage is null || rule.UsagePage == collection.UsagePage) && (rule.Usage is null || rule.Usage == collection.Usage)))
                return true;
        }

        return false;
    }

    // https://hid.spec.whatwg.org/#dfn-create-a-hid-report-item
    private static HidReportItem CreateReportItem(uint value, LocalState local, GlobalState global, Func<uint, string?>? getString)
    {
        var isRange = local.UsageMinimum is { } minimum && local.UsageMaximum is { } maximum && minimum <= maximum;
        var strings = new List<string>();
        var stringIndices = new List<uint>(local.StringIndices);
        if (local.StringMinimum is { } stringMinimum && local.StringMaximum is { } stringMaximum)
        {
            for (var index = (ulong)stringMinimum; index <= stringMaximum && stringIndices.Count < MaxStringsPerItem; index++)
            {
                stringIndices.Add((uint)index);
            }
        }

        if (getString is not null)
        {
            foreach (var index in stringIndices.Take(MaxStringsPerItem))
            {
                if (getString(index) is { } text)
                {
                    strings.Add(text);
                }
            }
        }

        return new HidReportItem
        {
            IsConstant = (value & 0x001) != 0,
            IsArray = (value & 0x002) == 0,
            IsAbsolute = (value & 0x004) == 0,
            Wrap = (value & 0x008) != 0,
            IsLinear = (value & 0x010) == 0,
            HasPreferredState = (value & 0x020) == 0,
            HasNull = (value & 0x040) != 0,
            IsVolatile = (value & 0x080) != 0,
            IsBufferedBytes = (value & 0x100) != 0,
            Usages = [.. local.Usages],
            ReportSize = (ushort)global.ReportSize,
            ReportCount = (ushort)global.ReportCount,
            UnitExponent = global.UnitExponent,
            UnitSystem = global.UnitSystem,
            UnitFactorLengthExponent = global.UnitFactorLengthExponent,
            UnitFactorMassExponent = global.UnitFactorMassExponent,
            UnitFactorTimeExponent = global.UnitFactorTimeExponent,
            UnitFactorTemperatureExponent = global.UnitFactorTemperatureExponent,
            UnitFactorCurrentExponent = global.UnitFactorCurrentExponent,
            UnitFactorLuminousIntensityExponent = global.UnitFactorLuminousIntensityExponent,
            LogicalMinimum = global.LogicalMinimum,
            LogicalMaximum = global.LogicalMaximum,
            PhysicalMinimum = global.PhysicalMinimum,
            PhysicalMaximum = global.PhysicalMaximum,
            IsRange = isRange,
            UsageMinimum = isRange ? local.UsageMinimum : null,
            UsageMaximum = isRange ? local.UsageMaximum : null,
            Strings = strings,
        };
    }

    // A 4-bit signed two's complement integer
    private static sbyte Nibble(uint value, int index)
    {
        var nibble = (int)((value >> (4 * index)) & 0xF);
        return (sbyte)(nibble >= 8 ? nibble - 16 : nibble);
    }

    private static int SignExtend(uint value, int size) => size switch
    {
        1 => (sbyte)value,
        2 => (short)value,
        _ => (int)value,
    };

    private sealed class LocalState
    {
        public List<uint> Usages { get; } = [];

        public List<uint> StringIndices { get; } = [];

        public uint? UsageMinimum { get; set; }

        public uint? UsageMaximum { get; set; }

        public uint? StringMinimum { get; set; }

        public uint? StringMaximum { get; set; }
    }

    private sealed class GlobalState
    {
        public uint UsagePage { get; set; }

        public int LogicalMinimum { get; set; }

        public int LogicalMaximum { get; set; }

        public int PhysicalMinimum { get; set; }

        public int PhysicalMaximum { get; set; }

        public sbyte UnitExponent { get; set; }

        public HidUnitSystem UnitSystem { get; set; } = HidUnitSystem.None;

        public sbyte UnitFactorLengthExponent { get; set; }

        public sbyte UnitFactorMassExponent { get; set; }

        public sbyte UnitFactorTimeExponent { get; set; }

        public sbyte UnitFactorTemperatureExponent { get; set; }

        public sbyte UnitFactorCurrentExponent { get; set; }

        public sbyte UnitFactorLuminousIntensityExponent { get; set; }

        public uint ReportSize { get; set; }

        public uint ReportCount { get; set; }

        public GlobalState Clone() => (GlobalState)MemberwiseClone();
    }

    /// <summary>A collection being parsed: the lists of its dictionary.</summary>
    private sealed class MutableCollection
    {
        private readonly List<HidReportInfo> _inputReports = [];
        private readonly List<HidReportInfo> _outputReports = [];
        private readonly List<HidReportInfo> _featureReports = [];

        public MutableCollection(ushort usagePage, ushort usage, byte type)
        {
            Info = new HidCollectionInfo
            {
                UsagePage = usagePage,
                Usage = usage,
                Type = type,
                Children = Children,
                InputReports = _inputReports,
                OutputReports = _outputReports,
                FeatureReports = _featureReports,
            };
        }

        public HidCollectionInfo Info { get; }

        public List<HidCollectionInfo> Children { get; } = [];

        public void AddItem(HidReportType type, byte reportId, HidReportItem item)
        {
            var reports = type switch
            {
                HidReportType.Input => _inputReports,
                HidReportType.Output => _outputReports,
                _ => _featureReports,
            };
            var report = reports.Find(r => r.ReportId == reportId);
            if (report is null)
            {
                report = new HidReportInfo { ReportId = reportId, Items = new List<HidReportItem>() };
                reports.Add(report);
            }

            ((List<HidReportItem>)report.Items!).Add(item);
        }
    }
}

/// <summary>
/// The HID blocklist (https://hid.spec.whatwg.org/#dfn-hid-blocklist): copied from blocklist.txt of https://github.com/whatwg/hid
/// at commit 3c85c594f30f89b3d4a35042926b3326ae4ddfab.
/// </summary>
internal static class HidBlocklist
{
    public static IReadOnlyList<Rule> Rules { get; } =
    [
        // FIDO U2F: the devices are handled by the Web Authentication API
        new(UsagePage: 0xF1D0),

        // The mice, the keyboards and the keypads (input loggers), and the system controls
        new(UsagePage: 0x0001, Usage: 0x0002),
        new(UsagePage: 0x0001, Usage: 0x0006),
        new(UsagePage: 0x0001, Usage: 0x0007),
        new(UsagePage: 0x0001, Usage: 0x0080),

        // Proprietary functionality of Jabra, and OnlyKey
        new(Vendor: 0x0b0e, UsagePage: 0xff00, ReportId: 0x05, ReportType: HidReportType.Output),
        new(Vendor: 0x1d50, Product: 0x60fc),
    ];

    public sealed record Rule(ushort? Vendor = null, ushort? Product = null, ushort? UsagePage = null, ushort? Usage = null, byte? ReportId = null, HidReportType? ReportType = null);
}
