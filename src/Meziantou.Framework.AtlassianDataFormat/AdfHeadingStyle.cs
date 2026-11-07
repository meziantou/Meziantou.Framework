namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>The style used for headings.</summary>
public enum AdfHeadingStyle
{
    /// <summary><c>#</c> prefixed headings.</summary>
    Atx = 0,

    /// <summary>Headings underlined with <c>=</c> or <c>-</c>. Only levels 1 and 2 can be represented; deeper levels fall back to ATX.</summary>
    Setext,
}
