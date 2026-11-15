using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Language.Markdown;

/// <summary>Creates the sites of <see cref="MarkdownMediaLinkOptions.Hosts"/>, as Markdig does.</summary>
public static class MarkdownMediaHost
{
    internal static readonly IMarkdownMediaHost[] KnownHosts =
    [
        Create("www.youtube.com", YouTubeShort, iframeClass: "youtubeshort"),
        Create("www.youtube.com", YouTube, iframeClass: "youtube"),
        Create("youtu.be", YouTubeShortened, iframeClass: "youtube"),
        Create("vimeo.com", Vimeo, iframeClass: "vimeo"),
        Create("music.yandex.ru", Yandex, allowFullScreen: false, iframeClass: "yandex"),
        Create("ok.ru", Odnoklassniki, iframeClass: "odnoklassniki"),
    ];

    /// <summary>Creates a site from its host, which also matches its subdomains, and the function that gives the URL of the <c>&lt;iframe&gt;</c> of a link, or <see langword="null"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="host"/> is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> or <paramref name="handler"/> is <see langword="null"/>.</exception>
    public static IMarkdownMediaHost Create(string host, Func<Uri, string?> handler, bool allowFullScreen = true, string? iframeClass = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);
        ArgumentNullException.ThrowIfNull(handler);

        return new DelegateHost(host, handler, allowFullScreen, iframeClass);
    }

    private sealed class DelegateHost(string host, Func<Uri, string?> handler, bool allowFullScreen, string? className) : IMarkdownMediaHost
    {
        public string? Class { get; } = className;

        public bool AllowFullScreen { get; } = allowFullScreen;

        public bool TryHandle(Uri mediaUri, bool isSchemaRelative, [NotNullWhen(true)] out string? iframeUrl)
        {
            ArgumentNullException.ThrowIfNull(mediaUri);

            if (!IsSameHostOrSubdomain(mediaUri.Host, host))
            {
                iframeUrl = null;
                return false;
            }

            iframeUrl = handler(mediaUri);
            return !string.IsNullOrEmpty(iframeUrl);
        }

        // A prefix match would accept "www.youtube.com.example.org"
        private static bool IsSameHostOrSubdomain(string value, string host) => string.Equals(value, host, StringComparison.OrdinalIgnoreCase)
            || (value.Length > host.Length && value.EndsWith(host, StringComparison.OrdinalIgnoreCase) && value[value.Length - host.Length - 1] == '.');
    }


    private static readonly string[] SplitAnd = ["&"];
    private static string[] SplitQuery(Uri uri)
    {
        var query = uri.Query.Substring(uri.Query.IndexOf('?', StringComparison.Ordinal) + 1);
        return query.Split(SplitAnd, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string? YouTube(Uri uri)
    {
        string uriPath = uri.AbsolutePath;
        if (string.Equals(uriPath, "/embed", StringComparison.OrdinalIgnoreCase) || uriPath.StartsWith("/embed/", StringComparison.OrdinalIgnoreCase))
        {
            return uri.ToString();
        }
        if (!string.Equals(uriPath, "/watch", StringComparison.OrdinalIgnoreCase) && !uriPath.StartsWith("/watch/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var queryParams = SplitQuery(uri);
        return BuildYouTubeIframeUrl(
            queryParams.FirstOrDefault(p => p.StartsWith("v=", StringComparison.Ordinal))?.Substring(2),
            queryParams.FirstOrDefault(p => p.StartsWith("t=", StringComparison.Ordinal))?.Substring(2)
        );
    }

    private static string? YouTubeShort(Uri uri)
    {
        string uriPath = uri.AbsolutePath;
        bool isYouTubeShort = uriPath.StartsWith("/shorts/", StringComparison.OrdinalIgnoreCase);
        if (!isYouTubeShort)
        {
            return null;
        }

        var shortId = uriPath.Substring("/shorts/".Length).Split('?').FirstOrDefault(); // the format might be "/shorts/6BUptHVuvyI?feature=share"
        return BuildYouTubeIframeUrl(shortId, null);
    }

    private static string? YouTubeShortened(Uri uri)
    {
        return BuildYouTubeIframeUrl(
            uri.AbsolutePath.Substring(1),
            SplitQuery(uri).FirstOrDefault(p => p.StartsWith("t=", StringComparison.Ordinal))?.Substring(2)
        );
    }

    private static string? BuildYouTubeIframeUrl(string? videoId, string? startTime)
    {
        if (string.IsNullOrEmpty(videoId))
        {
            return null;
        }
        string url = $"https://www.youtube.com/embed/{videoId}";
        return string.IsNullOrEmpty(startTime) ? url : $"{url}?start={startTime}";
    }

    private static string? Vimeo(Uri uri)
    {
        var items = uri.GetComponents(UriComponents.Path, UriFormat.Unescaped).Split('/');
        return items.Length > 0 ? $"https://player.vimeo.com/video/{ items[items.Length - 1] }" : null;
    }

    private static string? Odnoklassniki(Uri uri)
    {
        var items = uri.GetComponents(UriComponents.Path, UriFormat.Unescaped).Split('/');
        return items.Length > 0 ? $"https://ok.ru/videoembed/{ items[items.Length - 1] }" : null;
    }

    private static string? Yandex(Uri uri)
    {
        string[] items = uri.GetComponents(UriComponents.Path, UriFormat.Unescaped).Split('/');
        string? albumKeyword = items.Skip(0).FirstOrDefault();
        string? albumId = items.Skip(1).FirstOrDefault();
        string? trackKeyword = items.Skip(2).FirstOrDefault();
        string? trackId = items.Skip(3).FirstOrDefault();

        if (albumKeyword != "album" || albumId is null || trackKeyword != "track" || trackId is null)
        {
            return null;
        }

        return $"https://music.yandex.ru/iframe/#track/{trackId}/{albumId}/";
    }
}
