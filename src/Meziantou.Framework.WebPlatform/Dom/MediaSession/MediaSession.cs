using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The host hooks of the Media Session API (https://w3c.github.io/mediasession/, <see cref="BrowsingContextOptions.MediaSession"/>).</summary>
public sealed class MediaSessionOptions
{
    /// <summary>
    /// Gets or sets the callback invoked when a page changes its media session (<c>navigator.mediaSession</c>): its metadata
    /// (or an attribute of its metadata), its playback state, its position state, its action handlers, or the state of its
    /// microphone, camera and screen sharing. The host reads the <see cref="MediaSession"/> to show the media controls of the
    /// platform (e.g. the lock screen), and calls <see cref="MediaSession.HandleAction"/> when the user presses one of them.
    /// The callback is invoked on the thread of the event loop, and must not block it. The default is null: the media
    /// sessions are shown nowhere.
    /// The first argument is the context of the window (see <see cref="CallbackContext"/>): its <see cref="CallbackContext.Window"/> is never null.
    /// </summary>
    public Action<CallbackContext, MediaSession>? SessionChanged { get; init; }
}

/// <summary>The playback state of a media session (https://w3c.github.io/mediasession/#enumdef-mediasessionplaybackstate).</summary>
/// <seealso href="https://w3c.github.io/mediasession/#enumdef-mediasessionplaybackstate"/>
[IdlInterface("MediaSessionPlaybackState", "https://w3c.github.io/mediasession/#enumdef-mediasessionplaybackstate")]
public enum MediaSessionPlaybackState
{
    /// <summary>The page does not say whether it is playing: the user agent decides from the media elements.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionplaybackstate-none"/>
    [IdlMember("none", "https://w3c.github.io/mediasession/#dom-mediasessionplaybackstate-none")]
    None,

    /// <summary>The media is paused.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionplaybackstate-paused"/>
    [IdlMember("paused", "https://w3c.github.io/mediasession/#dom-mediasessionplaybackstate-paused")]
    Paused,

    /// <summary>The media is playing.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionplaybackstate-playing"/>
    [IdlMember("playing", "https://w3c.github.io/mediasession/#dom-mediasessionplaybackstate-playing")]
    Playing,
}

/// <summary>An action of a media session, which the user triggers from the media controls (https://w3c.github.io/mediasession/#enumdef-mediasessionaction).</summary>
/// <seealso href="https://w3c.github.io/mediasession/#enumdef-mediasessionaction"/>
[IdlInterface("MediaSessionAction", "https://w3c.github.io/mediasession/#enumdef-mediasessionaction")]
public enum MediaSessionAction
{
    /// <summary>Plays the media.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionaction-play"/>
    [IdlMember("play", "https://w3c.github.io/mediasession/#dom-mediasessionaction-play")]
    Play,

    /// <summary>Pauses the media.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionaction-pause"/>
    [IdlMember("pause", "https://w3c.github.io/mediasession/#dom-mediasessionaction-pause")]
    Pause,

    /// <summary>Seeks backward (by <see cref="MediaSessionActionDetails.SeekOffset"/> seconds, or a default offset).</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionaction-seekbackward"/>
    [IdlMember("seekbackward", "https://w3c.github.io/mediasession/#dom-mediasessionaction-seekbackward")]
    SeekBackward,

    /// <summary>Seeks forward (by <see cref="MediaSessionActionDetails.SeekOffset"/> seconds, or a default offset).</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionaction-seekforward"/>
    [IdlMember("seekforward", "https://w3c.github.io/mediasession/#dom-mediasessionaction-seekforward")]
    SeekForward,

    /// <summary>Moves to the previous track.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionaction-previoustrack"/>
    [IdlMember("previoustrack", "https://w3c.github.io/mediasession/#dom-mediasessionaction-previoustrack")]
    PreviousTrack,

    /// <summary>Moves to the next track.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionaction-nexttrack"/>
    [IdlMember("nexttrack", "https://w3c.github.io/mediasession/#dom-mediasessionaction-nexttrack")]
    NextTrack,

    /// <summary>Skips the advertisement that is playing.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionaction-skipad"/>
    [IdlMember("skipad", "https://w3c.github.io/mediasession/#dom-mediasessionaction-skipad")]
    SkipAd,

    /// <summary>Stops the playback.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionaction-stop"/>
    [IdlMember("stop", "https://w3c.github.io/mediasession/#dom-mediasessionaction-stop")]
    Stop,

    /// <summary>Seeks to <see cref="MediaSessionActionDetails.SeekTime"/>.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionaction-seekto"/>
    [IdlMember("seekto", "https://w3c.github.io/mediasession/#dom-mediasessionaction-seekto")]
    SeekTo,

    /// <summary>Mutes or unmutes the microphone of a call.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionaction-togglemicrophone"/>
    [IdlMember("togglemicrophone", "https://w3c.github.io/mediasession/#dom-mediasessionaction-togglemicrophone")]
    ToggleMicrophone,

    /// <summary>Turns the camera of a call on or off.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionaction-togglecamera"/>
    [IdlMember("togglecamera", "https://w3c.github.io/mediasession/#dom-mediasessionaction-togglecamera")]
    ToggleCamera,

    /// <summary>Starts or stops sharing the screen in a call.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionaction-togglescreenshare"/>
    [IdlMember("togglescreenshare", "https://w3c.github.io/mediasession/#dom-mediasessionaction-togglescreenshare")]
    ToggleScreenshare,

    /// <summary>Ends a call.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionaction-hangup"/>
    [IdlMember("hangup", "https://w3c.github.io/mediasession/#dom-mediasessionaction-hangup")]
    HangUp,

    /// <summary>Moves to the previous slide of a presentation.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionaction-previousslide"/>
    [IdlMember("previousslide", "https://w3c.github.io/mediasession/#dom-mediasessionaction-previousslide")]
    PreviousSlide,

    /// <summary>Moves to the next slide of a presentation.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionaction-nextslide"/>
    [IdlMember("nextslide", "https://w3c.github.io/mediasession/#dom-mediasessionaction-nextslide")]
    NextSlide,

    /// <summary>Shows the media in a picture-in-picture window.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionaction-enterpictureinpicture"/>
    [IdlMember("enterpictureinpicture", "https://w3c.github.io/mediasession/#dom-mediasessionaction-enterpictureinpicture")]
    EnterPictureInPicture,

    /// <summary>The user agent detected that the user speaks (with a muted microphone, e.g. to unmute it).</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionaction-voiceactivity"/>
    [IdlMember("voiceactivity", "https://w3c.github.io/mediasession/#dom-mediasessionaction-voiceactivity")]
    VoiceActivity,
}

/// <summary>Why the user agent asks to show the media in a picture-in-picture window (https://w3c.github.io/mediasession/#enumdef-mediasessionenterpictureinpicturereason).</summary>
/// <seealso href="https://w3c.github.io/mediasession/#enumdef-mediasessionenterpictureinpicturereason"/>
[IdlInterface("MediaSessionEnterPictureInPictureReason", "https://w3c.github.io/mediasession/#enumdef-mediasessionenterpictureinpicturereason")]
public enum MediaSessionEnterPictureInPictureReason
{
    /// <summary>Another reason.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionenterpictureinpicturereason-other"/>
    [IdlMember("other", "https://w3c.github.io/mediasession/#dom-mediasessionenterpictureinpicturereason-other")]
    Other,

    /// <summary>The user asked for it.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionenterpictureinpicturereason-useraction"/>
    [IdlMember("useraction", "https://w3c.github.io/mediasession/#dom-mediasessionenterpictureinpicturereason-useraction")]
    UserAction,

    /// <summary>The content is hidden by another window.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionenterpictureinpicturereason-contentoccluded"/>
    [IdlMember("contentoccluded", "https://w3c.github.io/mediasession/#dom-mediasessionenterpictureinpicturereason-contentoccluded")]
    ContentOccluded,
}

/// <summary>The handler of a media session action (https://w3c.github.io/mediasession/#callbackdef-mediasessionactionhandler).</summary>
/// <param name="details">The action and its parameters.</param>
/// <seealso href="https://w3c.github.io/mediasession/#callbackdef-mediasessionactionhandler"/>
[IdlInterface("MediaSessionActionHandler", "https://w3c.github.io/mediasession/#callbackdef-mediasessionactionhandler")]
public delegate void MediaSessionActionHandler(MediaSessionActionDetails details);

/// <summary>An image of the artwork of a media (https://w3c.github.io/mediasession/#dictdef-mediaimage).</summary>
/// <seealso href="https://w3c.github.io/mediasession/#dictdef-mediaimage"/>
[IdlInterface("MediaImage", "https://w3c.github.io/mediasession/#dictdef-mediaimage")]
public sealed class MediaImage
{
    /// <summary>Gets the URL of the image (an absolute URL once it is in a <see cref="MediaMetadata"/>).</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediaimage-src"/>
    [IdlMember("src", "https://w3c.github.io/mediasession/#dom-mediaimage-src")]
    public required string Src { get; init; }

    /// <summary>Gets the sizes of the image, as the <c>sizes</c> attribute of a <c>link</c> element (e.g. <c>96x96</c>).</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediaimage-sizes"/>
    [IdlMember("sizes", "https://w3c.github.io/mediasession/#dom-mediaimage-sizes")]
    public string Sizes { get; init; } = "";

    /// <summary>Gets the MIME type of the image.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediaimage-type"/>
    [IdlMember("type", "https://w3c.github.io/mediasession/#dom-mediaimage-type")]
    public string Type { get; init; } = "";

    /// <summary>https://w3c.github.io/mediasession/#convert-artwork-algorithm: the images, with their URL parsed with a base URL.</summary>
    internal static List<MediaImage> ConvertArtwork(IReadOnlyList<MediaImage> artwork, UrlRecord baseUrl)
    {
        var result = new List<MediaImage>(artwork.Count);
        foreach (var image in artwork)
        {
            var url = UrlParser.Parse(image.Src, baseUrl) ?? throw new TypeErrorException($"'{image.Src}' is not a valid URL");
            result.Add(new MediaImage { Src = url.Serialize(), Sizes = image.Sizes, Type = image.Type });
        }

        return result;
    }
}

/// <summary>The initial values of a <see cref="MediaMetadata"/> (https://w3c.github.io/mediasession/#dictdef-mediametadatainit).</summary>
/// <seealso href="https://w3c.github.io/mediasession/#dictdef-mediametadatainit"/>
[IdlInterface("MediaMetadataInit", "https://w3c.github.io/mediasession/#dictdef-mediametadatainit")]
public sealed class MediaMetadataInit
{
    /// <summary>Gets the title of the media.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediametadatainit-title"/>
    [IdlMember("title", "https://w3c.github.io/mediasession/#dom-mediametadatainit-title")]
    public string Title { get; init; } = "";

    /// <summary>Gets the artist of the media.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediametadatainit-artist"/>
    [IdlMember("artist", "https://w3c.github.io/mediasession/#dom-mediametadatainit-artist")]
    public string Artist { get; init; } = "";

    /// <summary>Gets the album of the media.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediametadatainit-album"/>
    [IdlMember("album", "https://w3c.github.io/mediasession/#dom-mediametadatainit-album")]
    public string Album { get; init; } = "";

    /// <summary>Gets the images of the artwork of the media.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediametadatainit-artwork"/>
    [IdlMember("artwork", "https://w3c.github.io/mediasession/#dom-mediametadatainit-artwork")]
    public IReadOnlyList<MediaImage> Artwork { get; init; } = [];

    /// <summary>Gets the chapters of the media.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediametadatainit-chapterinfo"/>
    [IdlMember("chapterInfo", "https://w3c.github.io/mediasession/#dom-mediametadatainit-chapterinfo")]
    public IReadOnlyList<ChapterInformationInit> ChapterInfo { get; init; } = [];
}

/// <summary>The initial values of a <see cref="ChapterInformation"/> (https://w3c.github.io/mediasession/#dictdef-chapterinformationinit).</summary>
/// <seealso href="https://w3c.github.io/mediasession/#dictdef-chapterinformationinit"/>
[IdlInterface("ChapterInformationInit", "https://w3c.github.io/mediasession/#dictdef-chapterinformationinit")]
public sealed class ChapterInformationInit
{
    /// <summary>Gets the title of the chapter.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-chapterinformationinit-title"/>
    [IdlMember("title", "https://w3c.github.io/mediasession/#dom-chapterinformationinit-title")]
    public string Title { get; init; } = "";

    /// <summary>Gets the start time of the chapter, in seconds.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-chapterinformationinit-starttime"/>
    [IdlMember("startTime", "https://w3c.github.io/mediasession/#dom-chapterinformationinit-starttime")]
    public double StartTime { get; init; }

    /// <summary>Gets the images of the artwork of the chapter.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-chapterinformationinit-artwork"/>
    [IdlMember("artwork", "https://w3c.github.io/mediasession/#dom-chapterinformationinit-artwork")]
    public IReadOnlyList<MediaImage> Artwork { get; init; } = [];
}

/// <summary>A chapter of a media (https://w3c.github.io/mediasession/#chapterinformation).</summary>
/// <seealso href="https://w3c.github.io/mediasession/#chapterinformation"/>
[IdlInterface("ChapterInformation", "https://w3c.github.io/mediasession/#chapterinformation")]
public sealed class ChapterInformation
{
    internal ChapterInformation(ChapterInformationInit init, UrlRecord baseUrl)
    {
        Title = init.Title;
        StartTime = init.StartTime;
        Artwork = MediaImage.ConvertArtwork(init.Artwork, baseUrl);
    }

    /// <summary>Gets the title of the chapter.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-chapterinformation-title"/>
    [IdlMember("title", "https://w3c.github.io/mediasession/#dom-chapterinformation-title")]
    public string Title { get; }

    /// <summary>Gets the start time of the chapter, in seconds.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-chapterinformation-starttime"/>
    [IdlMember("startTime", "https://w3c.github.io/mediasession/#dom-chapterinformation-starttime")]
    public double StartTime { get; }

    /// <summary>Gets the images of the artwork of the chapter, with absolute URLs.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-chapterinformation-artwork"/>
    [IdlMember("artwork", "https://w3c.github.io/mediasession/#dom-chapterinformation-artwork")]
    public IReadOnlyList<MediaImage> Artwork { get; }
}

/// <summary>
/// The metadata of a media (https://w3c.github.io/mediasession/#the-mediametadata-interface): its title, its artist, its
/// album, its artwork and its chapters, shown by the media controls of the platform when it is the metadata of a
/// <see cref="MediaSession"/>.
/// </summary>
/// <seealso href="https://w3c.github.io/mediasession/#mediametadata"/>
[IdlInterface("MediaMetadata", "https://w3c.github.io/mediasession/#mediametadata")]
public sealed class MediaMetadata
{
    private readonly List<WeakReference<MediaSession>> _sessions = [];
    private IReadOnlyList<MediaImage> _artwork;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaMetadata"/> class (https://w3c.github.io/mediasession/#dom-mediametadata-mediametadata).
    /// The URLs of the images are parsed with the API base URL of the current global object.
    /// </summary>
    /// <param name="init">The initial values.</param>
    /// <exception cref="TypeErrorException">The URL of an image is not valid.</exception>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediametadata-mediametadata"/>
    [IdlMember("constructor", "https://w3c.github.io/mediasession/#dom-mediametadata-mediametadata")]
    public MediaMetadata(MediaMetadataInit? init = null)
    {
        init ??= new MediaMetadataInit();
        var baseUrl = GetBaseUrl();
        Title = init.Title;
        Artist = init.Artist;
        Album = init.Album;
        _artwork = MediaImage.ConvertArtwork(init.Artwork, baseUrl);
        ChapterInfo = [.. init.ChapterInfo.Select(chapter => new ChapterInformation(chapter, baseUrl))];
    }

    /// <summary>Gets or sets the title of the media.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediametadata-title"/>
    [IdlMember("title", "https://w3c.github.io/mediasession/#dom-mediametadata-title")]
    public string Title
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
            NotifySessions();
        }
    }

    /// <summary>Gets or sets the artist of the media.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediametadata-artist"/>
    [IdlMember("artist", "https://w3c.github.io/mediasession/#dom-mediametadata-artist")]
    public string Artist
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
            NotifySessions();
        }
    }

    /// <summary>Gets or sets the album of the media.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediametadata-album"/>
    [IdlMember("album", "https://w3c.github.io/mediasession/#dom-mediametadata-album")]
    public string Album
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
            NotifySessions();
        }
    }

    /// <summary>
    /// Gets or sets the images of the artwork of the media, with absolute URLs: the URLs of the new images are parsed with the
    /// API base URL of the current global object (https://w3c.github.io/mediasession/#convert-artwork-algorithm). The pages
    /// get a frozen array of frozen objects.
    /// </summary>
    /// <exception cref="TypeErrorException">The URL of an image is not valid.</exception>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediametadata-artwork"/>
    [IdlMember("artwork", "https://w3c.github.io/mediasession/#dom-mediametadata-artwork")]
    public IReadOnlyList<MediaImage> Artwork
    {
        get => _artwork;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _artwork = MediaImage.ConvertArtwork(value, GetBaseUrl());
            NotifySessions();
        }
    }

    /// <summary>Gets the chapters of the media.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediametadata-chapterinfo"/>
    [IdlMember("chapterInfo", "https://w3c.github.io/mediasession/#dom-mediametadata-chapterinfo")]
    public IReadOnlyList<ChapterInformation> ChapterInfo { get; }

    /// <summary>The metadata becomes the metadata of a media session, which is notified when the metadata changes.</summary>
    internal void Attach(MediaSession session)
    {
        _sessions.RemoveAll(reference => !reference.TryGetTarget(out var target) || ReferenceEquals(target, session));
        _sessions.Add(new WeakReference<MediaSession>(session));
    }

    /// <summary>The metadata is no longer the metadata of a media session.</summary>
    internal void Detach(MediaSession session) => _sessions.RemoveAll(reference => !reference.TryGetTarget(out var target) || ReferenceEquals(target, session));

    /// <summary>https://w3c.github.io/mediasession/#update-metadata-algorithm: the media sessions of the metadata are updated.</summary>
    private void NotifySessions()
    {
        foreach (var reference in _sessions.ToArray())
        {
            if (reference.TryGetTarget(out var session) && ReferenceEquals(session.Metadata, this))
            {
                session.OnChanged();
            }
        }
    }

    private static UrlRecord GetBaseUrl() => GlobalScope.Current?.ApiBaseUrl ?? UrlParser.Parse("about:blank", baseUrl: null)!;
}

