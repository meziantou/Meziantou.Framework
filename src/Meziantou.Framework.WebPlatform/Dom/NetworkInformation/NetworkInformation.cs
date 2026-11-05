using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The host hooks of the Network Information API (https://wicg.github.io/netinfo/, <see cref="BrowsingContextOptions.NetworkInformation"/>).</summary>
public sealed class NetworkInformationOptions
{
    /// <summary>
    /// Gets or sets the source of the information about the network connection of the device (<c>navigator.connection</c>),
    /// e.g. a <see cref="NetworkInformationProvider"/> whose <see cref="NetworkInformationProvider.Status"/> is set by the
    /// application, or a subclass that reads the network of the operating system. A provider can be shared by several
    /// browsing contexts. It also gives the values of the <c>RTT</c>, <c>Downlink</c> and <c>ECT</c> client hints that
    /// <see cref="ClientHintsOptions"/> does not set. The default is null: the connection is the one of
    /// <see cref="NetworkConnectionStatus.Default"/>, with the values of <see cref="ClientHintsOptions.RoundTripTime"/>,
    /// <see cref="ClientHintsOptions.Downlink"/> and <see cref="ClientHintsOptions.EffectiveConnectionType"/> when they are set.
    /// </summary>
    public NetworkInformationProvider? Provider { get; init; }
}

/// <summary>The type of the connection of the device (https://wicg.github.io/netinfo/#connectiontype-enum).</summary>
/// <seealso href="https://wicg.github.io/netinfo/#dom-connectiontype"/>
[IdlInterface("ConnectionType", "https://wicg.github.io/netinfo/#dom-connectiontype")]
public enum ConnectionType
{
    /// <summary>The type of the connection is unknown.</summary>
    /// <seealso href="https://wicg.github.io/netinfo/#dom-connectiontype-unknown"/>
    [IdlMember("unknown", "https://wicg.github.io/netinfo/#dom-connectiontype-unknown")]
    Unknown,

    /// <summary>A Bluetooth connection.</summary>
    /// <seealso href="https://wicg.github.io/netinfo/#dom-connectiontype-bluetooth"/>
    [IdlMember("bluetooth", "https://wicg.github.io/netinfo/#dom-connectiontype-bluetooth")]
    Bluetooth,

    /// <summary>A cellular connection (e.g. EDGE, HSPA, LTE).</summary>
    /// <seealso href="https://wicg.github.io/netinfo/#dom-connectiontype-cellular"/>
    [IdlMember("cellular", "https://wicg.github.io/netinfo/#dom-connectiontype-cellular")]
    Cellular,

    /// <summary>An Ethernet connection.</summary>
    /// <seealso href="https://wicg.github.io/netinfo/#dom-connectiontype-ethernet"/>
    [IdlMember("ethernet", "https://wicg.github.io/netinfo/#dom-connectiontype-ethernet")]
    Ethernet,

    /// <summary>Several types of connection.</summary>
    /// <seealso href="https://wicg.github.io/netinfo/#dom-connectiontype-mixed"/>
    [IdlMember("mixed", "https://wicg.github.io/netinfo/#dom-connectiontype-mixed")]
    Mixed,

    /// <summary>No connection: the network is unavailable.</summary>
    /// <seealso href="https://wicg.github.io/netinfo/#dom-connectiontype-none"/>
    [IdlMember("none", "https://wicg.github.io/netinfo/#dom-connectiontype-none")]
    None,

    /// <summary>A connection of another type.</summary>
    /// <seealso href="https://wicg.github.io/netinfo/#dom-connectiontype-other"/>
    [IdlMember("other", "https://wicg.github.io/netinfo/#dom-connectiontype-other")]
    Other,

    /// <summary>A Wi-Fi connection.</summary>
    /// <seealso href="https://wicg.github.io/netinfo/#dom-connectiontype-wifi"/>
    [IdlMember("wifi", "https://wicg.github.io/netinfo/#dom-connectiontype-wifi")]
    Wifi,

    /// <summary>A WiMAX connection.</summary>
    /// <seealso href="https://wicg.github.io/netinfo/#dom-connectiontype-wimax"/>
    [IdlMember("wimax", "https://wicg.github.io/netinfo/#dom-connectiontype-wimax")]
    Wimax,
}

/// <summary>The effective type of the connection, measured from its performance (https://wicg.github.io/netinfo/#effectiveconnectiontype-enum).</summary>
/// <seealso href="https://wicg.github.io/netinfo/#dom-effectiveconnectiontype"/>
[IdlInterface("EffectiveConnectionType", "https://wicg.github.io/netinfo/#dom-effectiveconnectiontype")]
public enum EffectiveConnectionType
{
    /// <summary>A connection as fast as a 4G cellular connection, or faster.</summary>
    /// <seealso href="https://wicg.github.io/netinfo/#dom-effectiveconnectiontype-4g"/>
    [IdlMember("4g", "https://wicg.github.io/netinfo/#dom-effectiveconnectiontype-4g")]
    Cellular4G,

    /// <summary>A connection as fast as a 3G cellular connection.</summary>
    /// <seealso href="https://wicg.github.io/netinfo/#dom-effectiveconnectiontype-3g"/>
    [IdlMember("3g", "https://wicg.github.io/netinfo/#dom-effectiveconnectiontype-3g")]
    Cellular3G,

    /// <summary>A connection as fast as a 2G cellular connection.</summary>
    /// <seealso href="https://wicg.github.io/netinfo/#dom-effectiveconnectiontype-2g"/>
    [IdlMember("2g", "https://wicg.github.io/netinfo/#dom-effectiveconnectiontype-2g")]
    Cellular2G,

    /// <summary>A connection as slow as a slow 2G cellular connection.</summary>
    /// <seealso href="https://wicg.github.io/netinfo/#dom-effectiveconnectiontype-slow-2g"/>
    [IdlMember("slow-2g", "https://wicg.github.io/netinfo/#dom-effectiveconnectiontype-slow-2g")]
    Slow2G,
}

/// <summary>
/// The state of the network connection of the device (https://wicg.github.io/netinfo/#networkinformation-interface),
/// returned by a <see cref="NetworkInformationProvider"/>.
/// </summary>
public sealed record NetworkConnectionStatus
{
    /// <summary>Gets the status reported when the user agent does not know the connection: an unknown type, and the performance of a 4G connection (10 Mbit/s, 50 ms).</summary>
    public static NetworkConnectionStatus Default { get; } = new();

    /// <summary>Gets the type of the connection. The default is <see cref="ConnectionType.Unknown"/>.</summary>
    public ConnectionType Type { get; init; } = ConnectionType.Unknown;

    /// <summary>Gets the effective type of the connection. The default is <see cref="EffectiveConnectionType.Cellular4G"/>.</summary>
    public EffectiveConnectionType EffectiveType { get; init; } = EffectiveConnectionType.Cellular4G;

    /// <summary>
    /// Gets the effective bandwidth of the connection, in megabits per second. The documents see it rounded to a multiple of
    /// 50 kilobits per second, 10 megabits per second at most (as in Chromium), to reduce fingerprinting. The default is 10.
    /// </summary>
    public double Downlink
    {
        get;
        init
        {
            if (!double.IsFinite(value) || value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The downlink must be a positive number");

            field = value;
        }
    } = 10;

    /// <summary>
    /// Gets the maximum bandwidth of the underlying connection technology, in megabits per second, or null when it is unknown
    /// (the documents see <c>Infinity</c>). The default is null.
    /// </summary>
    public double? DownlinkMax
    {
        get;
        init
        {
            if (value is { } downlinkMax && (double.IsNaN(downlinkMax) || downlinkMax < 0))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The maximum downlink must be a positive number");

            field = value;
        }
    }

    /// <summary>
    /// Gets the effective round-trip time of the connection. The documents see it rounded to a multiple of 50 milliseconds, 3
    /// seconds at most (as in Chromium). The default is 50 milliseconds.
    /// </summary>
    public TimeSpan RoundTripTime
    {
        get;
        init
        {
            if (value < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The round-trip time must be positive");

            field = value;
        }
    } = TimeSpan.FromMilliseconds(50);

    /// <summary>The downlink seen by the documents: a multiple of 50 kbit/s, 10 Mbit/s at most.</summary>
    internal static double RoundDownlink(double downlink) => Math.Min(10, Math.Round(Math.Max(0, downlink) * 20, MidpointRounding.AwayFromZero) / 20);

    /// <summary>The round-trip time seen by the documents, in milliseconds: a multiple of 50 ms, 3 s at most.</summary>
    internal static double RoundRoundTripTime(TimeSpan roundTripTime) => Math.Min(3000, Math.Round(Math.Max(0, roundTripTime.TotalMilliseconds) / 50, MidpointRounding.AwayFromZero) * 50);

    /// <summary>The serialized effective type (the <c>ECT</c> client hint).</summary>
    internal static string Serialize(EffectiveConnectionType type) => type switch
    {
        EffectiveConnectionType.Slow2G => "slow-2g",
        EffectiveConnectionType.Cellular2G => "2g",
        EffectiveConnectionType.Cellular3G => "3g",
        _ => "4g",
    };

    /// <summary>
    /// The status of the connection seen by the documents of a browsing context: the one of the provider, or the default
    /// status with the network client hints of the options.
    /// </summary>
    internal static NetworkConnectionStatus Get(BrowsingContextOptions options)
    {
        if (options.NetworkInformation?.Provider is { } provider)
        {
            try
            {
                return provider.GetStatus();
            }
#pragma warning disable CA1031 // An error of the provider reports the default status
            catch (Exception)
#pragma warning restore CA1031
            {
                return Default;
            }
        }

        var hints = options.GetClientHints();
        if (hints.RoundTripTime is null && hints.Downlink is null && hints.EffectiveConnectionType is null)
            return Default;

        return Default with
        {
            RoundTripTime = hints.RoundTripTime is { } roundTripTime && roundTripTime >= TimeSpan.Zero ? roundTripTime : Default.RoundTripTime,
            Downlink = hints.Downlink is { } downlink && double.IsFinite(downlink) && downlink >= 0 ? downlink : Default.Downlink,
            EffectiveType = hints.EffectiveConnectionType switch
            {
                "slow-2g" => EffectiveConnectionType.Slow2G,
                "2g" => EffectiveConnectionType.Cellular2G,
                "3g" => EffectiveConnectionType.Cellular3G,
                _ => EffectiveConnectionType.Cellular4G,
            },
        };
    }
}

/// <summary>
/// The source of the information about the network connection of the device (https://wicg.github.io/netinfo/), read by
/// <c>navigator.connection</c>, of the browsing contexts that share a <see cref="BrowsingContextOptions"/>
/// (<see cref="NetworkInformationOptions.Provider"/>). The default implementation returns <see cref="Status"/>, set by the
/// application (e.g. to simulate a slow network). Override <see cref="GetStatus"/> to read another source, and call
/// <see cref="OnStatusChanged"/> when the connection changes, to update the <c>NetworkInformation</c> objects of the
/// documents and of the workers.
/// </summary>
/// <remarks>A provider can be shared by several browsing contexts, and used from any thread.</remarks>
public class NetworkInformationProvider
{
    private readonly Lock _lock = new();
    private readonly List<WeakReference<NetworkInformation>> _objects = [];
    private NetworkConnectionStatus _status = NetworkConnectionStatus.Default;

    /// <summary>Raised when the status changes (<see cref="OnStatusChanged"/>).</summary>
    public event EventHandler? StatusChanged;

    /// <summary>
    /// Gets or sets the status returned by the default implementation of <see cref="GetStatus"/>
    /// (<see cref="NetworkConnectionStatus.Default"/> by default). Setting it updates the <c>NetworkInformation</c> objects,
    /// which fire their <c>change</c> event.
    /// </summary>
    public NetworkConnectionStatus Status
    {
        get
        {
            lock (_lock)
            {
                return _status;
            }
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (_lock)
            {
                _status = value;
            }

            OnStatusChanged();
        }
    }

    /// <summary>Returns the status of the connection. The default implementation returns <see cref="Status"/>. It is called on the thread of an event loop, and must not block it.</summary>
    public virtual NetworkConnectionStatus GetStatus() => Status;

    /// <summary>
    /// Raises <see cref="StatusChanged"/>, then reads the status again for the <c>NetworkInformation</c> objects, which fire
    /// their <c>change</c> event when a value they expose changed. It can be called from any thread.
    /// </summary>
    protected void OnStatusChanged()
    {
        StatusChanged?.Invoke(this, EventArgs.Empty);
        List<NetworkInformation> objects = [];
        lock (_lock)
        {
            foreach (var reference in _objects)
            {
                if (reference.TryGetTarget(out var networkInformation))
                {
                    objects.Add(networkInformation);
                }
            }
        }

        foreach (var networkInformation in objects)
        {
            networkInformation.OnStatusChanged();
        }
    }

    /// <summary>Registers a <c>NetworkInformation</c> object, to update it.</summary>
    internal void Register(NetworkInformation networkInformation)
    {
        lock (_lock)
        {
            _objects.RemoveAll(static reference => !reference.TryGetTarget(out _));
            _objects.Add(new WeakReference<NetworkInformation>(networkInformation));
        }
    }
}

/// <summary>
/// The network connection of the device (https://wicg.github.io/netinfo/#networkinformation-interface), returned by
/// <see cref="Navigator.Connection"/> and <see cref="WorkerNavigator.Connection"/>: it is read from the
/// <see cref="NetworkInformationProvider"/> of the browsing context (<see cref="NetworkInformationOptions.Provider"/>), and
/// fires the <c>change</c> event when the connection changes.
/// </summary>
/// <seealso href="https://wicg.github.io/netinfo/#dom-networkinformation"/>
[IdlInterface("NetworkInformation", "https://wicg.github.io/netinfo/#dom-networkinformation")]
public sealed class NetworkInformation : EventTarget
{
    private readonly GlobalScope _global;

    internal NetworkInformation(GlobalScope global)
    {
        _global = global;
        Update(NetworkConnectionStatus.Get(global.Options));
        global.Options.NetworkInformation?.Provider?.Register(this);
    }

    internal override GlobalScope? RelevantGlobal => _global;

    /// <summary>Gets the type of the connection.</summary>
    /// <seealso href="https://wicg.github.io/netinfo/#dom-networkinformation-type"/>
    [IdlMember("type", "https://wicg.github.io/netinfo/#dom-networkinformation-type")]
    public ConnectionType Type { get; private set; }

    /// <summary>Gets the effective type of the connection.</summary>
    /// <seealso href="https://wicg.github.io/netinfo/#dom-networkinformation-effectivetype"/>
    [IdlMember("effectiveType", "https://wicg.github.io/netinfo/#dom-networkinformation-effectivetype")]
    public EffectiveConnectionType EffectiveType { get; private set; }

    /// <summary>Gets the maximum bandwidth of the connection technology, in megabits per second, or positive infinity when it is unknown.</summary>
    /// <seealso href="https://wicg.github.io/netinfo/#dom-networkinformation-downlinkmax"/>
    [IdlMember("downlinkMax", "https://wicg.github.io/netinfo/#dom-networkinformation-downlinkmax")]
    public double DownlinkMax { get; private set; }

    /// <summary>Gets the effective bandwidth of the connection, in megabits per second, rounded to a multiple of 50 kilobits per second.</summary>
    /// <seealso href="https://wicg.github.io/netinfo/#dom-networkinformation-downlink"/>
    [IdlMember("downlink", "https://wicg.github.io/netinfo/#dom-networkinformation-downlink")]
    public double Downlink { get; private set; }

    /// <summary>Gets the effective round-trip time of the connection, in milliseconds, rounded to a multiple of 50 milliseconds.</summary>
    /// <seealso href="https://wicg.github.io/netinfo/#dom-networkinformation-rtt"/>
    [IdlMember("rtt", "https://wicg.github.io/netinfo/#dom-networkinformation-rtt")]
    public ulong Rtt { get; private set; }

    /// <summary>Gets whether the user prefers a reduced data usage (https://wicg.github.io/savedata/): <see cref="ClientHintsOptions.SaveData"/>.</summary>
    /// <seealso href="https://wicg.github.io/savedata/#dom-networkinformationsavedata-savedata"/>
    [IdlMember("saveData", "https://wicg.github.io/savedata/#dom-networkinformationsavedata-savedata")]
    public bool SaveData => _global.Options.GetClientHints().SaveData;

    /// <summary>Gets or sets the event handler of the <c>change</c> event.</summary>
    /// <seealso href="https://wicg.github.io/netinfo/#dom-networkinformation-onchange"/>
    [IdlMember("onchange", "https://wicg.github.io/netinfo/#dom-networkinformation-onchange")]
    public Action<Event>? OnChange
    {
        get => GetEventHandler("change");
        set => SetEventHandler("change", value);
    }

    /// <summary>
    /// The status of the provider changed (https://wicg.github.io/netinfo/#handling-changes-to-the-underlying-connection):
    /// the attributes are updated by a task, which fires the <c>change</c> event when one of them changed. Called from any thread.
    /// </summary>
    internal void OnStatusChanged()
    {
        _global.QueueGlobalTask(() =>
        {
            if (Update(NetworkConnectionStatus.Get(_global.Options)))
            {
                FireEvent(new Event("change"));
            }
        });
    }

    private bool Update(NetworkConnectionStatus status)
    {
        var downlink = NetworkConnectionStatus.RoundDownlink(status.Downlink);
        var downlinkMax = status.DownlinkMax ?? double.PositiveInfinity;
        var rtt = (ulong)NetworkConnectionStatus.RoundRoundTripTime(status.RoundTripTime);
        if (Type == status.Type && EffectiveType == status.EffectiveType && Downlink == downlink && DownlinkMax == downlinkMax && Rtt == rtt)
            return false;

        Type = status.Type;
        EffectiveType = status.EffectiveType;
        Downlink = downlink;
        DownlinkMax = downlinkMax;
        Rtt = rtt;
        return true;
    }
}

// The Network Information API (https://wicg.github.io/netinfo/), the Device Memory API (https://www.w3.org/TR/device-memory/)
// and Global Privacy Control (https://w3c.github.io/gpc/)
public sealed partial class Navigator
{
    private NetworkInformation? _connection;

    /// <summary>Gets the network connection of the device (https://wicg.github.io/netinfo/#navigatornetworkinformation-interface), read from <see cref="NetworkInformationOptions.Provider"/>.</summary>
    /// <seealso href="https://wicg.github.io/netinfo/#dom-navigatornetworkinformation-connection"/>
    [IdlMember("connection", "https://wicg.github.io/netinfo/#dom-navigatornetworkinformation-connection")]
    public NetworkInformation Connection => _connection ??= new NetworkInformation(_window);

    /// <summary>
    /// Gets the approximate memory of the device, in GiB (https://www.w3.org/TR/device-memory/#computing-device-memory-value):
    /// <see cref="ClientHintsOptions.DeviceMemory"/> rounded down to a power of two between 0.25 and 8, or 8 when it is not
    /// set. The scripts only see it in a secure context.
    /// </summary>
    [NotInIdl]
    public double DeviceMemory => ClientHints.GetReportedDeviceMemory(_window.Options);

    /// <summary>Gets whether the user does not consent to the sale or sharing of their data (https://w3c.github.io/gpc/#dom-globalprivacycontrol-globalprivacycontrol): see <see cref="GlobalPrivacyControlOptions"/>.</summary>
    /// <seealso href="https://w3c.github.io/gpc/#dom-globalprivacycontrol-globalprivacycontrol"/>
    [IdlMember("globalPrivacyControl", "https://w3c.github.io/gpc/#dom-globalprivacycontrol-globalprivacycontrol")]
    public bool GlobalPrivacyControl => GlobalPrivacyControlOptions.IsEnabledFor(CallbackContext.For(_window));
}

// The Network Information API (https://wicg.github.io/netinfo/), the Device Memory API (https://www.w3.org/TR/device-memory/)
// and Global Privacy Control (https://w3c.github.io/gpc/)
public sealed partial class WorkerNavigator
{
    private NetworkInformation? _connection;

    /// <summary>Gets the network connection of the device (https://wicg.github.io/netinfo/#navigatornetworkinformation-interface), read from <see cref="NetworkInformationOptions.Provider"/>.</summary>
    /// <seealso href="https://wicg.github.io/netinfo/#dom-navigatornetworkinformation-connection"/>
    [IdlMember("connection", "https://wicg.github.io/netinfo/#dom-navigatornetworkinformation-connection")]
    public NetworkInformation Connection => _connection ??= new NetworkInformation(_global);

    /// <summary>Gets the approximate memory of the device, in GiB (see <see cref="Navigator.DeviceMemory"/>).</summary>
    [NotInIdl]
    public double DeviceMemory => ClientHints.GetReportedDeviceMemory(_global.Options);

    /// <summary>Gets whether the user does not consent to the sale or sharing of their data (https://w3c.github.io/gpc/#dom-globalprivacycontrol-globalprivacycontrol): see <see cref="GlobalPrivacyControlOptions"/>.</summary>
    /// <seealso href="https://w3c.github.io/gpc/#dom-globalprivacycontrol-globalprivacycontrol"/>
    [IdlMember("globalPrivacyControl", "https://w3c.github.io/gpc/#dom-globalprivacycontrol-globalprivacycontrol")]
    public bool GlobalPrivacyControl => GlobalPrivacyControlOptions.IsEnabledFor(CallbackContext.For(_global));
}

/// <summary>
/// The preference of the user expressed by Global Privacy Control (https://w3c.github.io/gpc/, see
/// <see cref="BrowsingContextOptions.GlobalPrivacyControl"/>): when it is enabled, <c>navigator.globalPrivacyControl</c> is
/// true and the requests have the <c>Sec-GPC: 1</c> header.
/// </summary>
public sealed class GlobalPrivacyControlOptions
{
    /// <summary>Gets or sets whether the user does not consent to the sale or sharing of their data. The default is false.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Gets or sets a callback that returns the preference of the user, read at each request and each read of
    /// <c>navigator.globalPrivacyControl</c> (e.g. a setting that the user can change). When set, it replaces
    /// <see cref="Enabled"/>. The default is null.
    /// </summary>
    public Func<CallbackContext, bool>? IsEnabled { get; init; }

    /// <summary>Whether Global Privacy Control is enabled for the documents or the workers of a callback context.</summary>
    internal static bool IsEnabledFor(CallbackContext context)
    {
        if (context.Options.GlobalPrivacyControl is not { } options)
            return false;

        return options.IsEnabled is { } isEnabled ? isEnabled(context) : options.Enabled;
    }

    /// <summary>
    /// https://w3c.github.io/gpc/#the-sec-gpc-header-field-for-http-requests: the <c>Sec-GPC: 1</c> header of a request,
    /// when Global Privacy Control is enabled for its client or its navigable.
    /// </summary>
    internal static void AppendToRequest(FetchRequest request, List<KeyValuePair<string, string>> headers)
    {
        CallbackContext context;
        if (request.Mode == RequestMode.Navigate && request.Navigable is { } navigable)
        {
            context = CallbackContext.For(navigable);
        }
        else if (request.Client is { WorkerGlobal: { } worker })
        {
            context = CallbackContext.For(worker);
        }
        else if (request.Client?.RelevantWindow is { } window)
        {
            context = CallbackContext.For(window);
        }
        else if ((request.Navigable ?? request.KeepaliveBrowsingContext) is { } browsingContext)
        {
            context = CallbackContext.For(browsingContext);
        }
        else
        {
            return;
        }

        if (context.Options.GlobalPrivacyControl is null || FetchHeaders.Contains(headers, "Sec-GPC"))
            return;

        bool enabled;
        try
        {
            enabled = IsEnabledFor(context);
        }
#pragma warning disable CA1031 // An error of the host callback sends no header
        catch (Exception)
#pragma warning restore CA1031
        {
            return;
        }

        if (enabled)
        {
            headers.Add(new("Sec-GPC", "1"));
        }
    }
}
