using System.Collections.Frozen;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Css.Animations;

/// <summary>How the values of a property are interpolated and combined (https://drafts.csswg.org/web-animations-1/#animation-type).</summary>
internal enum CssInterpolationKind : byte
{
    /// <summary>https://drafts.csswg.org/web-animations-1/#not-animatable</summary>
    NotAnimatable,

    /// <summary>https://drafts.csswg.org/web-animations-1/#discrete</summary>
    Discrete,

    /// <summary>https://drafts.csswg.org/web-animations-1/#by-computed-value: the components of the values are combined pairwise.</summary>
    ByComputedValue,

    /// <summary>https://drafts.csswg.org/web-animations-1/#repeatable-list: the comma-separated lists are repeated to their least common multiple.</summary>
    RepeatableList,

    /// <summary>https://drafts.csswg.org/css-backgrounds-3/#box-shadow: shadow lists (the shorter list is padded).</summary>
    ShadowList,

    /// <summary>https://drafts.csswg.org/css-transforms-2/#interpolation-of-transforms</summary>
    TransformList,

    /// <summary>https://drafts.csswg.org/css-transforms-2/#individual-transform-interpolation (translate).</summary>
    Translate,

    /// <summary>https://drafts.csswg.org/css-transforms-2/#individual-transform-interpolation (rotate).</summary>
    Rotate,

    /// <summary>https://drafts.csswg.org/css-transforms-2/#individual-transform-interpolation (scale).</summary>
    Scale,

    /// <summary>https://drafts.fxtf.org/filter-effects/#interpolation-of-filters</summary>
    FilterList,

    /// <summary>The d property (https://svgwg.org/svg2-draft/paths.html#PathElementDAttribute): path data with the same structure.</summary>
    Path,

    /// <summary>https://drafts.csswg.org/css-shapes/#basic-shape-interpolation</summary>
    BasicShape,

    /// <summary>https://drafts.csswg.org/web-animations-1/#animating-visibility</summary>
    Visibility,

    /// <summary>https://drafts.csswg.org/css-display-4/#display-animation: none is used only at the ends of the interval.</summary>
    Display,

    /// <summary>https://drafts.csswg.org/css-contain-2/#content-visibility-animation: hidden is used only at the ends of the interval.</summary>
    ContentVisibility,

    /// <summary>https://drafts.csswg.org/css-fonts-4/#font-style-prop: normal animates as oblique 0deg.</summary>
    FontStyle,

    /// <summary>https://drafts.csswg.org/css-values-4/#combine-ratio: ratios are interpolated by their logarithm.</summary>
    AspectRatio,

    /// <summary>https://drafts.csswg.org/css-fonts-4/#font-palette-prop: the palettes are interpolated with palette-mix().</summary>
    FontPalette,

    /// <summary>https://drafts.csswg.org/css-borders-4/#corner-shape-interpolation: the superellipse parameters are interpolated by their normalized superellipse half corner.</summary>
    CornerShape,

    /// <summary>
    /// The images (https://drafts.csswg.org/css-images-4/#interpolation): they are transitionable, but interpolated discretely
    /// (the browsers do not interpolate them with cross-fade()).
    /// </summary>
    Image,

    /// <summary>
    /// The grid track lists (https://drafts.csswg.org/css-grid-2/#track-sizing): the lists must have the same length, and the
    /// tracks are combined one by one (a track that cannot be combined is interpolated discretely).
    /// </summary>
    TrackList,
}

