namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Options that control how an ADF document is converted to Markdown.</summary>
public sealed class AdfToMarkdownOptions
{
    /// <summary>Gets or sets the heading style to use in Markdown output.</summary>
    public AdfHeadingStyle HeadingStyle { get; set; } = AdfHeadingStyle.Atx;

    /// <summary>Gets or sets the marker used for emphasis.</summary>
    public AdfEmphasisMarker EmphasisMarker { get; set; } = AdfEmphasisMarker.Asterisk;

    /// <summary>Gets or sets the style used for code blocks.</summary>
    public AdfCodeBlockStyle CodeBlockStyle { get; set; } = AdfCodeBlockStyle.Fenced;

    /// <summary>Gets or sets the fence character used for fenced code blocks.</summary>
    public char CodeBlockFenceCharacter { get; set; } = '`';

    /// <summary>Gets or sets the marker used for unordered lists.</summary>
    public char UnorderedListMarker { get; set; } = '-';

    /// <summary>Gets or sets the thematic break text to emit for horizontal rules.</summary>
    public string ThematicBreak { get; set; } = "---";

    /// <summary>Gets or sets the style used for explicit line breaks.</summary>
    public AdfLineBreakStyle LineBreakStyle { get; set; } = AdfLineBreakStyle.TrailingSpaces;

    /// <summary>Gets or sets how panels are converted.</summary>
    public AdfPanelStyle PanelStyle { get; set; } = AdfPanelStyle.Blockquote;

    /// <summary>Gets or sets how collapsible sections are converted.</summary>
    public AdfExpandStyle ExpandStyle { get; set; } = AdfExpandStyle.Blockquote;

    /// <summary>Gets or sets how tables are converted.</summary>
    public AdfTableStyle TableStyle { get; set; } = AdfTableStyle.PipeTable;

    /// <summary>Gets or sets how media items are converted.</summary>
    public AdfMediaRendering MediaRendering { get; set; } = AdfMediaRendering.Image;

    /// <summary>Gets or sets how emoji are converted.</summary>
    public AdfEmojiRendering EmojiRendering { get; set; } = AdfEmojiRendering.Text;

    /// <summary>Gets or sets how task lists are converted.</summary>
    public AdfTaskListStyle TaskListStyle { get; set; } = AdfTaskListStyle.Checkbox;

    /// <summary>Gets or sets how decision lists are converted.</summary>
    public AdfDecisionListStyle DecisionListStyle { get; set; } = AdfDecisionListStyle.BulletList;

    /// <summary>Gets or sets how nodes whose type is not part of the supported schema are converted.</summary>
    public AdfUnknownNodeHandling UnknownNodeHandling { get; set; } = AdfUnknownNodeHandling.Skip;

    /// <summary>
    /// Gets or sets the format used for mentions. The <c>{text}</c> placeholder is replaced with the
    /// display name of the mentioned user, and <c>{id}</c> with their account identifier.
    /// </summary>
    public string MentionFormat { get; set; } = "@{text}";

    /// <summary>
    /// Gets or sets the format used for status lozenges. The <c>{text}</c> placeholder is replaced
    /// with the text of the lozenge, and <c>{color}</c> with its color.
    /// </summary>
    public string StatusFormat { get; set; } = "`{text}`";

    /// <summary>
    /// Gets or sets the format used for dates. The value is a standard or custom
    /// <see cref="DateTimeOffset"/> format string, applied with the invariant culture.
    /// </summary>
    public string DateFormat { get; set; } = "yyyy-MM-dd";

    /// <summary>
    /// Gets or sets a callback that returns the URL of a media item, or <see langword="null"/> when
    /// it cannot be resolved.
    /// </summary>
    /// <remarks>
    /// Only media items of type <see cref="AdfMediaType.External"/> carry a URL in the document.
    /// Items stored by Atlassian only carry an identifier and a collection, and resolving them
    /// requires an authenticated call to the media API.
    /// </remarks>
    public Func<AdfMedia, string?>? MediaUrlResolver { get; set; }

    /// <summary>
    /// Gets or sets a callback that returns the display name of a mentioned user, or
    /// <see langword="null"/> to use the text stored in the document.
    /// </summary>
    /// <remarks>Documents returned by the APIs often omit the display name of a mention.</remarks>
    public Func<AdfMention, string?>? MentionResolver { get; set; }
}
