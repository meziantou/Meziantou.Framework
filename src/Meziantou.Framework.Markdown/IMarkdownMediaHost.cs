using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Markdown;

/// <summary>A site whose links <see cref="MarkdownExtensions.MediaLinks"/> renders as an <c>&lt;iframe&gt;</c>.</summary>
public interface IMarkdownMediaHost
{
    /// <summary>Gets the hosts of Markdig: YouTube (and its shorts), youtu.be, Vimeo, Yandex Music and Odnoklassniki.</summary>
    static IReadOnlyList<IMarkdownMediaHost> KnownHosts { get; } = MarkdownMediaHost.KnownHosts;

    /// <summary>Gets the class of the <c>&lt;iframe&gt;</c>, or <see langword="null"/> for none.</summary>
    string? Class { get; }

    /// <summary>Gets a value indicating whether the <c>&lt;iframe&gt;</c> has the <c>allowfullscreen</c> attribute.</summary>
    bool AllowFullScreen { get; }

    /// <summary>Gets the URL of the <c>&lt;iframe&gt;</c> of a link, when the link is for this site.</summary>
    /// <param name="mediaUri">The URL of the image, made absolute with <c>https:</c> when it starts with <c>//</c>.</param>
    /// <param name="isSchemaRelative">Whether the URL of the image starts with <c>//</c>.</param>
    /// <param name="iframeUrl">The URL of the <c>&lt;iframe&gt;</c>.</param>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "The URL is written to the HTML as it is")]
    bool TryHandle(Uri mediaUri, bool isSchemaRelative, [NotNullWhen(true)] out string? iframeUrl);
}
