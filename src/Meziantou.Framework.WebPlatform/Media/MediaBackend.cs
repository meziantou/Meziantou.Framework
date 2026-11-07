using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Media;

/// <summary>
/// The media pipeline plugged into the browsing contexts (<see cref="BrowsingContextOptions.MediaBackend"/>): it tells which
/// media types can be played, reads the media resources fetched by the <c>audio</c> and <c>video</c> elements (their
/// duration, their dimensions, their tracks) and plays them. The library never decodes audio or video by itself: it
/// implements the processing model of the media elements (the resource selection, the fetch of the resource, the ready
/// states, the events, the promises of <c>play()</c>, seeking, looping...) on top of the <see cref="MediaPlayer"/> the
/// backend creates for each media resource.
/// </summary>
/// <remarks>
/// <para>
/// Without a backend (the default), the media elements have no media resource: nothing is fetched, no media event is
/// fired, <c>canPlayType()</c> returns the empty string and <c>play()</c> returns a promise rejected with a
/// <c>NotSupportedError</c>.
/// </para>
/// <para>
/// A backend can be shared by several browsing contexts. Its methods are called on the thread of the event loop of the
/// browsing context that uses them.
/// </para>
/// </remarks>
public abstract class MediaBackend
{
    /// <summary>
    /// Returns how confident the backend is that it can play the media resources of a type
    /// (https://html.spec.whatwg.org/multipage/media.html#dom-navigator-canplaytype): the result of <c>canPlayType()</c>, and
    /// the <c>source</c> elements whose <c>type</c> attribute is <see cref="MediaTypeSupport.NotSupported"/> are skipped by the
    /// resource selection algorithm. As in the specification, a backend should not return <see cref="MediaTypeSupport.Probably"/>
    /// for a type that allows the <c>codecs</c> parameter when there is no codec.
    /// </summary>
    /// <param name="mimeType">The essence of the MIME type, in ASCII lowercase (e.g. <c>video/webm</c>).</param>
    /// <param name="codecs">The codecs of the <c>codecs</c> parameter of the MIME type (e.g. <c>vp9</c> and <c>opus</c> for <c>video/webm; codecs="vp9, opus"</c>), or an empty list.</param>
    public abstract MediaTypeSupport CanPlayType(string mimeType, IReadOnlyList<string> codecs);

    /// <summary>
    /// Opens a media resource fetched by a media element: the backend inspects the data (the format is sniffed, the
    /// <c>Content-Type</c> is only a hint) and returns the player of the resource once its metadata is known (its duration,
    /// its dimensions and its tracks), or null when the resource is in an unsupported format or cannot be rendered at all (the
    /// element then fails with <c>MEDIA_ERR_SRC_NOT_SUPPORTED</c>, or tries its next <c>source</c> element).
    /// </summary>
    /// <remarks>
    /// The method is called when the response arrives. For a response that arrives over time
    /// (<see cref="MediaResource.IsComplete"/> is false), a backend that cannot read the metadata from the first bytes returns
    /// null: the method is called again with the bytes received so far each time more bytes arrive, and the resource is
    /// unsupported when the complete resource gives no player.
    /// </remarks>
    /// <param name="resource">The media resource.</param>
    /// <returns>The player of the resource, or null. The media element disposes it when it selects another resource.</returns>
    public abstract MediaPlayer? CreatePlayer(MediaResource resource);

    /// <summary>
    /// Creates the byte stream parser of a <c>SourceBuffer</c> of the Media Source Extensions
    /// (https://w3c.github.io/media-source/): the backend parses the initialization segments and the media segments appended
    /// by the scripts (<see cref="MediaSegmentParser"/>), and the library implements <c>MediaSource</c> and
    /// <c>SourceBuffer</c> on top of it. The method also answers <c>MediaSource.isTypeSupported()</c>: the parser created to
    /// answer it is disposed at once.
    /// </summary>
    /// <remarks>
    /// The default implementation returns null: no type is supported, so <c>addSourceBuffer()</c> throws a
    /// <c>NotSupportedError</c>. The position of a media element attached to a <c>MediaSource</c> advances with the clock of
    /// the event loop within the buffered ranges (the default playback of <see cref="MediaPlayer"/>): the coded frames are not
    /// decoded.
    /// </remarks>
    /// <param name="mimeType">The essence of the MIME type, in ASCII lowercase (e.g. <c>video/mp4</c>).</param>
    /// <param name="codecs">The codecs of the <c>codecs</c> parameter of the MIME type, or an empty list.</param>
    /// <returns>The parser, or null when the backend cannot create a <c>SourceBuffer</c> for the type and its codecs.</returns>
    public virtual MediaSegmentParser? CreateSegmentParser(string mimeType, IReadOnlyList<string> codecs) => null;
}

