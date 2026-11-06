using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Internals;

/// <summary>
/// Port of the <c>iconToSVG</c> and <c>iconToHTML</c> functions of @iconify/utils.
/// </summary>
internal static class IconifySvgBuilder
{
    /// <summary>
    /// Applies the rotation and flips of an icon to its body and view box, so the icon can then be rendered without transformations.
    /// </summary>
    public static IconifyIconData ApplyTransformations(string name, string body, double left, double top, double width, double height, int rotate, bool hFlip, bool vFlip, bool hidden)
    {
        var transformations = new List<string>();
        var rotation = rotate;
        if (hFlip)
        {
            if (vFlip)
            {
                rotation += 2;
            }
            else
            {
                transformations.Add("translate(" + FormatNumber(width + left) + " " + FormatNumber(0 - top) + ")");
                transformations.Add("scale(-1 1)");
                top = left = 0;
            }
        }
        else if (vFlip)
        {
            transformations.Add("translate(" + FormatNumber(0 - left) + " " + FormatNumber(height + top) + ")");
            transformations.Add("scale(1 -1)");
            top = left = 0;
        }

        rotation %= 4;
        if (rotation < 0)
        {
            rotation += 4;
        }

        switch (rotation)
        {
            case 1:
                {
                    var center = FormatNumber((height / 2) + top);
                    transformations.Insert(0, "rotate(90 " + center + " " + center + ")");
                    break;
                }

            case 2:
                transformations.Insert(0, "rotate(180 " + FormatNumber((width / 2) + left) + " " + FormatNumber((height / 2) + top) + ")");
                break;

            case 3:
                {
                    var center = FormatNumber((width / 2) + left);
                    transformations.Insert(0, "rotate(-90 " + center + " " + center + ")");
                    break;
                }
        }

        if (rotation % 2 == 1)
        {
            (left, top) = (top, left);
            (width, height) = (height, width);
        }

        if (transformations.Count > 0)
        {
            body = "<g transform=\"" + string.Join(" ", transformations) + "\">" + body + "</g>";
        }

        return new IconifyIconData(name, body, left, top, width, height, hidden);
    }

    /// <summary>
    /// Wraps the body of an icon in an <c>svg</c> element.
    /// </summary>
    public static string BuildSvg(string body, double left, double top, double width, double height)
    {
        var formattedWidth = FormatNumber(width);
        var formattedHeight = FormatNumber(height);
        var sb = new StringBuilder(body.Length + 128);
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\"");
        if (body.Contains("xlink:", StringComparison.Ordinal))
        {
            sb.Append(" xmlns:xlink=\"http://www.w3.org/1999/xlink\"");
        }

        sb.Append(" width=\"").Append(formattedWidth).Append('"');
        sb.Append(" height=\"").Append(formattedHeight).Append('"');
        sb.Append(" viewBox=\"")
            .Append(FormatNumber(left)).Append(' ')
            .Append(FormatNumber(top)).Append(' ')
            .Append(formattedWidth).Append(' ')
            .Append(formattedHeight).Append("\">");
        sb.Append(body);
        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>
    /// Formats a number the way JavaScript converts a number to a string, for the values found in icons.
    /// </summary>
    public static string FormatNumber(double value)
    {
        // JavaScript formats -0 as "0"
        if (value == 0)
            return "0";

        return value.ToString("R", CultureInfo.InvariantCulture);
    }
}
