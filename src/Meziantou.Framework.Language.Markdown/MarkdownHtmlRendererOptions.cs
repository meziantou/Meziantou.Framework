namespace Meziantou.Framework.Language.Markdown;

/// <summary>Configures how <see cref="MarkdownHtmlRenderer"/> writes HTML.</summary>
public sealed record MarkdownHtmlRendererOptions
{
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