/// <summary>The position of the playback of a media session (https://w3c.github.io/mediasession/#dictdef-mediapositionstate).</summary>
/// <seealso href="https://w3c.github.io/mediasession/#dictdef-mediapositionstate"/>
[IdlInterface("MediaPositionState", "https://w3c.github.io/mediasession/#dictdef-mediapositionstate")]
public sealed class MediaPositionState
{
    /// <summary>Gets the duration of the media, in seconds (positive infinity for a live stream).</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediapositionstate-duration"/>
    [IdlMember("duration", "https://w3c.github.io/mediasession/#dom-mediapositionstate-duration")]
    public double? Duration { get; init; }

    /// <summary>Gets the playback rate (negative when the media plays backward).</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediapositionstate-playbackrate"/>
    [IdlMember("playbackRate", "https://w3c.github.io/mediasession/#dom-mediapositionstate-playbackrate")]
    public double? PlaybackRate { get; init; }

    /// <summary>Gets the position of the playback, in seconds.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediapositionstate-position"/>
    [IdlMember("position", "https://w3c.github.io/mediasession/#dom-mediapositionstate-position")]
    public double? Position { get; init; }
}

/// <summary>An action of a media session and its parameters, given to the action handler (https://w3c.github.io/mediasession/#dictdef-mediasessionactiondetails).</summary>
/// <seealso href="https://w3c.github.io/mediasession/#dictdef-mediasessionactiondetails"/>
[IdlInterface("MediaSessionActionDetails", "https://w3c.github.io/mediasession/#dictdef-mediasessionactiondetails")]
public sealed class MediaSessionActionDetails
{
    /// <summary>Gets the action.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionactiondetails-action"/>
    [IdlMember("action", "https://w3c.github.io/mediasession/#dom-mediasessionactiondetails-action")]
    public required MediaSessionAction Action { get; init; }

