using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The capture handle that a page exposes to the applications that capture it (https://w3c.github.io/mediacapture-handle/identity/#dom-capturehandleconfig).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-handle/identity/#dom-capturehandleconfig"/>
[IdlInterface("CaptureHandleConfig", "https://w3c.github.io/mediacapture-handle/identity/#dom-capturehandleconfig")]
public sealed class CaptureHandleConfig
{
    /// <summary>Gets whether the origin of the page is exposed.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-handle/identity/#dom-capturehandleconfig-exposeorigin"/>
    [IdlMember("exposeOrigin", "https://w3c.github.io/mediacapture-handle/identity/#dom-capturehandleconfig-exposeorigin")]
    public bool ExposeOrigin { get; init; }

    /// <summary>Gets the handle (at most 1024 characters).</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-handle/identity/#dom-capturehandleconfig-handle"/>
    [IdlMember("handle", "https://w3c.github.io/mediacapture-handle/identity/#dom-capturehandleconfig-handle")]
    public string Handle { get; init; } = "";

    /// <summary>Gets the origins of the applications that can read the handle (<c>*</c> for all of them).</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-handle/identity/#dom-capturehandleconfig-permittedorigins"/>
    [IdlMember("permittedOrigins", "https://w3c.github.io/mediacapture-handle/identity/#dom-capturehandleconfig-permittedorigins")]
    public IReadOnlyList<string> PermittedOrigins { get; init; } = [];
}

/// <summary>The capture handle of a captured page (https://w3c.github.io/mediacapture-handle/identity/#dom-capturehandle).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-handle/identity/#dom-capturehandle"/>
[IdlInterface("CaptureHandle", "https://w3c.github.io/mediacapture-handle/identity/#dom-capturehandle")]
public sealed class CaptureHandle
{
    /// <summary>Gets the origin of the captured page, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-handle/identity/#dom-capturehandle-origin"/>
    [IdlMember("origin", "https://w3c.github.io/mediacapture-handle/identity/#dom-capturehandle-origin")]
    public string? Origin { get; init; }

    /// <summary>Gets the handle of the captured page, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-handle/identity/#dom-capturehandle-handle"/>
    [IdlMember("handle", "https://w3c.github.io/mediacapture-handle/identity/#dom-capturehandle-handle")]
    public string? Handle { get; init; }
}

/// <summary>
/// An element that the capture of the tab of its document can be cropped to (https://w3c.github.io/mediacapture-region/#dom-croptarget).
/// The capture backend of the library captures no tab: the targets cannot be used to crop a track.
/// </summary>
/// <seealso href="https://w3c.github.io/mediacapture-region/#dom-croptarget"/>
[IdlInterface("CropTarget", "https://w3c.github.io/mediacapture-region/#dom-croptarget")]
public sealed class CropTarget
{
    private CropTarget(Element element) => Element = element;

    internal Element Element { get; }

    /// <summary>Creates the crop target of an element (https://w3c.github.io/mediacapture-region/#dom-croptarget-fromelement).</summary>
    /// <param name="element">The element.</param>
    /// <seealso href="https://w3c.github.io/mediacapture-region/#dom-croptarget-fromelement"/>
    [IdlMember("fromElement", "https://w3c.github.io/mediacapture-region/#dom-croptarget-fromelement")]
    public static Task<CropTarget> FromElement(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return Task.FromResult(new CropTarget(element));
    }
}

// Capture Handle - Identity (https://w3c.github.io/mediacapture-handle/identity/)
public sealed partial class MediaDevices
{
    /// <summary>The maximum length of a capture handle (https://w3c.github.io/mediacapture-handle/identity/#dom-mediadevices-setcapturehandleconfig).</summary>
    private const int MaxCaptureHandleLength = 1024;

    /// <summary>Gets the capture handle configuration of the document, or null.</summary>
    internal CaptureHandleConfig? CaptureHandleConfig { get; private set; }

    /// <summary>
    /// Sets the capture handle that the page exposes to the applications that capture it
    /// (https://w3c.github.io/mediacapture-handle/identity/#dom-mediadevices-setcapturehandleconfig).
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <exception cref="TypeErrorException">The handle is longer than 1024 characters.</exception>
    /// <exception cref="DomException">A <c>NotSupportedError</c>: a permitted origin is invalid, or <c>*</c> is not alone; an <c>InvalidStateError</c>: the document is not the document of a top-level browsing context.</exception>
    /// <seealso href="https://w3c.github.io/mediacapture-handle/identity/#dom-mediadevices-setcapturehandleconfig"/>
    [IdlMember("setCaptureHandleConfig", "https://w3c.github.io/mediacapture-handle/identity/#dom-mediadevices-setcapturehandleconfig")]
    public void SetCaptureHandleConfig(CaptureHandleConfig? config = null)
    {
        config ??= new CaptureHandleConfig();
        if (config.Handle.Length > MaxCaptureHandleLength)
            throw new TypeErrorException("The handle must be at most 1024 characters long");

        var origins = config.PermittedOrigins;
        if (origins.Contains("*", StringComparer.Ordinal))
        {
            if (origins.Count > 1)
                throw DomException.NotSupported("The wildcard must be the only permitted origin");
        }
        else
        {
            foreach (var origin in origins)
            {
                if (UrlParser.Parse(origin, baseUrl: null) is not { } url || url.SerializeOrigin() == "null")
                    throw DomException.NotSupported($"The permitted origin '{origin}' is not a valid origin");
            }
        }

        if (_window.BrowsingContext is not { Parent: null })
            throw DomException.InvalidState("The capture handle can only be set by a top-level document");

        CaptureHandleConfig = config;
    }
}

public partial class MediaStreamTrack
{
    /// <summary>
    /// Returns the capture handle of the captured page (https://w3c.github.io/mediacapture-handle/identity/#dom-mediastreamtrack-getcapturehandle):
    /// null, as the capture backend captures no page of the library.
    /// </summary>
    /// <seealso href="https://w3c.github.io/mediacapture-handle/identity/#dom-mediastreamtrack-getcapturehandle"/>
    [IdlMember("getCaptureHandle", "https://w3c.github.io/mediacapture-handle/identity/#dom-mediastreamtrack-getcapturehandle")]
    public CaptureHandle? GetCaptureHandle() => null;

    /// <summary>Gets or sets the event handler of the <c>capturehandlechange</c> event.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-handle/identity/#dom-mediastreamtrack-oncapturehandlechange"/>
    [IdlMember("oncapturehandlechange", "https://w3c.github.io/mediacapture-handle/identity/#dom-mediastreamtrack-oncapturehandlechange")]
    public Action<Event>? OnCaptureHandleChange
    {
        get => GetEventHandler("capturehandlechange");
        set => SetEventHandler("capturehandlechange", value);
    }
}
