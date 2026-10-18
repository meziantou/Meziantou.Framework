using System.Collections.Frozen;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Html.Parsing;

/// <summary>Adjustments of foreign elements and attributes (https://html.spec.whatwg.org/multipage/parsing.html#creating-and-inserting-nodes).</summary>
internal static class ForeignAttributes
{
    private static readonly FrozenDictionary<string, string> SvgAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["attributename"] = "attributeName",
        ["attributetype"] = "attributeType",
        ["basefrequency"] = "baseFrequency",
        ["baseprofile"] = "baseProfile",
        ["calcmode"] = "calcMode",
        ["clippathunits"] = "clipPathUnits",
        ["diffuseconstant"] = "diffuseConstant",
        ["edgemode"] = "edgeMode",
        ["filterunits"] = "filterUnits",
        ["glyphref"] = "glyphRef",
        ["gradienttransform"] = "gradientTransform",
        ["gradientunits"] = "gradientUnits",
        ["kernelmatrix"] = "kernelMatrix",
        ["kernelunitlength"] = "kernelUnitLength",
        ["keypoints"] = "keyPoints",
        ["keysplines"] = "keySplines",
        ["keytimes"] = "keyTimes",
        ["lengthadjust"] = "lengthAdjust",
        ["limitingconeangle"] = "limitingConeAngle",
        ["markerheight"] = "markerHeight",
        ["markerunits"] = "markerUnits",
        ["markerwidth"] = "markerWidth",
        ["maskcontentunits"] = "maskContentUnits",
        ["maskunits"] = "maskUnits",
        ["numoctaves"] = "numOctaves",
        ["pathlength"] = "pathLength",
        ["patterncontentunits"] = "patternContentUnits",
        ["patterntransform"] = "patternTransform",
        ["patternunits"] = "patternUnits",
        ["pointsatx"] = "pointsAtX",
        ["pointsaty"] = "pointsAtY",
        ["pointsatz"] = "pointsAtZ",
        ["preservealpha"] = "preserveAlpha",
        ["preserveaspectratio"] = "preserveAspectRatio",
        ["primitiveunits"] = "primitiveUnits",
        ["refx"] = "refX",
        ["refy"] = "refY",
        ["repeatcount"] = "repeatCount",
        ["repeatdur"] = "repeatDur",
        ["requiredextensions"] = "requiredExtensions",
        ["requiredfeatures"] = "requiredFeatures",
        ["specularconstant"] = "specularConstant",
        ["specularexponent"] = "specularExponent",
        ["spreadmethod"] = "spreadMethod",
        ["startoffset"] = "startOffset",
        ["stddeviation"] = "stdDeviation",
        ["stitchtiles"] = "stitchTiles",
        ["surfacescale"] = "surfaceScale",
        ["systemlanguage"] = "systemLanguage",
        ["tablevalues"] = "tableValues",
        ["targetx"] = "targetX",
        ["targety"] = "targetY",
        ["textlength"] = "textLength",
        ["viewbox"] = "viewBox",
        ["viewtarget"] = "viewTarget",
        ["xchannelselector"] = "xChannelSelector",
        ["ychannelselector"] = "yChannelSelector",
        ["zoomandpan"] = "zoomAndPan",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> SvgTagNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["altglyph"] = "altGlyph",
        ["altglyphdef"] = "altGlyphDef",
        ["altglyphitem"] = "altGlyphItem",
        ["animatecolor"] = "animateColor",
        ["animatemotion"] = "animateMotion",
        ["animatetransform"] = "animateTransform",
        ["clippath"] = "clipPath",
        ["feblend"] = "feBlend",
        ["fecolormatrix"] = "feColorMatrix",
        ["fecomponenttransfer"] = "feComponentTransfer",
        ["fecomposite"] = "feComposite",
        ["feconvolvematrix"] = "feConvolveMatrix",
        ["fediffuselighting"] = "feDiffuseLighting",
        ["fedisplacementmap"] = "feDisplacementMap",
        ["fedistantlight"] = "feDistantLight",
        ["fedropshadow"] = "feDropShadow",
        ["feflood"] = "feFlood",
        ["fefunca"] = "feFuncA",
        ["fefuncb"] = "feFuncB",
        ["fefuncg"] = "feFuncG",
        ["fefuncr"] = "feFuncR",
        ["fegaussianblur"] = "feGaussianBlur",
        ["feimage"] = "feImage",
        ["femerge"] = "feMerge",
        ["femergenode"] = "feMergeNode",
        ["femorphology"] = "feMorphology",
        ["feoffset"] = "feOffset",
        ["fepointlight"] = "fePointLight",
        ["fespecularlighting"] = "feSpecularLighting",
        ["fespotlight"] = "feSpotLight",
        ["fetile"] = "feTile",
        ["feturbulence"] = "feTurbulence",
        ["foreignobject"] = "foreignObject",
        ["glyphref"] = "glyphRef",
        ["lineargradient"] = "linearGradient",
        ["radialgradient"] = "radialGradient",
        ["textpath"] = "textPath",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#adjust-mathml-attributes</summary>
    public static void AdjustMathMLAttributes(HtmlTagToken tag)
    {
        foreach (ref var attribute in tag.Attributes)
        {
            if (attribute.Name == "definitionurl")
            {
                attribute.Name = "definitionURL";
                attribute.LocalName = "definitionURL";
            }
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#adjust-svg-attributes</summary>
    public static void AdjustSvgAttributes(HtmlTagToken tag)
    {
        foreach (ref var attribute in tag.Attributes)
        {
            if (SvgAttributes.TryGetValue(attribute.Name, out var adjusted))
            {
                attribute.Name = adjusted;
                attribute.LocalName = adjusted;
            }
        }
    }

    /// <summary>Fixes the case of SVG elements that are not all lowercase (rules for parsing tokens in foreign content).</summary>
    public static void AdjustSvgTagName(HtmlTagToken tag)
    {
        if (SvgTagNames.TryGetValue(tag.Name, out var adjusted))
        {
            tag.Name = adjusted;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#adjust-foreign-attributes</summary>
    public static void AdjustForeignAttributes(HtmlTagToken tag)
    {
        foreach (ref var attribute in tag.Attributes)
        {
            switch (attribute.Name)
            {
                case "xlink:actuate" or "xlink:arcrole" or "xlink:href" or "xlink:role" or "xlink:show" or "xlink:title" or "xlink:type":
                    attribute.Prefix = "xlink";
                    attribute.LocalName = attribute.Name["xlink:".Length..];
                    attribute.Namespace = Namespaces.XLink;
                    break;
                case "xml:lang" or "xml:space":
                    attribute.Prefix = "xml";
                    attribute.LocalName = attribute.Name["xml:".Length..];
                    attribute.Namespace = Namespaces.Xml;
                    break;
                case "xmlns":
                    attribute.Prefix = null;
                    attribute.LocalName = "xmlns";
                    attribute.Namespace = Namespaces.Xmlns;
                    break;
                case "xmlns:xlink":
                    attribute.Prefix = "xmlns";
                    attribute.LocalName = "xlink";
                    attribute.Namespace = Namespaces.Xmlns;
                    break;
            }
        }
    }
}