    /// <summary>Gets the time to seek by, in seconds, for <see cref="MediaSessionAction.SeekBackward"/> and <see cref="MediaSessionAction.SeekForward"/> (null for the default offset).</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionactiondetails-seekoffset"/>
    [IdlMember("seekOffset", "https://w3c.github.io/mediasession/#dom-mediasessionactiondetails-seekoffset")]
    public double? SeekOffset { get; init; }

    /// <summary>Gets the time to seek to, in seconds, for <see cref="MediaSessionAction.SeekTo"/> (required for this action).</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionactiondetails-seektime"/>
    [IdlMember("seekTime", "https://w3c.github.io/mediasession/#dom-mediasessionactiondetails-seektime")]
    public double? SeekTime { get; init; }

    /// <summary>Gets whether the seek is part of a sequence of seeks (<see cref="MediaSessionAction.SeekTo"/>), which can be faster and less precise.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionactiondetails-fastseek"/>
    [IdlMember("fastSeek", "https://w3c.github.io/mediasession/#dom-mediasessionactiondetails-fastseek")]
    public bool? FastSeek { get; init; }

    /// <summary>Gets whether the microphone, the camera or the screen sharing is turned on (the toggle actions).</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionactiondetails-isactivating"/>
    [IdlMember("isActivating", "https://w3c.github.io/mediasession/#dom-mediasessionactiondetails-isactivating")]
    public bool? IsActivating { get; init; }

