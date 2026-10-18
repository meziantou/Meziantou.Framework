using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Svg;

/// <summary>
/// Creates the SVG DOM objects that reflect the attributes of SVG elements (SVGAnimatedLength...), and keeps them per
/// element so each IDL attribute returns the same object ([SameObject]). The keys are "Interface.idlAttribute", where
/// Interface is the interface or mixin that declares the IDL attribute.
/// </summary>
/// <remarks>
/// The initial values and the keywords of the enumerations are defined in the prose of SVG 2
/// (https://svgwg.org/svg2-draft/), Filter Effects (https://drafts.fxtf.org/filter-effects/) and CSS Masking
/// (https://drafts.fxtf.org/css-masking/).
/// </remarks>
internal static class SvgProperties
{
    private const ushort UserSpaceOnUse = SvgUnitTypes.SvgUnitTypeUserSpaceOnUse;
    private const ushort ObjectBoundingBox = SvgUnitTypes.SvgUnitTypeObjectBoundingBox;

    private static readonly (string Keyword, ushort Value)[] UnitTypes = [("userSpaceOnUse", UserSpaceOnUse), ("objectBoundingBox", ObjectBoundingBox)];

    private static readonly Dictionary<string, SvgEnumeration> Enumerations = new(StringComparer.Ordinal)
    {
        ["SVGClipPathElement.clipPathUnits"] = new(UserSpaceOnUse, UnitTypes),
        ["SVGComponentTransferFunctionElement.type"] = new(1, ("identity", 1), ("table", 2), ("discrete", 3), ("linear", 4), ("gamma", 5)),
        ["SVGFEBlendElement.mode"] = new(1, ("normal", 1), ("multiply", 2), ("screen", 3), ("darken", 4), ("lighten", 5), ("overlay", 6), ("color-dodge", 7), ("color-burn", 8), ("hard-light", 9), ("soft-light", 10), ("difference", 11), ("exclusion", 12), ("hue", 13), ("saturation", 14), ("color", 15), ("luminosity", 16)),
        ["SVGFEColorMatrixElement.type"] = new(1, ("matrix", 1), ("saturate", 2), ("hueRotate", 3), ("luminanceToAlpha", 4)),
        ["SVGFECompositeElement.operator"] = new(1, ("over", 1), ("in", 2), ("out", 3), ("atop", 4), ("xor", 5), ("arithmetic", 6), ("lighter", 7)),
        ["SVGFEConvolveMatrixElement.edgeMode"] = new(1, ("duplicate", 1), ("wrap", 2), ("none", 3)),
        ["SVGFEDisplacementMapElement.xChannelSelector"] = new(4, ("R", 1), ("G", 2), ("B", 3), ("A", 4)),
        ["SVGFEDisplacementMapElement.yChannelSelector"] = new(4, ("R", 1), ("G", 2), ("B", 3), ("A", 4)),
        ["SVGFEGaussianBlurElement.edgeMode"] = new(3, ("duplicate", 1), ("wrap", 2), ("none", 3)),
        ["SVGFEMorphologyElement.operator"] = new(1, ("erode", 1), ("dilate", 2)),
        ["SVGFETurbulenceElement.stitchTiles"] = new(2, ("stitch", 1), ("noStitch", 2)),
        ["SVGFETurbulenceElement.type"] = new(2, ("fractalNoise", 1), ("turbulence", 2)),
        ["SVGFilterElement.filterUnits"] = new(ObjectBoundingBox, UnitTypes),
        ["SVGFilterElement.primitiveUnits"] = new(UserSpaceOnUse, UnitTypes),
        ["SVGGradientElement.gradientUnits"] = new(ObjectBoundingBox, UnitTypes),
        ["SVGGradientElement.spreadMethod"] = new(1, ("pad", 1), ("reflect", 2), ("repeat", 3)),
        ["SVGMarkerElement.markerUnits"] = new(2, ("userSpaceOnUse", 1), ("strokeWidth", 2)),
        ["SVGMaskElement.maskUnits"] = new(ObjectBoundingBox, UnitTypes),
        ["SVGMaskElement.maskContentUnits"] = new(UserSpaceOnUse, UnitTypes),
        ["SVGPatternElement.patternUnits"] = new(ObjectBoundingBox, UnitTypes),
        ["SVGPatternElement.patternContentUnits"] = new(UserSpaceOnUse, UnitTypes),
        ["SVGTextContentElement.lengthAdjust"] = new(1, ("spacing", 1), ("spacingAndGlyphs", 2)),
        ["SVGTextPathElement.method"] = new(1, ("align", 1), ("stretch", 2)),
        ["SVGTextPathElement.spacing"] = new(2, ("auto", 1), ("exact", 2)),
    };

