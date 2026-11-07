namespace Meziantou.Framework.Json.Internals;

/// <summary>
/// Builds normalized paths per RFC 9535 §2.7.
/// Format: $['name'][index]
/// </summary>
internal static class NormalizedPathBuilder
{
    public static string Build(PathNode? path)
    {
        if (path is null)
        {
            return "$";
        }

        // The steps are linked from the last one back to the root, so put them in order first.
        var steps = new PathNode[path.Depth];
        for (var step = path; step is not null; step = step.Parent)
        {
            steps[step.Depth - 1] = step;
        }

        var sb = new StringBuilder();
        sb.Append('$');
        foreach (var step in steps)
        {
            if (step.Name is null)
            {
                sb.Append('[');
                sb.Append(step.Index);
                sb.Append(']');
            }
            else
            {
                sb.Append("['");
                AppendEscapedName(sb, step.Name);
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