    /// <summary>Gets why the media is shown in a picture-in-picture window (<see cref="MediaSessionAction.EnterPictureInPicture"/>).</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasessionactiondetails-enterpictureinpicturereason"/>
    [IdlMember("enterPictureInPictureReason", "https://w3c.github.io/mediasession/#dom-mediasessionactiondetails-enterpictureinpicturereason")]
    public MediaSessionEnterPictureInPictureReason? EnterPictureInPictureReason { get; init; }
}

/// <summary>
/// The media session of a window (https://w3c.github.io/mediasession/#the-mediasession-interface, <c>navigator.mediaSession</c>):
/// what the page plays (its metadata, its playback state and its position), and the actions that the media controls of the
/// platform can trigger. The host is notified of the changes with <see cref="MediaSessionOptions.SessionChanged"/>, and
/// triggers the actions with <see cref="HandleAction"/>.
/// </summary>
/// <seealso href="https://w3c.github.io/mediasession/#mediasession"/>
[IdlInterface("MediaSession", "https://w3c.github.io/mediasession/#mediasession")]
public sealed class MediaSession
{
    private readonly Window _window;
    private readonly Dictionary<MediaSessionAction, MediaSessionActionHandler> _actionHandlers = [];

    internal MediaSession(Window window) => _window = window;

