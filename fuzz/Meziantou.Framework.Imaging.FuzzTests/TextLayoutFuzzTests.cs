using System.Diagnostics;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>
/// Deterministic, bounded fuzzing of text layout: random texts (letters of several scripts and directions, marks,
/// spaces, line breaks, soft hyphens, joiners, directional controls, lone surrogates) with random styles and options,
/// laid out with the fonts of the corpus and drawn. The fonts are valid: nothing may throw. The lines of a layout follow
/// each other and cover the text, or the layout says it is truncated; a wrapped line fits its width unless nothing can
/// be broken; every measure is a finite number; the path of a layout has the bounds of what it measures. The budget is
/// set by <see cref="FuzzSettings"/>.
/// </summary>
public sealed class TextLayoutFuzzTests
{
    // The characters a text is made of, by weight: what each one exercises is in the summary of the class.
    private static readonly string[] Alphabet =
    [
        "A", "V", "o", "f", "i", "T", ".", ",", "e", "a", "A", "V", "o", " ", " ", " ", " ", "\t", "\n", "\r\n", "\u2028",
        "\u00E9", "e\u0301", "\u0301", "\u0308\u0304", "\u00AD", "\u200B", "\u200C", "\u200D", "\uFE0F", "\u00A0", "\u2003", "-", "\u2010", "1\u20442",
        "\u05D0", "\u05D1", "\u05D2", "\u0627", "\u0628", "\u0644", "\u0661", "(", ")", "[", "\u202E", "\u202C", "\u2067", "\u2069", "\u200F",
        "\u65E5", "\u672C", "\u306E", "\u30C6", "\u3002", "\u300C", "\uFF21", "\U0001F600", "\uD83D", "\uDE00", "\u0E01", "\u0915\u094D\u0937",
    ];

    /// <summary>Creates a path for a text to follow.</summary>
    private static VectorPath CreateGuide(FuzzRandom random)
    {
        var builder = new VectorPathBuilder();
        var contours = random.Chance(80) ? 1 : random.Next(0, 4);
        for (var contour = 0; contour < contours; contour++)
        {
            var current = CreatePoint(random);
            builder.MoveTo(current.X, current.Y);
            var segments = random.Next(0, 6);
            for (var segment = 0; segment < segments; segment++)
            {
                // A segment without length, now and then: it has no direction.
                var next = random.Chance(10) ? current : CreatePoint(random);
                switch (random.Next(4))
                {
                    case 0:
                        var control = CreatePoint(random);
                        builder.QuadraticBezierTo(control.X, control.Y, next.X, next.Y);
                        break;

                    case 1:
                        var first = CreatePoint(random);
                        var second = CreatePoint(random);
                        builder.CubicBezierTo(first.X, first.Y, second.X, second.Y, next.X, next.Y);
                        break;

                    default:
                        builder.LineTo(next.X, next.Y);
                        break;
                }

                current = next;
            }

            if (random.Chance(30))
            {
                builder.Close();
            }
        }

        if (random.Chance(10))
        {
            builder.AddCircle(CreatePoint(random), random.Next(0, 200) / 4.0);
        }

        return builder.Build();

        static PointD CreatePoint(FuzzRandom random) => random.Next(40) switch
        {
            0 => new PointD(random.Chance(50) ? 16777216 : -16777216, random.Chance(50) ? 16777216 : -16777216),
            1 => new PointD(random.Next(-100000, 100000), random.Next(-100000, 100000)),
            2 => new PointD(random.Next(0, 96) + (1 / 1048576.0), random.Next(0, 64)),
            _ => new PointD(random.Next(-40, 540) / 4.0, random.Next(-40, 400) / 4.0),
        };
    }

