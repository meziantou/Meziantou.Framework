using System.Text;
using Meziantou.Framework.Charts;
using Meziantou.Framework.Mermaid.Internals.Charts;

namespace Meziantou.Framework.Mermaid.Internals.SankeyDiagrams;

/// <summary>
/// Draws a <see cref="SankeyChart"/> with the <see cref="SankeyMark{T}"/> of Meziantou.Framework.Charts: the nodes in the
/// colors of the Tableau 10 scheme, as Mermaid, and their labels with their values.
/// </summary>
internal static class SankeyChartPlot
{
    /// <summary>Writes a value as Mermaid does in a label: rounded to two decimals (<c>Math.round(value * 100) / 100</c>), then written by <see cref="JavaScriptNumber.ToString"/>.</summary>
    public static string FormatValue(double value) => JavaScriptNumber.ToString(Math.Round(value * 100, MidpointRounding.AwayFromZero) / 100);

    // Mermaid draws the labels in 14 pixels, the links half transparent
    private const double LabelFontSize = 14;
    private const double LinkOpacity = 0.5;

    // Mermaid adds this space between the nodes of a column when the labels show the values, on two lines
    private const double ValuesNodePadding = 15;

    // The space Meziantou.Framework.Charts leaves around the chart and under the title
    private const double PlotPadding = 10;
    private const double TitleSpacing = 2 + 6;

    // d3.schemeTableau10, which Mermaid uses whatever the theme
    private static readonly SceneColor[] Tableau10 =
    [
        SceneColor.Parse("#4e79a7"), SceneColor.Parse("#f28e2c"), SceneColor.Parse("#e15759"), SceneColor.Parse("#76b7b2"), SceneColor.Parse("#59a14f"),
        SceneColor.Parse("#edc949"), SceneColor.Parse("#af7aa1"), SceneColor.Parse("#ff9da7"), SceneColor.Parse("#9c755f"), SceneColor.Parse("#bab0ab"),
    ];

    public static void Configure(SankeyChart chart, Plot plot)
    {
        var links = GetDrawnLinks(chart.Links);
        var nodes = GetNodes(links);
        var labels = GetLabels(chart, links, nodes);
        var colors = new SceneColor[nodes.Count];
        var next = 0;
        for (var i = 0; i < nodes.Count; i++)
        {
            colors[i] = chart.NodeColors.TryGetValue(nodes[i], out var color) ? color : Tableau10[next++ % Tableau10.Length];
        }

        // The layout does not depend on the scale of the values: the values so small or so large that the heights would
        // not be finite are divided by the largest one
        var largest = links.Count is 0 ? 0 : links.Max(link => link.Value);
        var divisor = largest is > 0 and (< 1e-200 or > 1e200) ? largest : 1;
        var rows = new List<Row>(links.Count);
        foreach (var link in links)
        {
            var tooltip = link.Source + " → " + link.Target + ": " + chart.ValuePrefix + FormatValue(link.Value) + chart.ValueSuffix;
            rows.Add(new Row(labels[link.Source], labels[link.Target], link.Value / divisor, tooltip));
        }

        var mark = new SankeyMark<Row>(rows, source: row => row.Source, target: row => row.Target, value: row => row.Value)
        {
            NodeWidth = chart.NodeWidth,
            NodePadding = chart.NodePadding + (chart.ShowValues ? ValuesNodePadding : 0),
            Alignment = chart.NodeAlignment switch
            {
                SankeyNodeAlignment.Left => SankeyAlignment.Left,
                SankeyNodeAlignment.Right => SankeyAlignment.Right,
                SankeyNodeAlignment.Center => SankeyAlignment.Center,
                _ => SankeyAlignment.Justify,
            },
            LinkOpacity = LinkOpacity,
            Title = row => row.Tooltip,
        };

        // The mark gives a node the color of the palette at its index, and a link the color of its source. A gradient
        // from the source to the target cannot be drawn: the links take the color of their source.
        if (chart.LinkColor is { } linkColor)
        {
            mark.FillColor = linkColor;
        }
        else if (chart.LinkColoring is SankeyLinkColoring.Target)
        {
            mark.Fill = row => row.Target;
            plot.ColorScale = new OrdinalColorScale { Domain = [.. nodes.Select(node => labels[node])], Palette = colors };
            plot.Legend.Visible = false;
        }

        plot.Add(mark);
        plot.Theme = plot.Theme with
        {
            Palette = colors.Length > 0 ? colors : plot.Theme.Palette,
            TickFontSize = LabelFontSize,
        };

        // The area of the nodes and the links is Width by Height, under the title
        var top = PlotPadding;
        if (plot.Title is { } title)
        {
            // The texts are measured with the family of the theme, as the chart measures them
            var measurer = (plot.TextMeasurer ?? AfmTextMeasurer.Default).WithFontFamily(plot.Theme.FontFamily);
            top += measurer.Measure(title, new SceneFont(plot.Theme.TitleFontSize, Weight: SceneFont.BoldWeight)).Height + TitleSpacing;
        }

        plot.Width = chart.Width + (2 * PlotPadding);
        plot.Height = chart.Height + top + PlotPadding;
        plot.Margin = new ChartMargin(top, PlotPadding, PlotPadding, PlotPadding);
    }

