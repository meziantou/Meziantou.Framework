using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Tests.Svg;

public sealed class SvgPathDataTests
{
    private static string ParseToString(string value) => string.Join(" | ", SvgPathData.Parse(value).Select(c => c.ToSegment().ToString()));

    private static string NormalizeToString(string value) => string.Join(" | ", SvgPathData.Normalize(SvgPathData.Parse(value)).Select(c => c.ToSegment().ToString()));

    private static SvgPathElement CreatePath(string? d)
    {
        var document = HtmlDocumentParser.ParseDocument("<svg></svg>");
        var path = (SvgPathElement)document.CreateElementNS("http://www.w3.org/2000/svg", "path");
        if (d is not null)
        {
            path.SetAttribute("d", d);
        }

        return path;
    }

    [Theory]
    [InlineData("M 10 20 L 30 40", "M 10 20 | L 30 40")]
    [InlineData("M10,20L30,40z", "M 10 20 | L 30 40 | z")]
    [InlineData("  m 1 2 3 4 5 6  ", "m 1 2 | l 3 4 | l 5 6")]
    [InlineData("M 0 0 100 100 L 10 10 20 20", "M 0 0 | L 100 100 | L 10 10 | L 20 20")]
    [InlineData("M 0 0 h 10 v 10 H 5 V 5", "M 0 0 | h 10 | v 10 | H 5 | V 5")]
    [InlineData("M 0 0 C 1 2 3 4 5 6 7 8 9 10 11 12", "M 0 0 | C 1 2 3 4 5 6 | C 7 8 9 10 11 12")]
    [InlineData("M 0 0 S 1 2 3 4 q 1 2 3 4 T 5 6 t 7 8", "M 0 0 | S 1 2 3 4 | q 1 2 3 4 | T 5 6 | t 7 8")]
    [InlineData("M 0 0 A 25 26 30 1 0 50 50", "M 0 0 | A 25 26 30 1 0 50 50")]
    [InlineData("M 0 0 a25,26 30 1050,50", "M 0 0 | a 25 26 30 1 0 50 50")] // The flags do not need a separator
    [InlineData("M 100-200 L 200-100", "M 100 -200 | L 200 -100")]
    [InlineData("M 0.6.5 L .5.6", "M 0.6 0.5 | L 0.5 0.6")]
    [InlineData("M 1e2,1E2 L 2e+1 -1.5e-1", "M 100 100 | L 20 -0.15")]
    [InlineData("M 50+100", "M 50 100")]
    [InlineData("M\t1\n2\r3\f4", "M 1 2 | L 3 4")]
    [InlineData("M 0 0 Z M 1 1 Z L 2 2", "M 0 0 | Z | M 1 1 | Z | L 2 2")]
    public void Parse_ValidPathData(string value, string expected) => Assert.Equal(expected, ParseToString(value));

    [Theory]
    [InlineData("", "")]
    [InlineData("none", "")]
    [InlineData("L 10 10", "")] // The path data must begin with a moveto
    [InlineData("Z", "")]
    [InlineData(",M 10 10", "")]
    [InlineData("M 10", "")]
    [InlineData("M 10 20 L", "M 10 20")]
    [InlineData("M 10,20 L 50,50 X 100,100", "M 10 20 | L 50 50")]
    [InlineData("M 10,110 L 50,110 60,110 70", "M 10 110 | L 50 110 | L 60 110")] // Renders up to the last complete segment
    [InlineData("M 10,10 L 50,50 L 23.,100", "M 10 10 | L 50 50")] // A decimal point must be followed by a digit
    [InlineData("M 10,10 L 50,50 L 1e 2", "M 10 10 | L 50 50")] // An exponent must have digits
    [InlineData("M 0 0 L 1e39 0", "M 0 0")] // Not a finite float
    [InlineData("M 10,210 L 50,210 A 25,25 0 2,1 100,210", "M 10 210 | L 50 210")] // A flag is 0 or 1
    [InlineData("M 0 0 Z 10 10", "M 0 0 | Z")] // A closepath has no implicit repetition
    [InlineData("M 100,10 x 150,10", "M 100 10")] // The commands are case-sensitive
    [InlineData("M 0 0 L 10,,10", "M 0 0")]
    public void Parse_Error_ReturnsTheSegmentsBeforeTheError(string value, string expected) => Assert.Equal(expected, ParseToString(value));

