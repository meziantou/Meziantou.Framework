namespace Meziantou.Framework.Language.Markdown;

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
