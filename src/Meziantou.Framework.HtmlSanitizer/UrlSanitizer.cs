using System.Text.RegularExpressions;

namespace Meziantou.Framework.Sanitizers;

/// <summary>
/// Provides URL sanitization to determine if URLs are safe for use in HTML content.
/// </summary>
public static partial class UrlSanitizer
{
    // https://github.com/angular/angular/blob/4d36b2f6e9a1a7673b3f233752895c96ca7dba1e/packages/core/src/sanitization/url_sanitizer.ts
    /**
     * A pattern that recognizes a commonly useful subset of URLs that are safe.
     *
     * This regular expression matches a subset of URLs that will not cause script
     * execution if used in URL context within a HTML document. Specifically, this
     * regular expression matches if (comment from here on and regex copied from
     * Soy's EscapingConventions):
     * (1) Either an allowed protocol (http, https, mailto or ftp).
     * (2) or no protocol.  A protocol must be followed by a colon. The below
     *     allows that by allowing colons only after one of the characters [/?#].
     *     A colon after a hash (#) must be in the fragment.
     *     Otherwise, a colon after a (?) must be in a query.
     *     Otherwise, a colon after a single solidus (/) must be in a path.
     *     Otherwise, a colon after a double solidus (//) must be in the authority
     *     (before port).
     *
     * The pattern disallows &amp;, used in HTML entity declarations before
     * one of the characters in [/?#]. This disallows HTML entities used in the
     * protocol name, which should never happen, e.g. "h&#116;tp" for "http".
     * It also disallows HTML entities in the first path part of a relative path,
     * e.g. "foo&lt;bar/baz".  Our existing escaping functions should not produce
     * that. More importantly, it disallows masking of a colon,
     * e.g. "javascript&#58;...".
     *
     * This regular expression was taken from the Closure sanitization library.
     */
    [GeneratedRegex("^(?:(?:https?|mailto|ftp|tel|file):|[^&:/?#]*(?:[/?#]|$))", RegexOptions.IgnoreCase | RegexOptions.Compiled, matchTimeoutMilliseconds: 10000)]
    private static partial Regex SafeUrlRegex();

    /** A pattern that matches safe data URLs. Only matches image, video and audio types. */
    [GeneratedRegex("^data:(?:image/(?:bmp|gif|jpeg|jpg|png|tiff|webp)|video/(?:mpeg|mp4|ogg|webm)|audio/(?:mp3|oga|ogg|opus));base64,[a-z0-9+/]+=*$", RegexOptions.IgnoreCase | RegexOptions.Compiled, matchTimeoutMilliseconds: 10000)]
    private static partial Regex DataUrlPattern();

    private static readonly char[] Whitespaces = ['\t', '\r', '\n', ' ', '\f'];

    /// <summary>
    /// Determines whether the specified URL is safe to use in HTML content.
    /// </summary>
    /// <param name="url">The URL to validate.</param>
    /// <returns><see langword="true"/> if the URL is safe; otherwise, <see langword="false"/>.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Breaking change")]
    public static bool IsSafeUrl(string url)
    {
        return SafeUrlRegex().IsMatch(url) || DataUrlPattern().IsMatch(url);
    }

    /// <summary>
    /// Determines whether the specified srcset value is safe to use in HTML content.
    /// </summary>
    /// <param name="url">The srcset value to validate.</param>
    /// <returns><see langword="true"/> if all URLs in the srcset are safe; otherwise, <see langword="false"/>.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Breaking change")]
    public static bool IsSafeSrcset(string url)
    {
        return url.Split(',').All(value => IsSafeUrl(GetUrlPart(value)));

        static string GetUrlPart(string value)
        {
            value = value.Trim(Whitespaces);
            var separator = value.IndexOfAny(Whitespaces);
            if (separator < 0)
                return value;

            return value[..separator];
        }
    }
}
