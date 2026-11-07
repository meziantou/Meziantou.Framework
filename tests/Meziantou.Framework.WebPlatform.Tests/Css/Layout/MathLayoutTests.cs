using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The MathML Core layout (https://w3c.github.io/mathml-core/#presentation-markup) and the OpenType MATH table.</summary>
public sealed class MathLayoutTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a document whose body has no margin, in a 800x600 viewport; the math elements use the 20px Ahem font unless the markup says otherwise.</summary>
    private Document Parse(string html, string style = "math, math * { font: 20px/1 Ahem }")
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0 } " + style + "</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static PhysicalRect GetRect(Document document, string id) => document.GetLayoutEngine().GetBorderBoxRect(document.GetElementById(id)!) ?? throw new InvalidOperationException("No box");

    private static OpenTypeFont LoadFace(string path) => FontFile.Load(File.ReadAllBytes(path))!.Faces[0];

    [Theory]
    [InlineData("+", "Infix", "B")]
    [InlineData("(", "Prefix", "F")]
    [InlineData(")", "Postfix", "G")]
    [InlineData("∑", "Prefix", "J")]
    [InlineData("∫", "Prefix", "H")]
    [InlineData("|", "Infix", "ForceDefault")]
    [InlineData("->", "Infix", "Default")]
    [InlineData("≠", "Infix", "Default")]
    [InlineData("+̸", "Infix", "B")]
    [InlineData("→", "Infix", "A")]
    [InlineData("abc", "Infix", "Default")]
    public void OperatorDictionary_Category(string content, string form, string expected)
        => Assert.Equal(Enum.Parse<MathOperatorCategory>(expected), MathOperatorDictionary.GetCategory(content, Enum.Parse<MathOperatorForm>(form)));

    [Fact]
    public void OperatorDictionary_Properties()
    {
        Assert.Equal(MathOperatorFlags.Symmetric | MathOperatorFlags.LargeOp | MathOperatorFlags.MovableLimits, MathOperatorDictionary.GetFlags(MathOperatorCategory.J));
        Assert.Equal(4.0 / 18, MathOperatorDictionary.GetLSpace(MathOperatorCategory.B), 6);
        Assert.True(MathOperatorDictionary.HasInlineStretchAxis(0x2192));
        Assert.False(MathOperatorDictionary.HasInlineStretchAxis('('));
    }

    [Theory]
    [InlineData('A', 0x1D434)]
    [InlineData('h', 0x210E)]
    [InlineData('α', 0x1D6FC)]
    [InlineData('1', '1')]
    public void MathAuto_ItalicMapping(char original, int expected) => Assert.Equal(expected, MathItalicMapping.GetItalic(original));

    [Fact]
    public void MathTable_BundledMathFont()
    {
        var font = BundledFonts.GetFamilyFonts(BundledFonts.MathFamily)[0].Font;
        var math = font.MathTable;
        Assert.NotNull(math);
        Assert.True(math.HasConstants);
        Assert.True(math.GetConstant(MathConstant.AxisHeight) > 0);
        Assert.True(math.DisplayOperatorMinHeight > 0);

        // The parenthesis has size variants and a glyph assembly
        var construction = math.GetConstruction(font.GetGlyphId('('), vertical: true);
        Assert.NotNull(construction);
        Assert.NotEmpty(construction.Variants);
        Assert.Contains(construction.AssemblyParts, part => part.IsExtender);
        Assert.Null(math.GetConstruction(font.GetGlyphId('('), vertical: false));
    }

    [Fact]
    public void MathTable_WptFontConstants()
    {
        var font = LoadFace(TestPaths.External("wpt", "fonts", "math", "fraction-axisheight7000-rulethickness1000.woff"));
        var math = font.MathTable!;
        Assert.Equal(7000, math.GetConstant(MathConstant.AxisHeight));
        Assert.Equal(1000, math.GetConstant(MathConstant.FractionRuleThickness));
    }

    [Fact]
    public void CffGlyphBounds()
    {
        // The glyphs of the fonts of the MathML tests are rectangles drawn with CFF charstrings
        var font = LoadFace(TestPaths.External("wpt", "fonts", "math", "operators.woff"));
        Assert.True(font.HasTable(OpenTypeTag.Cff));
        Assert.True(font.TryGetGlyphBounds(font.GetGlyphId(0x296F), out var bounds));
        Assert.Equal(new GlyphBounds(0, 0, 1000, 1000), bounds);
    }

    [Fact]
    public void GsubSingleSubstitution_Rtlm()
    {
        var font = LoadFace(TestPaths.External("wpt", "fonts", "math", "radical-rtlm.woff"));
        var radical = font.GetGlyphId(0x221A);
        var mirrored = font.GetSingleSubstitution(radical, OpenTypeTag.RightToLeftMirroredForms);
        Assert.NotEqual(radical, mirrored);
        Assert.Equal(radical, font.GetSingleSubstitution(radical, OpenTypeTag.Kern));
    }

    [Fact]
    public void BoxTree_AnonymousMrowAndIgnoredText()
    {
        var document = Parse("<math><msqrt id=s>text<mi>x</mi></msqrt><mrow>text<mi>y</mi></mrow><mspace><mi>z</mi></mspace></math>");
        document.GetLayoutEngine().EnsureLayout();
        var dump = document.GetLayoutEngine().BoxTree!.Dump();
        Assert.Contains("""
                  math <math>
                    block math <msqrt#s>
                      block math anonymous
                        block math <mi>
                          text "x"
                    block math <mrow>
                      block math <mi>
                        text "y"
                    block math <mspace>

            """.ReplaceLineEndings("\n"), dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Mrow_ChildrenAlignedOnTheirBaselines()
    {
        var document = Parse("<math id=m><mspace id=a width=10px height=20px depth=5px></mspace><mspace id=b width=15px height=8px depth=12px></mspace></math>");
        Assert.Equal(new PhysicalRect(0, 0, 25, 32), GetRect(document, "m"));
        Assert.Equal(new PhysicalRect(0, 0, 10, 25), GetRect(document, "a"));
        Assert.Equal(new PhysicalRect(10, 12, 15, 20), GetRect(document, "b"));
    }

    [Fact]
    public void Mrow_OperatorSpacing()
    {
        // + is an infix operator of the category B: 4/18em on each side
        var document = Parse("<math><mn id=a>X</mn><mo id=o>+</mo><mn id=b>X</mn></math>");
        var a = GetRect(document, "a");
        var o = GetRect(document, "o");
        var b = GetRect(document, "b");
        Assert.Equal(20 * 4.0 / 18, o.X - (a.X + a.Width), 3);
        Assert.Equal(20 * 4.0 / 18, b.X - (o.X + o.Width), 3);
    }

    [Fact]
    public void Token_InkExtent()
    {
        // The Ahem glyphs fill the em box (ascent 0.8em, descent 0.2em); the line-height does not change the token
        var document = Parse("<math display=block style='line-height: 5'><mn id=i>X</mn></math>");
        Assert.Equal(20, GetRect(document, "i").Height);
    }

    [Fact]
    public void Token_MathAutoTransform()
    {
        var document = Parse("<div id=d><math><mi>x</mi><mi>xy</mi></math></div>", style: "");
        Assert.Equal("\U0001D465\nxy", ((HtmlElement)document.GetElementById("d")!).InnerText);
    }

    [Fact]
    public void Fraction_NumeratorAboveDenominatorCentered()
    {
        var document = Parse("<math><mfrac id=f><mspace id=n width=10px height=10px></mspace><mspace id=d width=30px height=10px></mspace></mfrac></math>");
        var fraction = GetRect(document, "f");
        var numerator = GetRect(document, "n");
        var denominator = GetRect(document, "d");

        // The UA style sheet adds 1px of inline padding
        Assert.Equal(32, fraction.Width);
        Assert.Equal(11, numerator.X);
        Assert.Equal(1, denominator.X);
        Assert.True(numerator.Y + numerator.Height <= denominator.Y);
    }

    [Fact]
    public void Script_SuperscriptRaised()
    {
        var document = Parse("<math><msup><mspace id=b width=10px height=20px></mspace><mspace id=s width=5px height=5px></mspace></msup></math>");
        var baseRect = GetRect(document, "b");
        var script = GetRect(document, "s");
        Assert.Equal(baseRect.X + baseRect.Width, script.X);
        Assert.True(script.Y + script.Height < baseRect.Y + baseRect.Height);
    }

    [Fact]
    public void Padded_RequestedParameters()
    {
        var document = Parse("<math><mpadded id=p width=50px height=10px depth=5px lspace=3px voffset=2px><mspace id=s width=10px height=10px></mspace></mpadded></math>");
        var padded = GetRect(document, "p");
        Assert.Equal(new PhysicalSize(50, 15), padded.Size);
        Assert.Equal(new PhysicalRect(padded.X + 3, padded.Y - 2, 10, 10), GetRect(document, "s"));
    }

    [Fact]
    public void StretchyOperator_StretchesToTheRow()
    {
        // The parenthesis of the math font (STIX Two Math) is stretched along the block axis with its glyph assembly
        var document = Parse("<math style='font-size: 20px'><mo id=o>(</mo><mspace width=10px height=100px depth=100px></mspace></math>", style: "");
        Assert.True(GetRect(document, "o").Height >= 200);
    }

    [Fact]
    public void SquareRoot_RadicalSymbol()
    {
        var document = Parse("<math style='font-size: 20px'><msqrt id=r><mspace id=s width=10px height=10px></mspace></msqrt></math>", style: "");
        var root = GetRect(document, "r");
        var baseRect = GetRect(document, "s");
        Assert.True(baseRect.X > root.X, "the radical symbol is before the base");
        Assert.True(baseRect.Y > root.Y, "the overbar is above the base");
        var fragment = document.GetLayoutEngine().GetFragments(document.GetElementById("r")!)[0];
        Assert.Equal(2, fragment.MathData!.PaintItems.Count);
    }

    [Fact]
    public void InlineMath_AlignedOnTheBaselineOfTheLine()
    {
        var document = Parse("<div style='font: 20px/1 Ahem'>X<math><mspace id=s width=10px height=30px></mspace></math></div>");
        Assert.Equal(new PhysicalRect(20, 0, 10, 30), GetRect(document, "s"));
    }

    [Fact]
    public void BlockMath_ContentCentered()
    {
        var document = Parse("<math display=block id=m><mspace id=s width=100px height=10px></mspace></math>");
        Assert.Equal(800, GetRect(document, "m").Width);
        Assert.Equal(350, GetRect(document, "s").X);
    }

    [Fact]
    public void Table_WhiteSpaceAroundCommentsDoesNotCreateCells()
    {
        var document = Parse("<table id=t><tr><td style='padding: 0'><div style='width: 10px; height: 10px'></div></td></tr><tr> <!-- c --> <td style='padding: 0'><div style='width: 10px; height: 10px'></div></td></tr></table>", style: "table { border-spacing: 0 }");
        Assert.Equal(10, GetRect(document, "t").Width);
    }

    [Fact]
    public void Table_ColumnSpanAndRowSpanOfTheCells()
    {
        var document = Parse("""
            <math><mtable>
              <mtr><mtd id=a columnspan=2><mspace width=10px height=10px></mspace></mtd><mtd id=b rowspan=2><mspace width=10px height=10px></mspace></mtd></mtr>
              <mtr><mtd id=c><mspace width=30px height=10px></mspace></mtd><mtd id=d><mspace width=30px height=10px></mspace></mtd></mtr>
            </mtable></math>
            """);

        // https://w3c.github.io/mathml-core/#entry-in-table-or-matrix-mtd: the attributes of the HTML cells
        Assert.Equal(GetRect(document, "c").Left, GetRect(document, "a").Left);
        Assert.Equal(GetRect(document, "d").Right, GetRect(document, "a").Right);
        Assert.Equal(GetRect(document, "a").Top, GetRect(document, "b").Top);
        Assert.Equal(GetRect(document, "d").Bottom, GetRect(document, "b").Bottom);

        document.GetElementById("a")!.SetAttribute("columnspan", "1");
        Assert.Equal(GetRect(document, "c").Right, GetRect(document, "a").Right);
    }

    [Fact]
    public void Operator_WithAnElementChildHasTheDefaultProperties()
    {
        // "(" is a prefix operator without spacing; an mo that does not contain only text has the default spacing (5/18em)
        var plain = GetRect(Parse("<math><mn>1</mn><mo>(</mo><mn id=b>1</mn></math>"), "b").X;
        var withChild = GetRect(Parse("<math><mn>1</mn><mo>(<span></span></mo><mn id=b>1</mn></math>"), "b").X;
        Assert.Equal(plain + (2 * 20 * 5.0 / 18), withChild, 3);
    }

    [Fact]
    public void BlockMath_ContentWiderThanTheBoxIsStartAligned()
    {
        var document = Parse("<math display=block id=m style='width: 50px'><mspace id=s width=100px height=10px></mspace></math>");
        Assert.Equal(0, GetRect(document, "s").X);
    }
}
