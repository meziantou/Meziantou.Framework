namespace Meziantou.Framework.FileConversionTool;

/// <summary>
/// The template of the output paths: text with the placeholders <c>{name}</c>, <c>{ext}</c>, <c>{dir}</c>, <c>{page}</c>,
/// <c>{index}</c> and <c>{count}</c>. A number placeholder takes a padding spec of zeros: <c>{page:000}</c>. <c>{{</c> and
/// <c>}}</c> are literal braces.
/// </summary>
internal sealed class OutputNameTemplate
{
    private readonly Segment[] _segments;

    private OutputNameTemplate(string text, Segment[] segments)
    {
        Text = text;
        _segments = segments;
    }

    public string Text { get; }

    /// <summary>Gets a value indicating whether the template contains <c>{page}</c> or <c>{index}</c>, which give a distinct name to each page.</summary>
    public bool HasPageNumber => _segments.Any(segment => segment.Placeholder is Placeholder.Page or Placeholder.Index);

    public bool HasName => _segments.Any(segment => segment.Placeholder is Placeholder.Name);

    /// <summary>Parses a template.</summary>
    /// <exception cref="FormatException">The template is not valid: an unknown placeholder, an unclosed brace, or an invalid padding spec.</exception>
    public static OutputNameTemplate Parse(string text)
    {
        var segments = new List<Segment>();
        var literal = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c is '{')
            {
                if (i + 1 < text.Length && text[i + 1] is '{')
                {
                    literal.Append('{');
                    i += 2;
                    continue;
                }

                var end = text.IndexOf('}', i + 1, StringComparison.Ordinal);
                if (end < 0)
                    throw new FormatException($"The placeholder at position {i.ToString(CultureInfo.InvariantCulture)} of the output template is not closed");

                if (literal.Length > 0)
                {
                    segments.Add(new Segment(literal.ToString(), Placeholder.None, Padding: 0));
                    literal.Clear();
                }

                segments.Add(ParsePlaceholder(text.AsSpan(i + 1, end - i - 1)));
                i = end + 1;
            }
            else if (c is '}')
            {
                if (i + 1 < text.Length && text[i + 1] is '}')
                {
                    literal.Append('}');
                    i += 2;
                    continue;
                }

                throw new FormatException($"The '}}' at position {i.ToString(CultureInfo.InvariantCulture)} of the output template has no matching '{{'. Use '}}}}' for a literal brace");
            }
            else
            {
                literal.Append(c);
                i++;
            }
        }

        if (literal.Length > 0)
        {
            segments.Add(new Segment(literal.ToString(), Placeholder.None, Padding: 0));
        }

        return new OutputNameTemplate(text, [.. segments]);
    }

    /// <summary>Formats the path of an output file.</summary>
    public string Format(OutputNameValues values)
    {
        var sb = new StringBuilder();
        foreach (var segment in _segments)
        {
            switch (segment.Placeholder)
            {
                case Placeholder.None:
                    sb.Append(segment.Literal);
                    break;
                case Placeholder.Name:
                    sb.Append(values.Name);
                    break;
                case Placeholder.Extension:
                    sb.Append(values.Extension);
                    break;
                case Placeholder.Directory:
                    sb.Append(values.Directory);
                    break;
                case Placeholder.Page:
                    AppendNumber(sb, values.Page, segment.Padding, values.PageCount);
                    break;
                case Placeholder.Index:
                    AppendNumber(sb, values.Index, segment.Padding, values.IndexCount);
                    break;
                case Placeholder.Count:
                    sb.Append((values.PageCount ?? 0).ToString(CultureInfo.InvariantCulture).PadLeft(segment.Padding, '0'));
                    break;
            }
        }

        return sb.ToString();
    }

    public override string ToString() => Text;

    private static void AppendNumber(StringBuilder sb, int value, int padding, int? total)
    {
        // Without a padding spec, the numbers are padded to the width of the total, so that the files sort in order.
        // When the total is not known before the conversion (EPUB rendering), they are padded to 4 digits.
        if (padding is 0)
        {
            padding = total is { } count ? CountDigits(count) : 4;
        }

        sb.Append(value.ToString(CultureInfo.InvariantCulture).PadLeft(padding, '0'));
    }

    private static int CountDigits(int value)
    {
        var digits = 1;
        while (value >= 10)
        {
            value /= 10;
            digits++;
        }

        return digits;
    }

    private static Segment ParsePlaceholder(ReadOnlySpan<char> content)
    {
        var name = content;
        var padding = 0;
        var colon = content.IndexOf(':');
        if (colon >= 0)
        {
            name = content[..colon];
            var spec = content[(colon + 1)..];
            if (spec.IsEmpty || spec.Length > 10 || spec.ContainsAnyExcept('0'))
                throw new FormatException($"The padding spec of the placeholder '{{{content}}}' must be zeros, such as '{{page:000}}'");

            padding = spec.Length;
        }

        var placeholder = name switch
        {
            "name" => Placeholder.Name,
            "ext" => Placeholder.Extension,
            "dir" => Placeholder.Directory,
            "page" => Placeholder.Page,
            "index" => Placeholder.Index,
            "count" => Placeholder.Count,
            _ => throw new FormatException($"The placeholder '{{{content}}}' is not supported. Use {{name}}, {{ext}}, {{dir}}, {{page}}, {{index}} or {{count}}"),
        };

        if (padding > 0 && placeholder is not (Placeholder.Page or Placeholder.Index or Placeholder.Count))
            throw new FormatException($"The placeholder '{{{name}}}' does not take a padding spec");

        return new Segment(Literal: null, placeholder, padding);
    }

    private enum Placeholder
    {
        None,
        Name,
        Extension,
        Directory,
        Page,
        Index,
        Count,
    }

    private sealed record Segment(string? Literal, Placeholder Placeholder, int Padding);
}
