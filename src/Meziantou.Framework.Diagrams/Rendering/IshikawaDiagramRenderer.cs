using System.Text;
using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Draws the Ishikawa diagrams with the geometry of Mermaid: the head on the right of the spine, the main causes as
/// bones at 82° alternately above and below the spine, from the head to the tail, and their causes as bones that are
/// alternately horizontal and parallel to the main bones.
/// </summary>
internal static class IshikawaDiagramRenderer
{
    private const double SpineBaseLength = 250;
    private const double BoneStub = 30;
    private const double BoneBase = 60;
    private const double BonePerChild = 5;
    private const int SubCauseWrap = 15;
    private const double Angle = 82 * Math.PI / 180;
    private static readonly double CosAngle = Math.Cos(Angle);
    private static readonly double SinAngle = Math.Sin(Angle);

    public static SceneBounds Render(IshikawaDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        if (diagram.Effect is null && diagram.Causes.Count is 0)
            return default;

        var state = new State(context, group.Add(new SceneGroup { CssClass = "ishikawa" }));
        var fontSize = context.FontSize;
        var causes = diagram.Causes;

        // The spine is drawn first, below the head and the bones, once its left end is known
        var spineGroup = state.Root.Add(new SceneGroup { CssClass = "spine" });
        var spineY = SpineBaseLength;
        var spineX = 0.0;
        var upperLength = SpineBaseLength;
        var lowerLength = SpineBaseLength;
        if (causes.Count > 0)
        {
            var upper = causes.Where((_, i) => i % 2 == 0).ToList();
            var lower = causes.Where((_, i) => i % 2 == 1).ToList();
            var (upperTotal, upperMax) = SideStatistics(upper);
            var (lowerTotal, lowerMax) = SideStatistics(lower);
            if (upperTotal + lowerTotal > 0)
            {
                var pool = SpineBaseLength * 2;
                var minimum = SpineBaseLength * 0.3;
                upperLength = Math.Max(minimum, pool * upperTotal / (upperTotal + lowerTotal));
                lowerLength = Math.Max(minimum, pool * lowerTotal / (upperTotal + lowerTotal));
            }

            var minSpacing = fontSize * 2;
            upperLength = Math.Max(upperLength, upperMax * minSpacing);
            lowerLength = Math.Max(lowerLength, lowerMax * minSpacing);
            spineY = Math.Max(upperLength, SpineBaseLength);
        }

        // As in Mermaid, the bones are drawn over the head
        state.DrawHead(diagram.Effect, spineY);
        if (causes.Count > 0)
        {
            spineX = -20;

            // A pair of bones, one above and one below, starts where the texts of the previous pair end
            for (var pair = 0; pair < (causes.Count + 1) / 2; pair++)
            {
                var pairGroup = state.Root.Add(new SceneGroup { CssClass = "pair" });
                var left = double.PositiveInfinity;
                left = Math.Min(left, state.DrawBranch(pairGroup, causes[pair * 2], spineX, spineY, -1, upperLength));
                if ((pair * 2) + 1 < causes.Count)
                {
                    left = Math.Min(left, state.DrawBranch(pairGroup, causes[(pair * 2) + 1], spineX, spineY, 1, lowerLength));
                }

                if (double.IsFinite(left))
                {
                    spineX = left;
                }
            }
        }

        var line = context.Theme.LineColor;
        spineGroup.Add(new SceneLine(spineX, spineY, 0, spineY) { Stroke = line, StrokeWidth = 2, CssClass = "ishikawa-spine" });
        state.Bounds.Add(new LayoutPoint(spineX, spineY));
        return state.Bounds.ToBounds();
    }

    private static (int Total, int Max) SideStatistics(List<IshikawaCause> causes)
    {
        var total = 0;
        var max = 0;
        foreach (var cause in causes)
        {
            var descendants = CountDescendants(cause, depth: 0);
            total += descendants;
            max = Math.Max(max, descendants);
        }

        return (total, max);

        static int CountDescendants(IshikawaCause cause, int depth) => depth > 64 ? 0 : cause.Causes.Sum(child => 1 + CountDescendants(child, depth + 1));
    }

