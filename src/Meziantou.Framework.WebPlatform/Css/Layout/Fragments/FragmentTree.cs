using System.Globalization;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>Helpers on the fragment tree.</summary>
internal static class FragmentTree
{
    /// <summary>
    /// Returns a text representation of a fragment and of its descendants, one fragment per line indented by its depth (for the
    /// tests and the debugging): the kind of the fragment, its box, and its rectangle in the coordinates of its parent
    /// (<c>x,y wxh</c>).
    /// </summary>
    public static string Dump(Fragment fragment)
    {
        var builder = new StringBuilder();
        Dump(builder, fragment, 0);
        return builder.ToString();
    }

    private static void Dump(StringBuilder builder, Fragment fragment, int depth)
    {
        builder.Append(' ', depth * 2);
        switch (fragment)
        {
            case BoxFragment box:
                var container = box.Box;
                if (container.Kind == ContainerBoxKind.Viewport)
                {
                    builder.Append("viewport");
                }
                else
                {
                    builder.Append("box");
                    if (container.IsAnonymous)
                    {
                        builder.Append(" anonymous");
                    }
                    else if (container.Element is { } element)
                    {
                        builder.Append(" <").Append(element.LocalName);
                        if (element.GetAttributeValue("id") is { Length: > 0 } id)
                        {
                            builder.Append('#').Append(id);
                        }

                        builder.Append('>');
                        if (container.PseudoElement is { } pseudoElement)
                        {
                            builder.Append("::").Append(pseudoElement);
                        }
                    }
                }

                break;

            case LineBoxFragment:
                builder.Append("line");
                break;

            case TextFragment text:
                builder.Append("text \"");
                foreach (var c in text.Run.Data.AsSpan(text.Start, text.End - text.Start))
                {
                    if (c == '\n')
                    {
                        builder.Append("\\n");
                    }
                    else
                    {
                        builder.Append(c);
                    }
                }

                builder.Append(CultureInfo.InvariantCulture, $"\" [{text.Start}, {text.End})");
                break;
        }

        builder.Append(CultureInfo.InvariantCulture, $" {Format(fragment.Offset.X)},{Format(fragment.Offset.Y)} {Format(fragment.Size.Width)}x{Format(fragment.Size.Height)}");
        builder.Append('\n');
        foreach (var child in fragment.Children)
        {
            Dump(builder, child, depth + 1);
        }
    }

    private static string Format(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
}
