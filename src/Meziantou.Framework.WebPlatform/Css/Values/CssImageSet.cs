using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>
/// The <c>image-set()</c> function (https://drafts.csswg.org/css-images-4/#image-set-notation): the choice of the image for
/// the resolution of the device, and the canonical form of its options.
/// </summary>
internal static class CssImageSet
{
    /// <summary>An option of an image set: its image (an &lt;image&gt; or a string, which is a URL) and its resolution in dppx.</summary>
    internal readonly record struct Option(CssComponentValue Image, double Resolution);

    /// <summary>Whether the function is <c>image-set()</c> or its alias <c>-webkit-image-set()</c>.</summary>
    public static bool IsImageSet(CssFunction function) => InfraAscii.EqualsIgnoreAsciiCase(function.Name, "image-set") || InfraAscii.EqualsIgnoreAsciiCase(function.Name, "-webkit-image-set");

    /// <summary>
    /// Selects the image of an image set for a device pixel ratio: the options whose <c>type()</c> is not supported are
    /// removed, then the options that have the resolution of a previous option; the option whose resolution is the smallest
    /// one that is not smaller than the device pixel ratio is selected, else the one that has the largest resolution (as for a
    /// source set, https://html.spec.whatwg.org/multipage/images.html#selecting-an-image-source). Returns null when no option
    /// remains. The natural resolution of the selected image is the resolution of its option: an option whose resolution is zero
    /// is an invalid image.
    /// </summary>
    public static Option? Select(CssFunction function, double devicePixelRatio)
    {
        Option? selected = null;
        var resolutions = new List<double>();
        foreach (var argument in ValueMatcher.SplitAtCommas(function.Value))
        {
            var values = ValueMatcher.RemoveWhitespace(argument);
            if (values.Count == 0)
                continue;

            var resolution = 1d;
            var supported = true;
            foreach (var value in values.Skip(1))
            {
                switch (value)
                {
                    case CssPreservedToken { Type: CssTokenType.Dimension } dimension:
                        resolution = ToDotsPerPixel(dimension.Token.NumericValue, dimension.Token.Value);
                        break;

                    case CssFunction type when InfraAscii.EqualsIgnoreAsciiCase(type.Name, "type"):
                        supported = ValueMatcher.RemoveWhitespace(type.Value) is [CssPreservedToken { Type: CssTokenType.String } mimeType] && HtmlImageElement.IsSupportedImageType(mimeType.Token.Value);
                        break;
                }
            }

            if (!supported || double.IsNaN(resolution) || resolutions.Contains(resolution))
                continue;

            resolutions.Add(resolution);
            if (selected is not { } current || IsBetter(resolution, current.Resolution, devicePixelRatio))
            {
                selected = new Option(values[0], resolution);
            }
        }

        return selected;

        static bool IsBetter(double candidate, double current, double devicePixelRatio)
        {
            if (candidate >= devicePixelRatio)
                return current < devicePixelRatio || candidate < current;

            return current < devicePixelRatio && candidate > current;
        }
    }

    /// <summary>Converts a resolution to dppx (https://drafts.csswg.org/css-values-4/#resolution); a negative resolution is zero.</summary>
    private static double ToDotsPerPixel(double value, string unit)
    {
        var resolution = InfraAscii.ToAsciiLowercase(unit) switch
        {
            "dpi" => value / 96,
            "dpcm" => value * 2.54 / 96,
            _ => value,
        };

        return Math.Max(0, resolution);
    }

    /// <summary>
    /// Returns the arguments of an image set in their canonical form (the serialization of the browsers): the strings are
    /// <c>url()</c> functions, an omitted resolution is <c>1x</c>, and the resolution precedes the type.
    /// </summary>
    public static List<CssComponentValue> Canonicalize(List<CssComponentValue> arguments)
    {
        var result = new List<CssComponentValue>(arguments.Count + 4);
        foreach (var argument in ValueMatcher.SplitAtCommas(arguments))
        {
            var values = ValueMatcher.RemoveWhitespace(argument);
            if (result.Count > 0)
            {
                result.Add(new CssPreservedToken(new CssToken { Type = CssTokenType.Comma, Value = "," }));
            }

            if (values.Count == 0)
                continue;

            CssComponentValue image = values[0] is CssPreservedToken { Type: CssTokenType.String } url ? new CssPreservedToken(new CssToken { Type = CssTokenType.Url, Value = url.Token.Value }) : values[0];
            CssComponentValue? resolution = null;
            CssComponentValue? type = null;
            foreach (var value in values.Skip(1))
            {
                if (value is CssFunction function && InfraAscii.EqualsIgnoreAsciiCase(function.Name, "type"))
                {
                    type = value;
                }
                else
                {
                    resolution = value;
                }
            }

            var option = new List<CssComponentValue> { image, resolution ?? new CssPreservedToken(new CssToken { Type = CssTokenType.Dimension, NumericValue = 1, Value = "x" }) };
            if (type is not null)
            {
                option.Add(type);
            }

            result.AddRange(CssShorthands.InsertSpaces(option));
        }

        return result;
    }
}
