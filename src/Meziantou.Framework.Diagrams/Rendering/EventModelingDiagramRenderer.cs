using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Draws the event models as Mermaid: a swimlane per kind of entity and namespace, the frames left to right in order,
/// each box starting where the previous one ends minus an overlap, and the relations as straight arrows between the
/// boxes of different swimlanes.
/// </summary>
internal static class EventModelingDiagramRenderer
{
    private const double SwimlaneMinHeight = 70;
    private const double SwimlanePadding = 15;
    private const double SwimlaneGap = 10;
    private const double BoxPadding = 10;
    private const double BoxOverlap = 90;
    private const double BoxMinWidth = 80;
    private const double BoxMaxWidth = 450;
    private const double BoxMinHeight = 80;
    private const double BoxMaxHeight = 750;
    private const double ContentStartX = 250;
    private const double TextMaxWidth = 430;
    private const double TextPadding = 10;

    private static readonly SceneColor LaneBackground = new(250, 250, 250);
    private static readonly SceneColor LaneStroke = new(240, 240, 240);
    private static readonly SceneColor TextColor = (SceneColor)"#000000";

    public static SceneBounds Render(EventModelingDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var lanes = new List<Lane>();
        var boxes = new List<Box>();
        var boxOf = new Dictionary<EventModelingFrame, Box>(ReferenceEqualityComparer.Instance);
        var maxRight = 0.0;
        Lane? previousLane = null;
        foreach (var frame in diagram.Frames)
        {
            if (boxOf.ContainsKey(frame))
                continue;

            var lane = GetLane(lanes, frame);
            var (name, data) = CreateText(context, frame);
            var textWidth = Math.Max(name.Width, data?.Width ?? 0);
            var textHeight = name.Height + (data is null ? 0 : name.LineHeight + data.Height);
            var width = Math.Clamp(textWidth + (2 * TextPadding), BoxMinWidth, BoxMaxWidth) + (2 * BoxPadding);
            var height = Math.Clamp(textHeight + (2 * TextPadding), BoxMinHeight, BoxMaxHeight) + (2 * BoxPadding);

            // The box follows the previous one of its swimlane, or overlaps the previous box of another swimlane
            double x;
            if (previousLane is null)
            {
                x = ContentStartX;
            }
            else if (previousLane == lane && lane.Right > 0)
            {
                x = lane.Right + BoxPadding;
            }
            else
            {
                x = boxes[^1].Right - BoxOverlap + BoxPadding;
            }

            if (lane.Right > 0)
            {
                x = Math.Max(x, lane.Right + BoxPadding);
            }

            var box = new Box(frame, lane, x, width, height, name, data);
            boxes.Add(box);
            boxOf[frame] = box;
            maxRight = Math.Max(maxRight, box.Right);
            lane.Right = x + width;
            lane.MaxBoxHeight = Math.Max(lane.MaxBoxHeight, height);
            previousLane = lane;
        }

        // The swimlanes are stacked in the order of their kind, then of their first frame
        lanes.Sort((a, b) => a.Order.CompareTo(b.Order));
        var y = 0.0;
        foreach (var lane in lanes)
        {
            lane.Y = y;
            lane.Height = Math.Max(SwimlaneMinHeight, lane.MaxBoxHeight) + (2 * SwimlanePadding);
            y += lane.Height + SwimlaneGap;
        }

        var bounds = new BoundsBuilder();
        var lanesGroup = group.Add(new SceneGroup { CssClass = "swimlanes" });
        foreach (var lane in lanes)
        {
            var laneGroup = lanesGroup.Add(new SceneGroup { CssClass = "em-swimlane" });
            var rectangle = new SceneBounds(0, lane.Y, maxRight + SwimlanePadding, lane.Height);
            laneGroup.Add(new SceneRectangle(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height) { Fill = LaneBackground, Stroke = LaneStroke, StrokeWidth = 1, CornerRadius = 3 });
            var label = context.Text(lane.Label, bold: true);
            label.Draw(laneGroup, 30 + (label.Width / 2), lane.Y + 30 - label.Ascent + (label.Height / 2), TextColor);
            bounds.Add(rectangle);
            bounds.Add(new SceneBounds(30, lane.Y + 30 - label.Ascent, label.Width, label.Height));
        }

        var boxesGroup = group.Add(new SceneGroup { CssClass = "boxes" });
        foreach (var box in boxes)
        {
            var (fill, stroke) = GetColors(box.Frame.Kind);
            var top = box.Lane.Y + SwimlanePadding;
            var boxGroup = boxesGroup.Add(new SceneGroup { CssClass = "em-box" });
            boxGroup.SetDataAttribute("id", box.Frame.Id);
            boxGroup.Add(new SceneRectangle(box.X, top, box.Width, box.Height) { Fill = fill, Stroke = stroke, StrokeWidth = 1, CornerRadius = 3 });
            var textHeight = box.Name.Height + (box.Data is null ? 0 : box.Name.LineHeight + box.Data.Height);
            var cursor = top + (box.Height / 2) - (textHeight / 2);
            var centerX = box.X + (box.Width / 2);
            box.Name.Draw(boxGroup, centerX, cursor + (box.Name.Height / 2), TextColor);
            if (box.Data is { } data)
            {
                cursor += box.Name.Height + box.Name.LineHeight;
                data.Draw(boxGroup, centerX, cursor + (data.Height / 2), TextColor, SceneTextAnchor.Start);
            }

            bounds.Add(new SceneBounds(box.X, top, box.Width, box.Height));
        }

        var relationsGroup = group.Add(new SceneGroup { CssClass = "relations" });
        for (var index = 0; index < boxes.Count; index++)
        {
            var target = boxes[index];
            if (target.Frame.Sources.Count > 0)
            {
                // The sources in the order of the timeline, as Mermaid
                foreach (var source in boxes)
                {
                    if (target.Frame.Sources.Contains(source.Frame))
                    {
                        DrawRelation(relationsGroup, source, target);
                    }
                }
            }
            else if (index > 0 && !target.Frame.IsReset)
            {
                // The relation is inferred from the last frame of another swimlane
                for (var i = index - 1; i >= 0; i--)
                {
                    if (boxes[i].Lane != target.Lane)
                    {
                        DrawRelation(relationsGroup, boxes[i], target);
                        break;
                    }
                }
            }
        }

        return bounds.ToBounds();
    }

