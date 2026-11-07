namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>
/// The animation type of a property definition (https://drafts.csswg.org/web-animations-1/#animation-type), from the
/// "Animation type" line of its definition table.
/// </summary>
internal enum CssAnimationTypeKind : byte
{
    /// <summary>The definition has no animation type.</summary>
    Unspecified,

    /// <summary>https://drafts.csswg.org/web-animations-1/#not-animatable</summary>
    NotAnimatable,

    /// <summary>https://drafts.csswg.org/web-animations-1/#discrete</summary>
    Discrete,

    /// <summary>"by computed value type" or "by computed value" (https://drafts.csswg.org/web-animations-1/#by-computed-value).</summary>
    ByComputedValue,

    /// <summary>https://drafts.csswg.org/web-animations-1/#repeatable-list</summary>
    RepeatableList,

    /// <summary>A shadow list (https://drafts.csswg.org/css-backgrounds-3/#box-shadow).</summary>
    ShadowList,

    /// <summary>A shorthand: "see individual properties".</summary>
    Shorthand,

    /// <summary>An animation type defined by prose (e.g. transform, d, display).</summary>
    Special,
}
