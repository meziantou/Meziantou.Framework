using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// Tells whether the user agent can decode or encode media with a configuration (https://w3c.github.io/media-capabilities/,
/// <c>navigator.mediaCapabilities</c>). The library validates the configurations as the specification requires, and the
/// backends of the browsing context answer the support:
/// <list type="bullet">
/// <item><description>
/// <c>file</c>: <see cref="MediaBackend.CanPlayType"/> of <see cref="BrowsingContextOptions.MediaBackend"/> (<c>probably</c>
/// for a container with the <c>codecs</c> parameter, <c>maybe</c> or <c>probably</c> for a type of a single codec such as
/// <c>audio/mpeg</c>);
/// </description></item>
/// <item><description><c>media-source</c>: the same answer, and <see cref="MediaBackend.CreateSegmentParser"/> creates a parser;</description></item>
/// <item><description>
/// <c>webrtc</c>: the RTP payload formats (<c>audio/opus</c>, <c>audio/PCMU</c>, <c>audio/PCMA</c>, <c>video/VP8</c>,
/// <c>video/VP9</c>, <c>video/H264</c>, <c>video/AV1</c>, <c>video/H265</c>) are converted to the WebCodecs configurations
/// of their codec and checked by <see cref="BrowsingContextOptions.MediaCodecs"/>;
/// </description></item>
/// <item><description>
/// <c>record</c>: what <see cref="MediaRecorder"/> records, VP8 and Opus in WebM or Matroska, when the encoders of
/// <see cref="BrowsingContextOptions.MediaCodecs"/> support the configuration.
/// </description></item>
/// </list>
/// Without a backend, nothing is supported. There is no performance model: a supported configuration is smooth, and not
/// power efficient (the library cannot tell whether a backend uses hardware codecs). A <c>colorGamut</c> or a
/// <c>transferFunction</c> must match the color space of a VP9 or AV1 codec string (BT.709 when the string does not give
/// it), and a video with HDR metadata is not supported (the rendering is in sRGB). Encrypted Media Extensions are not
/// implemented: a configuration with a key system is not supported.
/// </summary>
/// <seealso href="https://w3c.github.io/media-capabilities/#mediacapabilities"/>
[IdlInterface("MediaCapabilities", "https://w3c.github.io/media-capabilities/#mediacapabilities")]
public sealed class MediaCapabilities
{
    private readonly GlobalScope _global;

    internal MediaCapabilities(GlobalScope global) => _global = global;

    /// <summary>Returns whether the user agent can decode media with a configuration (https://w3c.github.io/media-capabilities/#dom-mediacapabilities-decodinginfo).</summary>
    /// <param name="configuration">The configuration.</param>
    /// <returns>
    /// A task that completes with the support, or fails with a <see cref="TypeErrorException"/> for an invalid
    /// configuration, or a <see cref="DomException"/> for a key system in a worker (<c>InvalidStateError</c>) or in a
    /// context that is not secure (<c>SecurityError</c>).
    /// </returns>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-mediacapabilities-decodinginfo"/>
    [IdlMember("decodingInfo", "https://w3c.github.io/media-capabilities/#dom-mediacapabilities-decodinginfo")]
    public Task<MediaCapabilitiesDecodingInfo> DecodingInfo(MediaDecodingConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!MediaCapabilitiesSupport.IsValid(configuration))
            return Task.FromException<MediaCapabilitiesDecodingInfo>(new TypeErrorException("The decoding configuration is invalid"));

        if (configuration.KeySystemConfiguration is not null)
        {
            if (_global is WorkerGlobalScope)
                return Task.FromException<MediaCapabilitiesDecodingInfo>(new DomException("A key system cannot be queried in a worker", "InvalidStateError"));

            if (!_global.IsSecureContext)
                return Task.FromException<MediaCapabilitiesDecodingInfo>(new DomException("A key system can only be queried in a secure context", "SecurityError"));
        }

        var supported = MediaCapabilitiesSupport.IsSupported(_global, configuration);
        return Resolve(new MediaCapabilitiesDecodingInfo { Supported = supported, Smooth = supported, PowerEfficient = false, KeySystemAccess = null, Configuration = configuration });
    }

    /// <summary>Returns whether the user agent can encode media with a configuration (https://w3c.github.io/media-capabilities/#dom-mediacapabilities-encodinginfo).</summary>
    /// <param name="configuration">The configuration.</param>
    /// <returns>A task that completes with the support, or fails with a <see cref="TypeErrorException"/> for an invalid configuration.</returns>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-mediacapabilities-encodinginfo"/>
    [IdlMember("encodingInfo", "https://w3c.github.io/media-capabilities/#dom-mediacapabilities-encodinginfo")]
    public Task<MediaCapabilitiesEncodingInfo> EncodingInfo(MediaEncodingConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!MediaCapabilitiesSupport.IsValid(configuration))
            return Task.FromException<MediaCapabilitiesEncodingInfo>(new TypeErrorException("The encoding configuration is invalid"));

        var supported = MediaCapabilitiesSupport.IsSupported(_global, configuration);
        return Resolve(new MediaCapabilitiesEncodingInfo { Supported = supported, Smooth = supported, PowerEfficient = false, Configuration = configuration });
    }

    /// <summary>Resolves the promise in a task of the media capabilities task source.</summary>
    private Task<T> Resolve<T>(T value)
    {
        var completion = new TaskCompletionSource<T>();
        _global.QueueGlobalTask(() => completion.TrySetResult(value));
        return completion.Task;
    }
}

// Media Capabilities (https://w3c.github.io/media-capabilities/#navigators-extensions)
public sealed partial class Navigator
{
    private MediaCapabilities? _mediaCapabilities;

    /// <summary>Gets the media capabilities of the window (https://w3c.github.io/media-capabilities/#dom-navigator-mediacapabilities).</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-navigator-mediacapabilities"/>
    [IdlMember("mediaCapabilities", "https://w3c.github.io/media-capabilities/#dom-navigator-mediacapabilities")]
    public MediaCapabilities MediaCapabilities => _mediaCapabilities ??= new MediaCapabilities(_window);
}

// Media Capabilities (https://w3c.github.io/media-capabilities/#navigators-extensions)
public sealed partial class WorkerNavigator
{
    private MediaCapabilities? _mediaCapabilities;

    /// <summary>Gets the media capabilities of the worker (https://w3c.github.io/media-capabilities/#dom-workernavigator-mediacapabilities).</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-workernavigator-mediacapabilities"/>
    [IdlMember("mediaCapabilities", "https://w3c.github.io/media-capabilities/#dom-workernavigator-mediacapabilities")]
    public MediaCapabilities MediaCapabilities => _mediaCapabilities ??= new MediaCapabilities(_global);
}