    private static readonly Dictionary<string, string> LengthDefaults = new(StringComparer.Ordinal)
    {
        ["SVGFilterElement.x"] = "-10%",
        ["SVGFilterElement.y"] = "-10%",
        ["SVGFilterElement.width"] = "120%",
        ["SVGFilterElement.height"] = "120%",
        ["SVGFilterPrimitiveStandardAttributes.x"] = "0%",
        ["SVGFilterPrimitiveStandardAttributes.y"] = "0%",
        ["SVGFilterPrimitiveStandardAttributes.width"] = "100%",
        ["SVGFilterPrimitiveStandardAttributes.height"] = "100%",
        ["SVGLinearGradientElement.x1"] = "0%",
        ["SVGLinearGradientElement.y1"] = "0%",
        ["SVGLinearGradientElement.x2"] = "100%",
        ["SVGLinearGradientElement.y2"] = "0%",
        ["SVGMarkerElement.markerWidth"] = "3",
        ["SVGMarkerElement.markerHeight"] = "3",
        ["SVGMaskElement.x"] = "-10%",
        ["SVGMaskElement.y"] = "-10%",
        ["SVGMaskElement.width"] = "120%",
        ["SVGMaskElement.height"] = "120%",
        ["SVGRadialGradientElement.cx"] = "50%",
        ["SVGRadialGradientElement.cy"] = "50%",
        ["SVGRadialGradientElement.r"] = "50%",
        ["SVGRadialGradientElement.fx"] = "50%",
        ["SVGRadialGradientElement.fy"] = "50%",
        ["SVGRadialGradientElement.fr"] = "0%",
        ["SVGSVGElement.width"] = "100%",
        ["SVGSVGElement.height"] = "100%",
    };

    private static readonly Dictionary<string, float> NumberDefaults = new(StringComparer.Ordinal)
    {
        ["SVGComponentTransferFunctionElement.slope"] = 1,
        ["SVGComponentTransferFunctionElement.amplitude"] = 1,
        ["SVGComponentTransferFunctionElement.exponent"] = 1,
        ["SVGFEConvolveMatrixElement.divisor"] = 1,
        ["SVGFEConvolveMatrixElement.orderX"] = 3,
        ["SVGFEConvolveMatrixElement.orderY"] = 3,
        ["SVGFEDiffuseLightingElement.surfaceScale"] = 1,
        ["SVGFEDiffuseLightingElement.diffuseConstant"] = 1,
        ["SVGFEDropShadowElement.dx"] = 2,
        ["SVGFEDropShadowElement.dy"] = 2,
        ["SVGFEDropShadowElement.stdDeviationX"] = 2,
        ["SVGFEDropShadowElement.stdDeviationY"] = 2,
        ["SVGFESpecularLightingElement.surfaceScale"] = 1,
        ["SVGFESpecularLightingElement.specularConstant"] = 1,
        ["SVGFESpecularLightingElement.specularExponent"] = 1,
        ["SVGFESpotLightElement.specularExponent"] = 1,
        ["SVGFETurbulenceElement.numOctaves"] = 1,
    };

    // The IDL attributes that are one of the two numbers of a "number-optional-number" attribute
    private static readonly string[] NumberPairs = ["order", "kernelUnitLength", "baseFrequency", "stdDeviation", "radius"];

    /// <summary>The default value of a length attribute (the key is "Interface.idlAttribute"), or null for 0.</summary>
    public static string? GetLengthDefault(string key) => LengthDefaults.GetValueOrDefault(key);

