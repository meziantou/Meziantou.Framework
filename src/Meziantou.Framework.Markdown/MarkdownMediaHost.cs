using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Markdown;

/// <summary>Creates the sites of <see cref="MarkdownMediaLinkOptions.Hosts"/>.</summary>
public static class MarkdownMediaHost
{
    private const string YouTubeEmbedUrl = "https://www.youtube.com/embed/";

    // The sites of Markdig, with the classes of their <iframe>: YouTube Shorts are tried before the other videos of YouTube
    internal static readonly IMarkdownMediaHost[] KnownHosts =
    [
        Create("www.youtube.com", GetYouTubeShortsIframeUrl, iframeClass: "youtubeshort"),
        Create("www.youtube.com", GetYouTubeIframeUrl, iframeClass: "youtube"),
        Create("youtu.be", GetYouTubeShortLinkIframeUrl, iframeClass: "youtube"),
        Create("vimeo.com", uri => GetLastPathSegment(uri) is { } id ? "https://player.vimeo.com/video/" + id : null, iframeClass: "vimeo"),
        Create("music.yandex.ru", GetYandexMusicIframeUrl, allowFullScreen: false, iframeClass: "yandex"),
        Create("ok.ru", uri => GetLastPathSegment(uri) is { } id ? "https://ok.ru/videoembed/" + id : null, iframeClass: "odnoklassniki"),
    ];

    /// <summary>Creates a site from its host, which also matches its subdomains, and the function that gives the URL of the <c>&lt;iframe&gt;</c> of a link, or <see langword="null"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="host"/> is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> or <paramref name="handler"/> is <see langword="null"/>.</exception>
    public static IMarkdownMediaHost Create(string host, Func<Uri, string?> handler, bool allowFullScreen = true, string? iframeClass = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);
        ArgumentNullException.ThrowIfNull(handler);

        return new MediaHost(host, handler, allowFullScreen, iframeClass);
    }

    // https://www.youtube.com/shorts/ID: the short in its own player
    private static string? GetYouTubeShortsIframeUrl(Uri uri)
    {
        const string Prefix = "/shorts/";
        var path = uri.AbsolutePath;
        return path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ? GetYouTubeEmbedUrl(path[Prefix.Length..], startTime: null) : null;
    }

    // https://www.youtube.com/watch?v=ID&t=SECONDS, or a URL of the player itself, https://www.youtube.com/embed/...
    private static string? GetYouTubeIframeUrl(Uri uri)
    {
        var path = uri.AbsolutePath;
        if (IsPathOrSubpath(path, "/embed"))
            return uri.ToString();

        if (!IsPathOrSubpath(path, "/watch"))
            return null;

        return GetYouTubeEmbedUrl(GetQueryParameter(uri, "v"), GetQueryParameter(uri, "t"));
    }

    // https://youtu.be/ID?t=SECONDS
    private static string? GetYouTubeShortLinkIframeUrl(Uri uri) => GetYouTubeEmbedUrl(uri.AbsolutePath.TrimStart('/'), GetQueryParameter(uri, "t"));

    private static string? GetYouTubeEmbedUrl(string? videoId, string? startTime)
    {
        if (string.IsNullOrEmpty(videoId))
            return null;

        return string.IsNullOrEmpty(startTime) ? YouTubeEmbedUrl + videoId : YouTubeEmbedUrl + videoId + "?start=" + startTime;
    }

    // https://music.yandex.ru/album/ALBUM/track/TRACK
    private static string? GetYandexMusicIframeUrl(Uri uri)
    {
        var segments = uri.GetComponents(UriComponents.Path, UriFormat.Unescaped).Split('/');
        if (segments is not ["album", { Length: > 0 } album, "track", { Length: > 0 } track, ..])
            return null;

        return $"https://music.yandex.ru/iframe/#track/{track}/{album}/";
    }

    private static bool IsPathOrSubpath(string path, string expected) => path.Equals(expected, StringComparison.OrdinalIgnoreCase)
        || (path.Length > expected.Length && path[expected.Length] == '/' && path.StartsWith(expected, StringComparison.OrdinalIgnoreCase));

    // The last segment of the path, unescaped, which the sites use as the identifier of the video, or null when it is empty
    private static string? GetLastPathSegment(Uri uri)
    {
        var path = uri.GetComponents(UriComponents.Path, UriFormat.Unescaped);
        var segment = path[(path.LastIndexOf('/', StringComparison.Ordinal) + 1)..];
        return segment.Length > 0 ? segment : null;
    }

    // The value of the first parameter of the query with the name, as written in the URL
    private static string? GetQueryParameter(Uri uri, string name)
    {
        foreach (var parameter in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            if (parameter.Length > name.Length && parameter[name.Length] == '=' && parameter.StartsWith(name, StringComparison.Ordinal))
                return parameter[(name.Length + 1)..];
        }

        return null;
    }

    private sealed class MediaHost(string host, Func<Uri, string?> handler, bool allowFullScreen, string? iframeClass) : IMarkdownMediaHost
    {
        public string? Class => iframeClass;

        public bool AllowFullScreen => allowFullScreen;

        public bool TryHandle(Uri mediaUri, bool isSchemaRelative, [NotNullWhen(true)] out string? iframeUrl)
        {
            ArgumentNullException.ThrowIfNull(mediaUri);

            iframeUrl = IsHostOrSubdomain(mediaUri.Host) ? handler(mediaUri) : null;
            return !string.IsNullOrEmpty(iframeUrl);
        }

        // The host itself, or a subdomain of it: "www.youtube.com.example.org" is not YouTube
        private bool IsHostOrSubdomain(string value) => value.Equals(host, StringComparison.OrdinalIgnoreCase)
            || (value.Length > host.Length && value[^(host.Length + 1)] == '.' && value.EndsWith(host, StringComparison.OrdinalIgnoreCase));
    }
}