/// <summary>The answer of <see cref="MediaBackend.CanPlayType"/> (https://html.spec.whatwg.org/multipage/media.html#dom-navigator-canplaytype).</summary>
public enum MediaTypeSupport
{
    /// <summary>The backend knows it cannot render the type: <c>canPlayType()</c> returns the empty string.</summary>
    NotSupported,

    /// <summary>The backend may be able to play the type (e.g. a container whose codecs are not given): <c>canPlayType()</c> returns <c>maybe</c>.</summary>
    Maybe,

    /// <summary>The backend is confident that it can play the type: <c>canPlayType()</c> returns <c>probably</c>.</summary>
    Probably,
}

/// <summary>Whether the media elements are allowed to play without a user gesture (<see cref="BrowsingContextOptions.MediaAutoplayPolicy"/>).</summary>
public enum MediaAutoplayPolicy
{
    /// <summary>The media elements are always allowed to play: <c>play()</c> and the <c>autoplay</c> attribute start the playback.</summary>
    Allowed,

    /// <summary>
    /// The media elements are allowed to play when they are muted, or once the user interacted with the page (its window
    /// has sticky activation): otherwise <c>play()</c> is rejected with a <c>NotAllowedError</c> and the <c>autoplay</c>
    /// attribute does not start the playback.
    /// </summary>
    UserActivationRequired,
}

/// <summary>A media resource fetched by a media element, given to <see cref="MediaBackend.CreatePlayer"/>.</summary>
public sealed class MediaResource
{
    internal MediaResource(string url, string? contentType, ReadOnlyMemory<byte> content, bool isComplete, bool isVideoElement, TimeProvider timeProvider)
    {
        Url = url;
        ContentType = contentType;
        Content = content;
        IsComplete = isComplete;
        IsVideoElement = isVideoElement;
        TimeProvider = timeProvider;
    }

    /// <summary>Gets the URL of the response (after the redirects), or an empty string for a media provider object (a <c>Blob</c> assigned to <c>srcObject</c>).</summary>
    public string Url { get; }

    /// <summary>Gets the <c>Content-Type</c> of the response (or the type of the <c>Blob</c>), or null when there is none. It is a hint: the format of a media resource is sniffed.</summary>
    public string? ContentType { get; }

    /// <summary>
    /// Gets the bytes of the resource: the whole body of the response, or the bytes received so far when the response
    /// arrives over time (<see cref="DocumentResource.RemainingContentBytes"/>) and <see cref="IsComplete"/> is false.
    /// </summary>
    public ReadOnlyMemory<byte> Content { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Content"/> is the entire resource. Otherwise the player receives the rest
    /// of the resource as it arrives (<see cref="MediaPlayer.OnDataReceived"/>).
    /// </summary>
    public bool IsComplete { get; }

    /// <summary>Gets a value indicating whether the resource is played by a <c>video</c> element, which paints the video frames (an <c>audio</c> element only plays the audio).</summary>
    public bool IsVideoElement { get; }

    /// <summary>Gets the clock of the event loop of the document of the media element (<see cref="BrowsingContextOptions.TimeProvider"/>): the clock of the media timeline.</summary>
    public TimeProvider TimeProvider { get; }
}

/// <summary>
/// How much of a media resource is available to render the current playback position
/// (https://html.spec.whatwg.org/multipage/media.html#ready-states). A <see cref="MediaPlayer"/> is created once the
/// metadata is known, so its state is at least <see cref="HaveMetadata"/>.
/// </summary>
public enum MediaReadyState
{
    /// <summary>No information about the media resource is available (<c>HAVE_NOTHING</c>).</summary>
    HaveNothing = 0,

    /// <summary>The duration and the dimensions are available, but no data for the current playback position (<c>HAVE_METADATA</c>).</summary>
    HaveMetadata = 1,

    /// <summary>The data for the current playback position is available, but not enough to advance (<c>HAVE_CURRENT_DATA</c>).</summary>
    HaveCurrentData = 2,

