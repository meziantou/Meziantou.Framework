namespace Meziantou.Framework.Markdown;

/// <summary>
/// The typographic character that <see cref="MarkdownExtensions.SmartyPants"/> writes instead of ASCII punctuation. It is the
/// <see cref="SyntaxToken.Value"/> of a <see cref="SyntaxKind.SmartyPantToken"/>.
/// </summary>
public enum MarkdownSmartyPant
{
    /// <summary>A single quote that opens: <c>'</c>, written <c>&amp;lsquo;</c>.</summary>
    LeftQuote = 1,

    /// <summary>A single quote that closes: <c>'</c>, written <c>&amp;rsquo;</c>.</summary>
    RightQuote,

    /// <summary>A double quote that opens: <c>"</c> or <c>''</c>, written <c>&amp;ldquo;</c>.</summary>
    LeftDoubleQuote,

    /// <summary>A double quote that closes: <c>"</c> or <c>''</c>, written <c>&amp;rdquo;</c>.</summary>
    RightDoubleQuote,

    /// <summary>An angle quote that opens: <c>&lt;&lt;</c>, written <c>&amp;laquo;</c>.</summary>
    LeftAngleQuote,

    /// <summary>An angle quote that closes: <c>&gt;&gt;</c>, written <c>&amp;raquo;</c>.</summary>
    RightAngleQuote,

    /// <summary>An ellipsis: <c>...</c>, written <c>&amp;hellip;</c>.</summary>
    Ellipsis,

    /// <summary>An en dash: <c>--</c>, written <c>&amp;ndash;</c>.</summary>
    EnDash,

    /// <summary>An em dash: <c>---</c>, written <c>&amp;mdash;</c>.</summary>
    EmDash,
}
