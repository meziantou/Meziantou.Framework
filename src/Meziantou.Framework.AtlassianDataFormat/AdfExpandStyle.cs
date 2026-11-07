namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>How an <see cref="AdfExpand"/> is converted.</summary>
public enum AdfExpandStyle
{
    /// <summary>A block quotation whose first line is the bold title.</summary>
    Blockquote = 0,

    /// <summary>An HTML <c>&lt;details&gt;</c> element.</summary>
    HtmlDetails,

    /// <summary>A level 3 heading followed by the content.</summary>
    Heading,
}
