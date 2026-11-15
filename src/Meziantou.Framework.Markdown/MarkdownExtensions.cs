namespace Meziantou.Framework.Markdown;

/// <summary>The extensions of CommonMark that a <see cref="MarkdownSyntaxTree"/> parses.</summary>
[Flags]
public enum MarkdownExtensions : long
{
    /// <summary>CommonMark only.</summary>
    None = 0,

    /// <summary>The tables of GitHub Flavored Markdown: rows of cells separated by pipes, under a delimiter row.</summary>
    Tables = 1L << 0,

    /// <summary>The strikethrough of GitHub Flavored Markdown: text between one or two tildes.</summary>
    Strikethrough = 1L << 1,

    /// <summary>The task list items of GitHub Flavored Markdown: a list item that starts with <c>[ ]</c> or <c>[x]</c>.</summary>
    TaskLists = 1L << 2,

    /// <summary>The extended autolinks of GitHub Flavored Markdown: the URLs, the <c>www.</c> addresses and the email addresses of the text.</summary>
    Autolinks = 1L << 3,

    /// <summary>The tag filter of GitHub Flavored Markdown: the HTML renderer escapes the tags that change how the rest of the page is read, such as <c>&lt;script&gt;</c>.</summary>
    TagFilter = 1L << 4,

    /// <summary>The line breaks of a paragraph are rendered as hard line breaks (<c>&lt;br /&gt;</c>).</summary>
    HardLineBreaks = 1L << 5,

    /// <summary>Subscript: text between single tildes (<c>H~2~O</c>), rendered as <c>&lt;sub&gt;</c>. With it, <c>~~</c> is a strikethrough when <see cref="Strikethrough"/> is set, and two tildes are no longer read as the GFM strikethrough is.</summary>
    Subscript = 1L << 6,

    /// <summary>Superscript: text between carets (<c>2^10^</c>), rendered as <c>&lt;sup&gt;</c>.</summary>
    Superscript = 1L << 7,

    /// <summary>Inserted text, between <c>++</c>, rendered as <c>&lt;ins&gt;</c>.</summary>
    Inserted = 1L << 8,

    /// <summary>Marked text, between <c>==</c>, rendered as <c>&lt;mark&gt;</c>.</summary>
    Marked = 1L << 9,

    /// <summary>A YAML front matter at the start of the document, between <c>---</c> and <c>---</c> or <c>...</c>, which the renderer does not write.</summary>
    YamlFrontMatter = 1L << 10,

    /// <summary>Diagrams: a fenced code block of <c>mermaid</c> is rendered as <c>&lt;pre class="mermaid"&gt;</c>, and one of <c>nomnoml</c> as <c>&lt;div class="nomnoml"&gt;</c>.</summary>
    Diagrams = 1L << 11,

    /// <summary>Mathematics: inline math between <c>$</c> or <c>$$</c>, and math blocks between <c>$$</c> lines.</summary>
    Mathematics = 1L << 12,

    /// <summary>The alerts of GitHub: a block quote whose first line is <c>[!NOTE]</c>, <c>[!TIP]</c>, <c>[!IMPORTANT]</c>, <c>[!WARNING]</c> or <c>[!CAUTION]</c>.</summary>
    Alerts = 1L << 13,

    /// <summary>Abbreviations: <c>*[HTML]: Hypertext Markup Language</c> defines an abbreviation, and the words of the text that are an abbreviation are rendered as <c>&lt;abbr&gt;</c>.</summary>
    Abbreviations = 1L << 14,

    /// <summary>Footnotes: <c>[^1]: text</c> defines a footnote, <c>[^1]</c> refers to it, and the renderer writes the footnotes that are referred to at the end of the document.</summary>
    Footnotes = 1L << 15,

    /// <summary>The generic attributes of Markdig: <c>{#id .class name=value}</c> after an inline or in a block sets the HTML attributes of its element.</summary>
    GenericAttributes = 1L << 16,

    /// <summary>Auto-identifiers: the renderer gives each heading an <c>id</c> made from its text, and a reference link whose label is the text of a heading links to it, as configured by <see cref="MarkdownParseOptions.AutoIdentifierOptions"/>.</summary>
    AutoIdentifiers = 1L << 17,

    /// <summary>Custom containers: blocks between <c>:::name</c> and <c>:::</c> lines, rendered as <c>&lt;div class="name"&gt;</c>, and text between <c>::</c>, rendered as <c>&lt;span&gt;</c>.</summary>
    CustomContainers = 1L << 18,

    /// <summary>Definition lists: the lines of a paragraph are terms when the next line starts with <c>:</c> or <c>~</c> and its content at four columns, which is a definition, rendered as <c>&lt;dl&gt;</c>.</summary>
    DefinitionLists = 1L << 19,

    /// <summary>Ordered lists numbered by letters (<c>a.</c>, <c>A.</c>) or roman numbers (<c>i.</c>, <c>I.</c>), rendered with the <c>type</c> attribute of <c>&lt;ol&gt;</c>.</summary>
    ListExtras = 1L << 20,

