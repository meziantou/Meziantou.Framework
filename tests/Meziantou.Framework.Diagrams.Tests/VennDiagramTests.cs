using System.Diagnostics;
using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Tests;

public sealed class VennDiagramTests
{
    [Fact]
    public void VennDiagram()
    {
        #region Diagrams_VennDiagram
        var diagram = new VennDiagram { Title = "What makes a good feature" };
        diagram.AddSet("Desirable");
        diagram.AddSet("Feasible");
        diagram.AddSet("Viable");
        diagram.AddOverlap(["Desirable", "Feasible"], "Buildable");
        diagram.AddOverlap(["Feasible", "Viable"], "Sustainable");
        diagram.AddOverlap(["Desirable", "Viable"], "Marketable");
        diagram.AddOverlap(["Desirable", "Feasible", "Viable"], "Ship it");
        #endregion

        Assert.Equal([1, 1, 1, 2, 2, 2, 3], diagram.Areas.Select(area => area.Sets.Count));
        Assert.Equal(2.5, diagram.Areas[3].EffectiveSize);
        DiagramAssert.Svg(diagram, "VennDiagram");
    }

    [Fact]
    public void SizesTextsAndStyles()
    {
        #region Diagrams_VennDiagramTexts
        var diagram = new VennDiagram();
        diagram.AddSet("A", "Frontend", size: 20);
        diagram.AddSet("B", "Backend", size: 12);
        diagram.AddOverlap(["A", "B"], "Shared", size: 3);
        diagram.AddText(["A"], "A1", "React");
        diagram.AddText(["A"], "A2", "Design Systems");
        diagram.AddText(["B"], "B1", "API");
        diagram.AddText(["A", "B"], "AB1", "OpenAPI");
        diagram.AddStyle(["A"], new VennStyle { Fill = "#ff6b6b" });
        diagram.AddStyle(["A", "B"], new VennStyle { Fill = "#ffe08a", TextColor = "#333333" });
        diagram.AddStyle(["A1"], new VennStyle { TextColor = "red" });
        #endregion

        DiagramAssert.Svg(diagram, "VennDiagramTexts");
    }

    [Fact]
    public void ThemesAndColors()
    {
        var diagram = new VennDiagram { Theme = DiagramTheme.Dark, Width = 400, Height = 300 };
        for (var i = 0; i < 9; i++)
        {
            diagram.AddSet("S" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), size: 10 - i);
        }

