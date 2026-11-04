using Meziantou.Framework.WebPlatform.Css.Animations;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Svg.Animations;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

// The SMIL animations of the properties (https://svgwg.org/specs/animations/#AnimationAttributesAndProperties): as in the
// browsers, the animated values override the declarations of the author style sheets and of the style attribute, except
// the important ones, and are the underlying values of the CSS animations and transitions. The animations of the
// attributes reflected by an SVG DOM object (e.g. width) change the presentation attribute instead (see
// PresentationalHints.GetSvgDeclarations).
internal sealed partial class ComputedStyle
{
    /// <summary>The SMIL animations in effect that animate properties of the element, sorted by priority, or null.</summary>
    private readonly List<SvgAnimationElement>? _smilAnimations;

    /// <summary>Applies the SMIL animations of a property to its value without animation (the base value of the sandwich).</summary>
    private string ApplySmilAnimations(CssPropertyDefinition property, string value)
    {
        SmilPropertyType? type = null;
        var result = value;
        foreach (var animation in _smilAnimations!)
        {
            var attribute = animation.TargetAttribute;
            if (attribute != property.Name && !(CssPropertyRegistry.Get(attribute) is { IsShorthand: true } shorthand && Array.IndexOf(shorthand.Longhands, property.Name) >= 0))
                continue;

            if (type is null)
            {
                if (TryGetCascadedValue(property.Name, out var cascaded) && cascaded.IsImportant)
                    return value;

                type = new SmilPropertyType(this, property);
            }

            type.Attribute = attribute;
            if (animation.ComputeAnimatedValue(type, result) is string animated)
            {
                result = animated;
            }
        }

        return ReferenceEquals(result, value) ? value : Canonicalize(property.Name, result);
    }

    /// <summary>The values of a property animated by SMIL: computed values, combined like the values of the CSS animations.</summary>
    private sealed class SmilPropertyType(ComputedStyle style, CssPropertyDefinition property) : SmilValueType
    {
        private readonly CssInterpolationContext _context = new(style);

        /// <summary>The presentation attribute the animation targets: the property, or a shorthand of the property.</summary>
        public string Attribute { get; set; } = property.Name;

        // The properties whose values cannot be added are animated discretely (the keywords, the images, the visibility...)
        public override bool IsAdditive => CssInterpolation.GetKind(property.Name, style)
            is not (CssInterpolationKind.NotAnimatable or CssInterpolationKind.Discrete or CssInterpolationKind.Visibility or CssInterpolationKind.Display or CssInterpolationKind.ContentVisibility or CssInterpolationKind.Image or CssInterpolationKind.FontPalette);

        public override object? GetBaseValue() => null;

        public override object? Parse(string value)
        {
            foreach (var declaration in PresentationalHints.ParseSvgPresentationAttribute(Attribute, value))
            {
                if (declaration.Name == property.Name)
                    return style.ComputeCascadedValue(property, new CascadedValue(declaration.Value, 0, source: null), property.Inherited, rollbacks: 0);
            }

            return null;
        }

        public override bool CanInterpolate(object from, object to) => !CssInterpolation.Prepare(_context, property.Name, (string)from, (string)to).IsDiscrete;

        public override object Interpolate(object from, object to, double progress)
            => InterpolateSrgbColors((string)from, (string)to, progress) ?? CssInterpolation.Prepare(_context, property.Name, (string)from, (string)to).Interpolate(progress);

        public override object Add(object underlying, object value) => CssInterpolation.Add(_context, property.Name, (string)underlying, (string)value);

        public override object Accumulate(object value, object end, long repeat) => CssInterpolation.AccumulateIterations(_context, property.Name, (string)end, repeat, (string)value);

        public override string Serialize(object value) => (string)value;

        /// <summary>
        /// The sRGB colors are interpolated component by component, without premultiplied alpha
        /// (https://www.w3.org/TR/2001/REC-smil-animation-20010904/#AnimationFramework: "the color values are interpolated in
        /// the RGB color space, each component independently"), as in Blink and WebKit. Returns null when a value is not
        /// an sRGB color.
        /// </summary>
        private static string? InterpolateSrgbColors(string from, string to, double progress)
        {
            if (CssGenericInterpolation.Parse(from) is not [var fromValue] || CssGenericInterpolation.Parse(to) is not [var toValue]
                || CssColor.Parse(fromValue)?.ToRgb() is not { } a || CssColor.Parse(toValue)?.ToRgb() is not { } b)
            {
                return null;
            }

            var red = CssGenericInterpolation.Lerp(a.R, b.R, progress);
            var green = CssGenericInterpolation.Lerp(a.G, b.G, progress);
            var blue = CssGenericInterpolation.Lerp(a.B, b.B, progress);
            var alpha = Math.Clamp(CssGenericInterpolation.Lerp(a.A, b.A, progress), 0, 1);
            return "rgba(" + CssGenericInterpolation.FormatNumber(red) + ", " + CssGenericInterpolation.FormatNumber(green) + ", " + CssGenericInterpolation.FormatNumber(blue) + ", " + CssGenericInterpolation.FormatNumber(alpha) + ")";
        }
    }
}
