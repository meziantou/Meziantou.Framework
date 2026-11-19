using Meziantou.Framework.WebPlatform.Dom.FileApi;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>How a camera reduces the red eyes of the photos (https://w3c.github.io/mediacapture-image/#redeyereduction-section).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-image/#dom-redeyereduction"/>
[IdlInterface("RedEyeReduction", "https://w3c.github.io/mediacapture-image/#dom-redeyereduction")]
public enum RedEyeReduction
{
    /// <summary>The camera never reduces the red eyes.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-redeyereduction-never"/>
    [IdlMember("never", "https://w3c.github.io/mediacapture-image/#dom-redeyereduction-never")]
    Never,

    /// <summary>The camera always reduces the red eyes.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-redeyereduction-always"/>
    [IdlMember("always", "https://w3c.github.io/mediacapture-image/#dom-redeyereduction-always")]
    Always,

    /// <summary>The application chooses whether the camera reduces the red eyes.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-redeyereduction-controllable"/>
    [IdlMember("controllable", "https://w3c.github.io/mediacapture-image/#dom-redeyereduction-controllable")]
    Controllable,
}

/// <summary>The mode of the flash of a camera (https://w3c.github.io/mediacapture-image/#filllightmode-section).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-image/#dom-filllightmode"/>
[IdlInterface("FillLightMode", "https://w3c.github.io/mediacapture-image/#dom-filllightmode")]
public enum FillLightMode
{
    /// <summary>The camera decides.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-filllightmode-auto"/>
    [IdlMember("auto", "https://w3c.github.io/mediacapture-image/#dom-filllightmode-auto")]
    Auto,

    /// <summary>The flash is off.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-filllightmode-off"/>
    [IdlMember("off", "https://w3c.github.io/mediacapture-image/#dom-filllightmode-off")]
    Off,

    /// <summary>The flash is used.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-filllightmode-flash"/>
    [IdlMember("flash", "https://w3c.github.io/mediacapture-image/#dom-filllightmode-flash")]
    Flash,
}

/// <summary>The capabilities of the photos of a camera (https://w3c.github.io/mediacapture-image/#photocapabilities-section).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-image/#dictdef-photocapabilities"/>
[IdlInterface("PhotoCapabilities", "https://w3c.github.io/mediacapture-image/#dictdef-photocapabilities")]
public sealed class PhotoCapabilities
{
    /// <summary>Gets how the camera reduces the red eyes, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-photocapabilities-redeyereduction"/>
    [IdlMember("redEyeReduction", "https://w3c.github.io/mediacapture-image/#dom-photocapabilities-redeyereduction")]
    public RedEyeReduction? RedEyeReduction { get; init; }

    /// <summary>Gets the range of the heights of the photos, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-photocapabilities-imageheight"/>
    [IdlMember("imageHeight", "https://w3c.github.io/mediacapture-image/#dom-photocapabilities-imageheight")]
    public MediaSettingsRange? ImageHeight { get; init; }

    /// <summary>Gets the range of the widths of the photos, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-photocapabilities-imagewidth"/>
    [IdlMember("imageWidth", "https://w3c.github.io/mediacapture-image/#dom-photocapabilities-imagewidth")]
    public MediaSettingsRange? ImageWidth { get; init; }

    /// <summary>Gets the modes of the flash, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-photocapabilities-filllightmode"/>
    [IdlMember("fillLightMode", "https://w3c.github.io/mediacapture-image/#dom-photocapabilities-filllightmode")]
    public IReadOnlyList<FillLightMode>? FillLightMode { get; init; }
}

/// <summary>The settings of a photo (https://w3c.github.io/mediacapture-image/#photosettings-section).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-image/#dictdef-photosettings"/>
[IdlInterface("PhotoSettings", "https://w3c.github.io/mediacapture-image/#dictdef-photosettings")]
public sealed class PhotoSettings
{
    /// <summary>Gets the mode of the flash, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-photosettings-filllightmode"/>
    [IdlMember("fillLightMode", "https://w3c.github.io/mediacapture-image/#dom-photosettings-filllightmode")]
    public FillLightMode? FillLightMode { get; init; }

    /// <summary>Gets the height of the photo, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-photosettings-imageheight"/>
    [IdlMember("imageHeight", "https://w3c.github.io/mediacapture-image/#dom-photosettings-imageheight")]
    public double? ImageHeight { get; init; }

    /// <summary>Gets the width of the photo, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-photosettings-imagewidth"/>
    [IdlMember("imageWidth", "https://w3c.github.io/mediacapture-image/#dom-photosettings-imagewidth")]
    public double? ImageWidth { get; init; }

    /// <summary>Gets whether the red eyes are reduced, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-photosettings-redeyereduction"/>
    [IdlMember("redEyeReduction", "https://w3c.github.io/mediacapture-image/#dom-photosettings-redeyereduction")]
    public bool? RedEyeReduction { get; init; }
}

/// <summary>
/// Takes photos and grabs frames of a video track (https://w3c.github.io/mediacapture-image/#imagecaptureapi). The photos
/// are the current frame of the track, encoded as PNG at the size of the track: the capture backend has no photo mode.
/// </summary>
/// <seealso href="https://w3c.github.io/mediacapture-image/#dom-imagecapture"/>
[IdlInterface("ImageCapture", "https://w3c.github.io/mediacapture-image/#dom-imagecapture")]
public sealed class ImageCapture
{
    /// <summary>Creates the image capture of a video track.</summary>
    /// <param name="videoTrack">The video track.</param>
    /// <exception cref="DomException">A <c>NotSupportedError</c>: the track is not a video track.</exception>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-imagecapture-imagecapture"/>
    [IdlMember("constructor", "https://w3c.github.io/mediacapture-image/#dom-imagecapture-imagecapture")]
    public ImageCapture(MediaStreamTrack videoTrack)
    {
        ArgumentNullException.ThrowIfNull(videoTrack);
        if (videoTrack.Kind != "video")
            throw DomException.NotSupported("The track is not a video track");

        Track = videoTrack;
    }

    /// <summary>Gets the video track.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-imagecapture-track"/>
    [IdlMember("track", "https://w3c.github.io/mediacapture-image/#dom-imagecapture-track")]
    public MediaStreamTrack Track { get; }

    /// <summary>
    /// Takes a photo (https://w3c.github.io/mediacapture-image/#dom-imagecapture-takephoto): the current frame of the track,
    /// as a PNG image. The task fails with an <c>InvalidStateError</c> when the track is not live, with an
    /// <see cref="OverconstrainedError"/> when the settings are not in the photo capabilities (the size of the photos is the
    /// size of the track, there is no flash and no red-eye reduction), and with an <c>UnknownError</c> when the track has no
    /// frame.
    /// </summary>
    /// <param name="photoSettings">The settings of the photo.</param>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-imagecapture-takephoto"/>
    [IdlMember("takePhoto", "https://w3c.github.io/mediacapture-image/#dom-imagecapture-takephoto")]
    public Task<Blob> TakePhoto(PhotoSettings? photoSettings = null)
    {
        if (Track.ReadyState != MediaStreamTrackState.Live)
            return Task.FromException<Blob>(DomException.InvalidState("The track is not live"));

        if (photoSettings is not null && GetUnsupportedPhotoSetting(photoSettings) is { } setting)
            return Task.FromException<Blob>(new OverconstrainedError(setting, "The photo setting is not in the photo capabilities"));

        if (Track.GetVideoFrame() is not { } frame)
            return Task.FromException<Blob>(new DomException("The track has no frame", "UnknownError"));

        var pixels = new BitmapPixels(frame.Width, frame.Height, frame.Pixels.ToArray());
        if (CanvasContexts.Serialize(pixels, "image/png", quality: null, backend: null) is not var (data, mimeType))
            return Task.FromException<Blob>(new DomException("The photo cannot be encoded", "UnknownError"));

        return Task.FromResult(Blob.Create(data, mimeType));
    }

    /// <summary>Returns the capabilities of the photos: their size is the size of the track (https://w3c.github.io/mediacapture-image/#dom-imagecapture-getphotocapabilities).</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-imagecapture-getphotocapabilities"/>
    [IdlMember("getPhotoCapabilities", "https://w3c.github.io/mediacapture-image/#dom-imagecapture-getphotocapabilities")]
    public Task<PhotoCapabilities> GetPhotoCapabilities()
    {
        if (Track.ReadyState != MediaStreamTrackState.Live)
            return Task.FromException<PhotoCapabilities>(DomException.InvalidState("The track is not live"));

        var (width, height) = GetSize();
        return Task.FromResult(new PhotoCapabilities
        {
            RedEyeReduction = Dom.RedEyeReduction.Never,
            ImageWidth = new MediaSettingsRange { Min = width, Max = width, Step = 1 },
            ImageHeight = new MediaSettingsRange { Min = height, Max = height, Step = 1 },
            FillLightMode = [],
        });
    }

    /// <summary>Returns the settings of the photos (https://w3c.github.io/mediacapture-image/#dom-imagecapture-getphotosettings).</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-imagecapture-getphotosettings"/>
    [IdlMember("getPhotoSettings", "https://w3c.github.io/mediacapture-image/#dom-imagecapture-getphotosettings")]
    public Task<PhotoSettings> GetPhotoSettings()
    {
        if (Track.ReadyState != MediaStreamTrackState.Live)
            return Task.FromException<PhotoSettings>(DomException.InvalidState("The track is not live"));

        var (width, height) = GetSize();
        return Task.FromResult(new PhotoSettings { ImageWidth = width, ImageHeight = height, RedEyeReduction = false });
    }

    /// <summary>
    /// Grabs the current frame of the track (https://w3c.github.io/mediacapture-image/#dom-imagecapture-grabframe). The task
    /// fails with an <c>InvalidStateError</c> when the track is not live, is disabled or is muted, and with an
    /// <c>UnknownError</c> when the track has no frame.
    /// </summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-imagecapture-grabframe"/>
    [IdlMember("grabFrame", "https://w3c.github.io/mediacapture-image/#dom-imagecapture-grabframe")]
    public Task<ImageBitmap> GrabFrame()
    {
        if (Track.ReadyState != MediaStreamTrackState.Live || !Track.Enabled || Track.Muted)
            return Task.FromException<ImageBitmap>(DomException.InvalidState("The track is not live, is disabled or is muted"));

        if (Track.GetVideoFrame() is not { } frame)
            return Task.FromException<ImageBitmap>(new DomException("The track has no frame", "UnknownError"));

        return Task.FromResult(new ImageBitmap(new BitmapPixels(frame.Width, frame.Height, frame.Pixels.ToArray())));
    }

    /// <summary>The name of a setting that the photo capabilities do not allow, or null.</summary>
    private string? GetUnsupportedPhotoSetting(PhotoSettings photoSettings)
    {
        var (width, height) = GetSize();
        if (photoSettings.ImageWidth is { } imageWidth && imageWidth != width)
            return "imageWidth";

        if (photoSettings.ImageHeight is { } imageHeight && imageHeight != height)
            return "imageHeight";

        if (photoSettings.FillLightMode is not null)
            return "fillLightMode";

        return photoSettings.RedEyeReduction is true ? "redEyeReduction" : null;
    }

    private (double Width, double Height) GetSize()
    {
        var settings = Track.GetSettings();
        return (settings.Width ?? 0, settings.Height ?? 0);
    }
}