    /// <summary>Returns the links to draw: d3-sankey cannot lay out a cycle, so a link that closes one is left out.</summary>
    private static List<SankeyLink> GetDrawnLinks(IList<SankeyLink> links)
    {
        var result = new List<SankeyLink>(links.Count);
        if (!HasCycle(links))
        {
            result.AddRange(links);
            return result;
        }

        var outgoing = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var link in links)
        {
            if (IsReachable(outgoing, link.Target, link.Source))
                continue;

            if (!outgoing.TryGetValue(link.Source, out var targets))
            {
                targets = [];
                outgoing.Add(link.Source, targets);
            }

            targets.Add(link.Target);
            result.Add(link);
        }

        return result;
    }

    private static bool HasCycle(IList<SankeyLink> links)
    {
        // Kahn's algorithm: the links form a cycle when some nodes are never free of incoming links
        var incoming = new Dictionary<string, int>(StringComparer.Ordinal);
        var outgoing = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var link in links)
        {
            incoming.TryAdd(link.Source, 0);
            incoming[link.Target] = incoming.GetValueOrDefault(link.Target) + 1;
            if (!outgoing.TryGetValue(link.Source, out var targets))
            {
                targets = [];
                outgoing.Add(link.Source, targets);
            }

            targets.Add(link.Target);
        }

        var queue = new Queue<string>(incoming.Where(pair => pair.Value is 0).Select(pair => pair.Key));
        var visited = 0;
        while (queue.TryDequeue(out var node))
        {
            visited++;
            if (!outgoing.TryGetValue(node, out var targets))
                continue;

            foreach (var target in targets)
            {
                if (--incoming[target] is 0)
                {
                    queue.Enqueue(target);
                }
            }
        }

        return visited < incoming.Count;
    }

    private static bool IsReachable(Dictionary<string, List<string>> outgoing, string from, string to)
    {
        if (string.Equals(from, to, StringComparison.Ordinal))
            return true;

        var visited = new HashSet<string>(StringComparer.Ordinal) { from };
        var stack = new Stack<string>();
        stack.Push(from);
        while (stack.TryPop(out var node))
        {
            if (!outgoing.TryGetValue(node, out var targets))
                continue;

            foreach (var target in targets)
            {
                if (string.Equals(target, to, StringComparison.Ordinal))
                    return true;

                if (visited.Add(target))
                {
                    stack.Push(target);
                }
            }
        }

        return false;
    }

    /// <summary>Returns the names of the nodes in the order the mark finds them: the source of a link, then its target.</summary>
    private static List<string> GetNodes(List<SankeyLink> links)
    {
        var nodes = new List<string>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var link in links)
        {
            if (known.Add(link.Source))
            {
                nodes.Add(link.Source);
            }

            if (known.Add(link.Target))
            {
                nodes.Add(link.Target);
            }
        }

        return nodes;
    }

    /// <summary>
    /// Returns the label of each node, which the mark draws and also uses to find the node: its name and, as Mermaid, its
    /// value (the larger of the totals of its incoming and outgoing links), with the white space collapsed as SVG does.
    /// </summary>
    private static Dictionary<string, string> GetLabels(SankeyChart chart, List<SankeyLink> links, List<string> nodes)
    {
        var incoming = new Dictionary<string, double>(StringComparer.Ordinal);
        var outgoing = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var link in links)
        {
            outgoing[link.Source] = outgoing.GetValueOrDefault(link.Source) + link.Value;
            incoming[link.Target] = incoming.GetValueOrDefault(link.Target) + link.Value;
        }

        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var text = node;
            if (chart.ShowValues)
            {
                var value = Math.Max(incoming.GetValueOrDefault(node), outgoing.GetValueOrDefault(node));
                text += "\n" + chart.ValuePrefix + FormatValue(value) + chart.ValueSuffix;
            }

            // Two nodes whose labels look the same stay apart with trailing spaces, which SVG does not draw
            var label = CollapseWhiteSpace(text);
            while (!used.Add(label))
            {
                label += ' ';
            }

            labels.Add(node, label);
        }

        return labels;
    }

    /// <summary>Collapses the white space of a text as SVG does: the line feeds and the runs of spaces become a single space, and the text is trimmed.</summary>
    internal static string CollapseWhiteSpace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            if (c is ' ' or '\t' or '\n' or '\r')
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    private sealed record Row(string Source, string Target, double Value, string Tooltip);
}
