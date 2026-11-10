using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>The geometry of the shapes of the nodes, sized to their label.</summary>
internal static class Shapes
{
    public const double PaddingX = 15;
    public const double PaddingY = 10;

    /// <summary>Returns the geometry of a shape around a label.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="labelWidth">The width of the label.</param>
    /// <param name="labelHeight">The height of the label.</param>
    /// <param name="horizontalFlow">Whether the diagram flows horizontally: a fork is then vertical.</param>
    public static ShapeDrawing Create(FlowchartNodeShape shape, double labelWidth, double labelHeight, bool horizontalFlow = false)
    {
        var w = labelWidth;
        var h = labelHeight;
        var boxW = w + (2 * PaddingX);
        var boxH = h + (2 * PaddingY);
        switch (shape)
        {
            case FlowchartNodeShape.RoundedRectangle:
                return new ShapeDrawing { Width = boxW, Height = boxH, Outline = Geometry.RoundedRectangle(boxW, boxH, 5) };

            case FlowchartNodeShape.Stadium:
                {
                    var width = w + boxH + PaddingX;
                    return new ShapeDrawing { Width = width, Height = boxH, Outline = Geometry.RoundedRectangle(width, boxH, boxH / 2) };
                }

            case FlowchartNodeShape.FramedRectangle:
                {
                    var width = boxW + 16;
                    var drawing = new ShapeDrawing { Width = width, Height = boxH, Outline = Rectangle(width, boxH) };
                    drawing.Decorations.Add(Line((-width / 2) + 8, -boxH / 2, (-width / 2) + 8, boxH / 2));
                    drawing.Decorations.Add(Line((width / 2) - 8, -boxH / 2, (width / 2) - 8, boxH / 2));
                    return drawing;
                }

            case FlowchartNodeShape.Cylinder:
            case FlowchartNodeShape.LinedCylinder:
                {
                    var width = boxW;
                    var rx = width / 2;
                    var ry = rx / (2.5 + (width / 50));
                    var height = boxH + (2 * ry);
                    var top = -height / 2;
                    var bottom = height / 2;
                    var outline = new PathBuilder()
                        .MoveTo(-rx, top + ry)
                        .CubicTo(-rx, top + (ry * (1 - Kappa)), -rx * Kappa, top, 0, top)
                        .CubicTo(rx * Kappa, top, rx, top + (ry * (1 - Kappa)), rx, top + ry)
                        .LineTo(rx, bottom - ry)
                        .CubicTo(rx, bottom - (ry * (1 - Kappa)), rx * Kappa, bottom, 0, bottom)
                        .CubicTo(-rx * Kappa, bottom, -rx, bottom - (ry * (1 - Kappa)), -rx, bottom - ry)
                        .Close()
                        .Build();
                    var drawing = new ShapeDrawing { Width = width, Height = height, Outline = outline, LabelOffsetY = ry / 2 };
                    drawing.Decorations.Add(LowerArc(top + ry));
                    if (shape is FlowchartNodeShape.LinedCylinder)
                    {
                        drawing.Decorations.Add(LowerArc(top + ry + 8));
                    }

                    return drawing;

                    PathData LowerArc(double y) => new PathBuilder().MoveTo(-rx, y).CubicTo(-rx, y + (ry * Kappa), -rx * Kappa, y + ry, 0, y + ry).CubicTo(rx * Kappa, y + ry, rx, y + (ry * Kappa), rx, y).Build();
                }

            case FlowchartNodeShape.DataStore:
                {
                    var drawing = new ShapeDrawing { Width = boxW, Height = boxH, Outline = Rectangle(boxW, boxH), StrokeOutline = false };
                    drawing.Decorations.Add(Line(-boxW / 2, -boxH / 2, boxW / 2, -boxH / 2));
                    drawing.Decorations.Add(Line(-boxW / 2, boxH / 2, boxW / 2, boxH / 2));
                    return drawing;
                }

            case FlowchartNodeShape.Folder:
                {
                    const double Tab = 8;
                    var height = boxH + Tab;
                    var tabWidth = Math.Min(boxW / 3, 40);
                    var x = -boxW / 2;
                    var y = -height / 2;
                    var outline = Geometry.Polygon((x, y), (x + tabWidth, y), (x + tabWidth + Tab, y + Tab), (boxW / 2, y + Tab), (boxW / 2, height / 2), (x, height / 2));
                    return new ShapeDrawing { Width = boxW, Height = height, Outline = outline, LabelOffsetY = Tab / 2 };
                }

            case FlowchartNodeShape.Bucket:
                {
                    var width = boxW / 0.85;
                    var height = boxH + 8;
                    var ry = 4.0;
                    var outline = new PathBuilder()
                        .MoveTo(-width / 2, (-height / 2) + ry)
                        .LineTo(-width * 0.4, height / 2)
                        .LineTo(width * 0.4, height / 2)
                        .LineTo(width / 2, (-height / 2) + ry)
                        .CubicTo(width / 2, (-height / 2) + (ry * (1 - Kappa)), width * Kappa / 2, -height / 2, 0, -height / 2)
                        .CubicTo(-width * Kappa / 2, -height / 2, -width / 2, (-height / 2) + (ry * (1 - Kappa)), -width / 2, (-height / 2) + ry)
                        .Close()
                        .Build();
                    var drawing = new ShapeDrawing { Width = width, Height = height, Outline = outline, LabelOffsetY = ry };
                    drawing.Decorations.Add(new PathBuilder().MoveTo(-width / 2, (-height / 2) + ry).CubicTo(-width / 2, (-height / 2) + (ry * (1 + Kappa)), -width * Kappa / 2, (-height / 2) + (2 * ry), 0, (-height / 2) + (2 * ry)).CubicTo(width * Kappa / 2, (-height / 2) + (2 * ry), width / 2, (-height / 2) + (ry * (1 + Kappa)), width / 2, (-height / 2) + ry).Build());
                    return drawing;
                }

            case FlowchartNodeShape.Console:
            case FlowchartNodeShape.Browser:
                {
                    const double Bar = 12;
                    var height = boxH + Bar;
                    var drawing = new ShapeDrawing { Width = boxW, Height = height, Outline = Geometry.RoundedRectangle(boxW, height, 3), LabelOffsetY = Bar / 2 };
                    drawing.Decorations.Add(Line(-boxW / 2, (-height / 2) + Bar, boxW / 2, (-height / 2) + Bar));
                    if (shape is FlowchartNodeShape.Browser)
                    {
                        for (var i = 0; i < 3; i++)
                        {
                            drawing.Decorations.Add(Geometry.Ellipse((-boxW / 2) + 8 + (i * 8), (-height / 2) + (Bar / 2), 2.5, 2.5));
                        }
                    }
                    else
                    {
                        drawing.Decorations.Add(new PathBuilder().MoveTo((-boxW / 2) + 6, (-height / 2) + 3).LineTo((-boxW / 2) + 10, (-height / 2) + 6).LineTo((-boxW / 2) + 6, (-height / 2) + 9).Build());
                    }

                    return drawing;
                }

            case FlowchartNodeShape.Person:
                {
                    const double Head = 10;
                    var height = boxH + (2 * Head) + 4;
                    var bodyTop = (-height / 2) + (2 * Head) + 4;
                    var outline = new PathBuilder()
                        .AddEllipse(0, (-height / 2) + Head, Head, Head)
                        .Build();
                    var body = Geometry.RoundedRectangle(boxW, boxH, Math.Min(12, boxH / 2));
                    var drawing = new ShapeDrawing { Width = boxW, Height = height, Outline = Combine(Translate(body, 0, bodyTop + (boxH / 2)), outline), LabelOffsetY = bodyTop + (boxH / 2) };
                    return drawing;
                }

            case FlowchartNodeShape.Circle:
            case FlowchartNodeShape.DoubleCircle:
            case FlowchartNodeShape.CrossedCircle:
                {
                    var radius = (Math.Max(w, h) / 2) + PaddingX;
                    var outer = shape is FlowchartNodeShape.DoubleCircle ? radius + 5 : radius;
                    var drawing = new ShapeDrawing { Width = 2 * outer, Height = 2 * outer, Outline = Geometry.Ellipse(0, 0, outer, outer) };
                    if (shape is FlowchartNodeShape.DoubleCircle)
                    {
                        drawing.Decorations.Add(Geometry.Ellipse(0, 0, radius, radius));
                    }
                    else if (shape is FlowchartNodeShape.CrossedCircle)
                    {
                        var d = radius / Math.Sqrt(2);
                        drawing.Decorations.Add(Line(-d, -d, d, d));
                        drawing.Decorations.Add(Line(-d, d, d, -d));
                    }

                    return drawing;
                }

            case FlowchartNodeShape.Bang:
                {
                    var width = boxW * 1.4;
                    var height = boxH * 1.6;
                    var points = new (double, double)[24];
                    for (var i = 0; i < points.Length; i++)
                    {
                        var angle = (i * Math.PI * 2 / points.Length) - (Math.PI / 2);
                        var scale = i % 2 == 0 ? 0.5 : 0.36;
                        points[i] = (Math.Cos(angle) * width * scale, Math.Sin(angle) * height * scale);
                    }

                    return new ShapeDrawing { Width = width, Height = height, Outline = Geometry.Polygon(points) };
                }

            case FlowchartNodeShape.Cloud:
                {
                    var width = boxW * 1.3;
                    var height = boxH * 1.5;
                    var builder = new PathBuilder();
                    const int Bumps = 9;
                    for (var i = 0; i <= Bumps; i++)
                    {
                        var angle = i * Math.PI * 2 / Bumps;
                        var x = Math.Cos(angle) * width * 0.42;
                        var y = Math.Sin(angle) * height * 0.38;
                        if (i == 0)
                        {
                            builder.MoveTo(x, y);
                            continue;
                        }

                        var middle = (i - 0.5) * Math.PI * 2 / Bumps;
                        builder.QuadraticTo(Math.Cos(middle) * width * 0.62, Math.Sin(middle) * height * 0.62, x, y);
                    }

                    return new ShapeDrawing { Width = width, Height = height, Outline = builder.Close().Build() };
                }

            case FlowchartNodeShape.Diamond:
                {
                    var size = w + h + (2 * PaddingX);
                    return new ShapeDrawing { Width = size, Height = size, Outline = Geometry.Polygon((0, -size / 2), (size / 2, 0), (0, size / 2), (-size / 2, 0)) };
                }

            case FlowchartNodeShape.Hexagon:
                {
                    var m = boxH / 4;
                    var width = boxW + (2 * m);
                    return new ShapeDrawing
                    {
                        Width = width,
                        Height = boxH,
                        Outline = Geometry.Polygon(((-width / 2) + m, -boxH / 2), ((width / 2) - m, -boxH / 2), (width / 2, 0), ((width / 2) - m, boxH / 2), ((-width / 2) + m, boxH / 2), (-width / 2, 0)),
                    };
                }

            case FlowchartNodeShape.LeanRight:
            case FlowchartNodeShape.LeanLeft:
            case FlowchartNodeShape.TrapezoidBaseBottom:
            case FlowchartNodeShape.TrapezoidBaseTop:
                {
                    var s = boxH / 2;
                    var isTrapezoid = shape is FlowchartNodeShape.TrapezoidBaseBottom or FlowchartNodeShape.TrapezoidBaseTop;
                    var width = boxW + (isTrapezoid ? 2 * s : s);
                    var x = width / 2;
                    var y = boxH / 2;
                    var outline = shape switch
                    {
                        FlowchartNodeShape.LeanRight => Geometry.Polygon((-x + s, -y), (x, -y), (x - s, y), (-x, y)),
                        FlowchartNodeShape.LeanLeft => Geometry.Polygon((-x, -y), (x - s, -y), (x, y), (-x + s, y)),
                        FlowchartNodeShape.TrapezoidBaseBottom => Geometry.Polygon((-x + s, -y), (x - s, -y), (x, y), (-x, y)),
                        _ => Geometry.Polygon((-x, -y), (x, -y), (x - s, y), (-x + s, y)),
                    };
                    return new ShapeDrawing { Width = width, Height = boxH, Outline = outline };
                }

            case FlowchartNodeShape.Text:
                {
                    var width = w + 8;
                    var height = h + 8;
                    return new ShapeDrawing { Width = width, Height = height, Outline = Rectangle(width, height), FillOutline = false, StrokeOutline = false };
                }

            case FlowchartNodeShape.NotchedRectangle:
                {
                    const double Notch = 12;
                    var x = boxW / 2;
                    var y = boxH / 2;
                    return new ShapeDrawing { Width = boxW, Height = boxH, Outline = Geometry.Polygon((-x + Notch, -y), (x, -y), (x, y), (-x, y), (-x, -y + Notch)) };
                }

            case FlowchartNodeShape.LinedRectangle:
                {
                    var width = boxW + 8;
                    var drawing = new ShapeDrawing { Width = width, Height = boxH, Outline = Rectangle(width, boxH), LabelOffsetX = 4 };
                    drawing.Decorations.Add(Line((-width / 2) + 8, -boxH / 2, (-width / 2) + 8, boxH / 2));
                    return drawing;
                }

            case FlowchartNodeShape.SmallCircle:
            case FlowchartNodeShape.FilledCircle:
                return new ShapeDrawing { Width = 14, Height = 14, Outline = Geometry.Ellipse(0, 0, 7, 7), FillWithLineColor = true, ShowLabel = false };

            case FlowchartNodeShape.FramedCircle:
                {
                    var drawing = new ShapeDrawing { Width = 14, Height = 14, Outline = Geometry.Ellipse(0, 0, 7, 7), ShowLabel = false };
                    drawing.FilledDecorations.Add(Geometry.Ellipse(0, 0, 4.5, 4.5));
                    return drawing;
                }

            case FlowchartNodeShape.Fork:
                {
                    var (width, height) = horizontalFlow ? (10.0, 70.0) : (70.0, 10.0);
                    return new ShapeDrawing { Width = width, Height = height, Outline = Rectangle(width, height), FillWithLineColor = true, ShowLabel = false };
                }

            case FlowchartNodeShape.Hourglass:
                {
                    // The label is under the shape
                    const double Size = 30;
                    var width = Math.Max(Size, w);
                    var height = Size + (h > 0 ? h + 4 : 0);
                    var x = Size / 2;
                    var top = -height / 2;
                    return new ShapeDrawing
                    {
                        Width = width,
                        Height = height,
                        Outline = Geometry.Polygon((-x, top), (x, top), (-x, top + Size), (x, top + Size)),
                        LabelOffsetY = top + Size + 4 + (h / 2),
                    };
                }

            case FlowchartNodeShape.CurlyBraceLeft:
            case FlowchartNodeShape.CurlyBraceRight:
            case FlowchartNodeShape.CurlyBraces:
                {
                    var both = shape is FlowchartNodeShape.CurlyBraces;
                    var width = w + (both ? 32 : 20);
                    var height = h + 10;
                    var drawing = new ShapeDrawing
                    {
                        Width = width,
                        Height = height,
                        Outline = Rectangle(width, height),
                        FillOutline = false,
                        StrokeOutline = false,
                        LabelOffsetX = shape switch { FlowchartNodeShape.CurlyBraceLeft => 6, FlowchartNodeShape.CurlyBraceRight => -6, _ => 0 },
                    };
                    if (shape is not FlowchartNodeShape.CurlyBraceRight)
                    {
                        drawing.Decorations.Add(Brace((-width / 2) + 8, height, left: true));
                    }

                    if (shape is not FlowchartNodeShape.CurlyBraceLeft)
                    {
                        drawing.Decorations.Add(Brace((width / 2) - 8, height, left: false));
                    }

                    return drawing;
                }

            case FlowchartNodeShape.LightningBolt:
                {
                    // The label is under the shape
                    const double BoltWidth = 35;
                    const double BoltHeight = 50;
                    var width = Math.Max(BoltWidth, w);
                    var height = BoltHeight + (h > 0 ? h + 4 : 0);
                    var top = -height / 2;
                    (double X, double Y)[] unit = [(-0.1, -0.5), (0.5, -0.5), (0.1, -0.05), (0.35, -0.05), (-0.3, 0.5), (-0.05, 0.05), (-0.4, 0.05)];
                    return new ShapeDrawing
                    {
                        Width = width,
                        Height = height,
                        Outline = Geometry.Polygon([.. unit.Select(point => (point.X * BoltWidth, top + ((point.Y + 0.5) * BoltHeight)))]),
                        LabelOffsetY = top + BoltHeight + 4 + (h / 2),
                    };
                }

            case FlowchartNodeShape.Document:
            case FlowchartNodeShape.LinedDocument:
            case FlowchartNodeShape.TaggedDocument:
            case FlowchartNodeShape.StackedDocument:
                {
                    var amplitude = boxH / 8;
                    var width = boxW + (shape is FlowchartNodeShape.LinedDocument ? 8 : 0);
                    var height = boxH + amplitude;
                    var outline = Wave(0, 0, width, height, amplitude, waveTop: false);
                    var drawing = new ShapeDrawing { Width = width, Height = height, Outline = outline, LabelOffsetY = -amplitude / 2, LabelOffsetX = shape is FlowchartNodeShape.LinedDocument ? 4 : 0 };
                    if (shape is FlowchartNodeShape.LinedDocument)
                    {
                        drawing.Decorations.Add(Line((-width / 2) + 8, -height / 2, (-width / 2) + 8, (height / 2) - amplitude));
                    }
                    else if (shape is FlowchartNodeShape.TaggedDocument)
                    {
                        drawing.Decorations.Add(Line((width / 2) - 14, (height / 2) - amplitude, width / 2, (height / 2) - amplitude - 14));
                    }
                    else if (shape is FlowchartNodeShape.StackedDocument)
                    {
                        drawing.Underlays.Add(Wave(10, -10, width, height, amplitude, waveTop: false));
                        drawing.Underlays.Add(Wave(5, -5, width, height, amplitude, waveTop: false));
                        return new ShapeDrawing { Width = width + 10, Height = height + 10, Outline = Translate(outline, -5, 5), LabelOffsetX = -5, LabelOffsetY = 5 - (amplitude / 2), Underlays = { Translate(drawing.Underlays[0], -5, 5), Translate(drawing.Underlays[1], -5, 5) } };
                    }

                    return drawing;
                }

            case FlowchartNodeShape.Flag:
                {
                    var amplitude = boxH / 8;
                    var height = boxH + (2 * amplitude);
                    return new ShapeDrawing { Width = boxW, Height = height, Outline = Wave(0, 0, boxW, height, amplitude, waveTop: true) };
                }

            case FlowchartNodeShape.HalfRoundedRectangle:
                {
                    var width = boxW + (boxH / 2);
                    var x = width / 2;
                    var y = boxH / 2;
                    var outline = new PathBuilder()
                        .MoveTo(-x, -y)
                        .LineTo(x - y, -y)
                        .CubicTo(x - y + (y * Kappa), -y, x, -y * Kappa, x, 0)
                        .CubicTo(x, y * Kappa, x - y + (y * Kappa), y, x - y, y)
                        .LineTo(-x, y)
                        .Close()
                        .Build();
                    return new ShapeDrawing { Width = width, Height = boxH, Outline = outline, LabelOffsetX = -y / 4 };
                }

            case FlowchartNodeShape.HorizontalCylinder:
                {
                    var ry = boxH / 2;
                    var rx = ry / (2.5 + (boxH / 50));
                    var width = boxW + (2 * rx);
                    var x = width / 2;
                    var outline = new PathBuilder()
                        .MoveTo(-x + rx, -ry)
                        .LineTo(x - rx, -ry)
                        .CubicTo(x - rx + (rx * Kappa), -ry, x, -ry * Kappa, x, 0)
                        .CubicTo(x, ry * Kappa, x - rx + (rx * Kappa), ry, x - rx, ry)
                        .LineTo(-x + rx, ry)
                        .CubicTo(-x + rx - (rx * Kappa), ry, -x, ry * Kappa, -x, 0)
                        .CubicTo(-x, -ry * Kappa, -x + rx - (rx * Kappa), -ry, -x + rx, -ry)
                        .Close()
                        .Build();
                    var drawing = new ShapeDrawing { Width = width, Height = boxH, Outline = outline, LabelOffsetX = -rx / 2 };
                    drawing.Decorations.Add(new PathBuilder().MoveTo(x - rx, -ry).CubicTo(x - rx - (rx * Kappa), -ry, x - (2 * rx), -ry * Kappa, x - (2 * rx), 0).CubicTo(x - (2 * rx), ry * Kappa, x - rx - (rx * Kappa), ry, x - rx, ry).Build());
                    return drawing;
                }

            case FlowchartNodeShape.CurvedTrapezoid:
                {
                    var s = boxH / 2;
                    var width = boxW + s + (boxH / 4);
                    var x = width / 2;
                    var y = boxH / 2;
                    var c = boxH / 4;
                    var outline = new PathBuilder()
                        .MoveTo(-x, 0)
                        .LineTo(-x + s, -y)
                        .LineTo(x - c, -y)
                        .CubicTo(x - c + (c * Kappa), -y, x, -y * Kappa, x, 0)
                        .CubicTo(x, y * Kappa, x - c + (c * Kappa), y, x - c, y)
                        .LineTo(-x + s, y)
                        .Close()
                        .Build();
                    return new ShapeDrawing { Width = width, Height = boxH, Outline = outline };
                }

            case FlowchartNodeShape.DividedRectangle:
                {
                    var height = boxH + 10;
                    var drawing = new ShapeDrawing { Width = boxW, Height = height, Outline = Rectangle(boxW, height), LabelOffsetY = 5 };
                    drawing.Decorations.Add(Line(-boxW / 2, (-height / 2) + 10, boxW / 2, (-height / 2) + 10));
                    return drawing;
                }

            case FlowchartNodeShape.Triangle:
            case FlowchartNodeShape.FlippedTriangle:
                {
                    // The label is in the wide third of the triangle, which is two thirds of its base wide
                    var width = (boxW * 1.5) + h;
                    var height = Math.Max(width * 0.5, (boxH * 2) + h);
                    var x = width / 2;
                    var y = height / 2;
                    return shape is FlowchartNodeShape.Triangle
                        ? new ShapeDrawing { Width = width, Height = height, Outline = Geometry.Polygon((0, -y), (x, y), (-x, y)), LabelOffsetY = height / 4 }
                        : new ShapeDrawing { Width = width, Height = height, Outline = Geometry.Polygon((-x, -y), (x, -y), (0, y)), LabelOffsetY = -height / 4 };
                }

            case FlowchartNodeShape.WindowPane:
                {
                    var width = boxW + 10;
                    var height = boxH + 10;
                    var drawing = new ShapeDrawing { Width = width, Height = height, Outline = Rectangle(width, height), LabelOffsetX = 5, LabelOffsetY = 5 };
                    drawing.Decorations.Add(Line((-width / 2) + 10, -height / 2, (-width / 2) + 10, height / 2));
                    drawing.Decorations.Add(Line(-width / 2, (-height / 2) + 10, width / 2, (-height / 2) + 10));
                    return drawing;
                }

            case FlowchartNodeShape.TrapezoidalPentagon:
                {
                    const double Cut = 10;
                    var x = boxW / 2;
                    var y = boxH / 2;
                    return new ShapeDrawing { Width = boxW, Height = boxH, Outline = Geometry.Polygon((-x + Cut, -y), (x - Cut, -y), (x, -y + Cut), (x, y), (-x, y), (-x, -y + Cut)) };
                }

            case FlowchartNodeShape.SlopedRectangle:
                {
                    var slope = boxH / 4;
                    var height = boxH + slope;
                    var x = boxW / 2;
                    var y = height / 2;
                    return new ShapeDrawing { Width = boxW, Height = height, Outline = Geometry.Polygon((-x, -y + slope), (x, -y), (x, y), (-x, y)), LabelOffsetY = slope / 2 };
                }

            case FlowchartNodeShape.StackedRectangle:
                {
                    var drawing = new ShapeDrawing { Width = boxW + 10, Height = boxH + 10, Outline = Translate(Rectangle(boxW, boxH), -5, 5), LabelOffsetX = -5, LabelOffsetY = 5 };
                    drawing.Underlays.Add(Translate(Rectangle(boxW, boxH), 5, -5));
                    drawing.Underlays.Add(Rectangle(boxW, boxH));
                    return drawing;
                }

            case FlowchartNodeShape.BowTieRectangle:
                {
                    const double Bow = 10;
                    var width = boxW + (2 * Bow);
                    var x = width / 2;
                    var y = boxH / 2;
                    var outline = new PathBuilder()
                        .MoveTo(-x + Bow, -y)
                        .LineTo(x, -y)
                        .QuadraticTo(x - (2 * Bow), 0, x, y)
                        .LineTo(-x + Bow, y)
                        .QuadraticTo(-x - Bow, 0, -x + Bow, -y)
                        .Close()
                        .Build();
                    return new ShapeDrawing { Width = width, Height = boxH, Outline = outline };
                }

            case FlowchartNodeShape.TaggedRectangle:
                {
                    var drawing = new ShapeDrawing { Width = boxW, Height = boxH, Outline = Rectangle(boxW, boxH) };
                    drawing.FilledDecorations.Add(Geometry.Polygon(((boxW / 2) - 12, boxH / 2), (boxW / 2, boxH / 2), (boxW / 2, (boxH / 2) - 12)));
                    return drawing;
                }

            case FlowchartNodeShape.Odd:
                {
                    var notch = boxH / 4;
                    var width = boxW + notch;
                    var x = width / 2;
                    var y = boxH / 2;
                    return new ShapeDrawing { Width = width, Height = boxH, Outline = Geometry.Polygon((-x, -y), (x, -y), (x, y), (-x, y), (-x + notch, 0)), LabelOffsetX = notch / 2 };
                }

            default:
                return new ShapeDrawing { Width = boxW, Height = boxH, Outline = Rectangle(boxW, boxH) };
        }
    }