    /// <summary>SmartyPants: quotes, <c>&lt;&lt;</c>, <c>&gt;&gt;</c>, <c>...</c>, <c>--</c> and <c>---</c> are written as typographic characters, such as <c>&amp;ldquo;</c> or <c>&amp;mdash;</c>.</summary>
    SmartyPants = 1L << 21,

    /// <summary>Emoji: shortcodes such as <c>:smile:</c> and smileys such as <c>:)</c>, which are not after a letter or a digit, are written as their emoji, as configured by <see cref="MarkdownParseOptions.EmojiMapping"/>.</summary>
    Emoji = 1L << 22,

    /// <summary>Figures: blocks between a fence of three carets or more, <c>^^^</c>, and a fence as long, rendered as <c>&lt;figure&gt;</c>, whose caption is the text after the fences.</summary>
    Figures = 1L << 23,

    /// <summary>Footers: lines that start with <c>^^</c>, as a block quote does with <c>&gt;</c>, rendered as <c>&lt;footer&gt;</c>.</summary>
    Footers = 1L << 24,

    /// <summary>Citations: text between <c>""</c>, rendered as <c>&lt;cite&gt;</c>.</summary>
    Citations = 1L << 25,

    /// <summary>The pipe tables of Markdig: the lines of a paragraph that have a pipe, whose second line is a delimiter row, are a table, which has as many columns as its longest row. It replaces <see cref="Tables"/>, whose rules are the ones of GitHub Flavored Markdown.</summary>
    PipeTables = 1L << 26,

    /// <summary>The grid tables of Markdig: the lines between <c>+---+</c> lines, whose cells are separated by <c>|</c>, and can span columns and rows. The content of a cell is a document of its own.</summary>
    GridTables = 1L << 27,

    /// <summary>Media links: an image of an audio or a video file is rendered as <c>&lt;audio&gt;</c> or <c>&lt;video&gt;</c>, and one of a site such as YouTube as an <c>&lt;iframe&gt;</c>, as configured by <see cref="MarkdownHtmlRendererOptions.MediaLinks"/>.</summary>
    MediaLinks = 1L << 28,

    /// <summary>Jira links: a reference to an issue, such as <c>PROJ-123</c>, after whitespace or <c>(</c> and before whitespace or <c>)</c>, is a link to it, as configured by <see cref="MarkdownParseOptions.JiraLinks"/>.</summary>
    JiraLinks = 1L << 29,

    /// <summary>The autolinks of Markdig: the URLs of the text that start with <c>http://</c>, <c>https://</c>, <c>ftp://</c>, <c>mailto:</c>, <c>tel:</c> or <c>www.</c>, after whitespace or one of <see cref="MarkdownAutolinkOptions.ValidPreviousCharacters"/>, as configured by <see cref="MarkdownParseOptions.AutolinkOptions"/>. It replaces <see cref="Autolinks"/>, whose rules are the ones of GitHub Flavored Markdown.</summary>
    MarkdigAutolinks = 1L << 30,

    /// <summary>The CJK-friendly emphasis of <see href="https://github.com/tats-u/markdown-cjk-friendly">markdown-cjk-friendly</see>: a delimiter next to a Chinese, Japanese or Korean character opens or closes emphasis even after or before punctuation, such as <c>**強調（Emphasis）**です</c>.</summary>
    CjkFriendlyEmphasis = 1L << 31,

    /// <summary>The classes of Bootstrap: the renderer adds <c>table</c> to the tables, <c>img-fluid</c> to the images, <c>blockquote</c> to the block quotes, <c>figure</c> and <c>figure-caption</c> to the figures, and <c>alert</c> to the alerts, with the class of their kind.</summary>
    Bootstrap = 1L << 32,

    /// <summary>The direction of the text: the renderer adds <c>dir="rtl"</c> to the elements whose text starts with a right-to-left character, such as Arabic or Hebrew, and <c>align="right"</c> to their tables.</summary>
    Globalization = 1L << 33,

    /// <summary>The extra emphasis: strikethrough, subscript, superscript, inserted and marked text.</summary>
    EmphasisExtras = Strikethrough | Subscript | Superscript | Inserted | Marked,

    /// <summary>The extensions of GitHub Flavored Markdown.</summary>
    GitHubFlavored = Tables | Strikethrough | TaskLists | Autolinks | TagFilter,

    /// <summary>The extensions that <c>UseAdvancedExtensions</c> enables in Meziantou.Framework.Markdown 1.x: alerts, abbreviations, auto identifiers, citations, custom containers, definition lists, the extra emphasis, figures, footers, footnotes, grid tables, mathematics, media links, pipe tables, list extras, task lists, diagrams, the autolinks of Markdig and generic attributes.</summary>
    Advanced = Alerts | Abbreviations | AutoIdentifiers | Citations | CustomContainers | DefinitionLists | EmphasisExtras | Figures | Footers | Footnotes | GridTables | Mathematics | MediaLinks | PipeTables | ListExtras | TaskLists | Diagrams | MarkdigAutolinks | GenericAttributes,
}