        diagram.SetColors[1] = "#00ff00";
        diagram.TextColor = "#ffffff";
        diagram.AddOverlap(["S0", "S1"], "Both");
        diagram.AddStyle(["S2"], new VennStyle { FillOpacity = 0.5, Stroke = "#ff0000", StrokeWidth = 0 });
        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void CustomTheme()
    {
        var diagram = new VennDiagram { Theme = DiagramTheme.Default with { PrimaryColor = "#123456" } };
        diagram.AddSet("A");
        diagram.AddSet("B");
        diagram.AddOverlap(["A", "B"]);
        var svg = diagram.ToSvg();
        Assert.Contains("venn-circle", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("NaN", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyDiagram()
    {
        DiagramAssert.Svg(new VennDiagram());
    }

    [Fact]
    public void SetsOfSizeZeroAreNotDrawn()
    {
        // A set of size 0 is removed with its overlaps and its texts, as Mermaid does
        var diagram = new VennDiagram();
        diagram.AddSet("A");
        diagram.AddSet("B", size: 0);
        diagram.AddOverlap(["A", "B"], "Hidden");
        diagram.AddText(["B"], "t", "Hidden text");
        var svg = diagram.ToSvg();
        Assert.Contains("data-venn-sets=\"A\"", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("data-venn-sets=\"B\"", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlapsOfUnknownSetsAreNotDrawn()
    {
        var diagram = new VennDiagram();
        diagram.AddSet("A");
        diagram.AddOverlap(["A", "Z"], "Unknown");
        Assert.DoesNotContain("Unknown", diagram.ToSvg(), StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicatedAreasAndLabels()
    {
        // The label of an area is the last one of the areas with the same sets, as venn.js indexes them
        var diagram = new VennDiagram();
        diagram.AddSet("A", "First");
        diagram.AddSet("A", "Second", size: 5);
        diagram.AddSet("B");
        diagram.AddOverlap(["A", "B"], size: 1);
        diagram.AddOverlap(["B", "A"], size: 3);
        diagram.AddOverlap(["A", "A"]);
        var svg = diagram.ToSvg();
        Assert.DoesNotContain("First", svg, StringComparison.Ordinal);
        Assert.Contains("Second", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("NaN", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void IdentifiersWithSeparators()
    {
        // The overlaps of the same sets are merged for the layout: identifiers with NUL characters or commas must not mix them up
        var diagram = new VennDiagram();
        diagram.AddSet("\0");
        diagram.AddSet("");
        diagram.AddSet("a,b");
        diagram.AddOverlap(["", "", ""]);
        diagram.AddOverlap(["\0", ""], size: 2);
        diagram.AddOverlap(["a,b", ""], "Label");
        var svg = diagram.ToSvg();
        Assert.DoesNotContain("NaN", svg, StringComparison.Ordinal);
        Assert.Contains("Label", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void SetsNamedAsOverlapsAreDistinctAreas()
    {
        // A set named "A,B" or "A|B" is not the overlap of A and B: each keeps its label and its style
        var diagram = new VennDiagram();
        diagram.AddSet("A");
        diagram.AddSet("B");
        diagram.AddSet("A,B", "Named set");
        diagram.AddSet("A|B");
        diagram.AddOverlap(["A", "B"], "Overlap", size: 0.5);
        diagram.AddStyle(["A", "B"], new VennStyle { Fill = "#abcdef" });
        var svg = diagram.ToSvg();
        Assert.Contains("Named set", svg, StringComparison.Ordinal);
        Assert.Contains("Overlap", svg, StringComparison.Ordinal);

        var start = svg.IndexOf("data-venn-sets=\"A|B\"", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var setGroup = svg[start..svg.IndexOf("</g>", start, StringComparison.Ordinal)];
        Assert.DoesNotContain("abcdef", setGroup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("abcdef", svg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManyTextsInAnArea()
    {
        var diagram = new VennDiagram { Title = "Languages" };
        diagram.AddSet("Static");
        diagram.AddSet("Dynamic");
        diagram.AddOverlap(["Dynamic", "Static"], "Gradual");
        foreach (var name in (string[])["C#", "Java", "Rust", "Go", "Kotlin"])
        {
            diagram.AddText(["Static"], name);
        }

        diagram.AddText(["Dynamic", "Static"], "TypeScript", "TypeScript with a long name that wraps");
        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void ManySetsAreLaidOutQuickly()
    {
        var diagram = new VennDiagram();
        for (var i = 0; i < 32; i++)
        {
            diagram.AddSet("S" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), size: 1 + (i % 5));
        }

        for (var i = 0; i < 31; i++)
        {
            diagram.AddOverlap(["S" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), "S" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)], size: 0.5);
        }

        diagram.AddOverlap(Enumerable.Range(0, 32).Select(i => "S" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)), "All");
        var stopwatch = Stopwatch.StartNew();
        var svg = diagram.ToSvg();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"The layout took {stopwatch.Elapsed}");
        Assert.DoesNotContain("NaN", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void LayoutOfThreeSetsMatchesVennJs()
    {
        // The values of venn.js 2.0.0 (venn, normalizeSolution, scaleSolution and computeTextCentres) for the same areas,
        // in Node 24 (V8 13.6), rounded to 4 decimals
        var (circles, centres) = VennLayout.Compute(
            [
                new(["Desirable"], 10), new(["Feasible"], 10), new(["Viable"], 10), new(["Desirable", "Feasible", "Viable"], 10.0 / 9),
                new(["Desirable", "Feasible"], 2.5), new(["Desirable", "Viable"], 2.5), new(["Feasible", "Viable"], 2.5),
            ],
            800,
            450,
            15);

        AssertCircle(circles["Desirable"], 314.2616, 299.2511, 135.7489);
        AssertCircle(circles["Feasible"], 485.7384, 299.2511, 135.7489);
        AssertCircle(circles["Viable"], 399.9997, 150.7489, 135.7489);
        AssertCentre(centres[VennLayout.GetAreaKey(["Desirable"])], 268.583, 325.625);
        AssertCentre(centres[VennLayout.GetAreaKey(["Viable"])], 400.000, 98.002);
        AssertCentre(centres[VennLayout.GetAreaKey(["Desirable", "Feasible", "Viable"])], 400.000, 249.751);
        AssertCentre(centres[VennLayout.GetAreaKey(["Desirable", "Viable"])], 329.752, 209.193);
    }

    [Fact]
    public void LayoutOfSizesMatchesVennJs()
    {
        var (circles, centres) = VennLayout.Compute([new(["A"], 20), new(["B"], 12), new(["A", "B"], 3)], 800, 450, 15);
        AssertCircle(circles["A"], 294.2164, 225.0000, 210.0000);
        AssertCircle(circles["B"], 553.1183, 225.0000, 162.6653);
        AssertCentre(centres[VennLayout.GetAreaKey(["A", "B"])], 447.335, 225.000);
    }

    [Fact]
    public void LayoutOfDisjointSetsMatchesVennJs()
    {
        // The clusters of sets that do not overlap are packed in a grid
        var (circles, _) = VennLayout.Compute([new(["A"], 10), new(["B"], 5), new(["C"], 3)], 800, 450, 15);
        AssertCircle(circles["A"], 302.6024, 148.9523, 133.9523);
        AssertCircle(circles["B"], 536.6313, 148.9523, 94.7186);
        AssertCircle(circles["C"], 302.6024, 361.6313, 73.3687);
    }

    [Fact]
    public void LayoutIsDeterministic()
    {
        // More than 7 areas use the multidimensional scaling, whose random restarts have a fixed seed
        VennLayoutArea[] areas = [new(["A"], 10), new(["B"], 8), new(["C"], 6), new(["D"], 4), new(["A", "B"], 2), new(["B", "C"], 1), new(["C", "D"], 1)];
        var (first, _) = VennLayout.Compute(areas, 800, 450, 15);
        var (second, _) = VennLayout.Compute(areas, 800, 450, 15);
        foreach (var key in first.Keys)
        {
            Assert.Equal((first[key].X, first[key].Y, first[key].Radius), (second[key].X, second[key].Y, second[key].Radius));
            Assert.True(first[key].X - first[key].Radius >= 14.999 && first[key].X + first[key].Radius <= 785.001);
        }
    }

    [Theory]
    [InlineData(-0.999, 3.0968675664210599)]
    [InlineData(-0.75, 2.4188584057763776)]
    [InlineData(-0.5, 2.0943951023931957)]
    [InlineData(-0.3, 1.8754889808102941)]
    [InlineData(-1e-9, 1.5707963277948966)]
    [InlineData(0, 1.5707963267948966)]
    [InlineData(0.1, 1.4706289056333368)]
    [InlineData(0.4999, 1.0473130174019469)]
    [InlineData(0.5, 1.0471975511965979)]
    [InlineData(0.73, 0.75247437616333679)]
    [InlineData(0.999999, 0.0014142136802445852)]
    public void AcosIsTheOneOfV8(double x, double expected)
    {
        // The values of Math.acos in V8 13.6 (fdlibm), to the last bit
        Assert.Equal(expected, VennMath.Acos(x));
    }

    [Theory]
    [InlineData(1, 2, 0.46364760900080609)]
    [InlineData(-1, 2, -0.46364760900080609)]
    [InlineData(-1, -2, -2.6779450445889870)]
    [InlineData(1, -2, 2.6779450445889870)]
    [InlineData(3, 0.0001, 1.5707629934615757)]
    [InlineData(-0.3, -0.7, -2.7367008673047097)]
    [InlineData(2, 1, 1.1071487177940904)]
    public void Atan2IsTheOneOfV8(double y, double x, double expected)
    {
        Assert.Equal(expected, VennMath.Atan2(y, x));
    }

    [Theory]
    [InlineData(-6, 0.27941549819892586, 0.96017028665036608)]
    [InlineData(-3.5, 0.35078322768961984, -0.93645668729079634)]
    [InlineData(-2, -0.90929742682568171, -0.41614683654714241)]
    [InlineData(-0.7, -0.64421768723769102, 0.76484218728448850)]
    [InlineData(0.2, 0.19866933079506122, 0.98006657784124163)]
    [InlineData(0.9, 0.78332690962748341, 0.62160996827066439)]
    [InlineData(1.6, 0.99957360304150511, -0.029199522301288815)]
    [InlineData(2.4, 0.67546318055115095, -0.73739371554124544)]
    [InlineData(3.1, 0.041580662433290491, -0.99913515027327948)]
    [InlineData(4.7, -0.99992325756410083, -0.012388663462890560)]
    [InlineData(6.2, -0.083089402817496397, 0.99654209702321750)]
    public void SinAndCosAreTheOnesOfV8(double x, double sin, double cos)
    {
        Assert.Equal(sin, VennMath.Sin(x));
        Assert.Equal(cos, VennMath.Cos(x));
    }

    [Fact]
    public void MathEdgeCases()
    {
        Assert.True(double.IsNaN(VennMath.Acos(1.5)));
        Assert.Equal(0.0, VennMath.Acos(1));
        Assert.Equal(Math.PI, VennMath.Acos(-1));
        Assert.True(double.IsNaN(VennMath.Atan2(double.NaN, 1)));
        Assert.Equal(Math.PI, VennMath.Atan2(0, -1));
        Assert.Equal(-Math.PI / 2, VennMath.Atan2(-1, 0));
        Assert.Equal(Math.PI / 4, VennMath.Atan2(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(Math.PI / 2, VennMath.Atan(double.PositiveInfinity));
        Assert.True(double.IsNaN(VennMath.Sin(double.PositiveInfinity)));
        Assert.True(double.IsNaN(VennMath.Cos(double.NaN)));
        Assert.Equal(Math.Sin(1e7), VennMath.Sin(1e7), 1e-6);
        Assert.Equal(Math.Cos(1e9), VennMath.Cos(1e9), 1e-6);
        for (var x = -10.0; x <= 10; x += 0.01)
        {
            Assert.Equal(Math.Sin(x), VennMath.Sin(x), 1e-15);
            Assert.Equal(Math.Cos(x), VennMath.Cos(x), 1e-15);
            Assert.Equal(Math.Atan2(x, 1.3), VennMath.Atan2(x, 1.3), 1e-15);
            Assert.Equal(Math.Acos(x / 10), VennMath.Acos(x / 10), 1e-15);
        }
    }

    [Fact]
    public void KeysAreInTheOrderOfJavaScriptObjects()
    {
        var map = new VennKeyedMap<int>
        {
            ["b"] = 1,
            ["10"] = 2,
            ["a"] = 3,
            ["2"] = 4,
            ["01"] = 5,
            ["4294967295"] = 6,
        };
        map["b"] = 7;
        Assert.Equal(["2", "10", "b", "a", "01", "4294967295"], map.Keys);
        Assert.Equal(7, map["b"]);
    }

    [Fact]
    public void StableSortKeepsTheOrderOfEqualItems()
    {
        List<(int Key, string Name)> items = [(2, "a"), (1, "b"), (2, "c"), (1, "d"), (0, "e")];
        VennSort.StableSort(items, (x, y) => x.Key - y.Key);
        Assert.Equal(["e", "b", "d", "a", "c"], items.Select(item => item.Name));

        // NaN keeps the order
        List<double> values = [double.NaN, 1, double.NaN, 0];
        VennSort.StableSort(values, (x, y) => x - y);
        Assert.HasCount(4, values);
    }

    [Fact]
    public void InvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new VennDiagram { Width = 0 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new VennDiagram { Height = double.NaN });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new VennArea(["A"]) { Size = -1 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new VennArea(["A"]) { Size = double.PositiveInfinity });
        Assert.Throws<ArgumentException>("sets", () => new VennArea([]));
        Assert.Throws<ArgumentException>("sets", () => new VennArea([null!]));
        Assert.Throws<ArgumentException>("sets", () => new VennDiagram().AddOverlap(["A"]));
        Assert.Throws<ArgumentNullException>("id", () => new VennDiagram().AddSet(null!));
        Assert.Throws<ArgumentNullException>("id", () => new VennTextNode(["A"], null!));
        Assert.Throws<ArgumentException>("sets", () => new VennTextNode([], "t"));
        Assert.Throws<ArgumentNullException>("value", () => new VennTextNode(["A"], "t").Id = null!);
        Assert.Throws<ArgumentNullException>("style", () => new VennStyleRule(["A"], null!));
        Assert.Throws<ArgumentException>("targets", () => new VennStyleRule([], new VennStyle()));
        Assert.Throws<ArgumentNullException>("value", () => new VennStyleRule(["A"], new VennStyle()).Style = null!);
    }

    [Fact]
    public void ModelMembers()
    {
        var area = new VennArea(["B", "A"], "label", 3);
        Assert.Equal(["A", "B"], area.Sets);
        Assert.False(area.IsSet);
        Assert.Equal("A, B", area.ToString());
        Assert.Equal(3, area.EffectiveSize);
        Assert.Equal(10, new VennArea(["A"]).EffectiveSize);

        var node = new VennTextNode(["A"], "id");
        Assert.Equal("id", node.ToString());
        node.Label = "Label";
        Assert.Equal("Label", node.ToString());

        var style = new VennStyle { Fill = "#ff0000", FillOpacity = 0.5 }.Merge(new VennStyle { Fill = "#00ff00", TextColor = "#000000" });
        Assert.Equal((SceneColor)"#00ff00", style.Fill);
        Assert.Equal(0.5, style.FillOpacity);
        Assert.Equal((SceneColor)"#000000", style.TextColor);
        Assert.Same(style, style.Merge(null));
        Assert.True(new VennStyle().IsEmpty);
    }

    private static void AssertCircle(VennCircle circle, double x, double y, double radius)
    {
        Assert.Equal(x, circle.X, 0.0001);
        Assert.Equal(y, circle.Y, 0.0001);
        Assert.Equal(radius, circle.Radius, 0.0001);
    }

    private static void AssertCentre(VennTextCentre centre, double x, double y)
    {
        Assert.False(centre.Disjoint);
        Assert.Equal(x, centre.X, 0.001);
        Assert.Equal(y, centre.Y, 0.001);
    }
}
