namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The state of a permission (https://w3c.github.io/permissions/#dom-permissionstate).</summary>
/// <seealso href="https://w3c.github.io/permissions/#dom-permissionstate"/>
[IdlInterface("PermissionState", "https://w3c.github.io/permissions/#dom-permissionstate")]
public enum PermissionState
{
    /// <summary>The user, or the user agent on the user's behalf, gave the permission.</summary>
    /// <seealso href="https://w3c.github.io/permissions/#dom-permissionstate-granted"/>
    [IdlMember("granted", "https://w3c.github.io/permissions/#dom-permissionstate-granted")]
    Granted,

    /// <summary>The permission is denied.</summary>
    /// <seealso href="https://w3c.github.io/permissions/#dom-permissionstate-denied"/>
    [IdlMember("denied", "https://w3c.github.io/permissions/#dom-permissionstate-denied")]
    Denied,

    /// <summary>Using the feature asks the user for the permission.</summary>
    /// <seealso href="https://w3c.github.io/permissions/#dom-permissionstate-prompt"/>
    [IdlMember("prompt", "https://w3c.github.io/permissions/#dom-permissionstate-prompt")]
    Prompt,
}

/// <summary>
/// The descriptor of a permission (https://w3c.github.io/permissions/#dom-permissiondescriptor): the name of a powerful
/// feature (e.g. <c>geolocation</c>, <c>notifications</c>, <c>camera</c>).
/// </summary>
/// <seealso href="https://w3c.github.io/permissions/#dom-permissiondescriptor"/>
[IdlInterface("PermissionDescriptor", "https://w3c.github.io/permissions/#dom-permissiondescriptor")]
public sealed class PermissionDescriptor
{
    /// <summary>
    /// The names of the powerful features that <see cref="Permissions.Query"/> supports: the features of the registry of the
    /// Permissions specification (https://w3c.github.io/permissions-registry/), and the ones that the browsers support.
    /// </summary>
    private static readonly HashSet<string> SupportedNames = new(StringComparer.Ordinal)
    {
        "accelerometer", "ambient-light-sensor", "background-fetch", "background-sync", "bluetooth", "camera",
        "captured-surface-control", "clipboard-read", "clipboard-write", "display-capture", "fullscreen", "geolocation",
        "gyroscope", "idle-detection", "keyboard-lock", "local-fonts", "local-network", "local-network-access",
        "loopback-network", "magnetometer", "microphone", "midi", "nfc", "notifications", "payment-handler",
        "periodic-background-sync", "persistent-storage", "pointer-lock", "push", "screen-wake-lock", "speaker-selection",
        "storage-access", "system-wake-lock", "top-level-storage-access", "window-management", "xr-spatial-tracking",
    };

    /// <summary>Gets the name of the powerful feature.</summary>
    /// <seealso href="https://w3c.github.io/permissions/#dom-permissiondescriptor-name"/>
    [IdlMember("name", "https://w3c.github.io/permissions/#dom-permissiondescriptor-name")]
    public required string Name { get; init; }

    /// <summary>
    /// Whether the options of the typed descriptor are not supported (e.g. <c>allowWithoutGesture: false</c> of the
    /// <c>fullscreen</c> permission, the only value the browsers support being <c>true</c>).
    /// </summary>
    internal bool HasUnsupportedOptions { get; init; }

    /// <summary>Gets whether the name is the name of a supported powerful feature.</summary>
    internal bool IsSupported => IsSupportedName(Name) && !HasUnsupportedOptions;

    internal static bool IsSupportedName(string name) => SupportedNames.Contains(name);
}

/// <summary>
/// The Permissions API of a window or a worker (https://w3c.github.io/permissions/#permissions-interface,
/// <c>navigator.permissions</c>): the states of the permissions of the powerful features, read from the
/// <see cref="PermissionStore"/> of the browsing context (<see cref="BrowsingContextOptions.Permissions"/>).
/// </summary>
/// <seealso href="https://w3c.github.io/permissions/#permissions-interface"/>
[IdlInterface("Permissions", "https://w3c.github.io/permissions/#permissions-interface")]
public sealed class Permissions
{
    private readonly GlobalScope _global;

    internal Permissions(GlobalScope global) => _global = global;

    /// <summary>
    /// https://w3c.github.io/permissions/#dfn-permission-state: denied when the document is not allowed to use the feature
    /// by its permissions policy, or in a context that is not secure (the powerful features need a secure context), else the
    /// state of the permission store for the top-level origin.
    /// </summary>
    internal static PermissionState GetPermissionState(GlobalScope global, string name)
    {
        if (!global.IsSecureContext)
            return PermissionState.Denied;

        if (global is Window window && PolicyControlledFeature.Get(name) is { } feature && !PermissionsPolicyAlgorithms.IsAllowedToUse(window.Document, feature))
            return PermissionState.Denied;

        return global.Options.EffectivePermissions.GetState(name, global.EnvironmentDocument.TopLevelOrigin.Serialize());
    }

    /// <summary>
    /// Returns the state of a permission (https://w3c.github.io/permissions/#dom-permissions-query). The task fails with an
    /// <c>InvalidStateError</c> <see cref="DomException"/> when the document is not fully active, and with a
    /// <see cref="TypeErrorException"/> when the feature is not supported.
    /// </summary>
    /// <param name="permissionDescriptor">The descriptor of the permission.</param>
    /// <seealso href="https://w3c.github.io/permissions/#dom-permissions-query"/>
    [IdlMember("query", "https://w3c.github.io/permissions/#dom-permissions-query")]
    public Task<PermissionStatus> Query(PermissionDescriptor permissionDescriptor)
    {
        ArgumentNullException.ThrowIfNull(permissionDescriptor);
        if (_global is Window && !_global.CanRunScript)
            return Task.FromException<PermissionStatus>(DomException.InvalidState("The document is not fully active"));

        if (!permissionDescriptor.IsSupported)
            return Task.FromException<PermissionStatus>(new TypeErrorException($"The permission '{permissionDescriptor.Name}' is not supported"));

        // The status is created and its state is read by a task of the permissions task source
        var result = new TaskCompletionSource<PermissionStatus>();
        _global.QueueGlobalTask(() =>
        {
            var status = new PermissionStatus(_global, permissionDescriptor.Name);
            _global.Options.EffectivePermissions.Register(status);
            result.TrySetResult(status);
        });
        return result.Task;
    }
}

/// <summary>
/// The state of a permission (https://w3c.github.io/permissions/#permissionstatus-interface), returned by
/// <see cref="Permissions.Query"/>. Its <c>change</c> event is fired when the state changes (e.g. when
/// <see cref="PermissionStore.SetState(string, PermissionState)"/> is called), while its document is fully active.
/// </summary>
/// <seealso href="https://w3c.github.io/permissions/#permissionstatus-interface"/>
[IdlInterface("PermissionStatus", "https://w3c.github.io/permissions/#permissionstatus-interface")]
public sealed class PermissionStatus : EventTarget
{
    private readonly GlobalScope _global;

    internal PermissionStatus(GlobalScope global, string name)
    {
        _global = global;
        Name = name;
        State = ComputeState();
    }

    internal override GlobalScope? RelevantGlobal => _global;

    /// <summary>Gets the state of the permission.</summary>
    /// <seealso href="https://w3c.github.io/permissions/#dom-permissionstatus-state"/>
    [IdlMember("state", "https://w3c.github.io/permissions/#dom-permissionstatus-state")]
    public PermissionState State { get; private set; }

    /// <summary>Gets the name of the powerful feature.</summary>
    /// <seealso href="https://w3c.github.io/permissions/#dom-permissionstatus-name"/>
    [IdlMember("name", "https://w3c.github.io/permissions/#dom-permissionstatus-name")]
    public string Name { get; }

    /// <summary>Gets or sets the event handler of the <c>change</c> event.</summary>
    /// <seealso href="https://w3c.github.io/permissions/#dom-permissionstatus-onchange"/>
    [IdlMember("onchange", "https://w3c.github.io/permissions/#dom-permissionstatus-onchange")]
    public Action<Event>? OnChange
    {
        get => GetEventHandler("change");
        set => SetEventHandler("change", value);
    }

    private PermissionState ComputeState() => Permissions.GetPermissionState(_global, Name);

    /// <summary>
    /// https://w3c.github.io/permissions/#dfn-permissionstatus-update-steps: the state of the permission may have changed.
    /// Called from any thread: the state is read by a task of the event loop of the status, which fires <c>change</c> when it
    /// changed and the document is fully active.
    /// </summary>
    internal void OnPermissionChanged()
    {
        _global.QueueGlobalTask(() =>
        {
            var state = ComputeState();
            if (state == State)
                return;

            State = state;
            FireEvent(new Event("change"));
        });
    }

    /// <summary>A status with <c>change</c> listeners lives as long as its global object (https://w3c.github.io/permissions/#garbage-collection).</summary>
    private protected override void OnEventListenerAdded(EventListenerEntry listener)
    {
        base.OnEventListenerAdded(listener);
        if (listener.Type == "change")
        {
            _global.KeepPermissionStatusAlive(this);
        }
    }
}