    /// <summary>Gets or sets the metadata of the media that the page plays, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasession-metadata"/>
    [IdlMember("metadata", "https://w3c.github.io/mediasession/#dom-mediasession-metadata")]
    public MediaMetadata? Metadata
    {
        get;
        set
        {
            if (ReferenceEquals(field, value))
                return;

            field?.Detach(this);
            field = value;
            value?.Attach(this);
            OnChanged();
        }
    }

    /// <summary>Gets or sets the playback state that the page declares (<see cref="MediaSessionPlaybackState.None"/> by default).</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasession-playbackstate"/>
    [IdlMember("playbackState", "https://w3c.github.io/mediasession/#dom-mediasession-playbackstate")]
    public MediaSessionPlaybackState PlaybackState
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            OnChanged();
        }
    }

    /// <summary>Gets the position state set by <see cref="SetPositionState"/>, with all its members, or null when the page did not set it or cleared it.</summary>
    [NotInIdl]
    public MediaPositionState? PositionState { get; private set; }

    /// <summary>Gets the time the position state was set (<see cref="TimeProvider.GetUtcNow"/> of the event loop), to compute the current position from the playback rate.</summary>
    [NotInIdl]
    public DateTimeOffset PositionStateUpdateTime { get; private set; }

    /// <summary>Gets the actions that have a handler: the media controls of the platform show them.</summary>
    [NotInIdl]
    public IReadOnlyCollection<MediaSessionAction> SupportedActions => [.. _actionHandlers.Keys.Order()];

    /// <summary>Gets whether the page says that its microphone is active (<c>setMicrophoneActive()</c>).</summary>
    [NotInIdl]
    public bool IsMicrophoneActive { get; private set; }

    /// <summary>Gets whether the page says that its camera is active (<c>setCameraActive()</c>).</summary>
    [NotInIdl]
    public bool IsCameraActive { get; private set; }

    /// <summary>Gets whether the page says that it shares the screen (<c>setScreenshareActive()</c>).</summary>
    [NotInIdl]
    public bool IsScreenshareActive { get; private set; }

    /// <summary>Gets the window of the media session.</summary>
    [NotInIdl]
    public Window Window => _window;

    /// <summary>Sets or removes the handler of an action (https://w3c.github.io/mediasession/#dom-mediasession-setactionhandler).</summary>
    /// <param name="action">The action.</param>
    /// <param name="handler">The handler, or null to remove the handler of the action.</param>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasession-setactionhandler"/>
    [IdlMember("setActionHandler", "https://w3c.github.io/mediasession/#dom-mediasession-setactionhandler")]
    public void SetActionHandler(MediaSessionAction action, MediaSessionActionHandler? handler)
    {
        var changed = handler is null ? _actionHandlers.Remove(action) : _actionHandlers.TryAdd(action, handler) || !ReferenceEquals(_actionHandlers[action], handler);
        if (handler is not null)
        {
            _actionHandlers[action] = handler;
        }

        if (changed)
        {
            OnChanged();
        }
    }

    /// <summary>
    /// Sets the position of the playback (https://w3c.github.io/mediasession/#dom-mediasession-setpositionstate), or clears
    /// it when the state is null or empty. The position defaults to 0, and the playback rate to 1.
    /// </summary>
    /// <param name="state">The position state.</param>
    /// <exception cref="TypeErrorException">The duration is missing or negative, the position is negative or greater than the duration, or the playback rate is 0.</exception>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasession-setpositionstate"/>
    [IdlMember("setPositionState", "https://w3c.github.io/mediasession/#dom-mediasession-setpositionstate")]
    public void SetPositionState(MediaPositionState? state = null)
    {
        if (state is null || (state.Duration is null && state.Position is null && state.PlaybackRate is null))
        {
            if (PositionState is null)
                return;

            PositionState = null;
            OnChanged();
            return;
        }

        if (state.Duration is not { } duration)
            throw new TypeErrorException("The duration of the position state is required");

        if (double.IsNaN(duration) || duration < 0)
            throw new TypeErrorException("The duration of the position state must not be negative");

        var position = state.Position ?? 0;
        if (position < 0 || position > duration)
            throw new TypeErrorException("The position must be between 0 and the duration");

        var playbackRate = state.PlaybackRate ?? 1;
        if (playbackRate == 0)
            throw new TypeErrorException("The playback rate must not be 0");

        PositionState = new MediaPositionState { Duration = duration, Position = position, PlaybackRate = playbackRate };
        PositionStateUpdateTime = _window.EventLoop.TimeProvider.GetUtcNow();
        OnChanged();
    }

    /// <summary>Tells the user agent whether the microphone of the page is active (https://w3c.github.io/mediasession/#dom-mediasession-setmicrophoneactive).</summary>
    /// <param name="active">Whether the microphone is active (not muted).</param>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasession-setmicrophoneactive"/>
    [IdlMember("setMicrophoneActive", "https://w3c.github.io/mediasession/#dom-mediasession-setmicrophoneactive")]
    public Task SetMicrophoneActive(bool active) => UpdateCaptureState(active, (session, value) => session.IsMicrophoneActive = value, session => session.IsMicrophoneActive);

    /// <summary>Tells the user agent whether the camera of the page is active (https://w3c.github.io/mediasession/#dom-mediasession-setcameraactive).</summary>
    /// <param name="active">Whether the camera is active (not muted).</param>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasession-setcameraactive"/>
    [IdlMember("setCameraActive", "https://w3c.github.io/mediasession/#dom-mediasession-setcameraactive")]
    public Task SetCameraActive(bool active) => UpdateCaptureState(active, (session, value) => session.IsCameraActive = value, session => session.IsCameraActive);

    /// <summary>Tells the user agent whether the page shares the screen (https://w3c.github.io/mediasession/#dom-mediasession-setscreenshareactive).</summary>
    /// <param name="active">Whether the screen sharing is active.</param>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-mediasession-setscreenshareactive"/>
    [IdlMember("setScreenshareActive", "https://w3c.github.io/mediasession/#dom-mediasession-setscreenshareactive")]
    public Task SetScreenshareActive(bool active) => UpdateCaptureState(active, (session, value) => session.IsScreenshareActive = value, session => session.IsScreenshareActive);

    /// <summary>
    /// Triggers an action of the media session, as the media controls of the platform do (https://w3c.github.io/mediasession/#handle-media-session-action):
    /// the handler of the action is invoked by a task of the event loop of the window. Call it from the thread of the event loop.
    /// </summary>
    /// <param name="details">The action and its parameters.</param>
    /// <returns>Whether the action has a handler.</returns>
    /// <exception cref="ArgumentException"><see cref="MediaSessionAction.SeekTo"/> has no <see cref="MediaSessionActionDetails.SeekTime"/>.</exception>
    [NotInIdl]
    public bool HandleAction(MediaSessionActionDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (details is { Action: MediaSessionAction.SeekTo, SeekTime: null })
            throw new ArgumentException("The seekto action needs a seek time", nameof(details));

        if (!_actionHandlers.ContainsKey(details.Action))
            return false;

        _window.QueueGlobalTask(() =>
        {
            if (_actionHandlers.TryGetValue(details.Action, out var handler))
            {
                handler(details);
            }
        });
        return true;
    }

    /// <summary>The media session changed: the host is notified.</summary>
    internal void OnChanged() => _window.Options.MediaSession?.SessionChanged?.Invoke(CallbackContext.For(_window), this);

    /// <summary>https://w3c.github.io/mediasession/#update-capture-state-algorithm</summary>
    private Task UpdateCaptureState(bool active, Action<MediaSession, bool> setState, Func<MediaSession, bool> getState)
    {
        if (!_window.Document.IsFullyActive)
            return Task.FromException(DomException.InvalidState("The document is not fully active"));

        var promise = new TaskCompletionSource();
        _window.QueueGlobalTask(() =>
        {
            if (getState(this) != active)
            {
                setState(this, active);
                OnChanged();
            }

            promise.TrySetResult();
        });
        return promise.Task;
    }
}

// The Media Session API (https://w3c.github.io/mediasession/)
public sealed partial class Navigator
{
    private MediaSession? _mediaSession;

    /// <summary>Gets the media session of the window (https://w3c.github.io/mediasession/#dom-navigator-mediasession).</summary>
    /// <seealso href="https://w3c.github.io/mediasession/#dom-navigator-mediasession"/>
    [IdlMember("mediaSession", "https://w3c.github.io/mediasession/#dom-navigator-mediasession")]
    public MediaSession MediaSession => _mediaSession ??= new MediaSession(_window);
}
