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
    public static PolicyControlledFeature Autoplay { get; } = new("autoplay", isAllowedForAllByDefault: false);

    public static PolicyControlledFeature ClipboardRead { get; } = new("clipboard-read", isAllowedForAllByDefault: false);

    public static PolicyControlledFeature ClipboardWrite { get; } = new("clipboard-write", isAllowedForAllByDefault: false);

    public static PolicyControlledFeature CrossOriginIsolated { get; } = new("cross-origin-isolated", isAllowedForAllByDefault: false);

    public static PolicyControlledFeature FocusWithoutUserActivation { get; } = new("focus-without-user-activation", isAllowedForAllByDefault: false);

    public static PolicyControlledFeature Fullscreen { get; } = new("fullscreen", isAllowedForAllByDefault: false);

    /// <summary>https://fetch.spec.whatwg.org/#policy-controlled-feature-deferred-fetch: its default allowlist is 'self'.</summary>
    public static PolicyControlledFeature DeferredFetch { get; } = new("deferred-fetch", isAllowedForAllByDefault: false);

    /// <summary>https://fetch.spec.whatwg.org/#policy-controlled-feature-deferred-fetch-minimal: its default allowlist is *.</summary>
    public static PolicyControlledFeature DeferredFetchMinimal { get; } = new("deferred-fetch-minimal", isAllowedForAllByDefault: true);

    public static PolicyControlledFeature MediaPlaybackWhileNotVisible { get; } = new("media-playback-while-not-visible", isAllowedForAllByDefault: true);

    public static PolicyControlledFeature SyncXhr { get; } = new("sync-xhr", isAllowedForAllByDefault: true);

    public static PolicyControlledFeature Unload { get; } = new("unload", isAllowedForAllByDefault: true);

    /// <summary>
    /// The supported features (https://w3c.github.io/webappsec-permissions-policy/#supported-features), in the order of
    /// <c>PermissionsPolicy.features()</c>: the features that the library enforces, and features whose API is not implemented
    /// but whose policy is computed, so that a page can observe it (<c>document.permissionsPolicy</c>,
    /// <c>iframe.permissionsPolicy</c>) and receive the reports of the potential violations of its iframes. The default
    /// allowlists are the ones of the specifications of the features.
    /// </summary>
    public static IReadOnlyList<PolicyControlledFeature> Supported { get; } =
    [
        new("accelerometer", isAllowedForAllByDefault: false),
        new("ambient-light-sensor", isAllowedForAllByDefault: false),
        Autoplay,
        new("bluetooth", isAllowedForAllByDefault: false),
        new("camera", isAllowedForAllByDefault: false),
        .. ClientHintFeatures.All,
        ClipboardRead,
        ClipboardWrite,
        CrossOriginIsolated,
        DeferredFetch,
        DeferredFetchMinimal,
        new("encrypted-media", isAllowedForAllByDefault: false),
        FocusWithoutUserActivation,
        Fullscreen,
        new("geolocation", isAllowedForAllByDefault: false),
        new("gyroscope", isAllowedForAllByDefault: false),
        new("local-fonts", isAllowedForAllByDefault: false),
        new("magnetometer", isAllowedForAllByDefault: false),
        MediaPlaybackWhileNotVisible,
        new("microphone", isAllowedForAllByDefault: false),
        new("midi", isAllowedForAllByDefault: false),
        new("payment", isAllowedForAllByDefault: false),
        new("picture-in-picture", isAllowedForAllByDefault: true),
        new("private-state-token-issuance", isAllowedForAllByDefault: false),
        new("private-state-token-redemption", isAllowedForAllByDefault: false),
        new("screen-wake-lock", isAllowedForAllByDefault: false),
        new("serial", isAllowedForAllByDefault: false),
        new("speaker-selection", isAllowedForAllByDefault: false),
        SyncXhr,
        Unload,
        new("usb", isAllowedForAllByDefault: false),
        new("xr-spatial-tracking", isAllowedForAllByDefault: false),
    ];

    private static readonly FrozenDictionary<string, PolicyControlledFeature> ByName = Supported.ToFrozenDictionary(feature => feature.Name, StringComparer.Ordinal);

    /// <summary>The supported feature identified by a token, or null.</summary>
    public static PolicyControlledFeature? Get(string name) => ByName.GetValueOrDefault(name);

    public override string ToString() => Name;
}