    /// <summary>Wraps a text at spaces to lines of a number of characters at most, as Mermaid: a line break is written <c>&lt;br&gt;</c> and counts as text.</summary>
    internal static DiagramText Wrap(DiagramText text, int maxCharacters)
    {
        var encoded = text.Text.Replace("\n", "<br>", StringComparison.Ordinal);
        if (encoded.Length <= maxCharacters)
            return text;

        var lines = new List<StringBuilder>();
        foreach (var word in SplitOnWhiteSpace(encoded))
        {
            if (lines.Count > 0 && lines[^1].Length + 1 + word.Length <= maxCharacters)
            {
                lines[^1].Append(' ').Append(word);
            }
            else
            {
                lines.Add(new StringBuilder(word));
            }
        }

        return new DiagramText(string.Join('\n', lines).Replace("<br>", "\n", StringComparison.Ordinal), text.Format);
    }

    /// <summary>Splits a text at the runs of white space, as <c>split(/\s+/)</c>: a leading or trailing run gives an empty word.</summary>
    private static List<string> SplitOnWhiteSpace(string text)
    {
        var result = new List<string>();
        var start = 0;
        var i = 0;
        while (i < text.Length)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                i++;
                continue;
            }

            result.Add(text[start..i]);
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            start = i;
        }

        result.Add(text[start..]);
        return result;
    }

    private sealed class State(DiagramRenderContext context, SceneGroup root)
    {
        public SceneGroup Root { get; } = root;

        public BoundsBuilder Bounds { get; } = new();

        /// <summary>Draws the head: a rounded shape on the right of the spine, around the effect.</summary>
        public void DrawHead(DiagramText? effect, double spineY)
        {
            var fontSize = context.FontSize;
            var maxCharacters = Math.Max(6, (int)Math.Floor(110 / (fontSize * 0.6)));
            var text = context.Text(effect is null ? null : Wrap(effect, maxCharacters), fontSize * 0.875, bold: true);
            var width = Math.Max(60, text.Width + 6);
            var height = Math.Max(40, (text.Height * 2) + 40);
            var headGroup = Root.Add(new SceneGroup { CssClass = "ishikawa-head-group", Transform = SceneTransform.Translate(0, spineY) });
            var path = new PathBuilder().MoveTo(0, -height / 2).LineTo(0, height / 2).QuadraticTo(width * 2.4, 0, 0, -height / 2).Close().Build();
            headGroup.Add(new ScenePath(path) { Fill = context.Theme.NodeBackground, Stroke = context.Theme.LineColor, StrokeWidth = 2, CssClass = "ishikawa-head" });
            text.Draw(headGroup, (width / 2) + 3, 0, context.Theme.TextColor);

            // The curve reaches 1.2 times the width, half way
            Bounds.Add(new SceneBounds(0, spineY - (height / 2), width * 1.2, height));
        }

        /// <summary>Draws a main cause and its causes; returns the left of their texts.</summary>
        public double DrawBranch(SceneGroup target, IshikawaCause cause, double startX, double startY, int direction, double length)
        {
            var theme = context.Theme;
            var fontSize = context.FontSize;
            var children = cause.Causes;
            var lineLength = length * (children.Count > 0 ? 1 : 0.2);
            var dx = -CosAngle * lineLength;
            var dy = SinAngle * lineLength * direction;
            var endX = startX + dx;
            var endY = startY + dy;
            DrawLine(target, startX, startY, endX, endY, 2, "ishikawa-branch");

            // The text of the main cause, in a box at the end of the bone
            var label = context.Text(cause.Label);
            var labelX = endX - (label.Width / 2);
            var labelY = endY + (11 * direction) - (label.Height / 2);
            var labelGroup = target.Add(new SceneGroup { CssClass = "ishikawa-label-group" });
            var box = new SceneBounds(labelX - 20, labelY - 2, label.Width + 40, label.Height + 4);
            labelGroup.Add(new SceneRectangle(box.X, box.Y, box.Width, box.Height) { Fill = theme.NodeBackground, Stroke = theme.LineColor, StrokeWidth = 2, CssClass = "ishikawa-label-box" });
            label.Draw(labelGroup, endX, labelY + (label.Height / 2), theme.TextColor);
            Bounds.Add(box);
            var left = labelX;
            if (children.Count is 0)
                return left;

            // The causes of the cause, flattened: the even depths in pre-order, close to the spine, the odd depths in post-order
            var entries = new List<Entry>();
            var order = new List<int>();
            Flatten(children, parent: -1, depth: 2);
            var ys = new double[entries.Count];
            for (var slot = 0; slot < order.Count; slot++)
            {
                ys[order[slot]] = startY + (dy * (slot + 1) / (entries.Count + 1));
            }

            var bones = new Dictionary<int, Bone> { [-1] = new Bone(startX, startY, endX, endY, children.Count) };
            var diagonalX = -CosAngle;
            var diagonalY = SinAngle * direction;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var y = ys[i];
                var parent = bones[entry.Parent];
                var subGroup = target.Add(new SceneGroup { CssClass = "ishikawa-sub-group" });
                double x0;
                double y0;
                double x1;
                var text = context.Text(Wrap(entry.Cause.Label, SubCauseWrap));
                double textCenterY;
                if (entry.Depth % 2 == 0)
                {
                    // A horizontal bone, from the bone of its parent at its height, to the left
                    var parentDy = parent.Y1 - parent.Y0;
                    x0 = Lerp(parent.X0, parent.X1, parentDy != 0 ? (y - parent.Y0) / parentDy : 0.5);
                    y0 = y;
                    x1 = x0 - (entry.Cause.Causes.Count > 0 ? BoneBase + (entry.Cause.Causes.Count * BonePerChild) : BoneStub);
                    DrawLine(subGroup, x0, y, x1, y, 1, "ishikawa-sub-branch");
                    textCenterY = y;
                }
                else
                {
                    // A bone parallel to the main bones, from evenly spaced points of the horizontal bone of its parent
                    var k = parent.ChildrenDrawn++;
                    x0 = Lerp(parent.X0, parent.X1, (parent.ChildCount - k) / (double)(parent.ChildCount + 1));
                    y0 = parent.Y0;
                    x1 = x0 + (diagonalX * ((y - y0) / diagonalY));
                    DrawLine(subGroup, x0, y0, x1, y, 1, "ishikawa-sub-branch");

                    // Above the spine, the text ends on the end of the bone; below, it starts there
                    textCenterY = direction < 0 ? y - (fontSize * 0.3) : y + (fontSize * 0.5);
                }

                text.Draw(subGroup, x1 - (text.Width / 2), textCenterY, theme.TextColor, SceneTextAnchor.End);
                Bounds.Add(new SceneBounds(x1 - text.Width, textCenterY - (text.Height / 2), text.Width, text.Height));
                left = Math.Min(left, x1 - text.Width);
                if (entry.Cause.Causes.Count > 0)
                {
                    bones[i] = new Bone(x0, y0, x1, y, entry.Cause.Causes.Count);
                }
            }

            return left;

            void Flatten(IList<IshikawaCause> nodes, int parent, int depth)
            {
                if (depth > 66)
                    return;

                var ordered = direction == -1 ? nodes.Reverse() : nodes;
                foreach (var child in ordered)
                {
                    var index = entries.Count;
                    entries.Add(new Entry(child, depth, parent));
                    if (depth % 2 == 0)
                    {
                        order.Add(index);
                        Flatten(child.Causes, index, depth + 1);
                    }
                    else
                    {
                        Flatten(child.Causes, index, depth + 1);
                        order.Add(index);
                    }
                }
            }
        }

        /// <summary>Draws a bone with an arrowhead at its start, pointing to the bone it leads to.</summary>
        private void DrawLine(SceneGroup target, double x0, double y0, double x1, double y1, double strokeWidth, string cssClass)
        {
            var color = context.Theme.LineColor;
            target.Add(new SceneLine(x0, y0, x1, y1) { Stroke = color, StrokeWidth = strokeWidth, CssClass = cssClass });
            Bounds.Add(new LayoutPoint(x0, y0));
            Bounds.Add(new LayoutPoint(x1, y1));
            var length = Math.Sqrt(((x1 - x0) * (x1 - x0)) + ((y1 - y0) * (y1 - y0)));
            if (length < 1e-9 || !double.IsFinite(length))
                return;

            // The marker of Mermaid is 6 stroke widths long and wide
            var size = 6 * strokeWidth;
            var ux = (x1 - x0) / length;
            var uy = (y1 - y0) / length;
            var baseX = x0 + (ux * size);
            var baseY = y0 + (uy * size);
            var arrow = Geometry.Polygon((x0, y0), (baseX - (uy * size / 2), baseY + (ux * size / 2)), (baseX + (uy * size / 2), baseY - (ux * size / 2)));
            target.Add(new ScenePath(arrow) { Fill = color, CssClass = "ishikawa-arrow" });
        }

        private static double Lerp(double a, double b, double t) => a + ((b - a) * t);
    }

    private sealed record Entry(IshikawaCause Cause, int Depth, int Parent);

    private sealed class Bone(double x0, double y0, double x1, double y1, int childCount)
    {
        public double X0 { get; } = x0;

        public double Y0 { get; } = y0;

        public double X1 { get; } = x1;

        public double Y1 { get; } = y1;

        public int ChildCount { get; } = childCount;

        public int ChildrenDrawn { get; set; }
    }
}
