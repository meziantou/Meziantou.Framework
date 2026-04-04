namespace Meziantou.Framework;

public sealed class HtmlToMarkdownOptions
{
    public EmphasisMarker EmphasisMarker { get; set; } = EmphasisMarker.Asterisk;

    public HeadingStyle HeadingStyle { get; set; } = HeadingStyle.Atx;

    public CodeBlockStyle CodeBlockStyle { get; set; } = CodeBlockStyle.Fenced;

    public char CodeBlockFenceCharacter { get; set; } = '`';

    public char UnorderedListMarker { get; set; } = '-';

    public string ThematicBreak { get; set; } = "---";

    public LineBreakStyle LineBreakStyle { get; set; } = LineBreakStyle.TrailingSpaces;

    public UnknownElementHandling UnknownElementHandling { get; set; }
        = UnknownElementHandling.PassThrough;
}

public enum EmphasisMarker
{
    Asterisk,
    Underscore,
}

public enum HeadingStyle
{
    Atx,
    Setext,
}

public enum CodeBlockStyle
{
    Fenced,
    Indented,
}

public enum LineBreakStyle
{
    TrailingSpaces,
    Backslash,
}

public enum UnknownElementHandling
{
    PassThrough,
    Strip,
    StripKeepContent,
}