    private const double Kappa = 0.5522847498307936;

    public static PathData Rectangle(double width, double height) => Geometry.Polygon((-width / 2, -height / 2), (width / 2, -height / 2), (width / 2, height / 2), (-width / 2, height / 2));

    private static PathData Line(double x1, double y1, double x2, double y2) => new PathBuilder().MoveTo(x1, y1).LineTo(x2, y2).Build();

    /// <summary>A rectangle whose bottom edge (and top edge) is a wave.</summary>
    private static PathData Wave(double offsetX, double offsetY, double width, double height, double amplitude, bool waveTop)
    {
        var x = width / 2;
        var top = (-height / 2) + (waveTop ? amplitude : 0);
        var bottom = (height / 2) - amplitude;
        var builder = new PathBuilder().MoveTo(offsetX - x, offsetY + top);
        if (waveTop)
        {
            builder.QuadraticTo(offsetX - (x / 2), offsetY + top - (2 * amplitude), offsetX, offsetY + top);
            builder.QuadraticTo(offsetX + (x / 2), offsetY + top + (2 * amplitude), offsetX + x, offsetY + top);
        }
        else
        {
            builder.LineTo(offsetX + x, offsetY + top);
        }

        builder.LineTo(offsetX + x, offsetY + bottom);
        builder.QuadraticTo(offsetX + (x / 2), offsetY + bottom + (2 * amplitude), offsetX, offsetY + bottom);
        builder.QuadraticTo(offsetX - (x / 2), offsetY + bottom - (2 * amplitude), offsetX - x, offsetY + bottom);
        return builder.Close().Build();
    }

