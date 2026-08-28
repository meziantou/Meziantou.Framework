namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>How an <see cref="AdfPanel"/> is converted.</summary>
public enum AdfPanelStyle
{
    /// <summary>A block quotation prefixed with an emoji matching the panel type.</summary>
    Blockquote = 0,

    /// <summary>A GitHub alert, such as <c>&gt; [!NOTE]</c>. Panel types with no matching alert fall back to <see cref="Blockquote"/>.</summary>
    GitHubAlert,

    /// <summary>A block quotation with no marker.</summary>
    PlainText,

    /// <summary>An HTML <c>&lt;div&gt;</c> carrying the panel type.</summary>
    Html,
}
