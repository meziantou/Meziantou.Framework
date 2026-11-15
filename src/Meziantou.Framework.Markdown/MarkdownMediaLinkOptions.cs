namespace Meziantou.Framework.Markdown;

/// <summary>How <see cref="MarkdownExtensions.MediaLinks"/> renders the images of audio, video, and of the sites that host them.</summary>
public sealed record MarkdownMediaLinkOptions
{
    // Before Default, which uses it
    private static readonly IReadOnlyDictionary<string, string> DefaultMimeTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".3gp"] = "video/3gpp",
        [".3g2"] = "video/3gpp2",
        [".avi"] = "video/x-msvideo",
        [".uvh"] = "video/vnd.dece.hd",
        [".uvm"] = "video/vnd.dece.mobile",
        [".uvu"] = "video/vnd.uvvu.mp4",
        [".uvp"] = "video/vnd.dece.pd",
        [".uvs"] = "video/vnd.dece.sd",
        [".uvv"] = "video/vnd.dece.video",
        [".fvt"] = "video/vnd.fvt",
        [".f4v"] = "video/x-f4v",
        [".flv"] = "video/x-flv",
        [".fli"] = "video/x-fli",
        [".h261"] = "video/h261",
        [".h263"] = "video/h263",
        [".h264"] = "video/h264",
        [".jpm"] = "video/jpm",
        [".jpgv"] = "video/jpeg",
        [".m4v"] = "video/x-m4v",
        [".asf"] = "video/x-ms-asf",
        [".pyv"] = "video/vnd.ms-playready.media.pyv",
        [".wm"] = "video/x-ms-wm",
        [".wmx"] = "video/x-ms-wmx",
        [".wmv"] = "video/x-ms-wmv",
        [".wvx"] = "video/x-ms-wvx",
        [".mj2"] = "video/mj2",
        [".mxu"] = "video/vnd.mpegurl",
        [".mpeg"] = "video/mpeg",
        [".mp4"] = "video/mp4",
        [".ogv"] = "video/ogg",
        [".webm"] = "video/webm",
        [".qt"] = "video/quicktime",
        [".movie"] = "video/x-sgi-movie",
        [".viv"] = "video/vnd.vivo",
        [".adp"] = "audio/adpcm",
        [".aac"] = "audio/x-aac",
        [".aif"] = "audio/x-aiff",
        [".uva"] = "audio/vnd.dece.audio",
        [".eol"] = "audio/vnd.digital-winds",
        [".dra"] = "audio/vnd.dra",
        [".dts"] = "audio/vnd.dts",
        [".dtshd"] = "audio/vnd.dts.hd",
        [".rip"] = "audio/vnd.rip",
        [".lvp"] = "audio/vnd.lucent.voice",
        [".m3u"] = "audio/x-mpegurl",
        [".pya"] = "audio/vnd.ms-playready.media.pya",
        [".wma"] = "audio/x-ms-wma",
        [".wax"] = "audio/x-ms-wax",
        [".mid"] = "audio/midi",
        [".mp3"] = "audio/mpeg",
        [".mpga"] = "audio/mpeg",
        [".mp4a"] = "audio/mp4",
        [".ecelp4800"] = "audio/vnd.nuera.ecelp4800",
        [".ecelp7470"] = "audio/vnd.nuera.ecelp7470",
        [".ecelp9600"] = "audio/vnd.nuera.ecelp9600",
        [".oga"] = "audio/ogg",
        [".ogg"] = "audio/ogg",
        [".weba"] = "audio/webm",
        [".ram"] = "audio/x-pn-realaudio",
        [".rmp"] = "audio/x-pn-realaudio-plugin",
        [".au"] = "audio/basic",
        [".wav"] = "audio/x-wav",
    };

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

    /// <summary>Gets the MIME types of the extensions of the audio and the video files, such as <c>.mp4</c>.</summary>
    public IReadOnlyDictionary<string, string> ExtensionToMimeType { get; init; } = DefaultMimeTypes;

    /// <summary>Gets the sites whose links are rendered as an <c>&lt;iframe&gt;</c>, tried in order.</summary>
    public IReadOnlyList<IMarkdownMediaHost> Hosts { get; init; } = IMarkdownMediaHost.KnownHosts;
}
