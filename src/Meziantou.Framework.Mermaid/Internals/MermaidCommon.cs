using Meziantou.Framework.Yaml;
using Meziantou.Framework.Yaml.Model;

namespace Meziantou.Framework.Mermaid.Internals;

/// <summary>The statements and values that several diagrams share.</summary>
internal static class MermaidCommon
{
    public static DiagramDirection? ParseDirection(string value) => value switch
    {
        "TB" or "TD" or "v" => DiagramDirection.TopToBottom,
        "BT" or "^" => DiagramDirection.BottomToTop,
        "LR" or ">" => DiagramDirection.LeftToRight,
        "RL" or "<" => DiagramDirection.RightToLeft,
        _ => null,
    };

    public static string FormatDirection(DiagramDirection direction) => direction switch
    {
        DiagramDirection.BottomToTop => "BT",
        DiagramDirection.LeftToRight => "LR",
        DiagramDirection.RightToLeft => "RL",
        _ => "TB",
    };

    public static EdgeCurve? ParseCurve(string? value) => value?.Trim() switch
    {
        "basis" => EdgeCurve.Basis,
        "linear" => EdgeCurve.Linear,
        "step" => EdgeCurve.Step,
        "stepBefore" => EdgeCurve.StepBefore,
        "stepAfter" => EdgeCurve.StepAfter,
        "cardinal" => EdgeCurve.Cardinal,
        "catmullRom" => EdgeCurve.CatmullRom,
        "monotoneX" => EdgeCurve.MonotoneX,
        "monotoneY" => EdgeCurve.MonotoneY,
        "natural" => EdgeCurve.Natural,
        "bumpX" => EdgeCurve.BumpX,
        "bumpY" => EdgeCurve.BumpY,
        _ => null,
    };

    public static string FormatCurve(EdgeCurve curve) => curve switch
    {
        EdgeCurve.Linear => "linear",
        EdgeCurve.Step => "step",
        EdgeCurve.StepBefore => "stepBefore",
        EdgeCurve.StepAfter => "stepAfter",
        EdgeCurve.Cardinal => "cardinal",
        EdgeCurve.CatmullRom => "catmullRom",
        EdgeCurve.MonotoneX => "monotoneX",
        EdgeCurve.MonotoneY => "monotoneY",
        EdgeCurve.Natural => "natural",
        EdgeCurve.BumpX => "bumpX",
        EdgeCurve.BumpY => "bumpY",
        _ => "basis",
    };

    /// <summary>
    /// Reads the accessibility statements: <c>accTitle: text</c>, <c>accDescr: text</c> and <c>accDescr { lines }</c>.
    /// </summary>
    /// <returns><see langword="true"/> when the scanner was on one of them.</returns>
    public static bool TryParseAccessibility(Scanner scanner, Diagram diagram)
    {
        if (scanner.StartsWith("accTitle") && IsFollowedBy(scanner, "accTitle".Length, ':'))
        {
            scanner.Position += "accTitle".Length;
            scanner.SkipSpaces();
            scanner.Expect(":");
            diagram.AccessibleTitle = scanner.ReadLine().Trim();
            return true;
        }

        if (scanner.StartsWith("accDescr"))
        {
            var start = scanner.Position;
            scanner.Position += "accDescr".Length;
            scanner.SkipSpaces();
            if (scanner.TryConsume(":"))
            {
                diagram.Description = scanner.ReadLine().Trim();
                return true;
            }

            if (scanner.TryConsume("{"))
            {
                var end = scanner.Text.IndexOfOrdinal('}', scanner.Position);
                if (end < 0)
                    throw scanner.Error(start, "The description is not closed by '}'.");

                // The %% comments of the block are not part of the description
                var lines = scanner.Text[scanner.Position..end].Split('\n').Select(line => line.Trim()).Where(line => !line.StartsWith("%%", StringComparison.Ordinal));
                diagram.Description = string.Join('\n', lines).Trim();
                scanner.Position = end + 1;
                return true;
            }

            scanner.Position = start;
        }

        return false;
    }

    private static bool IsFollowedBy(Scanner scanner, int length, char c)
    {
        var position = scanner.Position + length;
        while (position < scanner.Text.Length && scanner.Text[position] is ' ' or '\t')
        {
            position++;
        }

        return position < scanner.Text.Length && scanner.Text[position] == c;
    }

    /// <summary>Applies the configuration that every diagram shares: the theme, the title and the padding.</summary>
    public static void ApplyCommonConfiguration(Diagram diagram, ParseContext context, string section)
    {
        MermaidTheme.Apply(diagram, context);
        if (context.Title is { Length: > 0 } title)
        {
            diagram.Title = title;
        }

        if (context.Configuration.GetNumber(section, "diagramPadding") is >= 0 and <= 1000 and var padding)
        {
            diagram.Padding = padding;
        }
    }

    /// <summary>Reads an inline YAML mapping, as the <c>@{ shape: rect }</c> of the flowcharts; the scanner is on the opening brace.</summary>
    public static Dictionary<string, object?> ReadInlineMapping(Scanner scanner)
    {
        var start = scanner.Position;
        var depth = 0;
        var quote = '\0';
        var position = start;
        for (; position < scanner.Text.Length; position++)
        {
            var c = scanner.Text[position];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    break;
            }
        }

        if (position >= scanner.Text.Length)
            throw scanner.Error(start, "The data is not closed by '}'.");

        var yaml = scanner.Text[start..(position + 1)];
        scanner.Position = position + 1;
        try
        {
            var stream = YamlStream.Load(new StringReader(yaml));
            if (stream.Count > 0 && MermaidConfiguration.FromYaml(stream[0].Contents) is Dictionary<string, object?> map)
                return map;
        }
        catch (Exception exception) when (exception is YamlException or InvalidOperationException or ArgumentException or FormatException or OverflowException or InvalidCastException or NotSupportedException or KeyNotFoundException)
        {
            throw scanner.Error(start, "The data is not a valid YAML mapping: " + exception.Message);
        }

        throw scanner.Error(start, "The data must be a mapping.");
    }

    /// <summary>Keeps the addresses that cannot run a script: http, https, mailto, relative addresses and fragments.</summary>
    public static string? SanitizeUrl(string url, ParseContext context, int offset)
    {
        var trimmed = url.Trim();
        var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
        var slash = trimmed.IndexOfAny(['/', '?', '#']);
        if (colon < 0 || (slash >= 0 && slash < colon))
            return trimmed;

        var scheme = trimmed[..colon].ToLowerInvariant();
        if (scheme is "http" or "https" or "mailto")
            return trimmed;

        context.Warn(offset, $"The link '{trimmed}' is not allowed: only http, https, mailto and relative addresses are.");
        return null;
    }
}
