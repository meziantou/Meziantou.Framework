namespace Meziantou.Framework.Language.Markdown;

/// <summary>The extensions of CommonMark that a <see cref="MarkdownSyntaxTree"/> parses.</summary>
[Flags]
public enum MarkdownExtensions
{
    /// <summary>CommonMark only.</summary>
    None = 0,

    /// <summary>The tables of GitHub Flavored Markdown: rows of cells separated by pipes, under a delimiter row.</summary>
    Tables = 1 << 0,

    /// <summary>The strikethrough of GitHub Flavored Markdown: text between one or two tildes.</summary>
    Strikethrough = 1 << 1,

    /// <summary>The task list items of GitHub Flavored Markdown: a list item that starts with <c>[ ]</c> or <c>[x]</c>.</summary>
    TaskLists = 1 << 2,

    /// <summary>The extended autolinks of GitHub Flavored Markdown: the URLs, the <c>www.</c> addresses and the email addresses of the text.</summary>
    Autolinks = 1 << 3,

    /// <summary>The tag filter of GitHub Flavored Markdown: the HTML renderer escapes the tags that change how the rest of the page is read, such as <c>&lt;script&gt;</c>.</summary>
    TagFilter = 1 << 4,

    /// <summary>The line breaks of a paragraph are rendered as hard line breaks (<c>&lt;br /&gt;</c>).</summary>
    HardLineBreaks = 1 << 5,

    /// <summary>Subscript: text between single tildes (<c>H~2~O</c>), rendered as <c>&lt;sub&gt;</c>. With it, <c>~~</c> is a strikethrough when <see cref="Strikethrough"/> is set, and two tildes are no longer read as the GFM strikethrough is.</summary>
    Subscript = 1 << 6,

    /// <summary>Superscript: text between carets (<c>2^10^</c>), rendered as <c>&lt;sup&gt;</c>.</summary>
    Superscript = 1 << 7,

    /// <summary>Inserted text, between <c>++</c>, rendered as <c>&lt;ins&gt;</c>.</summary>
    Inserted = 1 << 8,

    /// <summary>Marked text, between <c>==</c>, rendered as <c>&lt;mark&gt;</c>.</summary>
    Marked = 1 << 9,

    /// <summary>The extra emphasis: strikethrough, subscript, superscript, inserted and marked text.</summary>
    EmphasisExtras = Strikethrough | Subscript | Superscript | Inserted | Marked,

    /// <summary>The extensions of GitHub Flavored Markdown.</summary>
    GitHubFlavored = Tables | Strikethrough | TaskLists | Autolinks | TagFilter,
}
