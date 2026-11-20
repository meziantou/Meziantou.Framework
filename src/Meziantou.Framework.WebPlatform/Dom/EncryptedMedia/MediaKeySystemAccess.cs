using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The access to a key system with a supported configuration (https://w3c.github.io/encrypted-media/#mediakeysystemaccess-interface),
/// returned by <see cref="Navigator.RequestMediaKeySystemAccess"/>.
/// </summary>
/// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysystemaccess"/>
[IdlInterface("MediaKeySystemAccess", "https://w3c.github.io/encrypted-media/#dom-mediakeysystemaccess")]
public sealed class MediaKeySystemAccess
{
    private readonly Window _window;
    private readonly MediaKeySystemConfiguration _configuration;

    internal MediaKeySystemAccess(Window window, string keySystem, MediaKeySystemConfiguration configuration)
    {
        _window = window;
        KeySystem = keySystem;
        _configuration = configuration;
    }

    /// <summary>Gets the key system (e.g. <c>org.w3.clearkey</c>).</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysystemaccess-keysystem"/>
    [IdlMember("keySystem", "https://w3c.github.io/encrypted-media/#dom-mediakeysystemaccess-keysystem")]
    public string KeySystem { get; }

    /// <summary>Returns the supported configuration selected by <c>requestMediaKeySystemAccess()</c>: a new object at each call.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysystemaccess-getconfiguration"/>
    [IdlMember("getConfiguration", "https://w3c.github.io/encrypted-media/#dom-mediakeysystemaccess-getconfiguration")]
    public MediaKeySystemConfiguration GetConfiguration() => KeySystemConfiguration.Copy(_configuration);

    /// <summary>Creates a <see cref="MediaKeys"/> object (a CDM instance) for the key system and the configuration (https://w3c.github.io/encrypted-media/#dom-mediakeysystemaccess-createmediakeys).</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysystemaccess-createmediakeys"/>
    [IdlMember("createMediaKeys", "https://w3c.github.io/encrypted-media/#dom-mediakeysystemaccess-createmediakeys")]
    public Task<MediaKeys> CreateMediaKeys()
    {
        var promise = new TaskCompletionSource<MediaKeys>();
        _window.QueueGlobalTask(() =>
        {
            var persistentStateAllowed = _configuration.PersistentState == MediaKeysRequirement.Required;
            var sessionTypes = new List<MediaKeySessionType>();
            foreach (var type in _configuration.SessionTypes ?? ["temporary"])
            {
                if (KeySystemConfiguration.ParseSessionType(type) is { } sessionType && !sessionTypes.Contains(sessionType))
                {
                    sessionTypes.Add(sessionType);
                }
            }

            promise.TrySetResult(new MediaKeys(_window, KeySystem, persistentStateAllowed, sessionTypes));
        });

        return promise.Task;
    }
}

/// <summary>
/// The configuration algorithms of <c>requestMediaKeySystemAccess()</c> (https://w3c.github.io/encrypted-media/#get-supported-configuration)
/// for the key systems that the library implements: Clear Key (https://w3c.github.io/encrypted-media/#clear-key).
/// </summary>
internal static class KeySystemConfiguration
{
    public static bool IsSupportedKeySystem(string keySystem) => string.Equals(keySystem, ClearKey.KeySystem, StringComparison.Ordinal);

    public static MediaKeySessionType? ParseSessionType(string value) => value switch
    {
        "temporary" => MediaKeySessionType.Temporary,
        "persistent-license" => MediaKeySessionType.PersistentLicense,
        _ => null,
    };

    public static MediaKeySystemConfiguration Copy(MediaKeySystemConfiguration configuration) => new()
    {
        Label = configuration.Label,
        InitDataTypes = [.. configuration.InitDataTypes],
        AudioCapabilities = [.. configuration.AudioCapabilities.Select(Copy)],
        VideoCapabilities = [.. configuration.VideoCapabilities.Select(Copy)],
        DistinctiveIdentifier = configuration.DistinctiveIdentifier,
        PersistentState = configuration.PersistentState,
        SessionTypes = configuration.SessionTypes is { } sessionTypes ? [.. sessionTypes] : null,
    };

    private static MediaKeySystemMediaCapability Copy(MediaKeySystemMediaCapability capability) => new()
    {
        ContentType = capability.ContentType,
        EncryptionScheme = capability.EncryptionScheme,
        Robustness = capability.Robustness,
    };

    /// <summary>
    /// https://w3c.github.io/encrypted-media/#get-supported-configuration-and-consent for Clear Key: the accumulated
    /// configuration, or null (NotSupported). Clear Key needs no consent: it uses no Distinctive Identifier.
    /// </summary>
    /// <param name="candidate">The candidate configuration.</param>
    /// <param name="backend">The media backend that plays the media types, or null.</param>
    /// <param name="canPersistState">Whether the origin can persist state (it is not opaque).</param>
    public static MediaKeySystemConfiguration? GetSupportedConfiguration(MediaKeySystemConfiguration candidate, MediaBackend? backend, bool canPersistState)
    {
        // The Initialization Data Types: the empty string is never supported
        IReadOnlyList<string> initDataTypes = [];
        if (candidate.InitDataTypes.Count > 0)
        {
            var supported = candidate.InitDataTypes.Where(ClearKey.IsSupportedInitDataType).ToList();
            if (supported.Count == 0)
                return null;

            initDataTypes = supported;
        }

        // Clear Key does not support Distinctive Identifiers (https://w3c.github.io/encrypted-media/#clear-key-capabilities)
        if (candidate.DistinctiveIdentifier == MediaKeysRequirement.Required)
            return null;

        var persistentState = candidate.PersistentState;
        if (persistentState == MediaKeysRequirement.Required && !canPersistState)
            return null;

        var sessionTypes = candidate.SessionTypes ?? ["temporary"];
        foreach (var value in sessionTypes)
        {
            if (ParseSessionType(value) is not { } sessionType)
                return null;

            if (sessionType == MediaKeySessionType.PersistentLicense)
            {
                if (persistentState == MediaKeysRequirement.NotAllowed || !canPersistState)
                    return null;

                if (persistentState == MediaKeysRequirement.Optional)
                {
                    persistentState = MediaKeysRequirement.Required;
                }
            }
        }

        if (candidate.VideoCapabilities.Count == 0 && candidate.AudioCapabilities.Count == 0)
            return null;

        IReadOnlyList<MediaKeySystemMediaCapability> videoCapabilities = [];
        if (candidate.VideoCapabilities.Count > 0)
        {
            if (GetSupportedCapabilities(candidate.VideoCapabilities, isVideo: true, backend) is not { } supported)
                return null;

            videoCapabilities = supported;
        }

        IReadOnlyList<MediaKeySystemMediaCapability> audioCapabilities = [];
        if (candidate.AudioCapabilities.Count > 0)
        {
            if (GetSupportedCapabilities(candidate.AudioCapabilities, isVideo: false, backend) is not { } supported)
                return null;

            audioCapabilities = supported;
        }

        // The optional requirements are resolved: Clear Key never requires a Distinctive Identifier, nor persisting state
        // unless a persistent session type is requested
        return new MediaKeySystemConfiguration
        {
            Label = candidate.Label,
            InitDataTypes = initDataTypes,
            AudioCapabilities = audioCapabilities,
            VideoCapabilities = videoCapabilities,
            DistinctiveIdentifier = MediaKeysRequirement.NotAllowed,
            PersistentState = persistentState == MediaKeysRequirement.Optional ? MediaKeysRequirement.NotAllowed : persistentState,
            SessionTypes = [.. sessionTypes],
        };
    }

    /// <summary>https://w3c.github.io/encrypted-media/#get-supported-capabilities-for-audio-video-type: the supported capabilities (unmodified), or null.</summary>
    private static List<MediaKeySystemMediaCapability>? GetSupportedCapabilities(IReadOnlyList<MediaKeySystemMediaCapability> requested, bool isVideo, MediaBackend? backend)
    {
        var supported = new List<MediaKeySystemMediaCapability>();
        foreach (var capability in requested)
        {
            if (capability.ContentType.Length == 0)
                return null;

            if (IsSupportedCapability(capability, isVideo, backend))
            {
                supported.Add(capability);
            }
        }

        return supported.Count == 0 ? null : supported;
    }

    private static bool IsSupportedCapability(MediaKeySystemMediaCapability capability, bool isVideo, MediaBackend? backend)
    {
        if (backend is null || Infra.MimeType.Parse(capability.ContentType) is not { } mimeType)
            return false;

        // The containers of the Common Encryption (MP4) and of the WebM encryption
        var isMp4 = mimeType.Subtype == "mp4";
        if (!isMp4 && mimeType.Subtype != "webm")
            return false;

        // A strictly audio or video type
        if (mimeType.Type != (isVideo ? "video" : "audio"))
            return false;

        // The parameters other than codecs are not recognized, and MP4 and WebM imply no codec
        string? codecsParameter = null;
        foreach (var (name, value) in mimeType.Parameters)
        {
            if (name != "codecs")
                return false;

            codecsParameter = value;
        }

        if (codecsParameter is null)
            return false;

        var codecs = codecsParameter.Split(',', StringSplitOptions.TrimEntries);
        foreach (var codec in codecs)
        {
            if (IsVideoCodec(codec) != isVideo)
                return false;
        }

        // The encryption schemes: WebM is encrypted with AES-CTR, MP4 with cenc or cbcs
        if (capability.EncryptionScheme is { } scheme && !(scheme == "cenc" || (isMp4 && scheme is "cbcs" or "cbcs-1-9")))
            return false;

        // Clear Key only supports the empty robustness
        if (capability.Robustness.Length > 0)
            return false;

        return backend.CanPlayType(mimeType.Essence, codecs) == MediaTypeSupport.Probably;
    }

    /// <summary>
    /// Whether a codec (RFC 6381) is a video codec, or null when it is not a codec of the containers that Clear Key supports.
    /// The names of the codecs are case-sensitive (e.g. <c>avc1</c>, not <c>AVC1</c>).
    /// </summary>
    private static bool? IsVideoCodec(string codec)
    {
        var name = codec.Split('.')[0];
        return name switch
        {
            "avc1" or "avc3" or "hev1" or "hvc1" or "vp8" or "vp9" or "vp09" or "av01" or "dvh1" or "dvhe" => true,
            "mp4a" or "opus" or "vorbis" or "flac" or "mp3" or "ac-3" or "ec-3" => false,
            _ => null,
        };
    }
}

// Encrypted Media Extensions (https://w3c.github.io/encrypted-media/#navigator-extension-requestmediakeysystemaccess)
public sealed partial class Navigator
{
    /// <summary>
    /// Requests the access to a key system with one of the configurations (https://w3c.github.io/encrypted-media/#navigator-extension-requestmediakeysystemaccess).
    /// The library implements Clear Key (<c>org.w3.clearkey</c>), for the media types that the media backend of the browsing
    /// context plays (<see cref="BrowsingContextOptions.MediaBackend"/>). The task fails with a <c>NotSupportedError</c>
    /// when the key system or none of the configurations is supported, and with a <c>SecurityError</c> when the document is
    /// not allowed to use the <c>encrypted-media</c> feature. In a prerendering document, the request starts when the
    /// document is activated.
    /// </summary>
    /// <param name="keySystem">The key system (case-sensitive).</param>
    /// <param name="supportedConfigurations">The configurations, by order of preference.</param>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-navigator-requestmediakeysystemaccess"/>
    [IdlMember("requestMediaKeySystemAccess", "https://w3c.github.io/encrypted-media/#dom-navigator-requestmediakeysystemaccess")]
    public Task<MediaKeySystemAccess> RequestMediaKeySystemAccess(string keySystem, IReadOnlyList<MediaKeySystemConfiguration> supportedConfigurations)
    {
        ArgumentNullException.ThrowIfNull(keySystem);
        ArgumentNullException.ThrowIfNull(supportedConfigurations);

        // https://wicg.github.io/nav-speculation/prerendering.html#eme-patch: [DelayWhilePrerendering]
        var configurations = supportedConfigurations.Select(KeySystemConfiguration.Copy).ToList();
        return Document.DelayWhilePrerendering(_window, () => RequestMediaKeySystemAccessCore(keySystem, configurations));
    }

    private Task<MediaKeySystemAccess> RequestMediaKeySystemAccessCore(string keySystem, List<MediaKeySystemConfiguration> configurations)
    {
        var document = _window.Document;
        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.EncryptedMedia))
            return Task.FromException<MediaKeySystemAccess>(new DomException("The document is not allowed to use the encrypted-media feature", "SecurityError"));

        if (keySystem.Length == 0)
            return Task.FromException<MediaKeySystemAccess>(new TypeErrorException("The key system is empty"));

        if (configurations.Count == 0)
            return Task.FromException<MediaKeySystemAccess>(new TypeErrorException("The list of configurations is empty"));

        var canPersistState = !document.Origin.IsOpaque;
        var backend = _window.Options.MediaBackend;
        var promise = new TaskCompletionSource<MediaKeySystemAccess>();

        // The steps run in parallel: the result is reported by a task
        _window.QueueGlobalTask(() =>
        {
            if (!KeySystemConfiguration.IsSupportedKeySystem(keySystem))
            {
                promise.TrySetException(DomException.NotSupported("The key system is not supported"));
                return;
            }

            // A document whose origin is opaque cannot use the key system (as in the browsers)
            if (document.Origin.IsOpaque)
            {
                promise.TrySetException(DomException.NotSupported("The key system is not allowed in an opaque origin"));
                return;
            }

            foreach (var candidate in configurations)
            {
                if (KeySystemConfiguration.GetSupportedConfiguration(candidate, backend, canPersistState) is { } configuration)
                {
                    promise.TrySetResult(new MediaKeySystemAccess(_window, keySystem, configuration));
                    return;
                }
            }

            promise.TrySetException(DomException.NotSupported("None of the configurations is supported"));
        });

        return promise.Task;
    }
}
