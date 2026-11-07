using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Formatting;

namespace Meziantou.Framework.WebPlatform.Tests;

/// <summary>Pathological inputs (deep nesting, many siblings) found by fuzzing: no stack overflow and no quadratic behavior.</summary>
public sealed class HardeningTests
{
    private const int Depth = 100_000;

    [Theory]
    [InlineData("a{")]
    [InlineData("(")]
    [InlineData("[")]
    [InlineData("f(")]
    public void Css_DeeplyNestedBlocks_DoNotOverflowTheStack(string block)
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync("a { b: " + string.Concat(Enumerable.Repeat(block, Depth)));
        sheet.ReplaceSync(string.Concat(Enumerable.Repeat(block, Depth)));

        var element = HtmlDocumentParser.ParseDocument("").CreateElement<HtmlElement>("div");
        element.Style.CssText = "color: " + string.Concat(Enumerable.Repeat(block, Depth));
    }

    [Fact]
    public void Css_BlocksDeeperThanTheLimit_HaveNoContent()
    {
        var values = CssParser.ParseListOfComponentValues(new string('(', CssParser.MaxNestingDepth + 10) + "a" + new string(')', CssParser.MaxNestingDepth + 10) + " b");

        var depth = 0;
        var block = (CssSimpleBlock)values[0];
        while (block.Value is [CssSimpleBlock child])
        {
            block = child;
            depth++;
        }

        Assert.Equal(CssParser.MaxNestingDepth, depth);
        Assert.Empty(block.Value);

        // The tokens after the skipped blocks are parsed
        Assert.Equal("b", ((CssPreservedToken)values[^1]).Token.Value);
    }

    [Theory]
    [InlineData(":is(")]
    [InlineData(":not(")]
    [InlineData(":has(")]
    [InlineData(":nth-child(2n of ")]
    public void Selectors_DeeplyNested_DoNotOverflowTheStack(string pseudoClass)
    {
        var document = HtmlDocumentParser.ParseDocument("<p>a</p>");
        var exception = Record.Exception(() => document.QuerySelectorAll(string.Concat(Enumerable.Repeat(pseudoClass, Depth)) + "p"));
        Assert.True(exception is null or DomException, exception?.ToString());
    }

    [Fact]
    public void Html_DeeplyNestedElements_AreSerializedAndFormatted()
    {
        var document = HtmlDocumentParser.ParseDocument(string.Concat(Enumerable.Repeat("<div>", Depth)));

        Assert.Equal((Depth * 11) + 39, document.DocumentElement!.OuterHtml.Length);
        Assert.Equal(Depth * 11, document.Body!.ToHtml(new HtmlMinifyFormatter()).Length - "<body></body>".Length);

        // The indentation is limited: the output is linear in the size of the input
        var pretty = document.ToHtml(new HtmlPrettyFormatter());
        Assert.InRange(pretty.Length, Depth * 11, Depth * 11 * 30);
    }

    [Fact]
    public void Html_ManyOptions_AreParsed()
    {
        var select = (HtmlSelectElement)HtmlDocumentParser.ParseDocument("<select>" + string.Concat(Enumerable.Repeat("<option>a", Depth))).QuerySelector("select")!;
        Assert.Equal((uint)Depth, select.Options.Length);
        Assert.Equal(0, select.SelectedIndex);
    }

    // The clone of the option into the selectedcontent contains a selected option, which is an option of the select
    private const string SelectedContentCloningASelectedOption = "<select><button><selectedcontent></selectedcontent></button><option>X<span><option selected>Y</option></span></option></select>";

    [Fact]
    public void Html_SelectedContentCloningASelectedOption_Terminates()
    {
        var document = HtmlDocumentParser.ParseDocument(SelectedContentCloningASelectedOption);
        var selectedContent = document.QuerySelector("selectedcontent")!;
        Assert.Equal("X<span><option selected=\"\">Y</option></span>", selectedContent.InnerHtml);

        var select = (HtmlSelectElement)document.QuerySelector("select")!;
        select.InnerHtml = SelectedContentCloningASelectedOption["<select>".Length..^"</select>".Length];
        Assert.Equal("X<span><option selected=\"\">Y</option></span>", select.QuerySelector("selectedcontent")!.InnerHtml);
        Assert.Equal(1u, select.Options.Length);
    }

    [Fact]
    public void Html_SelectedContentCloningASelectedOption_TerminatesWithEventLoop()
    {
        using var scope = new BrowsingContextScope();
        var document = scope.Create().Navigate("https://example.com/", _ => SelectedContentCloningASelectedOption);
        var select = (HtmlSelectElement)document.QuerySelector("select")!;
        select.InnerHtml = SelectedContentCloningASelectedOption["<select>".Length..^"</select>".Length];
        scope.EventLoop.PerformMicrotaskCheckpoint();
        Assert.Equal("X<span><option selected=\"\">Y</option></span>", select.QuerySelector("selectedcontent")!.InnerHtml);
        Assert.Equal(1u, select.Options.Length);
    }

    [Fact]
    public void Html_DeeplyNestedTemplates_AreParsed()
    {
        var document = HtmlDocumentParser.ParseDocument(string.Concat(Enumerable.Repeat("<template>", Depth)));
        var template = (HtmlTemplateElement)document.QuerySelector("template")!;
        Assert.IsType<HtmlTemplateElement>(template.Content.FirstChild);
    }

    [Fact]
    public void HtmlPrettyFormatter_MaxIndentationDepth()
    {
        var document = HtmlDocumentParser.ParseDocument("<div><div><div><p>a</p></div></div></div>");
        var expected = """
            <body>
             <div>
              <div>
              <div>
              <p>a</p>
              </div>
              </div>
             </div>
            </body>
            """;
        Assert.Equal(expected.ReplaceLineEndings("\n"), document.Body!.ToHtml(new HtmlPrettyFormatter { Indentation = " ", MaxIndentationDepth = 2 }));
    }

    [Fact]
    public void CssPrettyFormatter_MaxIndentationDepth()
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync("@media print { a { color: red; & b { color: blue } } }");
        var expected = """
            @media print {
             a {
             color: red;
             & b {
             color: blue;
             }
             }
            }
            """;
        Assert.Equal(expected.ReplaceLineEndings("\n"), sheet.ToCss(new CssPrettyFormatter { Indentation = " ", MaxIndentationDepth = 1 }));
    }

    [Fact]
    public void Select_AppendingOptions_KeepsTheSelectedOption()
    {
        var document = HtmlDocumentParser.ParseDocument("<select><option>a<option selected>b<option>c</select>");
        var select = (HtmlSelectElement)document.QuerySelector("select")!;
        Assert.Equal(1, select.SelectedIndex);

        select.Append(document.CreateElement("option"));
        Assert.Equal(1, select.SelectedIndex);

        // The selected option is no longer selected: the first enabled option is selected when an option is appended
        ((HtmlOptionElement)select.Options[1]!).Selected = false;
        select.Append(document.CreateElement("option"));
        Assert.Equal(0, select.SelectedIndex);

        // An appended selected option becomes the selected option
        var selected = (HtmlOptionElement)document.CreateElement("option");
        selected.Selected = true;
        select.Append(selected);
        Assert.Equal(5, select.SelectedIndex);
        Assert.Single(select.Options.Cast<HtmlOptionElement>(), option => option.Selected);
    }

    [Fact]
    public void GetRootNode_MovedBetweenTheDocumentAndAShadowTree()
    {
        var document = HtmlDocumentParser.ParseDocument("<div id=host></div><p id=a><b>b</b></p>");
        var shadowRoot = document.GetElementById("host")!.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        var p = document.GetElementById("a")!;
        var b = p.FirstChild!;
        Assert.Same(document, b.GetRootNode());

        shadowRoot.MoveBefore(p, null);
        Assert.Same(shadowRoot, b.GetRootNode());
        Assert.Null(document.GetElementById("a"));
        Assert.Same(p, shadowRoot.GetElementById("a"));

        document.Body!.MoveBefore(p, null);
        Assert.Same(document, b.GetRootNode());

        p.Remove();
        Assert.Same(p, b.GetRootNode());
    }
}
