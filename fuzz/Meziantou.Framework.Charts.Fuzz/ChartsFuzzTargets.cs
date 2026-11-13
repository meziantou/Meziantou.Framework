using System.Globalization;
using System.Text;
using System.Xml;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Geo;

namespace Meziantou.Framework.Charts.Fuzz;

/// <summary>
/// Drawing a chart never throws for the content of its data, apart from the documented cases (a cycle in the links of a
/// Sankey diagram), and gives a well-formed SVG document whose root is <c>svg</c>. The output is deterministic, and the
/// scene it comes from only holds finite coordinates: the SVG writer replaces a non-finite number with 0, which would
/// hide a shape drawn at the wrong place. The GeoJSON reader only throws a <see cref="FormatException"/>.
/// </summary>
public static class ChartsFuzzTargets
{
    private const int MaxRows = 200;

    private static readonly DateTime TimeOrigin = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Tiny sizes leave no room for the plot area once the margins are taken; huge ones multiply the coordinates
    private static readonly double[] Sizes = [640, 400, 1, 7, 50, 120, 300, 1000, 0.25, 4096, 33.3, 1e6, 200, 500, 80, 2];

    private static readonly string[] Tokens =
    [
        ",", "\n", "@", "-", "+", ".", "e", "E", "0", "1", "-1", "0.5", "1e308", "-1e308", "1e-308", "5e-324", "-0", "NaN", "Infinity", "-Infinity",
        "@0", "@-730000", "@2900000", "@36524", "<", ">", "&", "\"", "'", "\t", "\r", "\u00E9", "\U0001F600", "\uD800", "\uFFFE", "\0",
        "a", "b", "c", "/", "a/b", "a/b/c", "//", "$", "^", "$1.50", "^0.5", "$NaN", "^-Infinity", "$1E+300",
    ];

