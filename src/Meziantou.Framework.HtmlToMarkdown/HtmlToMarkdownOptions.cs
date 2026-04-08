namespace Meziantou.Framework;

/// <summary>Options that control how HTML is converted to Markdown.</summary>
public sealed class HtmlToMarkdownOptions
{
    /// <summary>Gets or sets the marker used for emphasis.</summary>
    public EmphasisMarker EmphasisMarker { get; set; } = EmphasisMarker.Asterisk;

    /// <summary>Gets or sets the heading style to use in Markdown output.</summary>
    public HeadingStyle HeadingStyle { get; set; } = HeadingStyle.Atx;

    /// <summary>Gets or sets the style used for code blocks.</summary>
    public CodeBlockStyle CodeBlockStyle { get; set; } = CodeBlockStyle.Fenced;

    /// <summary>Gets or sets the fence character used for fenced code blocks.</summary>
    public char CodeBlockFenceCharacter { get; set; } = '`';

    /// <summary>Gets or sets the marker used for unordered lists.</summary>
    public char UnorderedListMarker { get; set; } = '-';

    /// <summary>Gets or sets the thematic break text to emit for horizontal rules.</summary>
    public string ThematicBreak { get; set; } = "---";

    /// <summary>Gets or sets the style used for line breaks.</summary>
    public LineBreakStyle LineBreakStyle { get; set; } = LineBreakStyle.TrailingSpaces;

    /// <summary>
    /// Gets or sets a value indicating whether straight quotes and ASCII punctuation
    /// are converted to smart punctuation characters.
    /// </summary>
    public bool UseSmartPunctuation { get; set; }

    /// <summary>Gets or sets how unknown HTML elements are handled.</summary>
    public UnknownElementHandling UnknownElementHandling { get; set; }
        = UnknownElementHandling.PassThrough;
}