    /// <summary>The default value of a number attribute, or null for 0.</summary>
    public static float? GetNumberDefault(string key) => NumberDefaults.TryGetValue(key, out var value) ? value : null;

    /// <summary>The keywords of an enumerated attribute, or null.</summary>
    public static SvgEnumeration? GetEnumeration(string key) => key == "SVGMarkerElement.orientType" ? SvgEnumeration.MarkerOrient : Enumerations.GetValueOrDefault(key);

    public static T Get<T>(Element element, string idlName, string key)
        where T : class
    {
        var properties = element.ElementRareData.SvgProperties ??= new Dictionary<string, object>(StringComparer.Ordinal);
        if (!properties.TryGetValue(idlName, out var value))
        {
            value = Create(typeof(T), element, key);
            properties[idlName] = value;
        }

        return (T)value;
    }

    private static object Create(Type type, Element element, string key)
    {
        var (name, component) = GetContentAttribute(key);
        if (type == typeof(SvgAnimatedLength))
            return new SvgAnimatedLength(element, name, LengthDefaults.GetValueOrDefault(key, "0"));

        if (type == typeof(SvgAnimatedNumber))
            return new SvgAnimatedNumber(element, name, component, NumberDefaults.GetValueOrDefault(key));

        if (type == typeof(SvgAnimatedInteger))
            return new SvgAnimatedInteger(element, name, component, NumberDefaults.GetValueOrDefault(key));

        if (type == typeof(SvgAnimatedEnumeration))
        {
            if (key == "SVGMarkerElement.orientType")
                return new SvgAnimatedEnumeration(element, name, SvgEnumeration.MarkerOrient);

            return new SvgAnimatedEnumeration(element, name, Enumerations.TryGetValue(key, out var enumeration) ? enumeration : throw new InvalidOperationException($"Unknown enumeration {key}"));
        }

        if (type == typeof(SvgAnimatedString))
            return new SvgAnimatedString(element, name, xlinkFallback: name == "href");

        if (type == typeof(SvgAnimatedBoolean))
            return new SvgAnimatedBoolean(element, name);

        if (type == typeof(SvgAnimatedAngle))
            return new SvgAnimatedAngle(element, name);

        if (type == typeof(SvgAnimatedRect))
            return new SvgAnimatedRect(element, name);

        if (type == typeof(SvgAnimatedPreserveAspectRatio))
            return new SvgAnimatedPreserveAspectRatio(element, name);

        if (type == typeof(SvgAnimatedNumberList))
            return new SvgAnimatedNumberList(element, name);

        if (type == typeof(SvgAnimatedLengthList))
            return new SvgAnimatedLengthList(element, name);

        if (type == typeof(SvgAnimatedTransformList))
            return new SvgAnimatedTransformList(element, name);

        if (type == typeof(SvgStringList))
            return new SvgStringList(new SvgAttributeBinding(element, name, readOnly: false), commaSeparated: name == "systemLanguage");

        if (type == typeof(SvgPointList))
            return new SvgPointList(new SvgAttributeBinding(element, name, readOnly: key.EndsWith(".animatedPoints", StringComparison.Ordinal)));

        throw new InvalidOperationException($"Unsupported SVG property type {type}");
    }

    /// <summary>The content attribute reflected by an IDL attribute, and the component for number-optional-number attributes (-1 otherwise).</summary>
    private static (string Name, int Component) GetContentAttribute(string key)
    {
        var member = key[(key.IndexOf('.', StringComparison.Ordinal) + 1)..];
        switch (member)
        {
            case "className":
                return ("class", -1);
            case "in1":
                return ("in", -1);
            case "orientType" or "orientAngle":
                return ("orient", -1);
            case "animatedPoints":
                return ("points", -1);
        }

        foreach (var pair in NumberPairs)
        {
            if (member.Length == pair.Length + 1 && member.StartsWith(pair, StringComparison.Ordinal) && member[^1] is 'X' or 'Y')
                return (pair, member[^1] == 'X' ? 0 : 1);
        }

        return (member, -1);
    }
}