/// <summary>The interpolation kind of the properties: the animation type of webref, with the hand-written types of the prose.</summary>
internal static class CssAnimationTypes
{
    /// <summary>
    /// The properties whose animation type is defined by prose, or whose type in webref does not match the browsers
    /// (visibility is "discrete" in CSS 2, but https://drafts.csswg.org/web-animations-1/#animating-visibility defines it).
    /// </summary>
    private static readonly FrozenDictionary<string, CssInterpolationKind> Overrides = new Dictionary<string, CssInterpolationKind>(StringComparer.Ordinal)
    {
        ["visibility"] = CssInterpolationKind.Visibility,
        ["display"] = CssInterpolationKind.Display,
        ["overlay"] = CssInterpolationKind.Display,
        ["content-visibility"] = CssInterpolationKind.ContentVisibility,
        ["transform"] = CssInterpolationKind.TransformList,
        ["translate"] = CssInterpolationKind.Translate,
        ["rotate"] = CssInterpolationKind.Rotate,
        ["scale"] = CssInterpolationKind.Scale,
        ["filter"] = CssInterpolationKind.FilterList,
        ["backdrop-filter"] = CssInterpolationKind.FilterList,
        ["d"] = CssInterpolationKind.Path,
        ["clip-path"] = CssInterpolationKind.BasicShape,
        ["shape-outside"] = CssInterpolationKind.BasicShape,
        ["shape-inside"] = CssInterpolationKind.BasicShape,
        ["offset-path"] = CssInterpolationKind.BasicShape,
        ["object-view-box"] = CssInterpolationKind.BasicShape,
        ["font-style"] = CssInterpolationKind.FontStyle,
        ["aspect-ratio"] = CssInterpolationKind.AspectRatio,
        ["font-palette"] = CssInterpolationKind.FontPalette,
        ["box-shadow"] = CssInterpolationKind.ShadowList,
        ["text-shadow"] = CssInterpolationKind.ShadowList,

        // "see prose": the colors are interpolated by computed value
        ["border-top-color"] = CssInterpolationKind.ByComputedValue,
        ["border-right-color"] = CssInterpolationKind.ByComputedValue,
        ["border-bottom-color"] = CssInterpolationKind.ByComputedValue,
        ["border-left-color"] = CssInterpolationKind.ByComputedValue,
        ["border-block-start-color"] = CssInterpolationKind.ByComputedValue,
        ["border-block-end-color"] = CssInterpolationKind.ByComputedValue,
        ["border-inline-start-color"] = CssInterpolationKind.ByComputedValue,
        ["border-inline-end-color"] = CssInterpolationKind.ByComputedValue,

        // https://drafts.csswg.org/css-fonts-4/#font-variation-settings-def: the settings with the same tags are interpolated
        ["font-variation-settings"] = CssInterpolationKind.ByComputedValue,

        // The keywords must match (the components are compared pairwise)
        ["font-size-adjust"] = CssInterpolationKind.ByComputedValue,
        ["box-shadow-offset"] = CssInterpolationKind.ByComputedValue,
        ["stroke-dash-corner"] = CssInterpolationKind.ByComputedValue,
        ["overflow-clip-margin-top"] = CssInterpolationKind.Discrete,
        ["overflow-clip-margin-right"] = CssInterpolationKind.Discrete,
        ["overflow-clip-margin-bottom"] = CssInterpolationKind.Discrete,
        ["overflow-clip-margin-left"] = CssInterpolationKind.Discrete,
        ["overflow-clip-margin-block-start"] = CssInterpolationKind.Discrete,
        ["overflow-clip-margin-block-end"] = CssInterpolationKind.Discrete,
        ["overflow-clip-margin-inline-start"] = CssInterpolationKind.Discrete,
        ["overflow-clip-margin-inline-end"] = CssInterpolationKind.Discrete,
        ["grid-template-columns"] = CssInterpolationKind.TrackList,
        ["grid-template-rows"] = CssInterpolationKind.TrackList,
        ["object-position"] = CssInterpolationKind.RepeatableList,
        ["corner-top-left-shape"] = CssInterpolationKind.CornerShape,
        ["corner-top-right-shape"] = CssInterpolationKind.CornerShape,
        ["corner-bottom-right-shape"] = CssInterpolationKind.CornerShape,
        ["corner-bottom-left-shape"] = CssInterpolationKind.CornerShape,
        ["corner-start-start-shape"] = CssInterpolationKind.CornerShape,
        ["corner-start-end-shape"] = CssInterpolationKind.CornerShape,
        ["corner-end-start-shape"] = CssInterpolationKind.CornerShape,
        ["corner-end-end-shape"] = CssInterpolationKind.CornerShape,
        ["dynamic-range-limit"] = CssInterpolationKind.Discrete,

        // The images are transitionable but interpolated discretely, like in browsers
        ["background-image"] = CssInterpolationKind.Image,
        ["list-style-image"] = CssInterpolationKind.Image,
        ["border-image-source"] = CssInterpolationKind.Discrete,
        ["mask-image"] = CssInterpolationKind.Discrete,
        ["mask-border-source"] = CssInterpolationKind.Discrete,

        // The properties that browsers interpolate discretely
        ["counter-increment"] = CssInterpolationKind.Discrete,
        ["counter-reset"] = CssInterpolationKind.Discrete,
        ["counter-set"] = CssInterpolationKind.Discrete,
        ["forced-color-adjust"] = CssInterpolationKind.Discrete,
        ["grid-auto-columns"] = CssInterpolationKind.Discrete,
        ["grid-auto-rows"] = CssInterpolationKind.Discrete,
        ["initial-letter"] = CssInterpolationKind.Discrete,

        // https://svgwg.org/svg2-draft/painting.html#StrokeMiterlimitProperty: animatable (discrete in fill-stroke-3)
        ["stroke-miterlimit"] = CssInterpolationKind.ByComputedValue,

        // The properties without animation type in webref
        ["ry"] = CssInterpolationKind.ByComputedValue,
        ["stop-color"] = CssInterpolationKind.ByComputedValue,
        ["stop-opacity"] = CssInterpolationKind.ByComputedValue,
        ["stroke-alignment"] = CssInterpolationKind.Discrete,
        ["stroke-dashadjust"] = CssInterpolationKind.Discrete,
        ["stroke-dashcorner"] = CssInterpolationKind.Discrete,
        ["-webkit-user-select"] = CssInterpolationKind.Discrete,

        // A placeholder of css-backgrounds-4 ("see individual properties"), which is not a shorthand
        ["background-tbd"] = CssInterpolationKind.NotAnimatable,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Returns whether the interpolation kind of the property is hand-written.</summary>
    internal static bool HasOverride(string property) => Overrides.ContainsKey(property);

    private static readonly CssInterpolationKind?[] Kinds = new CssInterpolationKind?[CssPropertyRegistry.PropertyNameCount];

    /// <summary>
    /// Returns the interpolation kind of a longhand (a legacy alias is resolved). A shorthand, an unknown property and a
    /// custom property are not animatable (the custom properties are handled by CssInterpolation).
    /// </summary>
    public static CssInterpolationKind Get(string property)
    {
        var index = CssPropertyRegistry.GetIndex(property);
        if (index < 0)
            return CssInterpolationKind.NotAnimatable;

        if (Kinds[index] is { } kind)
            return kind;

        kind = Compute(property);
        Kinds[index] = kind;
        return kind;
    }

    private static CssInterpolationKind Compute(string name)
    {
        if (CssPropertyRegistry.Get(name) is not { } property || property.IsShorthand)
            return CssInterpolationKind.NotAnimatable;

        if (Overrides.TryGetValue(property.Name, out var kind))
            return kind;

        return property.AnimationType switch
        {
            CssAnimationTypeKind.NotAnimatable or CssAnimationTypeKind.Shorthand => CssInterpolationKind.NotAnimatable,
            CssAnimationTypeKind.ByComputedValue => CssInterpolationKind.ByComputedValue,
            CssAnimationTypeKind.RepeatableList => CssInterpolationKind.RepeatableList,
            CssAnimationTypeKind.ShadowList => CssInterpolationKind.ShadowList,
            _ => CssInterpolationKind.Discrete,
        };
    }
}
