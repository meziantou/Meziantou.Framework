namespace Meziantou.Framework.Markdown;

/// <summary>The style of an extra emphasis.</summary>
public enum MarkdownEmphasisExtraStyle
{
    /// <summary>Subscript, between single tildes: <c>H~2~O</c>.</summary>
    Subscript,

    /// <summary>Superscript, between carets: <c>2^10^</c>.</summary>
    Superscript,

    /// <summary>Inserted text, between <c>++</c>.</summary>
    Inserted,

    /// <summary>Marked text, between <c>==</c>.</summary>
    Marked,

    /// <summary>The text of an inline custom container, between <c>::</c>, rendered as <c>&lt;span&gt;</c>.</summary>
    CustomContainer,

    /// <summary>A citation, between <c>""</c>, rendered as <c>&lt;cite&gt;</c>.</summary>
    Citation,
}
