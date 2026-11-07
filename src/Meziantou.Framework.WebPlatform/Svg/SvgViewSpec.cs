using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Svg;

/// <summary>
/// The view of an SVG document given by the fragment of its URL (https://svgwg.org/svg2-draft/linking.html#SVGFragmentIdentifiers):
/// an SVG view specification (<c>#svgView(viewBox(0,0,100,100);preserveAspectRatio(none);transform(scale(2)))</c>, optionally
/// followed by a time segment), the id of a <c>view</c> element (https://svgwg.org/svg2-draft/linking.html#ViewElement), or a
/// spatial media fragment (<c>#xywh=0,0,100,100</c>, https://www.w3.org/TR/media-frags/#naming-space). It overrides the
/// <c>viewBox</c> and <c>preserveAspectRatio</c> attributes of the root svg element, and adds a transform to its content. The
/// temporal media fragments (<c>#t=</c>) are ignored.
/// </summary>
/// <param name="ViewBox">The value of the viewBox of the view, or null when the view does not override it.</param>
/// <param name="PreserveAspectRatio">The value of preserveAspectRatio of the view, or null.</param>
/// <param name="Transform">The transform of the view (applied in the user space of the content of the root element), or null.</param>
internal sealed record SvgViewSpec(string? ViewBox, string? PreserveAspectRatio, SvgAffine? Transform)
{
    /// <summary>
    /// Returns the view of a document whose root element is an svg element, or null (no fragment, or a fragment that is neither a
    /// valid view specification nor the id of a view element).
    /// </summary>
    public static SvgViewSpec? Get(Document document)
    {
        if (document.UrlRecord.Fragment is not { Length: > 0 } fragment || document.DocumentElement is not SvgSvgElement)
            return null;

        var cache = document.SvgViewSpecCache;
        if (cache is { } cached && cached.Version == document.DomVersion && cached.Fragment == fragment)
            return cached.Spec;

        var decoded = fragment.Contains('%', StringComparison.Ordinal) ? System.Text.Encoding.UTF8.GetString(Urls.PercentEncoding.PercentDecode(fragment)) : fragment;
        var spec = decoded.StartsWith("svgView(", StringComparison.Ordinal) ? ParseViewSpecification(decoded)
            : FromViewElement(document, decoded) ?? FromSpatialMediaFragment((SvgSvgElement)document.DocumentElement, fragment);
        document.SvgViewSpecCache = (document.DomVersion, fragment, spec);
        return spec;
    }

    /// <summary>The view of a view element: its viewBox and preserveAspectRatio attributes (an invalid viewBox is ignored).</summary>
    private static SvgViewSpec? FromViewElement(Document document, string id)
    {
        if (document.GetElementById(id) is not SvgViewElement view)
            return null;

        var viewBox = view.GetAttributeValue("viewBox");
        if (!SvgParsing.TryParseNumberList(viewBox, out var numbers) || numbers.Count != 4 || numbers[2] < 0 || numbers[3] < 0)
        {
            viewBox = null;
        }

        var preserveAspectRatio = view.GetAttributeValue("preserveAspectRatio");
        return viewBox is null && preserveAspectRatio is null ? null : new SvgViewSpec(viewBox, preserveAspectRatio, Transform: null);
    }

    /// <summary>
    /// The view of a spatial media fragment (https://svgwg.org/svg2-draft/linking.html#SVGFragmentIdentifiers: "the viewBox is
    /// overridden by the x, y, width and height values provided by the fragment identifier"): the name-value pairs of the fragment
    /// are separated by <c>&amp;</c> and percent-decoded (https://www.w3.org/TR/media-frags/#processing-name-value-components), the
    /// last valid <c>xywh</c> dimension wins, and the other dimensions are ignored. The values are integers
    /// (https://www.w3.org/TR/media-frags/#naming-space: <c>xywh=[pixel:|percent:]x,y,w,h</c>), in the user units of the root
    /// element, or percentages of the resolved size of the image; a region without area is invalid, and a region that extends
    /// past the image is clipped to it (https://www.w3.org/TR/media-frags/#processing-overview-interpret).
    /// </summary>
    private static SvgViewSpec? FromSpatialMediaFragment(SvgSvgElement root, string fragment)
    {
        if (!fragment.Contains("xywh", StringComparison.Ordinal))
            return null;

        (double X, double Y, double Width, double Height)? region = null;
        Span<double> numbers = stackalloc double[4];
        var (imageWidth, imageHeight) = GetResolvedSize(root);
        foreach (var pair in fragment.Split('&'))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator < 0 || Decode(pair[..separator]) != "xywh")
                continue;

            var value = Decode(pair[(separator + 1)..]);
            var percent = value.StartsWith("percent:", StringComparison.Ordinal);
            if (percent || value.StartsWith("pixel:", StringComparison.Ordinal))
            {
                value = value[(value.IndexOf(':', StringComparison.Ordinal) + 1)..];
            }

