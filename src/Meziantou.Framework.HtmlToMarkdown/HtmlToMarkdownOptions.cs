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

    private char _codeBlockFenceCharacter = '`';
    private char _unorderedListMarker = '-';

    /// <summary>Gets or sets the fence character used for fenced code blocks.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is neither a backtick nor a tilde.</exception>
    public char CodeBlockFenceCharacter
    {
        get => _codeBlockFenceCharacter;
        set
        {
            if (value is not ('`' or '~'))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The code block fence character must be '`' or '~'.");

            _codeBlockFenceCharacter = value;
        }
    }

    /// <summary>Gets or sets the marker used for unordered lists.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not '-', '*' or '+'.</exception>
    public char UnorderedListMarker
    {
        get => _unorderedListMarker;
        set
        {
            if (value is not ('-' or '*' or '+'))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The unordered list marker must be '-', '*' or '+'.");

            _unorderedListMarker = value;
        }
    }

    /// <summary>Gets or sets the thematic break text to emit for horizontal rules.</summary>
    public string ThematicBreak { get; set; } = "---";

    /// <summary>Gets or sets the style used for line breaks.</summary>
    public LineBreakStyle LineBreakStyle { get; set; } = LineBreakStyle.TrailingSpaces;

    /// <summary>
    /// Gets or sets a value indicating whether smart punctuation characters
    /// are converted to simple ASCII punctuation.
    /// </summary>
    public bool UseSimplePunctuation { get; set; }

    /// <summary>Gets or sets how emoji are converted to shortcodes.</summary>
    public EmojiShortcodeMode EmojiShortcodeMode { get; set; } = EmojiShortcodeMode.None;

    /// <summary>Gets or sets how unknown HTML elements are handled.</summary>
    public UnknownElementHandling UnknownElementHandling { get; set; }
        = UnknownElementHandling.PassThrough;

    /// <summary>Gets or sets how subscript (<c>sub</c>) elements are converted.</summary>
    public SubscriptStyle SubscriptStyle { get; set; } = SubscriptStyle.UnknownElement;

    /// <summary>Gets or sets how superscript (<c>sup</c>) elements are converted.</summary>
    public SuperscriptStyle SuperscriptStyle { get; set; } = SuperscriptStyle.UnknownElement;

    /// <summary>Gets or sets how underline (<c>u</c>) elements are converted.</summary>
    public UnderlineStyle UnderlineStyle { get; set; } = UnderlineStyle.UnknownElement;

    /// <summary>Gets or sets how keyboard input (<c>kbd</c>) elements are converted.</summary>
    public KeyboardStyle KeyboardStyle { get; set; } = KeyboardStyle.UnknownElement;

    /// <summary>Gets or sets how <c>video</c>, <c>audio</c> and <c>iframe</c> elements are converted.</summary>
    public EmbeddedContentStyle EmbeddedContentStyle { get; set; } = EmbeddedContentStyle.UnknownElement;

    /// <summary>
    /// Gets or sets a value indicating whether the <c>white-space</c> and <c>white-space-collapse</c> declarations of the
    /// <c>style</c> attributes are honored. When <see langword="true"/>, the spaces and the line breaks that the style
    /// preserves are written as <c>&amp;nbsp;</c> and as hard line breaks.
    /// </summary>
    public bool UseCssWhiteSpace { get; set; } = true;

    /// <summary>Gets or sets how the <c>colspan</c> and <c>rowspan</c> attributes of table cells are handled.</summary>
    public TableSpanHandling TableSpanHandling { get; set; } = TableSpanHandling.Ignore;

    /// <summary>Gets or sets how the tables that a pipe table cannot represent are converted.</summary>
    public ComplexTableHandling ComplexTableHandling { get; set; } = ComplexTableHandling.Flatten;
}
