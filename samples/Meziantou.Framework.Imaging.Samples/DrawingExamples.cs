using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Samples;

/// <summary>Examples of the documentation site (docs/Imaging/07-Drawing), run by DocumentationExampleTests.</summary>
internal static class DrawingExamples
{
    /// <summary>The shapes a canvas fills and strokes directly, without a path.</summary>
    #region Imaging_DrawBasicShapes
    public static void DrawBasicShapes(string outputPath)
    {
        using var image = new Image<Rgba32>(300, 140, Colors.White);
        var pen = new Pen(Colors.DimGray, 3);
        PointD[] triangle = [new(210, 15), new(235, 60), new(185, 60)];
        PointD[] outlinedTriangle = [new(210, 80), new(235, 125), new(185, 125)];

        image.Draw(canvas =>
        {
            // Filled shapes
            canvas.FillRectangle(Colors.SteelBlue, new RectangleD(10, 15, 40, 45));
            canvas.FillRoundedRectangle(Colors.SeaGreen, new RectangleD(70, 15, 40, 45), 10);
            canvas.FillEllipse(Colors.Goldenrod, new PointD(150, 37.5), 20, 22.5);
            canvas.FillPolygon(Colors.Crimson, triangle);
            canvas.FillPie(Colors.MediumPurple, new PointD(270, 37.5), 22, 30, 300); // from 30 degrees, clockwise

            // The same shapes, stroked: the pen is centered on their outline
            canvas.StrokeRectangle(pen, new RectangleD(10, 80, 40, 45));
            canvas.StrokeRoundedRectangle(pen, new RectangleD(70, 80, 40, 45), 10);
            canvas.StrokeEllipse(pen, new PointD(150, 102.5), 20, 22.5);
            canvas.StrokePolygon(pen, outlinedTriangle);
            canvas.StrokeArc(pen, new PointD(270, 102.5), 22, 30, 300); // the arc alone, without the sides of the slice
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>A path built segment by segment, with a hole.</summary>
    #region Imaging_DrawPathBuilder
    public static void DrawPin(string outputPath)
    {
        // The outline of a map pin: two curves from its tip, and the top half of a circle between them
        var pin = new VectorPathBuilder()
            .MoveTo(60, 110)
            .QuadraticBezierTo(30, 70, 30, 45)
            .ArcTo(30, 30, 0, largeArc: false, clockwise: true, 90, 45)
            .QuadraticBezierTo(90, 70, 60, 110)
            .Close()
            .AddCircle(new PointD(60, 45), 12) // a second contour, inside the first one
            .Build();

        using var image = new Image<Rgba32>(120, 120, Colors.White);
        image.Draw(canvas =>
        {
            // With the even-odd rule, the circle is inside two contours: it is a hole
            canvas.Fill(Colors.Crimson, pin, FillRule.EvenOdd);
            canvas.Stroke(new Pen(Colors.DarkRed, 2) { LineJoin = LineJoin.Round }, pin);
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>The two fill rules on a contour that crosses itself.</summary>
    #region Imaging_DrawFillRules
    public static void DrawFillRules(string outputPath)
    {
        // A star drawn as one contour that crosses itself: each point is two tips further than the previous one
        var center = new PointD(55, 60);
        var points = new PointD[5];
        for (var i = 0; i < points.Length; i++)
        {
            points[i] = AffineTransform.CreateRotation(i * 144, center).Transform(new PointD(center.X, center.Y - 48));
        }

        var star = new VectorPathBuilder().AddPolygon(points).Build();
        var outline = new Pen(Colors.DarkSlateBlue, 2) { LineJoin = LineJoin.Round };

        using var image = new Image<Rgba32>(220, 110, Colors.White);
        image.Draw(canvas =>
        {
            // Non-zero (the default): everything the contour winds around, the pentagon in the middle included
            canvas.Fill(Colors.Orange, star);
            canvas.Stroke(outline, star);

            // Even-odd: the pentagon is inside the contour twice, so it is not filled
            var right = star.Transform(AffineTransform.CreateTranslation(110, 0));
            canvas.Fill(Colors.Orange, right, FillRule.EvenOdd);
            canvas.Stroke(outline, right);
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>The four ways of combining two paths.</summary>
    #region Imaging_CombinePaths
    public static void DrawCombinedPaths(string outputPath)
    {
        var first = new VectorPathBuilder().AddCircle(new PointD(27, 45), 22).Build();
        var second = new VectorPathBuilder().AddCircle(new PointD(47, 45), 22).Build();
        PathCombineMode[] modes = [PathCombineMode.Union, PathCombineMode.Intersect, PathCombineMode.Exclude, PathCombineMode.Xor];
        var guide = new Pen(Colors.Gray, 1) { DashPattern = [3, 3] };

        using var image = new Image<Rgba32>(300, 90, Colors.White);
        image.Draw(canvas =>
        {
            for (var i = 0; i < modes.Length; i++)
            {
                // The result is a path: it is filled, stroked or combined again like any other one
                var cell = AffineTransform.CreateTranslation(i * 75, 0);
                canvas.Fill(Colors.SteelBlue, first.Combine(second, modes[i]).Transform(cell));
                canvas.Stroke(guide, first.Transform(cell));
                canvas.Stroke(guide, second.Transform(cell));
            }
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>The kinds of gradients.</summary>
    #region Imaging_DrawGradients
    public static void DrawGradients(string outputPath)
    {
        // Gradients are positioned in the coordinates of the canvas, not of the shape they fill
        var linear = new LinearGradientBrush(new PointD(6, 0), new PointD(70, 0), [new GradientStop(0, Colors.Crimson), new GradientStop(1, Colors.Gold)]);

        // A light on a sphere: the gradient spreads from a focal point above and left of the center
        var radial = new RadialGradientBrush(new PointD(112, 40), 32, [new GradientStop(0, Colors.White), new GradientStop(1, Colors.MidnightBlue)])
        {
            FocalPoint = new PointD(102, 30),
        };

        // A color wheel: the colors go around the center, clockwise from its right
        var sweep = new SweepGradientBrush(new PointD(188, 40), [
            new GradientStop(0, Colors.Red),
            new GradientStop(1 / 3.0, Colors.Lime),
            new GradientStop(2 / 3.0, Colors.Blue),
            new GradientStop(1, Colors.Red)]);

        // A color for each corner of a triangle, mixed across it
        PointD[] triangle = [new(262, 8), new(294, 72), new(230, 72)];
        var mesh = new PathGradientBrush(triangle, [Colors.Red, Colors.Lime, Colors.Blue], Colors.White);

        using var image = new Image<Rgba32>(300, 80, Colors.White);
        image.Draw(canvas =>
        {
            canvas.FillRectangle(linear, new RectangleD(6, 8, 64, 64));
            canvas.FillCircle(radial, new PointD(112, 40), 32);
            canvas.FillCircle(sweep, new PointD(188, 40), 32);
            canvas.FillPolygon(mesh, triangle);
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>The same gradient mixed in each color space.</summary>
    #region Imaging_GradientColorSpaces
    public static void DrawGradientColorSpaces(string outputPath)
    {
        GradientColorSpace[] spaces = [GradientColorSpace.Encoded, GradientColorSpace.LinearSrgb, GradientColorSpace.Oklab, GradientColorSpace.Oklch];

        using var image = new Image<Rgba32>(256, 100, Colors.White);
        image.Draw(canvas =>
        {
            for (var i = 0; i < spaces.Length; i++)
            {
                // From blue to yellow, one row per color space, from top to bottom
                var brush = new LinearGradientBrush(new PointD(8, 0), new PointD(248, 0), [new GradientStop(0, Colors.Blue), new GradientStop(1, Colors.Yellow)])
                {
                    ColorSpace = spaces[i],
                };
                canvas.FillRectangle(brush, new RectangleD(8, 4 + (i * 24), 240, 20));
            }
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>The joins and the caps of a pen.</summary>
    #region Imaging_DrawJoinsAndCaps
    public static void DrawJoinsAndCaps(string outputPath)
    {
        LineJoin[] joins = [LineJoin.Miter, LineJoin.Round, LineJoin.Bevel];
        LineCap[] caps = [LineCap.Butt, LineCap.Round, LineCap.Square];

        // A thin line on the path shows where the wide stroke is centered
        var path = new Pen(Colors.White, 1);

        using var image = new Image<Rgba32>(300, 120, Colors.White);
        image.Draw(canvas =>
        {
            for (var i = 0; i < 3; i++)
            {
                var x = 30 + (i * 95);

                // The corner of the path is a join
                PointD[] corner = [new(x, 55), new(x + 30, 15), new(x + 60, 55)];
                canvas.StrokePolyline(new Pen(Colors.SteelBlue, 12) { LineJoin = joins[i] }, corner);
                canvas.StrokePolyline(path, corner);

                // The ends of an open path are caps
                var start = new PointD(x + 5, 95);
                var end = new PointD(x + 55, 95);
                canvas.StrokeLine(new Pen(Colors.Coral, 12) { LineCap = caps[i] }, start, end);
                canvas.StrokeLine(path, start, end);
            }
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>Dashes, dots and markers.</summary>
    #region Imaging_DrawDashes
    public static void DrawDashes(string outputPath)
    {
        // Dashes of 10 pixels and gaps of 5
        var dashed = new Pen(Colors.SteelBlue, 3) { DashPattern = [10, 5] };

        // Dashes of length 0 are drawn by their caps alone: round caps make dots
        var dotted = new Pen(Colors.SteelBlue, 5) { DashPattern = [0, 10], LineCap = LineCap.Round };

        // A long dash, a gap, a short dash, a gap, repeated
        var dashDot = new Pen(Colors.SteelBlue, 3) { DashPattern = [14, 4, 3, 4] };

        // A disk at the start and an arrow head at the end, scaled by the width of the pen
        var arrow = new Pen(Colors.Crimson, 3) { StartMarker = LineMarker.Circle, EndMarker = LineMarker.Arrow };

        using var image = new Image<Rgba32>(260, 130, Colors.White);
        image.Draw(canvas =>
        {
            canvas.StrokeLine(dashed, new PointD(20, 20), new PointD(240, 20));
            canvas.StrokeLine(dotted, new PointD(20, 45), new PointD(240, 45));
            canvas.StrokeLine(dashDot, new PointD(20, 70), new PointD(240, 70));
            canvas.StrokeBezier(arrow, new PointD(20, 110), new PointD(100, 80), new PointD(160, 125), new PointD(235, 95));
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>Hatches and a pattern drawn by a callback.</summary>
    #region Imaging_DrawPatterns
    public static void DrawPatterns(string outputPath)
    {
        // Ready-made tiles of 8 by 8 pixels: lines, or a fraction of the pixels
        var crossHatch = new PatternBrush(HatchStyle.DiagonalCross, Colors.SteelBlue, Colors.White);
        var halftone = new PatternBrush(HatchStyle.Percent50, Colors.DarkSlateGray, Colors.LightGray);

        // A tile of 16 by 16 drawn once by a callback, then repeated: a dot, the tiles turned by 45 degrees
        var dots = new PatternBrush(new RectangleD(0, 0, 16, 16), tile =>
        {
            tile.Fill(Colors.MistyRose);
            tile.FillCircle(Colors.Crimson, new PointD(8, 8), 4);
        })
        {
            Transform = AffineTransform.CreateRotation(45),
        };

        using var image = new Image<Rgba32>(300, 100, Colors.White);
        image.Draw(canvas =>
        {
            canvas.FillRoundedRectangle(crossHatch, new RectangleD(10, 10, 80, 80), 12);
            canvas.FillRoundedRectangle(halftone, new RectangleD(110, 10, 80, 80), 12);
            canvas.FillRoundedRectangle(dots, new RectangleD(210, 10, 80, 80), 12);
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>The same drawing repeated, turned around a point by a transformation of the canvas.</summary>
    #region Imaging_DrawRotated
    public static void DrawClock(string outputPath)
    {
        var center = new PointD(80, 80);
        var tick = new Pen(Colors.DimGray, 3) { LineCap = LineCap.Round };
        var hand = new Pen(Colors.Crimson, 4) { LineCap = LineCap.Round };

        using var image = new Image<Rgba32>(160, 160, Colors.WhiteSmoke);
        image.Draw(canvas =>
        {
            canvas.FillCircle(Colors.White, center, 70);
            canvas.StrokeCircle(new Pen(Colors.DimGray, 4), center, 70);

            for (var hour = 0; hour < 12; hour++)
            {
                // The same tick at the top of the dial, turned by a twelfth of a turn around the center each time
                var length = hour % 3 == 0 ? 16 : 8;
                canvas.Transform(AffineTransform.CreateRotation(hour * 30, center), turned => turned.StrokeLine(tick, new PointD(80, 18), new PointD(80, 18 + length)));
            }

            // Ten past ten: the hands point up, then turn clockwise
            canvas.Transform(AffineTransform.CreateRotation(305, center), turned => turned.StrokeLine(hand, center, new PointD(80, 45)));
            canvas.Transform(AffineTransform.CreateRotation(60, center), turned => turned.StrokeLine(hand, center, new PointD(80, 25)));
            canvas.FillCircle(Colors.Crimson, center, 5);
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>An icon drawn at several sizes, with a pen that scales with it and one that does not.</summary>
    #region Imaging_DrawScaled
    public static void DrawScaledIcons(string outputPath)
    {
        // A house, in the coordinates of an icon of 24 by 24 pixels
        var house = VectorPath.ParseSvgPathData("M3 11 L12 3 L21 11 V21 H3 Z");
        var scaled = new Pen(Colors.DarkSlateBlue, 1.5) { LineJoin = LineJoin.Round };
        var fixedWidth = new Pen(Colors.DarkSlateBlue, 1.5) { LineJoin = LineJoin.Round, ScalesWithTransform = false };
        double[] scales = [1.5, 2.5, 3.5];

        using var image = new Image<Rgba32>(200, 184, Colors.White);
        image.Draw(canvas =>
        {
            var x = 8.0;
            foreach (var scale in scales)
            {
                // Scaled, then moved: the transformation passed to Then applies after the first one
                var top = AffineTransform.CreateScale(scale, scale).Then(AffineTransform.CreateTranslation(x, 8));
                var bottom = AffineTransform.CreateScale(scale, scale).Then(AffineTransform.CreateTranslation(x, 100));

                // First row: the pen is scaled with the drawing; second row: it stays 1.5 pixels wide
                canvas.Transform(top, icon =>
                {
                    icon.Fill(Colors.LightSteelBlue, house);
                    icon.Stroke(scaled, house);
                });
                canvas.Transform(bottom, icon =>
                {
                    icon.Fill(Colors.LightSteelBlue, house);
                    icon.Stroke(fixedWidth, house);
                });

                x += (21 * scale) + 6;
            }
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>The same paint mixed with what is under it by several blend modes.</summary>
    #region Imaging_DrawBlendModes
    public static void DrawBlendModes(string outputPath)
    {
        BlendMode[] modes = [BlendMode.Normal, BlendMode.Multiply, BlendMode.Screen, BlendMode.Overlay, BlendMode.Difference, BlendMode.Color];
        var background = new LinearGradientBrush(new PointD(0, 0), new PointD(0, 70), [new GradientStop(0, Colors.Gold), new GradientStop(1, Colors.Teal)]);

        using var image = new Image<Rgba32>(300, 70);
        image.Draw(canvas =>
        {
            canvas.Fill(background);
            for (var i = 0; i < modes.Length; i++)
            {
                // The same color each time: only the blend mode of the brush changes
                var paint = new SolidBrush(Colors.MediumVioletRed) { BlendMode = modes[i] };
                canvas.FillCircle(paint, new PointD(25 + (i * 50), 35), 20);
            }
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>An eraser: shapes that remove the pixels they cover.</summary>
    #region Imaging_DrawEraser
    public static void DrawTicket(string outputPath)
    {
        // A paint that removes the pixels where it is opaque, whatever its color
        var eraser = new SolidBrush(Colors.Black) { CompositeMode = CompositeMode.DestinationOut };
        var perforation = new Pen(eraser, 4) { DashPattern = [0, 8], LineCap = LineCap.Round };

        // The image starts transparent
        using var image = new Image<Rgba32>(240, 110);
        image.Draw(canvas =>
        {
            canvas.FillRoundedRectangle(Colors.Goldenrod, new RectangleD(10, 10, 220, 90), 8);

            // Two notches and a line of holes: the pixels become transparent again
            canvas.FillCircle(eraser, new PointD(170, 10), 10);
            canvas.FillCircle(eraser, new PointD(170, 100), 10);
            canvas.StrokeLine(perforation, new PointD(170, 26), new PointD(170, 86));
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>A color filter applied to what is already drawn, outside a circle.</summary>
    #region Imaging_DrawSpotlight
    public static void DrawSpotlight(string inputPath, string outputPath)
    {
        using var image = Image.Load(inputPath);
        var spot = new PointD(image.Width * 0.7, image.Height / 2.0);

        // The image and a circle: with the even-odd rule, the circle is a hole, so the clip is everything around it
        var around = new VectorPathBuilder()
            .AddRectangle(new RectangleD(0, 0, image.Width, image.Height))
            .AddCircle(spot, 48)
            .Build();

        image.Draw(canvas =>
        {
            canvas.Clip(around, outside => outside.FilterBackdrop(ColorMatrix.CreateGrayscale()), FillRule.EvenOdd);
            canvas.StrokeCircle(new Pen(Colors.White, 2), spot, 48);
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>Antialiased edges, hard edges, and edges mixed in linear light.</summary>
    #region Imaging_DrawEdges
    public static void DrawEdges(string outputPath)
    {
        using var image = new Image<Rgba32>(72, 24, Colors.Black);
        var thin = new Pen(Colors.White, 1);

        image.Draw(canvas =>
        {
            // Left: the default, each pixel on an edge is painted in proportion to the part of it the shape covers
            DrawShapes(canvas, 0);

            // Middle: hard edges, each pixel is painted entirely or not at all
            canvas.Antialiasing(enabled: false, hard => DrawShapes(hard, 24));

            // Right: the edges are mixed in linear light, so they are not darker than they should be
            canvas.Compositing(CompositingSpace.LinearSrgb, light => DrawShapes(light, 48));
        });

        // Enlarged 4 times, each pixel as a square, to show the edges
        image.Resize(new ResizeOptions(288, 96) { Filter = ResamplingFilter.NearestNeighbor });
        image.Save(outputPath);

        void DrawShapes(DrawingCanvas canvas, double x)
        {
            canvas.FillCircle(Colors.Gold, new PointD(x + 12, 9), 6);
            canvas.StrokeLine(thin, new PointD(x + 2, 21), new PointD(x + 22, 16.5));
        }
    }
    #endregion

    /// <summary>A different drawing on each frame of an animation.</summary>
    #region Imaging_DrawOnEachFrame
    public static void DrawProgressBar(string inputPath, string outputPath)
    {
        using var image = Image.Load(inputPath);
        var count = image.Frames.Count;
        for (var i = 0; i < count; i++)
        {
            // A bar that grows with the position of the frame in the animation
            var progress = (i + 1.0) / count;
            image.Frames[i].Draw(canvas =>
            {
                var track = new RectangleD(4, canvas.Height - 10, canvas.Width - 8, 6);
                canvas.FillRectangle(new Rgba32(0, 0, 0, 120), track);
                canvas.FillRectangle(Colors.White, new RectangleD(track.X, track.Y, track.Width * progress, track.Height));
            });
        }

        // The durations and the loop count of the animation are kept
        image.Save(outputPath);
    }
    #endregion
}