    private static PathData Brace(double x, double height, bool left)
    {
        var d = left ? 1 : -1;
        var y = height / 2;
        return new PathBuilder()
            .MoveTo(x + (6 * d), -y)
            .QuadraticTo(x, -y, x, -y + 6)
            .LineTo(x, -6)
            .QuadraticTo(x, 0, x - (6 * d), 0)
            .QuadraticTo(x, 0, x, 6)
            .LineTo(x, y - 6)
            .QuadraticTo(x, y, x + (6 * d), y)
            .Build();
    }

    public static PathData Translate(PathData path, double dx, double dy)
    {
        var builder = new PathBuilder();
        foreach (var command in path.Commands)
        {
            switch (command.Kind)
            {
                case PathCommandKind.MoveTo:
                    builder.MoveTo(command.X + dx, command.Y + dy);
                    break;
                case PathCommandKind.LineTo:
                    builder.LineTo(command.X + dx, command.Y + dy);
                    break;
                case PathCommandKind.CubicTo:
                    builder.CubicTo(command.X1 + dx, command.Y1 + dy, command.X2 + dx, command.Y2 + dy, command.X + dx, command.Y + dy);
                    break;
                case PathCommandKind.QuadraticTo:
                    builder.QuadraticTo(command.X1 + dx, command.Y1 + dy, command.X + dx, command.Y + dy);
                    break;
                case PathCommandKind.ArcTo:
                    builder.ArcTo(command.X1, command.Y1, command.X2, command.LargeArc, command.Sweep, command.X + dx, command.Y + dy);
                    break;
                case PathCommandKind.Close:
                    builder.Close();
                    break;
            }
        }

        return builder.Build();
    }

    public static PathData Combine(PathData first, PathData second) => Concat(first, second);

    private static PathData Concat(PathData first, PathData second)
    {
        var builder = new PathBuilder();
        foreach (var path in new[] { first, second })
        {
            foreach (var command in path.Commands)
            {
                switch (command.Kind)
                {
                    case PathCommandKind.MoveTo:
                        builder.MoveTo(command.X, command.Y);
                        break;
                    case PathCommandKind.LineTo:
                        builder.LineTo(command.X, command.Y);
                        break;
                    case PathCommandKind.CubicTo:
                        builder.CubicTo(command.X1, command.Y1, command.X2, command.Y2, command.X, command.Y);
                        break;
                    case PathCommandKind.QuadraticTo:
                        builder.QuadraticTo(command.X1, command.Y1, command.X, command.Y);
                        break;
                    case PathCommandKind.ArcTo:
                        builder.ArcTo(command.X1, command.Y1, command.X2, command.LargeArc, command.Sweep, command.X, command.Y);
                        break;
                    case PathCommandKind.Close:
                        builder.Close();
                        break;
                }
            }
        }

        return builder.Build();
    }
}
