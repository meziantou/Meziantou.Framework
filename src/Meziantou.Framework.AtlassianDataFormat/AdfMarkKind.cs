namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Identifies the type of an <see cref="AdfMark"/>.</summary>
public enum AdfMarkKind
{
    /// <summary>A mark type that is not part of the supported schema.</summary>
    Unknown = 0,
    Link,
    Emphasis,
    Strong,
    Strike,
    SubSup,
    Underline,
    TextColor,
    Annotation,
    BackgroundColor,
    Code,
    Alignment,
    Indentation,
    Border,
    Breakout,
}