    /// <summary>The data for the current playback position and for at least a little more is available (<c>HAVE_FUTURE_DATA</c>).</summary>
    HaveFutureData = 3,

    /// <summary>Enough data is available to play the resource to its end without stopping (<c>HAVE_ENOUGH_DATA</c>).</summary>
    HaveEnoughData = 4,
}

/// <summary>An audio or video track of a media resource (https://html.spec.whatwg.org/multipage/media.html#media-resources-with-multiple-media-tracks), reported by a <see cref="MediaPlayer"/>.</summary>
/// <param name="Id">The identifier of the track (<c>AudioTrack.id</c>, <c>VideoTrack.id</c>), or an empty string.</param>
/// <param name="Kind">The category of the track (<c>main</c>, <c>alternative</c>, <c>commentary</c>...), or an empty string when it is unknown.</param>
/// <param name="Label">The label of the track, or an empty string.</param>
/// <param name="Language">The BCP 47 language tag of the track, or an empty string.</param>
public sealed record MediaTrackInfo(string Id, string Kind = "", string Label = "", string Language = "");

/// <summary>
/// A text track of a media resource (https://html.spec.whatwg.org/multipage/media.html#media-resource-specific-text-track),
/// reported by a <see cref="MediaPlayer"/>, with its cues.
/// </summary>
/// <param name="Kind">The kind of the track.</param>
/// <param name="Cues">The cues of the track.</param>
/// <param name="Id">The identifier of the track (<c>TextTrack.id</c>), or an empty string.</param>
/// <param name="Label">The label of the track, or an empty string.</param>
/// <param name="Language">The BCP 47 language tag of the track, or an empty string.</param>
/// <param name="InBandMetadataTrackDispatchType">The in-band metadata track dispatch type of a chapters or metadata track (e.g. the <c>CodecID</c> element of a WebM file), or an empty string.</param>
public sealed record MediaTextTrackInfo(Dom.TextTrackKind Kind, IReadOnlyList<MediaTextTrackCue> Cues, string Id = "", string Label = "", string Language = "", string InBandMetadataTrackDispatchType = "");

/// <summary>A cue of a text track of a media resource, exposed as a <c>VTTCue</c>.</summary>
/// <param name="StartTime">The start time of the cue, in seconds.</param>
/// <param name="EndTime">The end time of the cue, in seconds (positive infinity for an unbounded cue).</param>
/// <param name="Text">The text of the cue (WebVTT cue text).</param>
/// <param name="Id">The identifier of the cue, or an empty string.</param>
/// <param name="Settings">The WebVTT cue settings of the cue (e.g. <c>line:0 align:start</c>), or an empty string.</param>
public sealed record MediaTextTrackCue(double StartTime, double EndTime, string Text, string Id = "", string Settings = "");

/// <summary>A range of the media timeline, in seconds (a range of a <c>TimeRanges</c> object).</summary>
/// <param name="Start">The start of the range, in seconds.</param>
/// <param name="End">The end of the range, in seconds.</param>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct MediaTimeRange(double Start, double End);

/// <summary>
/// A frame of a video, painted in the box of the <c>video</c> element by the rendering backend of the browsing context and
/// drawn by <c>drawImage()</c>: pixels in the RGBA format (4 bytes per pixel, not premultiplied, sRGB), from the top-left
/// pixel, row by row.
/// </summary>
public sealed class MediaVideoFrame
{
    /// <summary>Creates a frame from its pixels.</summary>
    /// <param name="width">The width of the frame in pixels.</param>
    /// <param name="height">The height of the frame in pixels.</param>
    /// <param name="pixels">The pixels: <paramref name="width"/> x <paramref name="height"/> x 4 bytes (RGBA, not premultiplied, sRGB).</param>
    public MediaVideoFrame(int width, int height, ReadOnlyMemory<byte> pixels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (pixels.Length != (long)width * height * 4)
            throw new ArgumentException("The pixels must have 4 bytes per pixel", nameof(pixels));

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>Gets the width of the frame in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height of the frame in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the pixels of the frame (RGBA, not premultiplied, sRGB).</summary>
    public ReadOnlyMemory<byte> Pixels { get; }

    /// <summary>The image of the frame for the rendering backend that created it (the frame is converted once).</summary>
    internal Rendering.PaintImage? PaintImage { get; set; }

    internal Rendering.RenderingBackend? PaintImageBackend { get; set; }
}
