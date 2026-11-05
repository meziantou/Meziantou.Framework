using System.Collections.Frozen;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A policy-controlled feature (https://w3c.github.io/webappsec-permissions-policy/#policy-controlled-feature): an API or a
/// behavior that a permissions policy enables or disables in a document, identified by a token.
/// </summary>
internal sealed partial class PolicyControlledFeature
{
    private PolicyControlledFeature(string name, bool isAllowedForAllByDefault)
    {
        Name = name;
        IsAllowedForAllByDefault = isAllowedForAllByDefault;
    }

    /// <summary>The token that identifies the feature in the policy directives (e.g. <c>sync-xhr</c>).</summary>
    public string Name { get; }

    /// <summary>
    /// Whether the default allowlist of the feature is <c>*</c> (https://w3c.github.io/webappsec-permissions-policy/#default-allowlists):
    /// the feature is then allowed in the documents of the child navigables unless a policy says otherwise. Otherwise it is
    /// <c>'self'</c>: the feature is only allowed in the child navigables whose document is same origin with its parent.
    /// </summary>
    public bool IsAllowedForAllByDefault { get; }

    // The features that the library enforces (https://html.spec.whatwg.org/multipage/infrastructure.html#policy-controlled-features
    // and the specifications of the features)
    /// <summary>https://w3c.github.io/deviceorientation/#permissions-policy: its default allowlist is 'self'.</summary>
    public static PolicyControlledFeature Accelerometer { get; } = new("accelerometer", isAllowedForAllByDefault: false);

    /// <summary>https://w3c.github.io/ambient-light/#permissions-policy-integration: its default allowlist is 'self'.</summary>
    public static PolicyControlledFeature AmbientLightSensor { get; } = new("ambient-light-sensor", isAllowedForAllByDefault: false);

    public static PolicyControlledFeature Autoplay { get; } = new("autoplay", isAllowedForAllByDefault: false);

    /// <summary>https://w3c.github.io/battery/#permissions-policy-integration: its default allowlist is 'self'.</summary>
    public static PolicyControlledFeature Battery { get; } = new("battery", isAllowedForAllByDefault: false);

    public static PolicyControlledFeature ClipboardRead { get; } = new("clipboard-read", isAllowedForAllByDefault: false);

    public static PolicyControlledFeature ClipboardWrite { get; } = new("clipboard-write", isAllowedForAllByDefault: false);

    /// <summary>https://w3c.github.io/compute-pressure/#policy-control: its default allowlist is 'self'.</summary>
    public static PolicyControlledFeature ComputePressure { get; } = new("compute-pressure", isAllowedForAllByDefault: false);

    public static PolicyControlledFeature CrossOriginIsolated { get; } = new("cross-origin-isolated", isAllowedForAllByDefault: false);

    /// <summary>https://wicg.github.io/page-lifecycle/#feature-policies: its default allowlist is *.</summary>
    public static PolicyControlledFeature ExecutionWhileNotRendered { get; } = new("execution-while-not-rendered", isAllowedForAllByDefault: true);

    /// <summary>https://wicg.github.io/page-lifecycle/#feature-policies: its default allowlist is *.</summary>
    public static PolicyControlledFeature ExecutionWhileOutOfViewport { get; } = new("execution-while-out-of-viewport", isAllowedForAllByDefault: true);

    public static PolicyControlledFeature FocusWithoutUserActivation { get; } = new("focus-without-user-activation", isAllowedForAllByDefault: false);

    public static PolicyControlledFeature Fullscreen { get; } = new("fullscreen", isAllowedForAllByDefault: false);

    public static PolicyControlledFeature Geolocation { get; } = new("geolocation", isAllowedForAllByDefault: false);

    /// <summary>https://w3c.github.io/deviceorientation/#permissions-policy: its default allowlist is 'self'.</summary>
    public static PolicyControlledFeature Gyroscope { get; } = new("gyroscope", isAllowedForAllByDefault: false);

    /// <summary>https://w3c.github.io/deviceorientation/#permissions-policy: its default allowlist is 'self'.</summary>
    public static PolicyControlledFeature Magnetometer { get; } = new("magnetometer", isAllowedForAllByDefault: false);

    /// <summary>https://fetch.spec.whatwg.org/#policy-controlled-feature-deferred-fetch: its default allowlist is 'self'.</summary>
    public static PolicyControlledFeature DeferredFetch { get; } = new("deferred-fetch", isAllowedForAllByDefault: false);

    /// <summary>https://fetch.spec.whatwg.org/#policy-controlled-feature-deferred-fetch-minimal: its default allowlist is *.</summary>
    public static PolicyControlledFeature DeferredFetchMinimal { get; } = new("deferred-fetch-minimal", isAllowedForAllByDefault: true);

    /// <summary>https://wicg.github.io/idle-detection/#api-permissions-policy: its default allowlist is 'self'.</summary>
    public static PolicyControlledFeature IdleDetection { get; } = new("idle-detection", isAllowedForAllByDefault: false);

    /// <summary>https://wicg.github.io/keyboard-map/#permissions-policy: its default allowlist is 'self'.</summary>
    public static PolicyControlledFeature KeyboardMap { get; } = new("keyboard-map", isAllowedForAllByDefault: false);

    public static PolicyControlledFeature MediaPlaybackWhileNotVisible { get; } = new("media-playback-while-not-visible", isAllowedForAllByDefault: true);

    /// <summary>https://w3c.github.io/proximity/#permissions-policy-integration: its default allowlist is 'self'.</summary>
    public static PolicyControlledFeature ProximitySensor { get; } = new("proximity-sensor", isAllowedForAllByDefault: false);

    public static PolicyControlledFeature ScreenWakeLock { get; } = new("screen-wake-lock", isAllowedForAllByDefault: false);

    public static PolicyControlledFeature SpeakerSelection { get; } = new("speaker-selection", isAllowedForAllByDefault: false);

    public static PolicyControlledFeature SyncXhr { get; } = new("sync-xhr", isAllowedForAllByDefault: true);

    public static PolicyControlledFeature Unload { get; } = new("unload", isAllowedForAllByDefault: true);

    public static PolicyControlledFeature WebShare { get; } = new("web-share", isAllowedForAllByDefault: false);

    /// <summary>
    /// The supported features (https://w3c.github.io/webappsec-permissions-policy/#supported-features), in the order of
    /// <c>PermissionsPolicy.features()</c>: the features that the library enforces, and features whose API is not implemented
    /// but whose policy is computed, so that a page can observe it (<c>document.permissionsPolicy</c>,
    /// <c>iframe.permissionsPolicy</c>) and receive the reports of the potential violations of its iframes. The default
    /// allowlists are the ones of the specifications of the features.
    /// </summary>
    public static IReadOnlyList<PolicyControlledFeature> Supported { get; } =
    [
        Accelerometer,
        AmbientLightSensor,
        Autoplay,
        Battery,
        new("bluetooth", isAllowedForAllByDefault: false),
        new("camera", isAllowedForAllByDefault: false),
        .. ClientHintFeatures.All,
        ClipboardRead,
        ClipboardWrite,
        ComputePressure,
        CrossOriginIsolated,
        DeferredFetch,
        DeferredFetchMinimal,
        new("encrypted-media", isAllowedForAllByDefault: false),
        ExecutionWhileNotRendered,
        ExecutionWhileOutOfViewport,
        FocusWithoutUserActivation,
        Fullscreen,
        Geolocation,
        Gyroscope,
        IdleDetection,
        KeyboardMap,
        new("local-fonts", isAllowedForAllByDefault: false),
        Magnetometer,
        MediaPlaybackWhileNotVisible,
        new("microphone", isAllowedForAllByDefault: false),
        new("midi", isAllowedForAllByDefault: false),
        new("payment", isAllowedForAllByDefault: false),
        new("picture-in-picture", isAllowedForAllByDefault: true),
        new("private-state-token-issuance", isAllowedForAllByDefault: false),
        new("private-state-token-redemption", isAllowedForAllByDefault: false),
        ProximitySensor,
        ScreenWakeLock,
        new("serial", isAllowedForAllByDefault: false),
        SpeakerSelection,
        SyncXhr,
        Unload,
        new("usb", isAllowedForAllByDefault: false),
        WebShare,
        new("xr-spatial-tracking", isAllowedForAllByDefault: false),
    ];

    private static readonly FrozenDictionary<string, PolicyControlledFeature> ByName = Supported.ToFrozenDictionary(feature => feature.Name, StringComparer.Ordinal);

    /// <summary>The supported feature identified by a token, or null.</summary>
    public static PolicyControlledFeature? Get(string name) => ByName.GetValueOrDefault(name);

    public override string ToString() => Name;
}
