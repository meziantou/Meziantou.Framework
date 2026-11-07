namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>How an <see cref="AdfDecisionList"/> is converted.</summary>
public enum AdfDecisionListStyle
{
    /// <summary>A bullet list.</summary>
    BulletList = 0,

    /// <summary>One paragraph per decision.</summary>
    PlainText,
}