    [Theory]
    [InlineData("M 10 20 L 30 40", true)]
    [InlineData("  M 10 20 L 30 40 Z  ", true)]
    [InlineData("", true)]
    [InlineData("M 10 20 L", false)]
    [InlineData("M 20 30 A 60 70 80", false)] // The last segment is truncated
    [InlineData("M 10 20 L 30", false)]
    [InlineData("L 10 20", false)]
    [InlineData("M 10 20 X", false)]
    public void Parse_Complete(string value, bool expected)
    {
        SvgPathData.Parse(value, out var complete);
        Assert.Equal(expected, complete);
    }

    [Theory]
    [InlineData("m 10 20 q 30 60 40 50 q 100 70 90 80", "M 10 20 Q 40 80 50 70 Q 150 140 140 150")]
    [InlineData("M 0 0 L 100 100 m 0 100 l 100 0 Z l 160 20 Z", "M 0 0 L 100 100 M 100 200 L 200 200 Z L 260 220 Z")]
    [InlineData("m 10 170 h 90 v 30 m 0 0 s 1 2 3 4 z c 9 8 7 6 5 4", "M 10 170 H 100 V 200 M 100 200 S 101 202 103 204 Z C 109 208 107 206 105 204")]
    [InlineData("m 10 20 a 10 20 30 1 0 40 50 a 110 120 30 1 1 140 50", "M 10 20 A 10 20 30 1 0 50 70 A 110 120 30 1 1 190 120")]
    [InlineData("m 10 20 l 20 30 Z l 50 60 Z m 70 80 l 90 60 Z t 70 120", "M 10 20 L 30 50 Z L 60 80 Z M 80 100 L 170 160 Z T 150 220")]
    public void ToAbsolute(string value, string expected) => Assert.Equal(expected, SvgPathData.Serialize(SvgPathData.ToAbsolute(SvgPathData.Parse(value))));

    [Theory]
    [InlineData("M 0 0 H 100 V 100 H 0 Z", "M 0 0 | L 100 0 | L 100 100 | L 0 100 | Z")]
    [InlineData("m 10 20 l 30 0 h 5 v 5 z l 1 1", "M 10 20 | L 40 20 | L 45 20 | L 45 25 | Z | L 11 21")]
    [InlineData("M 0 0 C 30,90 25,10 50,10 S 70,90 90,90", "M 0 0 | C 30 90 25 10 50 10 | C 75 10 70 90 90 90")]
    [InlineData("M 0 0 S 10 10 20 0", "M 0 0 | C 0 0 10 10 20 0")] // Without a previous cubic Bézier, the first control point is the current point
    [InlineData("M 0 0 Q 30 30 30 60 t 30 0", "M 0 0 | C 20 20 30 40 30 60 | C 30 80 40 80 60 60")]
    [InlineData("M 0 0 A 0 10 0 0 0 10 10", "M 0 0 | L 10 10")] // An arc with a zero radius is a line
    [InlineData("M 5 5 A 10 10 0 0 0 5 5", "M 5 5 | L 5 5")] // An arc whose endpoints are identical is a zero-length line
    public void Normalize(string value, string expected) => Assert.Equal(expected, NormalizeToString(value));