    private static readonly string[] GeoJsonTokens =
    [
        "{", "}", "[", "]", ",", ":", "\"", "null", "true", "false", "0", "-1", "1.5", "1e400", "-180", "180", "90", "-90", "360",
        "\"type\"", "\"coordinates\"", "\"features\"", "\"geometry\"", "\"geometries\"", "\"properties\"", "\"id\"",
        "\"FeatureCollection\"", "\"Feature\"", "\"Point\"", "\"MultiPoint\"", "\"LineString\"", "\"MultiLineString\"", "\"Polygon\"", "\"MultiPolygon\"", "\"GeometryCollection\"",
    ];


    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("charts-plot", PlotTarget) { Seeds = GetPlotSeeds, Dictionary = Tokens },
        new("charts-hierarchy", HierarchyTarget) { Seeds = GetHierarchySeeds, Dictionary = Tokens },
        new("charts-geojson", GeoJsonTarget) { Seeds = GetGeoJsonSeeds, Dictionary = GeoJsonTokens },
    ];

    private enum PlotKind
    {
        Bar,
        Line,
        Area,
        Dot,
        Text,
        Tick,
        Rect,
        RuleX,
        RuleY,
        Box,
        Violin,
        Ridgeline,
        Contour,
        Hexbin,
        Candlestick,
        Waterfall,
        Funnel,
        Arc,
        Annotation,
        Sankey,
        Chord,
        Network,
        ParallelCoordinates,
        Layers,
    }

    private static readonly PlotKind[] PlotKinds = Enum.GetValues<PlotKind>();

    /// <summary>
    /// The input is a byte that selects the kind of mark, a byte of scales and coordinates, a byte of options, a byte
    /// that selects the width (low nibble) and the height (high nibble), then a text: its first line is the title, and
    /// each other line is a row of fields separated by commas. A field is missing when empty, a time when it is
    /// <c>@</c> followed by a number of days since 2000-01-01, a number when it parses as one (<c>NaN</c> and
    /// <c>Infinity</c> included), and a category otherwise.
    /// </summary>
    private static void PlotTarget(ReadOnlySpan<byte> data)
    {
        var kind = PlotKinds[FuzzInput.ReadByte(ref data) % PlotKinds.Length];
        var scales = FuzzInput.ReadByte(ref data);
        var options = FuzzInput.ReadByte(ref data);
        var size = FuzzInput.ReadByte(ref data);
        var (title, rows) = ReadRows(FuzzText.Decode(data));

        var plot = CreatePlot(title, size, options);
        plot.XScale = CreateScale(scales & 7);
        plot.YScale = CreateScale((scales >> 3) & 7);
        if ((scales & 0x40) != 0)
        {
            plot.Coordinates = new PolarCoordinates { InnerRadius = (scales & 0x80) != 0 ? 0.4 : 0, StartAngle = -90, EndAngle = (scales & 0x80) != 0 ? 90 : 360 };
        }

        foreach (var mark in CreateMarks(kind, rows, options))
        {
            plot.Add(mark);
        }

        try
        {
            CheckPlot(plot, kind.ToString());
        }
        catch (InvalidOperationException ex) when (kind is PlotKind.Sankey && ex.Message.Contains("cycle", StringComparison.Ordinal))
        {
        }
    }

    /// <summary>
    /// The input is a byte that selects the mark (pack, treemap, partition, tree) and the way the tree is built (paths or
    /// identifiers), a byte of options, a byte that selects the size, then rows as in <c>charts-plot</c>: the first field
    /// is the path of the node (or its identifier), the second its value, the third the identifier of its parent.
    /// <see cref="Hierarchy.Stratify"/> may only reject the rows with an <see cref="ArgumentException"/>.
    /// </summary>
    private static void HierarchyTarget(ReadOnlySpan<byte> data)
    {
        var selector = FuzzInput.ReadByte(ref data);
        var options = FuzzInput.ReadByte(ref data);
        var size = FuzzInput.ReadByte(ref data);
        var (title, rows) = ReadRows(FuzzText.Decode(data));

        HierarchyNode<Row?> root;
        if ((selector & 0x10) == 0)
        {
            root = Hierarchy.FromPaths(rows, row => row.Text(0) ?? "", row => row.Number(1), separator: (selector & 0x20) == 0 ? '/' : ' ');
        }
        else
        {
            try
            {
                root = Hierarchy.Stratify(rows, row => row.Text(0) ?? "", row => row.Text(2), row => row.Number(1))!;
            }
            catch (ArgumentException ex) when (ex.GetType() == typeof(ArgumentException))
            {
                return;
            }
        }

        Mark mark = (selector & 3) switch
        {
            0 => new PackMark<Row?>(root) { Padding = (options & 0x40) != 0 ? 0 : 3, Label = node => node.Name },
            1 => new TreemapMark<Row?>(root) { Tiling = (TreemapTiling)((selector >> 2) & 3), PaddingOuter = (options & 0x40) != 0 ? 2 : 0, PaddingTop = (options & 0x80) != 0 ? 12 : 0 },
            2 => new PartitionMark<Row?>(root) { ShowRoot = (options & 0x40) != 0 },
            _ => new TreeMark<Row?>(root) { Layout = (TreeLayoutKind)((selector >> 2) & 1) },
        };

        var plot = CreatePlot(title, size, options);
        if ((selector & 0x40) != 0)
        {
            plot.Coordinates = new PolarCoordinates();
        }

        plot.Add(mark);
        CheckPlot(plot, mark.GetType().Name);
    }

    /// <summary>
    /// The input is a byte that selects the projection and the options of the map, then a GeoJSON document. Reading it
    /// throws a <see cref="FormatException"/> or gives features, read the same from a string and from a stream, that a
    /// map draws.
    /// </summary>
    private static void GeoJsonTarget(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var json = FuzzText.Decode(data);

        IReadOnlyList<GeoFeature> features;
        try
        {
            features = GeoJson.Parse(json);
        }
        catch (FormatException)
        {
            return;
        }

        GeoProjection projection = (flags & 3) switch
        {
            0 => new EquirectangularProjection(),
            1 => new MercatorProjection(),
            2 => new EqualEarthProjection(),
            _ => new AlbersProjection(),
        };

        var plot = new Plot { Title = "Map", Coordinates = new GeoCoordinates { Projection = projection, Outline = (flags & 4) != 0, GraticuleStep = (flags & 8) != 0 ? 30 : null } };
        plot.Add(new GeoMark<GeoFeature>(features, feature => feature.Geometry)
        {
            Fill = (flags & 0x10) != 0 ? feature => feature.Properties.GetValueOrDefault("value") : null,
            Title = feature => feature.Id,
        });
        var svg = CheckPlot(plot, "GeoMark");

        // The same document read from UTF-8 bytes, when the text can be encoded (no unpaired surrogates)
        if ((flags & 0x20) != 0 && !HasUnpairedSurrogate(json))
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var streamFeatures = GeoJson.Read(stream);
            var streamPlot = new Plot { Title = "Map", Coordinates = plot.Coordinates };
            streamPlot.Add(new GeoMark<GeoFeature>(streamFeatures, feature => feature.Geometry)
            {
                Fill = (flags & 0x10) != 0 ? feature => feature.Properties.GetValueOrDefault("value") : null,
                Title = feature => feature.Id,
            });
            FuzzAssert.Equal(svg, streamPlot.ToSvg(), "The features read from a stream draw differently from the features read from a string");
        }
    }


    private static Plot CreatePlot(string title, byte size, byte options)
    {
        var plot = new Plot
        {
            Width = Sizes[size & 15],
            Height = Sizes[size >> 4],
            Title = title.Length > 0 ? title : null,
            Subtitle = (options & 0x20) != 0 ? title : null,
            Theme = (options & 8) != 0 ? ChartTheme.Dark : ChartTheme.Light,
            Clip = (options & 0x10) != 0,
        };
        plot.Legend.Position = (LegendPosition)((options >> 6) % 3);
        return plot;
    }

    private static Scale? CreateScale(int selector) => selector switch
    {
        1 => new LinearScale { Nice = false, Zero = true },
        2 => new LogScale(),
        3 => new SymlogScale(),
        4 => new SqrtScale(),
        5 => new PowScale { Exponent = -2 },
        6 => new BandScale(),
        7 => new TimeScale(),
        _ => null,
    };

    /// <summary>
    /// The options byte: bit 0 adds a series channel (field 2), bit 1 a fill channel (field 2), bit 2 facets (field 3),
    /// bits 6-7 a transform for the marks that take one.
    /// </summary>
    private static IEnumerable<Mark> CreateMarks(PlotKind kind, List<Row> rows, byte options)
    {
        Func<Row, object?>? series = (options & 1) != 0 ? row => row[2] : null;
        Func<Row, object?>? fill = (options & 2) != 0 ? row => row[2] : null;
        Transform? transform = (options >> 6) switch
        {
            1 => new BinTransform(),
            2 => new StackTransform { Offset = (StackOffset)(options & 3) },
            3 => new GroupTransform { Reducer = (GroupReducer)((options >> 2) % 6) },
            _ => null,
        };

        Mark<Row> mark = kind switch
        {
            PlotKind.Bar => new BarMark<Row>(rows, row => row[0], row => row[1]) { Series = series, CornerRadius = (options & 4) != 0 ? 3 : 0, Transform = transform },
            PlotKind.Line => new LineMark<Row>(rows, row => row[0], row => row[1]) { Series = series, Curve = (Curve)(options % 6), MarkerRadius = (options & 4) != 0 ? 2 : 0, Transform = transform },
            PlotKind.Area => new AreaMark<Row>(rows, row => row[0], row => row[1]) { Series = series, Curve = (Curve)(options % 6), Transform = transform },
            PlotKind.Dot => new DotMark<Row>(rows, row => row[0], row => row[1]) { Size = (options & 4) != 0 ? row => row[3] : null, Symbol = (DotSymbol)(options % 5), Transform = transform },
            PlotKind.Text => new TextMark<Row>(rows, row => row.Text(2), row => row[0], row => row[1]) { Rotation = (options & 4) != 0 ? 45 : 0 },
            PlotKind.Tick => new TickMark<Row>(rows, row => row[0], (options & 4) != 0 ? row => row[1] : null),
            PlotKind.Rect => new RectMark<Row>(rows) { X1 = row => row[0], X2 = row => row[1], Y1 = row => row[2], Y2 = row => row[3], Text = row => row.Text(4) },
            PlotKind.RuleX => new RuleXMark<Row>(rows, row => row[0]) { Y = (options & 4) != 0 ? row => row[1] : null },
            PlotKind.RuleY => new RuleYMark<Row>(rows, row => row[0]) { X = (options & 4) != 0 ? row => row[1] : null },
            PlotKind.Box => new BoxMark<Row>(rows, row => row[0], row => row[1]),
            PlotKind.Violin => new ViolinMark<Row>(rows, row => row[0], row => row[1]) { ShowBox = (options & 4) != 0 },
            PlotKind.Ridgeline => new RidgelineMark<Row>(rows, row => row[0], row => row[1]),
            PlotKind.Contour => new ContourMark<Row>(rows, row => row[0], row => row[1]) { Filled = (options & 4) != 0 },
            PlotKind.Hexbin => new HexbinMark<Row>(rows, row => row[0], row => row[1]),
            PlotKind.Candlestick => new CandlestickMark<Row>(rows, row => row[0], row => row[1], row => row[2], row => row[3], row => row[4]),
            PlotKind.Waterfall => new WaterfallMark<Row>(rows, row => row[0], row => row[1]) { IsTotal = row => row.Text(2) is "total" },
            PlotKind.Funnel => new FunnelMark<Row>(rows, row => row[0], row => row[1]) { Label = row => row.Text(2) },
            PlotKind.Arc => new ArcMark<Row>(rows, row => row[1]) { Label = row => row.Text(0), Radius = (options & 4) != 0 ? row => row[2] : null },
            PlotKind.Annotation => new AnnotationMark<Row>(rows, row => row[0], row => row[1], row => row.Text(2)),
            PlotKind.Sankey => new SankeyMark<Row>(rows, row => row[0], row => row[1], row => row[2]) { Alignment = (SankeyAlignment)(options & 3) },
            PlotKind.Chord => new ChordMark<Row>(rows, row => row[0], row => row[1], row => row[2]),
            PlotKind.Network => new NetworkMark<Row>(rows, row => row[0], row => row[1]),
            PlotKind.ParallelCoordinates => new ParallelCoordinatesMark<Row>(rows, [.. Enumerable.Range(0, 1 + (options & 3)).Select(i => new ParallelDimension<Row>("d" + i.ToString(CultureInfo.InvariantCulture), row => row[i]))]),
            _ => new DotMark<Row>(rows, row => row[0], row => row[1]),
        };

        mark.Fill ??= fill;
        mark.Title = row => row.Text(0);
        mark.DataAttributes = row => [new("value", row[1]), new("index", row.Index)];
        if ((options & 4) != 0)
        {
            mark.FacetX = row => row[3];
        }

        yield return mark;

        if (kind is PlotKind.Layers)
        {
            // Several marks that share the scales, one on the secondary y scale
            yield return new LineMark<Row>(rows, row => row[0], row => row[2]) { UseSecondaryYScale = true, Series = series };
            yield return new BarMark<Row>(rows, row => row[0], row => row[3]) { Opacity = 0.5 };
            yield return new RuleYMark(0);
            yield return new RuleXMark(1);
        }
    }

    private static string CheckPlot(Plot plot, string description)
    {
        var scene = plot.CreateScene();
        CheckScene(scene, description);

        var svg = plot.ToSvg();
        FuzzAssert.Equal(svg, plot.ToSvg(), $"Drawing the {description} chart twice gives different outputs");
        FuzzAssert.Equal(svg, SvgSceneRenderer.Render(scene, new SvgRenderOptions { IdPrefix = "chart" }), $"The SVG of the {description} chart differs from the SVG of its scene");

        try
        {
            using var reader = XmlReader.Create(new StringReader(svg), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            reader.MoveToContent();
            FuzzAssert.Equal("svg", reader.LocalName, $"The root element of the {description} chart is not svg");
            while (reader.Read())
            {
            }
        }
        catch (XmlException ex)
        {
            FuzzAssert.Fail($"The SVG of the {description} chart is not well-formed XML: {ex.Message}");
        }

        return svg;
    }

    private static void CheckScene(Scene scene, string description)
    {
        var stack = new Stack<(SceneElement Element, string Path)>();
        stack.Push((scene.Root, "root"));
        while (stack.TryPop(out var item))
        {
            var (element, path) = item;
            path += "/" + element.GetType().Name + (element.CssClass is null ? "" : "." + element.CssClass);
            CheckFinite(element.Opacity, path, "opacity", description);
            switch (element)
            {
                case SceneGroup group:
                    if (group.Transform is { } transform)
                    {
                        CheckFinite(transform.A, path, "transform", description);
                        CheckFinite(transform.B, path, "transform", description);
                        CheckFinite(transform.C, path, "transform", description);
                        CheckFinite(transform.D, path, "transform", description);
                        CheckFinite(transform.E, path, "transform", description);
                        CheckFinite(transform.F, path, "transform", description);
                    }

                    foreach (var child in group.Children)
                    {
                        stack.Push((child, path));
                    }

                    break;

                case SceneRectangle rectangle:
                    CheckFinite(rectangle.X, path, "x", description);
                    CheckFinite(rectangle.Y, path, "y", description);
                    CheckFinite(rectangle.Width, path, "width", description);
                    CheckFinite(rectangle.Height, path, "height", description);
                    break;

                case SceneCircle circle:
                    CheckFinite(circle.CenterX, path, "cx", description);
                    CheckFinite(circle.CenterY, path, "cy", description);
                    CheckFinite(circle.Radius, path, "r", description);
                    break;

                case SceneLine line:
                    CheckFinite(line.X1, path, "x1", description);
                    CheckFinite(line.Y1, path, "y1", description);
                    CheckFinite(line.X2, path, "x2", description);
                    CheckFinite(line.Y2, path, "y2", description);
                    break;

                case ScenePath scenePath:
                    foreach (var command in scenePath.Data.Commands)
                    {
                        CheckFinite(command.X, path, "path", description);
                        CheckFinite(command.Y, path, "path", description);
                        CheckFinite(command.X1, path, "path", description);
                        CheckFinite(command.Y1, path, "path", description);
                        CheckFinite(command.X2, path, "path", description);
                        CheckFinite(command.Y2, path, "path", description);
                    }

                    break;

                case SceneText text:
                    CheckFinite(text.X, path, "x", description);
                    CheckFinite(text.Y, path, "y", description);
                    CheckFinite(text.Rotation, path, "rotation", description);
                    break;
            }
        }
    }

    private static void CheckFinite(double value, string path, string name, string description)
    {
        if (!double.IsFinite(value))
            FuzzAssert.Fail($"The {name} of {path} in the {description} chart is {value.ToString(CultureInfo.InvariantCulture)}");
    }

    private static (string Title, List<Row> Rows) ReadRows(string text)
    {
        var lines = text.Split('\n');
        var rows = new List<Row>();
        for (var i = 1; i < lines.Length && rows.Count < MaxRows; i++)
        {
            rows.Add(new Row(rows.Count, [.. lines[i].Split(',').Select(ParseField)]));
        }

        return (lines[0], rows);
    }

    private static object? ParseField(string field)
    {
        if (field.Length == 0)
            return null;

        if (field[0] is '@' && long.TryParse(field.AsSpan(1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var days) && days is >= -730_000 and <= 2_900_000)
            return TimeOrigin.AddDays(days);

        // '$' and '^' select the other number types: a decimal floating-point (System.Decimal on .NET 10) and a 16-bit float
#if NET11_0_OR_GREATER
        if (field[0] is '$' && System.Numerics.Decimal64.TryParse(field.AsSpan(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var decimal64))
            return decimal64;

        if (field[0] is '^' && System.Numerics.BFloat16.TryParse(field.AsSpan(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var bfloat16))
            return bfloat16;
#else
        if (field[0] is '$' && decimal.TryParse(field.AsSpan(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue))
            return decimalValue;

        if (field[0] is '^' && Half.TryParse(field.AsSpan(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var half))
            return half;
#endif

        if (double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return number;

        return field;
    }

    private static bool HasUnpairedSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<FuzzSeed> GetPlotSeeds()
    {
        const string Categories = "Sales\na,10,x,1,lbl\nb,20,y,2,lbl\nc,-5,x,1,lbl\nd,15.5,y,2,total\n";
        const string Numbers = "Numbers\n1,2,3,4,5\n2,4,1,3,6\n3,1,2,5,4\n4,8,6,2,3\n5,3,9,7,2\n6,5,4,4,8\n";
        const string Times = "Times\n@0,1,a,1\n@31,3,b,2\n@59,2,a,1\n@90,5,b,2\n@365,4,a,1\n";
        const string Flows = "Flows\na,b,5\na,c,3\nb,d,2\nc,d,4\nd,e,6\n";
        const string Specials = "Specials <&\"'>\nNaN,1,a,1\nInfinity,-Infinity,b,2\n1e308,-1e308,c,3\n5e-324,-0,d,4\n,,,\n\u00E9,\U0001F600,\uD800,\n";
        string[] texts = [Categories, Numbers, Times, Flows, Specials];
        for (var kind = 0; kind < PlotKinds.Length; kind++)
        {
            var text = PlotKinds[kind] is PlotKind.Sankey or PlotKind.Chord or PlotKind.Network ? Flows : texts[kind % texts.Length];
            yield return FuzzSeed.FromText(PlotKinds[kind].ToString(), [(byte)kind, 0, 0, 0], text);
            yield return FuzzSeed.FromText(PlotKinds[kind] + "-numbers", [(byte)kind, 0, 1, 0x11], Numbers);
        }

        // Scales, polar coordinates, transforms, facets and options
        yield return FuzzSeed.FromText("bar-log", [(byte)PlotKind.Bar, 2 << 3, 0, 0], Categories);
        yield return FuzzSeed.FromText("bar-stacked", [(byte)PlotKind.Bar, 0, 0x81, 0], Categories);
        yield return FuzzSeed.FromText("bar-histogram", [(byte)PlotKind.Bar, 0, 0x40, 0], Numbers);
        yield return FuzzSeed.FromText("bar-polar", [(byte)PlotKind.Bar, 0x40, 0, 0], Categories);
        yield return FuzzSeed.FromText("line-symlog", [(byte)PlotKind.Line, 3 | (4 << 3), 4, 0], Numbers);
        yield return FuzzSeed.FromText("line-time", [(byte)PlotKind.Line, 7, 1, 0], Times);
        yield return FuzzSeed.FromText("dot-specials", [(byte)PlotKind.Dot, 1 | (5 << 3), 0x1E, 0x23], Specials);
        yield return FuzzSeed.FromText("area-faceted", [(byte)PlotKind.Area, 0, 0x85, 0x45], Numbers);
        yield return FuzzSeed.FromText("layers-dark", [(byte)PlotKind.Layers, 0, 0x29, 0], Numbers);
        yield return FuzzSeed.FromText("arc-gauge", [(byte)PlotKind.Arc, 0xC0, 0, 0], Categories);
        yield return FuzzSeed.FromText("dot-number-types", [(byte)PlotKind.Dot, 0, 0, 0], "Number types\n$1.50,^2,a,1\n$-3.25,^0.5,b,2\n$NaN,^NaN,a,1\n$1E+300,^-Infinity,b,2\n");
        yield return FuzzSeed.FromText("bar-number-types", [(byte)PlotKind.Bar, 0, 0, 0], "Number types\na,$10.5\nb,$20\nc,^-5\n");
        yield return FuzzSeed.FromText("tiny", [(byte)PlotKind.Bar, 6 | (1 << 3), 0x3F, 0x22], Categories);
        yield return FuzzSeed.FromText("empty", [(byte)PlotKind.Line, 0, 0, 0], "");
    }

    private static IEnumerable<FuzzSeed> GetHierarchySeeds()
    {
        const string Paths = "Files\nsrc/app/main.cs,120\nsrc/app/util.cs,40\nsrc/lib/a.cs,60\ndocs/readme.md,10\ndocs/,5\ntests/t.cs,0\n";
        const string Ids = "Org\nceo,10,\ncto,5,ceo\ncfo,4,ceo\ndev,3,cto\nops,2,cto\nacc,1,cfo\n";
        const string Specials = "Specials\na/b,NaN\na/c,Infinity\na/c,1e-300\nb,-1\nc/d/e/f/g,1e308\nc/d/e/f/h,1e308\n\u00E9/\uD800,1\n<&>/\",2\n";
        for (var selector = 0; selector < 4; selector++)
        {
            yield return FuzzSeed.FromText("paths-" + selector.ToString(CultureInfo.InvariantCulture), [(byte)selector, 0, 0], Paths);
            yield return FuzzSeed.FromText("ids-" + selector.ToString(CultureInfo.InvariantCulture), [(byte)(0x10 | selector), 0x40, 0x11], Ids);
            yield return FuzzSeed.FromText("specials-" + selector.ToString(CultureInfo.InvariantCulture), [(byte)(selector | 4), 0xC8, 0x23], Specials);
            yield return FuzzSeed.FromText("polar-" + selector.ToString(CultureInfo.InvariantCulture), [(byte)(0x40 | selector), 0, 0], Paths);
        }

        yield return FuzzSeed.FromText("ids-cycle", [0x11, 0, 0], "Cycle\na,1,b\nb,1,a\n");
        yield return FuzzSeed.FromText("empty", [1, 0, 0], "");
    }

    private static IEnumerable<FuzzSeed> GetGeoJsonSeeds()
    {
        string[] documents =
        [
            """{"type":"FeatureCollection","features":[{"type":"Feature","id":"a","properties":{"value":1},"geometry":{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]]]}}]}""",
            """{"type":"Feature","properties":{"value":"x"},"geometry":{"type":"MultiPolygon","coordinates":[[[[-180,-90],[180,-90],[180,90],[-180,90],[-180,-90]]],[[[1,1],[2,1],[2,2],[1,1]]]]}}""",
            """{"type":"LineString","coordinates":[[-170,10],[170,10],[0,89.9]]}""",
            """{"type":"MultiPoint","coordinates":[[0,0],[1e308,1],[-0.5,90]]}""",
            """{"type":"GeometryCollection","geometries":[{"type":"Point","coordinates":[2.35,48.85]},{"type":"MultiLineString","coordinates":[[[0,0],[1,1]],[[2,2],[3,3,4]]]}]}""",
            """{"type":"Polygon","coordinates":[[[0,0],[0,0],[0,0]]]}""",
            """{"type":"Polygon","coordinates":[]}""",
            """{"type":"Feature","geometry":null}""",
            """{"type":"FeatureCollection","features":[]}""",
            """{"type":"Unknown"}""",
            """[1,2""",
        ];
        for (var i = 0; i < documents.Length; i++)
        {
            yield return FuzzSeed.FromText("geojson-" + i.ToString(CultureInfo.InvariantCulture), [(byte)(i | 0x20 | ((i & 1) << 4))], documents[i]);
        }
    }


    /// <summary>A row of the data: its fields, parsed as numbers, times or categories.</summary>
    private sealed class Row(int index, object?[] fields)
    {
        public int Index { get; } = index;

        public object? this[int field] => field < fields.Length ? fields[field] : null;

        public string? Text(int field) => this[field] switch
        {
            null => null,
            string text => text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            var value => value.ToString(),
        };

        public double Number(int field) => this[field] is double value ? value : 0;

        public override string ToString() => Text(0) ?? "";
    }
}
