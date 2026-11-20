using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Meziantou.Framework.Markdown;

/// <summary>How <see cref="MarkdownExtensions.MediaLinks"/> renders the images of audio, video, and of the sites that host them.</summary>
public sealed record MarkdownMediaLinkOptions
{
    // Before Default, which uses it. The formats that browsers play in <video> and <audio>: a link to another format, such as
    // .avi or .wmv, stays an image, which is a link, rather than a player that cannot play it.
    private static readonly FrozenDictionary<string, string> DefaultMimeTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // Video
        [".3g2"] = "video/3gpp2",
        [".3gp"] = "video/3gpp",
        [".m4v"] = "video/mp4",
        [".mkv"] = "video/x-matroska",
        [".mov"] = "video/quicktime",
        [".mp4"] = "video/mp4",
        [".mpeg"] = "video/mpeg",
        [".mpg"] = "video/mpeg",
        [".ogv"] = "video/ogg",
        [".webm"] = "video/webm",

        // Audio
        [".aac"] = "audio/aac",
        [".aif"] = "audio/aiff",
        [".aiff"] = "audio/aiff",
        [".flac"] = "audio/flac",
        [".m4a"] = "audio/mp4",
        [".mka"] = "audio/x-matroska",
        [".mp3"] = "audio/mpeg",
        [".oga"] = "audio/ogg",
        [".ogg"] = "audio/ogg",
        [".opus"] = "audio/ogg",
        [".wav"] = "audio/wav",
        [".weba"] = "audio/webm",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the options of Markdig: a width of 500, a height of 281, controls, and the hosts of <see cref="IMarkdownMediaHost.KnownHosts"/>.</summary>
    public static MarkdownMediaLinkOptions Default { get; } = new();

    /// <summary>Gets the width of the element, or an empty string for none.</summary>
    public string Width { get; init; } = "500";

    /// <summary>Gets the height of the element, or an empty string for none. Audio has no height.</summary>
    public string Height { get; init; } = "281";

    /// <summary>Gets a value indicating whether audio and video have the <c>controls</c> attribute.</summary>
    public bool AddControls { get; init; } = true;

    /// <summary>Gets the class of the element, or an empty string for none.</summary>
    public string Class { get; init; } = "";

    /// <summary>
    /// Gets the MIME types of the extensions of the audio and the video files, such as <c>.mp4</c>. By default, the formats that
    /// browsers play: <c>.mp4</c>, <c>.m4v</c>, <c>.webm</c>, <c>.ogv</c>, <c>.mov</c>, <c>.mkv</c>, <c>.3gp</c>, <c>.3g2</c>,
    /// <c>.mpeg</c> and <c>.mpg</c> for video, and <c>.mp3</c>, <c>.m4a</c>, <c>.aac</c>, <c>.ogg</c>, <c>.oga</c>,
    /// <c>.opus</c>, <c>.wav</c>, <c>.weba</c>, <c>.flac</c>, <c>.mka</c>, <c>.aif</c> and <c>.aiff</c> for audio. The keys
    /// start with a dot, and a dictionary whose comparer ignores the case matches <c>.MP4</c> too.
    /// </summary>
    public IReadOnlyDictionary<string, string> ExtensionToMimeType { get; init; } = DefaultMimeTypes;

    /// <summary>Gets the sites whose links are rendered as an <c>&lt;iframe&gt;</c>, tried in order.</summary>
    public ImmutableArray<IMarkdownMediaHost> Hosts { get => field.IsDefault ? [] : field; init; } = IMarkdownMediaHost.KnownHosts;
}
