namespace Meziantou.Framework.Json.Internals;

/// <summary>
/// Builds normalized paths per RFC 9535 §2.7.
/// Format: $['name'][index]
/// </summary>
internal static class NormalizedPathBuilder
{
    public static string Build(List<PathComponent> components)
    {
        var sb = new StringBuilder();
        sb.Append('$');
        foreach (var component in components)
        {
            if (component.IsIndex)
            {
                sb.Append('[');
                sb.Append(component.Index);
                sb.Append(']');
            }
            else
            {
                sb.Append("['");
                AppendEscapedName(sb, component.Name!);
                sb.Append("']");
            }
        }

        return sb.ToString();
    }

    private static void AppendEscapedName(StringBuilder sb, string name)
    {
        foreach (var ch in name)
        {
            switch (ch)
            {
                case '\'':
                    sb.Append("\\'");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (ch < '\x20')
                    {
                        // Control characters: use \u00XX hex escape with lowercase hex
                        sb.Append("\\u00");
                        sb.Append(((int)ch).ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(ch);
                    }

                    break;
            }
        }
    }
}
