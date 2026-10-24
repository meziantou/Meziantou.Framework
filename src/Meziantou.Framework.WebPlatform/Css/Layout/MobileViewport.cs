using System.Globalization;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The kind of a length of the viewport meta element (https://drafts.csswg.org/css-viewport/#width-desc).</summary>
internal enum ViewportLengthKind
{
    Pixels,
    DeviceWidth,
    DeviceHeight,
}

/// <summary>A length of the viewport meta element: a number of CSS pixels, <c>device-width</c> or <c>device-height</c>.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct ViewportLength(ViewportLengthKind Kind, double Pixels = 0)
{
    public double Resolve(double deviceWidth, double deviceHeight) => Kind switch
    {
        ViewportLengthKind.DeviceWidth => deviceWidth,
        ViewportLengthKind.DeviceHeight => deviceHeight,
        _ => Pixels,
    };
}

/// <summary>The properties of the content of a viewport meta element (https://drafts.csswg.org/css-viewport/#viewport-meta).</summary>
internal sealed record ViewportDescription
{
    public ViewportLength? Width { get; init; }
    public ViewportLength? Height { get; init; }
    public double? InitialScale { get; init; }
    public double? MinimumScale { get; init; }
    public double? MaximumScale { get; init; }
    public bool? UserScalable { get; init; }
}

/// <summary>
/// The viewport of a mobile device (<see cref="Dom.ViewportOptions.IsMobile"/>), as in the mobile browsers and the mobile
/// emulation of Chromium: the <c>viewport</c> meta element of the top-level document (https://drafts.csswg.org/css-viewport/)
/// sets the size of the layout viewport (the initial containing block, the viewport units and the width media queries) and
/// the initial scale of the page; without one, the page is laid out in a layout viewport of 980px and scaled down to fit the
/// width of the device. The visual viewport is the viewport of the device, scaled by the page scale.
/// </summary>
internal static class MobileViewport
{
    /// <summary>The width of the layout viewport of a page without viewport meta element (980px, as in the mobile browsers).</summary>
    public const double DefaultLayoutWidth = 980;

    /// <summary>The default maximum scale of the page (as in Chromium).</summary>
    private const double DefaultMaximumScale = 5;

    private const double MinimumZoom = 0.1;
    private const double MaximumZoom = 10;
    private const double MaximumLength = 10_000;

    /// <summary>
    /// Parses the content of a viewport meta element (https://drafts.csswg.org/css-viewport/#parsing-algorithm): the properties
    /// are separated by commas, semicolons or white space, and the unknown properties and the invalid values are ignored.
    /// </summary>
    public static ViewportDescription Parse(string content)
    {
        var result = new ViewportDescription();
        var span = content.AsSpan();
        var index = 0;
        while (index < span.Length)
        {
            // The name
            while (index < span.Length && IsSeparator(span[index]))
                index++;

            var nameStart = index;
            while (index < span.Length && !IsSeparator(span[index]) && span[index] != '=')
                index++;

            var name = span[nameStart..index];

            // The equal sign, with optional white space around it
            while (index < span.Length && IsWhiteSpace(span[index]))
                index++;

            if (index >= span.Length || span[index] != '=')
            {
                continue;
            }

            index++;
            while (index < span.Length && IsWhiteSpace(span[index]))
                index++;

            var valueStart = index;
            while (index < span.Length && !IsSeparator(span[index]) && span[index] != '=')
                index++;

            var value = span[valueStart..index];
            if (name.IsEmpty)
                continue;

            result = Infra.InfraAscii.ToAsciiLowercase(name.ToString()) switch
            {
                "width" => ParseLength(value) is { } width ? result with { Width = width } : result,
                "height" => ParseLength(value) is { } height ? result with { Height = height } : result,
                "initial-scale" => ParseZoom(value) is { } initialScale ? result with { InitialScale = initialScale } : result,
                "minimum-scale" => ParseZoom(value) is { } minimumScale ? result with { MinimumScale = minimumScale } : result,
                "maximum-scale" => ParseZoom(value) is { } maximumScale ? result with { MaximumScale = maximumScale } : result,
                "user-scalable" => ParseUserScalable(value) is { } userScalable ? result with { UserScalable = userScalable } : result,
                _ => result,
            };
        }

        return result;

        static bool IsWhiteSpace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';
        static bool IsSeparator(char c) => IsWhiteSpace(c) || c is ',' or ';';
    }

    private static ViewportLength? ParseLength(ReadOnlySpan<char> value)
    {
        if (value.Equals("device-width", StringComparison.OrdinalIgnoreCase))
            return new ViewportLength(ViewportLengthKind.DeviceWidth);

        if (value.Equals("device-height", StringComparison.OrdinalIgnoreCase))
            return new ViewportLength(ViewportLengthKind.DeviceHeight);

        // A number of CSS pixels (its leading numeric part), clamped to [1, 10000]
        if (ParseNumber(value) is not { } number || number < 0)
            return null;

        return new ViewportLength(ViewportLengthKind.Pixels, Math.Clamp(number, 1, MaximumLength));
    }

    private static double? ParseZoom(ReadOnlySpan<char> value)
    {
        if (value.Equals("yes", StringComparison.OrdinalIgnoreCase))
            return 1;

        if (value.Equals("no", StringComparison.OrdinalIgnoreCase))
            return MinimumZoom;

        if (value.Equals("device-width", StringComparison.OrdinalIgnoreCase) || value.Equals("device-height", StringComparison.OrdinalIgnoreCase))
            return MaximumZoom;

        if (ParseNumber(value) is not { } number || number < 0)
            return null;

        return Math.Clamp(number, MinimumZoom, MaximumZoom);
    }

    private static bool? ParseUserScalable(ReadOnlySpan<char> value)
    {
        if (value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("device-width", StringComparison.OrdinalIgnoreCase) || value.Equals("device-height", StringComparison.OrdinalIgnoreCase))
            return true;

        if (value.Equals("no", StringComparison.OrdinalIgnoreCase))
            return false;

        return ParseNumber(value) is { } number ? Math.Abs(number) >= 1 : false;
    }

    /// <summary>Parses the leading number of a value (e.g. <c>1.5</c> of <c>1.5px</c>), or null when it does not start with a number.</summary>
    private static double? ParseNumber(ReadOnlySpan<char> value)
    {
        var length = 0;
        if (length < value.Length && value[length] is '+' or '-')
            length++;

        var digits = 0;
        while (length < value.Length && char.IsAsciiDigit(value[length]))
        {
            length++;
            digits++;
        }

        if (length < value.Length && value[length] == '.')
        {
            length++;
            while (length < value.Length && char.IsAsciiDigit(value[length]))
            {
                length++;
                digits++;
            }
        }

        if (digits == 0 || !double.TryParse(value[..length], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
            return null;

        return number;
    }

    /// <summary>
    /// Returns the size of the layout viewport of a mobile device (in CSS pixels): the width of the viewport meta element (and
    /// at least the width of the device divided by its initial scale, when it has one), the width of the device divided by
    /// the initial scale when it only has an initial scale, else 980px; the height of the viewport meta element, else the
    /// height that keeps the aspect ratio of the device.
    /// </summary>
    public static (double Width, double Height) GetLayoutViewportSize(ViewportDescription? description, double deviceWidth, double deviceHeight)
    {
        if (deviceWidth <= 0 || deviceHeight <= 0)
            return (deviceWidth, deviceHeight);

        var width = description?.Width?.Resolve(deviceWidth, deviceHeight);
        if (description?.InitialScale is { } initialScale)
        {
            var extendToZoom = deviceWidth / initialScale;
            width = width is { } specified ? Math.Max(specified, extendToZoom) : extendToZoom;
        }

        var layoutWidth = width ?? DefaultLayoutWidth;
        var layoutHeight = description?.Height?.Resolve(deviceWidth, deviceHeight) ?? deviceHeight * layoutWidth / deviceWidth;
        return (layoutWidth, layoutHeight);
    }

    /// <summary>
    /// Returns the initial scale of the page: the initial scale of the viewport meta element, else the scale that fits the
    /// width of the contents (at least the layout viewport) in the width of the device; clamped by the minimum scale (the
    /// scale that fits the contents by default) and the maximum scale (5 by default), and by both when the page is not
    /// scalable.
    /// </summary>
    public static double GetInitialScale(ViewportDescription? description, double deviceWidth, double layoutWidth, double contentsWidth)
    {
        if (deviceWidth <= 0 || layoutWidth <= 0)
            return 1;

        var fit = deviceWidth / Math.Max(layoutWidth, contentsWidth);
        var minimum = description?.MinimumScale ?? fit;
        var maximum = Math.Max(minimum, description?.MaximumScale ?? DefaultMaximumScale);
        var scale = description?.InitialScale ?? fit;
        if (description?.UserScalable == false && description.InitialScale is { } fixedScale)
        {
            minimum = maximum = fixedScale;
        }

        return Math.Clamp(Math.Clamp(scale, minimum, maximum), MinimumZoom, MaximumZoom);
    }
}