    [Fact]
    public void Normalize_Arc()
    {
        var segments = SvgPathData.Normalize(SvgPathData.Parse("M 6 10 A 10 10 10 0 0 15 10"));
        Assert.Equal(2, segments.Count);
        Assert.Equal('C', segments[1].Type);
        float[] expected = [8.8305f, 11.4263f, 12.1695f, 11.4263f, 15, 10];
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], segments[1].Values[i], 0.0005f);
        }
    }

    [Fact]
    public void Normalize_LargeArc_IsSplitInQuarters()
    {
        var segments = SvgPathData.Normalize(SvgPathData.Parse("M 0 0 A 10 10 0 1 0 20 0"));
        Assert.Equal("M", segments[0].Type.ToString());
        Assert.True(segments.Count > 2);
        Assert.All(segments.Skip(1), s => Assert.Equal('C', s.Type));
        Assert.Equal(new[] { 20f, 0f }, segments[^1].Values[4..]);
    }

    [Fact]
    public void GetPathData_ReturnsFreshSegments()
    {
        var path = CreatePath("M 0 0 L 10 10");
        var first = path.GetPathData();
        var second = path.GetPathData();
        Assert.Equal(["M 0 0", "L 10 10"], first.Select(s => s.ToString()));
        Assert.NotSame(first[0], second[0]);
        Assert.NotSame(first[0].Values, second[0].Values);
    }

    [Fact]
    public void GetPathData_MissingAttribute_ReturnsEmpty() => Assert.Empty(CreatePath(d: null).GetPathData());

    [Fact]
    public void GetPathData_FollowsAttributeChanges()
    {
        var path = CreatePath("M 0 0");
        Assert.Single(path.GetPathData());
        path.SetAttribute("d", "M 0 0 L 1 1 Z");
        Assert.Equal(["M 0 0", "L 1 1", "Z"], path.GetPathData().Select(s => s.ToString()));
        path.RemoveAttribute("d");
        Assert.Empty(path.GetPathData());
    }

    [Fact]
    public void GetPathData_Normalize() =>
        Assert.Equal(["M 10 20", "L 40 20"], CreatePath("m 10 20 l 30 0").GetPathData(new SvgPathDataSettings { Normalize = true }).Select(s => s.ToString()));

    [Fact]
    public void SetPathData_SerializesTheSegments()
    {
        var path = CreatePath("M 99 99");
        path.SetPathData(
        [
            new SvgPathSegment("m", [5, 5]),
            new SvgPathSegment("l", [3.5f, -4]),
            new SvgPathSegment("A", [25, 25, 0, 0, 2, 50, 50]),
            new SvgPathSegment("z", []),
        ]);
        Assert.Equal("m 5 5 l 3.5 -4 A 25 25 0 0 1 50 50 Z", path.GetAttribute("d"));
    }

    [Theory]
    [InlineData("X", new float[] { })]
    [InlineData("LL", new float[] { 10, 10 })]
    [InlineData("L", new float[] { 10 })]
    [InlineData("L", new float[] { 10, 10, 20, 20 })]
    [InlineData("L", new float[] { float.NaN, 10 })]
    [InlineData("L", new float[] { float.PositiveInfinity, 10 })]
    [InlineData("Z", new float[] { 1, 2 })]
    public void SetPathData_TruncatesAtTheFirstInvalidSegment(string type, float[] values)
    {
        var path = CreatePath(d: null);
        path.SetPathData([new SvgPathSegment("M", [0, 0]), new SvgPathSegment(type, values), new SvgPathSegment("L", [1, 1])]);
        Assert.Equal("M 0 0", path.GetAttribute("d"));
    }

    [Fact]
    public void SetPathData_InvalidFirstSegment_RemovesTheAttribute()
    {
        var path = CreatePath("M 99 99");
        path.SetPathData([new SvgPathSegment("L", [10, 10])]);
        Assert.Null(path.GetAttribute("d"));

        path.SetAttribute("d", "M 1 1");
        path.SetPathData([]);
        Assert.False(path.HasAttribute("d"));
    }

    [Fact]
    public void SetPathData_RoundTrips()
    {
        const string Value = "M 0 0 H 50 V 50 h -50 v -50 C 10 10 20 20 30 30 S 50 50 60 60 q 1 1 2 2 t 3 3 a 10 10 0 1 0 5 5 Z";
        var path = CreatePath(Value);
        var copy = CreatePath(d: null);
        copy.SetPathData(path.GetPathData());
        Assert.Equal(Value, copy.GetAttribute("d"));
    }

    [Theory]
    [InlineData("M 0 0 H 100 V 100 H 0 Z", 400)]
    [InlineData("M 50 100 l 0 0 l 0 -50 l 100 0 l 86.3325 122.665 z", 500)]
    [InlineData("m 0 0 L 3 4 M 10 10 l 6 8", 15)]
    [InlineData("M 0 0 t 0 100", 100)]
    [InlineData("M 0 0", 0)]
    [InlineData("", 0)]
    public void GetTotalLength(string value, float expected) => Assert.Equal(expected, CreatePath(value).GetTotalLength(), 0.001f);

    [Theory]
    [InlineData("M 100,100 A 50,50 0 0,1 200,100", 157.0796f)] // Half circle
    [InlineData("M 0 0 A 50 25 0 0 1 100 0", 121.1056f)] // Half ellipse
    [InlineData("M 0 0 A 1 1 0 0 1 100 0", 157.0796f)] // The radii are scaled up
    [InlineData("M 0 0 A 10 10 0 1 1 0 0.0001", 62.8318f)] // Almost a full circle
    [InlineData("M 0 0 C 0 0 100 0 100 0", 100)] // Straight cubic Bézier
    [InlineData("M 0 0 Q 50 50 100 0", 114.7793f)]
    public void GetTotalLength_Curves(string value, float expected) => Assert.Equal(expected, CreatePath(value).GetTotalLength(), 0.0005f);

    [Theory]
    [InlineData(-10, 40, 70)]
    [InlineData(0, 40, 70)]
    [InlineData(35, 75, 70)]
    [InlineData(70, 110, 70)]
    [InlineData(80, 110, 70)]
    public void GetPointAtLength_Line(float distance, float x, float y)
    {
        var point = CreatePath("M 40 70 L 110 70").GetPointAtLength(distance);
        Assert.Equal(x, point.X, 0.0001);
        Assert.Equal(y, point.Y, 0.0001);
    }

    [Fact]
    public void GetPointAtLength_Arc()
    {
        // A quarter of the half circle is at 45 degrees
        var point = CreatePath("M 100,100 A 50,50 0 0,1 200,100").GetPointAtLength(157.0796f / 2);
        Assert.Equal(150, point.X, 0.001);
        Assert.Equal(50, point.Y, 0.001);
    }

    [Theory]
    [InlineData("M 0 0 C 0 0 0 0 100 0", 100, 50, 50, 0)] // Coincident control points
    [InlineData("M 0 0 C 0 0 0 0 100 0", 100, 12.5f, 12.5f, 0)]
    [InlineData("M 0 0 C 90 0 95 0 100 0", 100, 50, 50, 0)] // Unevenly spaced control points
    [InlineData("M 0 0 C 10 10 20 20 100 100", 141.4214f, 70.7107f, 50, 50)]
    [InlineData("M 100 0 C 100 0 100 0 0 0", 100, 25, 75, 0)]
    [InlineData("M 0 0 C 100 0 100 0 50 0", 115.6854f, 50, 50, 0)] // The curve reverses direction at x=200(sqrt(2)-1)
    [InlineData("M 0 0 C 100 0 100 0 50 0", 115.6854f, 100, 65.6854f, 0)]
    public void GetPointAtLength_StraightCubic(string value, float totalLength, float distance, float x, float y)
    {
        var path = CreatePath(value);
        Assert.Equal(totalLength, path.GetTotalLength(), 0.001f);
        var point = path.GetPointAtLength(distance);
        Assert.Equal(x, point.X, 0.001);
        Assert.Equal(y, point.Y, 0.001);
    }

    [Fact]
    public void GetPointAtLength_EmptyPath_Throws()
    {
        var exception = Assert.Throws<DomException>(() => CreatePath("L 10 10").GetPointAtLength(0));
        Assert.Equal("InvalidStateError", exception.Name);
    }

    [Theory]
    [InlineData(0, "M 0 0")]
    [InlineData(5, "L 10 0")]
    [InlineData(10, "L 10 0")]
    [InlineData(15, "v 10")]
    [InlineData(100, "v 10")]
    public void GetPathSegmentAtLength(float distance, string expected) =>
        Assert.Equal(expected, CreatePath("M 0 0 L 10 0 v 10").GetPathSegmentAtLength(distance)?.ToString());

    [Fact]
    public void GetPathSegmentAtLength_EmptyPath_ReturnsNull() => Assert.Null(CreatePath(d: null).GetPathSegmentAtLength(0));

    [Theory]
    [InlineData("<path id=target d='M 0 0 h 10'/>", 10)]
    [InlineData("<path id=target style='d: path(\"M 0 0 v 20\")'/>", 20)]
    [InlineData("<path id=target d='M 0 0 h 10' style='d: path(\"M 0 0 v 20\")'/>", 20)] // The d property wins over the d attribute
    [InlineData("<path id=target d='M 0 0 h 10' style='display: none; d: path(\"M 0 0 v 20\")'/>", 20)]
    [InlineData("<style>path { d: path('M 0 0 h 30') }</style><path id=target d='M 0 0 h 10'/>", 30)]
    [InlineData("<path id=target d='M 0 0 h 10' style='d: none'/>", 0)]
    public void GetTotalLength_UsesTheDProperty(string svg, float expected)
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><body><svg>" + svg + "</svg>");
        Assert.Equal(expected, ((SvgPathElement)document.GetElementById("target")!).GetTotalLength(), 0.001f);
    }

    [Fact]
    public void Geometry_FollowsTheChangesOfTheDProperty()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><body><svg><path id=target d='M 0 0 h 10 v 10'/></svg>");
        var path = (SvgPathElement)document.GetElementById("target")!;

        // The segments of the d attribute keep their relative commands
        Assert.Equal("v 10", path.GetPathSegmentAtLength(15)?.ToString());

        path.Style.SetProperty("d", "path('M 0 0 H 20 V 20')");
        Assert.Equal(40, path.GetTotalLength(), 0.001f);
        Assert.Equal("V 20", path.GetPathSegmentAtLength(30)?.ToString());

        // getPathData() and setPathData() use the d attribute
        Assert.Equal("M 0 0 | h 10 | v 10", string.Join(" | ", path.GetPathData().Select(s => s.ToString())));
        path.SetPathData([new SvgPathSegment("M", [0, 0]), new SvgPathSegment("L", [5, 0])]);
        Assert.Equal(40, path.GetTotalLength(), 0.001f);

        path.Style.RemoveProperty("d");
        Assert.Equal(5, path.GetTotalLength(), 0.001f);
    }

    [Fact]
    public void Geometry_OfBasicShapes()
    {
        // A disconnected shape has the geometry of its attributes; an empty path cannot give a point
        var document = HtmlDocumentParser.ParseDocument("<svg></svg>");
        var rect = (SvgGeometryElement)document.CreateElementNS("http://www.w3.org/2000/svg", "rect");
        Assert.Equal(0, rect.GetTotalLength());
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => rect.GetPointAtLength(1)).Name);
        rect.SetAttribute("width", "20");
        rect.SetAttribute("height", "10");
        Assert.Equal(60, rect.GetTotalLength());
        var point = rect.GetPointAtLength(25);
        Assert.Equal((20d, 5d), (point.X, point.Y));
    }
}