    private static void DrawRelation(SceneGroup target, Box source, Box destination)
    {
        var sourceTop = source.Lane.Y + SwimlanePadding;
        var targetTop = destination.Lane.Y + SwimlanePadding;
        var upwards = sourceTop > targetTop;
        var start = new LayoutPoint(source.X + (source.Width * 2 / 3), upwards ? sourceTop : sourceTop + source.Height);
        var end = new LayoutPoint(destination.X + (destination.Width / 3), upwards ? targetTop + destination.Height : targetTop);
        if (Geometry.Distance(start, end) < 1e-6)
            return;

        var relation = target.Add(new SceneGroup { CssClass = "em-relation" });
        var lineEnd = Geometry.Distance(start, end) > EdgeMarkers.GetSetback(EdgeMarker.Arrow) ? Geometry.MoveToward(end, start, EdgeMarkers.GetSetback(EdgeMarker.Arrow)) : end;
        relation.Add(new SceneLine(start.X, start.Y, lineEnd.X, lineEnd.Y) { Stroke = TextColor, StrokeWidth = 1 });
        EdgeMarkers.Draw(relation, EdgeMarker.Arrow, end, start, TextColor, SceneColor.White, 1);
    }

    /// <summary>Returns the text of a box: the name of the entity, and its data below, from its data block or inline.</summary>
    private static (TextBlock Name, TextBlock? Data) CreateText(DiagramRenderContext context, EventModelingFrame frame)
    {
        var name = context.Text(GetName(frame.Entity), context.FontSize, bold: true, maxWidth: TextMaxWidth);
        var data = frame.DataBlock?.Data ?? frame.Data;
        if (data is null)
            return (name, null);

        var content = data.Content.Replace("\r\n", "\n", StringComparison.Ordinal).Trim('\n');
        return (name, context.Text(content, context.FontSize * 0.75, maxWidth: TextMaxWidth));
    }

    /// <summary>Returns the name shown for an entity: the second part of <c>Namespace.Name</c>, or the whole identifier.</summary>
    internal static string GetName(string entity)
    {
        var parts = entity.Split('.');
        return parts.Length == 2 ? parts[1] : entity;
    }

    /// <summary>Returns the namespace of an entity: the first part of <c>Namespace.Name</c>, or <see langword="null"/>.</summary>
    internal static string? GetNamespace(string entity)
    {
        var parts = entity.Split('.');
        return parts.Length == 2 && parts[0].Length > 0 ? parts[0] : null;
    }

    private static Lane GetLane(List<Lane> lanes, EventModelingFrame frame)
    {
        var band = frame.Kind switch
        {
            EventModelingEntityKind.Ui or EventModelingEntityKind.Processor => 0,
            EventModelingEntityKind.Command or EventModelingEntityKind.ReadModel => 1,
            _ => 2,
        };
        var ns = GetNamespace(frame.Entity);
        foreach (var lane in lanes)
        {
            if (lane.Band == band && string.Equals(lane.Namespace, ns, StringComparison.Ordinal))
                return lane;
        }

        var label = (band, ns) switch
        {
            (0, null) => "UI/Automation",
            (0, _) => "UI/A: " + ns,
            (1, null) => "Command/Read Model",
            (1, _) => "C/RM: " + ns,
            (_, null) => "Events",
            _ => "Stream: " + ns,
        };

        // The swimlane without namespace comes first in its kind, the others in the order they appear
        var order = (band * 100_000) + (ns is null ? 0 : 1 + lanes.Count(other => other.Band == band && other.Namespace is not null));
        var result = new Lane(band, ns, label, order);
        lanes.Add(result);
        return result;
    }

    internal static (SceneColor Fill, SceneColor Stroke) GetColors(EventModelingEntityKind kind) => kind switch
    {
        EventModelingEntityKind.Ui => ((SceneColor)"#ffffff", (SceneColor)"#dbdada"),
        EventModelingEntityKind.Processor => ((SceneColor)"#edb3f6", (SceneColor)"#b88cbf"),
        EventModelingEntityKind.ReadModel => ((SceneColor)"#d3f1a2", (SceneColor)"#a3b732"),
        EventModelingEntityKind.Command => ((SceneColor)"#bcd6fe", (SceneColor)"#679ac3"),
        _ => ((SceneColor)"#ffb778", (SceneColor)"#c19a0f"),
    };

    private sealed class Lane(int band, string? ns, string label, int order)
    {
        public int Band { get; } = band;

        public string? Namespace { get; } = ns;

        public string Label { get; } = label;

        public int Order { get; } = order;

        public double Right { get; set; }

        public double MaxBoxHeight { get; set; } = SwimlaneMinHeight;

        public double Y { get; set; }

        public double Height { get; set; }
    }

    private sealed record Box(EventModelingFrame Frame, Lane Lane, double X, double Width, double Height, TextBlock Name, TextBlock? Data)
    {
        public double Right => X + Width + BoxPadding;
    }
}