    [Fact]
    public void RandomTextsAreLaidOutAndDrawnWithoutError()
    {
        var root = FixtureRoot.GetDirectory();
        Font[] fonts =
        [
            Font.Load(root / "fonts" / "noto-sans" / "NotoSans-Regular.subset.ttf"),
            Font.Load(root / "fonts" / "noto-sans-cff" / "NotoSans-Regular.subset.otf"),
            Font.Load(root / "fonts" / "noto-sans-cjk" / "NotoSansCJKjp-Regular.subset.otf"),
            Font.Load(root / "fonts" / "synthetic" / "boxes.ttf"),
        ];
        var hyphenator = Hyphenator.Parse("a1 e1 o1 1b 1f 1t", options: new HyphenatorOptions { LeftHyphenMin = 1, RightHyphenMin = 1 });
        var random = new FuzzRandom(FuzzSettings.Seed ^ FuzzSeeds.GetGroupSalt("text-layout"));
        var stopwatch = Stopwatch.StartNew();
        var failures = new List<string>();
        var executed = 0;
        var wrapped = 0;
        var truncated = 0;
        using var image = new Image<Rgba32>(96, 64);
        var pen = new Pen(new SolidBrush(new Rgba32(0, 0, 0, 120)), 0.75);
        for (var iteration = 0; iteration < FuzzSettings.Iterations && failures.Count < 4; iteration++)
        {
            if (FuzzSettings.Duration is { } budget && stopwatch.Elapsed > budget)
                break;

            // A text of spans with their own style.
            var builder = new RichTextBuilder();
            var spanCount = random.Next(1, 4);
            for (var span = 0; span < spanCount; span++)
            {
                var text = new StringBuilder();
                // Now and then a long span: of the text after a line, only the start is shaped again for the next one.
                var length = random.Chance(4) ? random.Next(150, 700) : random.Next(0, 24);
                for (var i = 0; i < length; i++)
                {
                    text.Append(random.Pick(Alphabet));
                }

                var stack = random.Chance(50) ? new FontStack(random.Pick(fonts)) : new FontStack(random.Pick(fonts), random.Pick(fonts), random.Pick(fonts));
                builder.Append(text.ToString(), new TextStyle(stack, random.Pick((double[])[6.5, 9, 12, 13.75, 20, 31]))
                {
                    Brush = random.Chance(50) ? new SolidBrush(new Rgba32(random.NextByte(), random.NextByte(), random.NextByte(), random.NextByte())) : null,
                    Decorations = (TextDecorations)random.Next(8),
                    LetterSpacing = random.Chance(20) ? random.Next(-3, 6) : 0,
                    WordSpacing = random.Chance(20) ? random.Next(-3, 9) : 0,
                    Features = random.Chance(20) ? [new FontFeature("kern", random.Next(2)), new FontFeature("liga", random.Next(2))] : [],
                    Hyphenator = random.Chance(50) ? hyphenator : null,
                });
            }

            var rich = builder.Build();
            if (rich.Spans.Count == 0)
                continue;

            var options = new TextLayoutOptions
            {
                MaxWidth = random.Chance(75) ? random.Next(0, 120) : double.PositiveInfinity,
                MaxHeight = random.Chance(20) ? random.Next(0, 80) : double.PositiveInfinity,
                MaxLines = random.Chance(20) ? random.Next(1, 5) : int.MaxValue,
                Wrapping = random.Chance(85) ? TextWrapping.Wrap : TextWrapping.NoWrap,
                WordBreak = (TextWordBreak)random.Next(3),
                LineBreak = (TextLineBreak)random.Next(3),
                OverflowWrap = (TextOverflowWrap)random.Next(2),
                Hyphenation = (TextHyphenation)random.Next(3),
                Alignment = (TextAlignment)random.Next(4),
                Direction = (TextDirection)random.Next(3),
                LineHeight = random.Chance(20) ? random.Next(1, 40) : null,
                TabSize = random.Next(1, 9),
                Trimming = (TextTrimming)random.Next(3),
                FirstLineIndent = random.Chance(20) ? random.Next(-10, 20) : 0,
            };

            executed++;
            try
            {
                var layout = TextLayout.Create(rich, options);
                wrapped += layout.LineCount > 1 ? 1 : 0;
                truncated += layout.IsTruncated ? 1 : 0;
                if (Check(rich, options, layout) is { } defect)
                {
                    failures.Add($"iteration {iteration}: {defect}\n    text: {Escape(rich.Text)}\n    options: MaxWidth={options.MaxWidth} MaxHeight={options.MaxHeight} MaxLines={options.MaxLines} {options.Wrapping} {options.WordBreak} {options.LineBreak} {options.OverflowWrap} {options.Hyphenation} {options.Alignment} {options.Direction} {options.Trimming}");
                    continue;
                }

                var origin = new PointD(random.Next(-8, 8) + 0.5, random.Next(-8, 8) + 0.25);
                image.Draw(
                    (layout, pen, origin, withStroke: random.Chance(10)),
                    static (canvas, state) =>
                    {
                        canvas.FillText(new Rgba32(10, 20, 30), state.layout, state.origin);
                        if (state.withStroke)
                        {
                            canvas.StrokeText(state.pen, state.layout, state.origin);
                        }
                    },
                    TestContext.Current.CancellationToken);
                // Along a path: lines and curves, several contours, closed or not, points on each other and far away.
                var guide = CreateGuide(random);
                var pathOptions = new TextPathOptions
                {
                    StartOffset = random.Chance(50) ? random.Next(-300, 300) / 4.0 : 0,
                    Alignment = (TextPathAlignment)random.Next(3),
                    BaselineOffset = random.Chance(30) ? random.Next(-40, 40) / 4.0 : 0,
                    Side = (TextPathSide)random.Next(2),
                    Overflow = (TextPathOverflow)random.Next(2),
                };
                image.Draw(
                    (layout, pen, guide, pathOptions, withStroke: random.Chance(10)),
                    static (canvas, state) =>
                    {
                        canvas.FillText(new Rgba32(200, 20, 30, 128), state.layout, state.guide, state.pathOptions);
                        if (state.withStroke)
                        {
                            canvas.StrokeText(state.pen, state.layout, state.guide, state.pathOptions);
                        }
                    },
                    TestContext.Current.CancellationToken);

                var path = layout.ToPath(origin);
                if (path.IsEmpty != layout.InkBounds.IsEmpty)
                {
                    failures.Add($"iteration {iteration}: the path and the ink bounds disagree on whether the layout draws something\n    text: {Escape(rich.Text)}");
                }
            }
            catch (Exception exception)
            {
                failures.Add($"iteration {iteration}: {exception.GetType().Name}: {exception.Message}\n    text: {Escape(rich.Text)}\n    {exception.StackTrace?.Split('\n').FirstOrDefault(line => line.Contains("Meziantou.Framework.Imaging", StringComparison.Ordinal))?.Trim()}");
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine(FormattableString.Invariant($"text-layout: {executed} texts (base seed {FuzzSettings.Seed}) in {stopwatch.Elapsed.TotalSeconds:F1} s; {wrapped} of several lines, {truncated} truncated"));
        Assert.Empty(failures, $"{failures.Count} failure(s):\n  " + string.Join("\n  ", failures));
        if (FuzzSettings.Duration is null && FuzzSettings.Iterations >= FuzzSettings.DefaultIterations)
        {
            Assert.True(wrapped > executed / 4, "Too few layouts have several lines.");
            Assert.True(truncated > 0, "No layout is truncated.");
        }
    }

    /// <summary>Checks what a layout says about itself.</summary>
    private static string? Check(RichText text, TextLayoutOptions options, TextLayout layout)
    {
        if (layout.LineCount < 1 || layout.Lines.Count != layout.LineCount)
            return "the layout has no line";

        if (!double.IsFinite(layout.Width) || !double.IsFinite(layout.Height) || layout.Width < 0 || layout.Height < 0)
            return $"the layout is {layout.Width} by {layout.Height}";

        if (!layout.InkBounds.IsEmpty && (!double.IsFinite(layout.InkBounds.Left) || !double.IsFinite(layout.InkBounds.Right) || !double.IsFinite(layout.InkBounds.Top) || !double.IsFinite(layout.InkBounds.Bottom)))
            return $"the ink bounds are {layout.InkBounds}";

        // The lines follow each other from the start of the text, and reach its end unless the layout is truncated.
        var position = 0;
        var y = 0.0;
        for (var i = 0; i < layout.LineCount; i++)
        {
            var line = layout.Lines[i];
            if (line.Start != position || line.Length < 0)
                return $"the line {i} starts at {line.Start} and has {line.Length} characters; the line before ends at {position}";

            if (!double.IsFinite(line.X) || !double.IsFinite(line.Width) || !(line.Height > 0) || !double.IsFinite(line.Baseline) || line.Y != y)
                return $"the line {i} is at {line.X},{line.Y} ({y} expected), {line.Width} by {line.Height}, baseline {line.Baseline}";

            position += line.Length;
            y += line.Height;
        }

        if (position > text.Text.Length || (position != text.Text.Length && !layout.IsTruncated))
            return $"the lines end at {position} of {text.Text.Length} characters and the layout is {(layout.IsTruncated ? "" : "not ")}truncated";

        if (Math.Abs(y - layout.Height) > 1e-9)
            return $"the lines are {y} tall and the layout {layout.Height}";

        if (options.MaxLines < layout.LineCount)
            return $"the layout has {layout.LineCount} lines for a maximum of {options.MaxLines}";

        return null;
    }

    private static string Escape(string text) => string.Concat(text.Select(character => character is >= ' ' and <= '~' ? character.ToString() : FormattableString.Invariant($"\\u{(int)character:X4}")));
}