            var parts = value.Split(',');
            var valid = parts.Length == 4;
            for (var i = 0; valid && i < 4; i++)
            {
                valid = parts[i].Length is > 0 and <= 9 && parts[i].AsSpan().IndexOfAnyExceptInRange('0', '9') < 0;
                if (valid)
                {
                    numbers[i] = int.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            if (!valid)
                continue;

            var (x, y, width, height) = (numbers[0], numbers[1], numbers[2], numbers[3]);
            if (percent)
            {
                // "the percent format is interpreted as a percentage of the width and height of the original media"
                if (x + width > 100 || y + height > 100)
                    continue;

                (x, y, width, height) = (x * imageWidth / 100, y * imageHeight / 100, width * imageWidth / 100, height * imageHeight / 100);
            }

            // "If xywh extends beyond the image, the UA should clip it to the actual image dimensions"
            width = Math.Min(width, imageWidth - x);
            height = Math.Min(height, imageHeight - y);
            if (width > 0 && height > 0)
            {
                region = (x, y, width, height);
            }
        }

        return region is var (regionX, regionY, regionWidth, regionHeight)
            ? new SvgViewSpec(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{regionX} {regionY} {regionWidth} {regionHeight}"), PreserveAspectRatio: null, Transform: null)
            : null;

        static string Decode(string text) => text.Contains('%', StringComparison.Ordinal) ? System.Text.Encoding.UTF8.GetString(Urls.PercentEncoding.PercentDecode(text)) : text;
    }

    /// <summary>
    /// The resolved size of an SVG document that the percentages of a spatial media fragment refer to: its width and height;
    /// "when the host document cannot resolve the width and height of the SVG document, the used values for width and height
    /// should be calculated according to CSS rules for calculating width and ratio for inline replaced elements" (a missing
    /// dimension is computed with the ratio of the viewBox, else it is 300 or 150 pixels).
    /// </summary>
    private static (double Width, double Height) GetResolvedSize(SvgSvgElement root)
    {
        var natural = Dom.ImageHeaders.GetSvgNaturalSize(root.GetAttributeValue("width"), root.GetAttributeValue("height"), root.GetAttributeValue("viewBox"));
        return (natural.Width, natural.Height, natural.AspectRatio) switch
        {
            ({ } width, { } height, _) => (width, height),
            ({ } width, null, { } ratio) => (width, width / ratio),
            (null, { } height, { } ratio) => (height * ratio, height),
            ({ } width, null, null) => (width, 150),
            (null, { } height, null) => (300, height),
            (null, null, { } ratio) => (300, 300 / ratio),
            _ => (300, 150),
        };
    }

    /// <summary>
    /// Parses <c>svgView(...)</c>: parameters separated by semicolons (white space is allowed around them), each a name and a
    /// value in parentheses; the specification can only be followed by a time segment (<c>&amp;t=...</c>). An invalid fragment is ignored.
    /// </summary>
    private static SvgViewSpec? ParseViewSpecification(string value)
    {
        var position = "svgView(".Length;
        string? viewBox = null;
        string? preserveAspectRatio = null;
        SvgAffine? transform = null;
        while (true)
        {
            SkipSpaces(value, ref position);
            if (position < value.Length && value[position] == ')')
            {
                position++;
                break;
            }

            var nameStart = position;
            while (position < value.Length && char.IsAsciiLetter(value[position]))
            {
                position++;
            }

            var name = value[nameStart..position];
            if (position >= value.Length || value[position] != '(')
                return null;

            // The value of the parameter, up to its closing parenthesis (a transform list has parentheses)
            var depth = 1;
            var argumentStart = ++position;
            while (position < value.Length && depth > 0)
            {
                depth += value[position] switch { '(' => 1, ')' => -1, _ => 0 };
                position++;
            }

            if (depth != 0)
                return null;

            var argument = value[argumentStart..(position - 1)].Trim();
            switch (name)
            {
                case "viewBox":
                    if (!SvgParsing.TryParseNumberList(argument, out var numbers) || numbers.Count != 4 || numbers[2] < 0 || numbers[3] < 0)
                        return null;

                    viewBox = argument;
                    break;

                case "preserveAspectRatio":
                    preserveAspectRatio = argument.Replace(',', ' ');
                    break;

                case "transform":
                    if (!SvgTransform.TryParseList(argument, out var transforms))
                        return null;

                    var result = SvgAffine.Identity;
                    foreach (var item in transforms)
                    {
                        var m = item.Matrix;
                        result = new SvgAffine(m.A, m.B, m.C, m.D, m.E, m.F).Then(result);
                    }

                    transform = result;
                    break;

                case "zoomAndPan" or "viewTarget":
                    break;

                default:
                    return null;
            }

            SkipSpaces(value, ref position);
            if (position < value.Length && value[position] == ';')
            {
                position++;
            }
            else if (position >= value.Length || value[position] != ')')
            {
                return null;
            }
        }

        // Only a time segment can follow the view specification (e.g. not a spatial media fragment)
        if (position < value.Length && !value.AsSpan(position).StartsWith("&t=", StringComparison.Ordinal))
            return null;

        return viewBox is null && preserveAspectRatio is null && transform is null ? null : new SvgViewSpec(viewBox, preserveAspectRatio, transform);

        static void SkipSpaces(string text, ref int index)
        {
            while (index < text.Length && text[index] == ' ')
            {
                index++;
            }
        }
    }
}
