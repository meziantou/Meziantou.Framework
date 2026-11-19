namespace Meziantou.Framework.Markdown;

/// <summary>Configures how <see cref="MarkdownHtmlRenderer"/> writes HTML.</summary>
public sealed record MarkdownHtmlRendererOptions
{
    // Before Default, which uses it
    internal static IReadOnlyDictionary<MarkdownSmartyPant, string> DefaultSmartyPants { get; } = new Dictionary<MarkdownSmartyPant, string>
    {
        [MarkdownSmartyPant.LeftQuote] = "&lsquo;",
        [MarkdownSmartyPant.RightQuote] = "&rsquo;",
        [MarkdownSmartyPant.LeftDoubleQuote] = "&ldquo;",
        [MarkdownSmartyPant.RightDoubleQuote] = "&rdquo;",
        [MarkdownSmartyPant.LeftAngleQuote] = "&laquo;",
        [MarkdownSmartyPant.RightAngleQuote] = "&raquo;",
        [MarkdownSmartyPant.Ellipsis] = "&hellip;",
        [MarkdownSmartyPant.EnDash] = "&ndash;",
        [MarkdownSmartyPant.EmDash] = "&mdash;",
    };

    /// <summary>Gets the options used when none are given.</summary>
    public static MarkdownHtmlRendererOptions Default { get; } = new();

    /// <summary>
    /// Gets which attributes of <see cref="MarkdownExtensions.GenericAttributes"/> the renderer writes, by name. The id and
    /// the classes are always written.
    /// </summary>
    /// <remarks>
    /// By default, only the attributes that describe the content are written, such as <c>title</c>, <c>lang</c> or
    /// <c>aria-*</c>: event handlers, URL attributes, <c>style</c> or <c>data-*</c> can run script or change the page when the
    /// Markdown is not trusted.
    /// </remarks>
    public Func<string, bool> AttributeFilter { get; init; } = IsSafeAttributeName;

    /// <summary>
    /// Gets the HTML that the renderer writes for each character of <see cref="MarkdownExtensions.SmartyPants"/>, written as
    /// it is: by default, the entities <c>&amp;lsquo;</c>, <c>&amp;rsquo;</c>, <c>&amp;ldquo;</c>, <c>&amp;rdquo;</c>,
    /// <c>&amp;laquo;</c>, <c>&amp;raquo;</c>, <c>&amp;hellip;</c>, <c>&amp;ndash;</c> and <c>&amp;mdash;</c>.
    /// </summary>
    /// <remarks>A character that the mapping does not have is written as the default entity.</remarks>
    public IReadOnlyDictionary<MarkdownSmartyPant, string> SmartyPants { get; init; } = DefaultSmartyPants;

    /// <summary>Gets the <c>rel</c> attribute of the links, such as <c>nofollow noopener</c>, or <see langword="null"/> for none. The autolinks of email addresses between <c>&lt;</c> and <c>&gt;</c> have none.</summary>
    public string? LinkRel { get; init; }

    /// <summary>
    /// Gets the schemes that the URLs of the links and of the images can have, such as <c>http</c>, <c>https</c> and
    /// <c>mailto</c>, or <see langword="null"/> to write every URL, as CommonMark does. A URL with another scheme, such as
    /// <c>javascript:</c> or <c>data:</c>, is written empty. A relative URL has no scheme and is always written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scheme is compared ignoring case, and read from the URL that is written, after <see cref="BaseUrl"/> and
    /// <see cref="LinkRewriter"/>, as a browser reads it: the spaces and the control characters at the start of the URL, and
    /// its tabs and line endings, are ignored.
    /// </para>
    /// <para>
    /// The filter applies to the URLs that the renderer writes, including the ones of <see cref="MarkdownHtmlWriter.WriteUrl"/>.
    /// It does not apply to raw HTML, nor to the generic attributes that <see cref="AttributeFilter"/> allows: it complements an
    /// HTML sanitizer, it does not replace one.
    /// </para>
    /// </remarks>
    public IReadOnlyCollection<string>? AllowedUrlSchemes { get; init; }

    /// <summary>Gets the URL that the relative URLs of the links and of the images are relative to, or <see langword="null"/> to keep them relative.</summary>
    public Uri? BaseUrl { get; init; }

    /// <summary>Gets a function that changes the URLs of the links and of the images, after <see cref="BaseUrl"/>, or <see langword="null"/>.</summary>
    public Func<string, string>? LinkRewriter { get; init; }

    /// <summary>Gets a value indicating whether the characters of the URLs that are not ASCII are percent-encoded, as CommonMark does. Markdig keeps them for <c>nonascii-noescape</c>.</summary>
    public bool EscapeNonAsciiUrlCharacters { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the element of the first block of each line has the id <c>pragma-line-N</c>, where N is
    /// the zero-based number of the line, as Markdig does with pragma lines: an editor can scroll its preview to a line. A block
    /// whose id is set gets an anchor with the id instead.
    /// </summary>
    public bool PragmaLines { get; init; }

    /// <summary>Gets how the images of audio, video, and of the sites that host them are rendered with <see cref="MarkdownExtensions.MediaLinks"/>.</summary>
    public MarkdownMediaLinkOptions MediaLinks { get; init; } = MarkdownMediaLinkOptions.Default;

    /// <summary>
    /// Gets the renderers that write some nodes instead of the built-in rendering, such as the code blocks of a language. Each
    /// block and each inline is offered to them in order, and the first one that writes it wins.
    /// </summary>
    public IReadOnlyList<MarkdownHtmlNodeRenderer> NodeRenderers { get; init; } = [];

    /// <summary>Gets whether an attribute only describes the content: whatever its value, it cannot run script or load a resource.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static bool IsSafeAttributeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (Array.Exists(Sanitizers.DescriptiveHtmlAttributes.Names, safe => string.Equals(safe, name, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        foreach (var prefix in Sanitizers.DescriptiveHtmlAttributes.Prefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
