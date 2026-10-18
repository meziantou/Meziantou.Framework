using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Css;

/// <summary>The public selector object model (<see cref="SelectorList"/> and the selectors it contains).</summary>
public sealed class SelectorObjectModelTests
{
    private static Document ParseDocument(string html) => HtmlDocumentParser.ParseDocument(html);

    [Theory]
    [InlineData("div[")]
    [InlineData("")]
    [InlineData("a,")]
    [InlineData("svg|rect")] // Namespace prefixes are not declared, as in querySelector
    [InlineData("::before::after")]
    public void Parse_Invalid_ThrowsSyntaxError(string selectors)
    {
        var exception = Assert.Throws<DomException>(() => SelectorList.Parse(selectors));
        Assert.Equal("SyntaxError", exception.Name);
        Assert.False(SelectorList.TryParse(selectors, out var result));
        Assert.Null(result);
    }

    [Fact]
    public void Parse_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SelectorList.Parse(null!));
        Assert.Throws<ArgumentNullException>(() => SelectorList.TryParse(null!, out _));
    }

    [Fact]
    public void TryParse_Valid()
    {
        Assert.True(SelectorList.TryParse("a, b", out var result));
        Assert.Equal(2, result.Selectors.Length);

        Assert.Equal(NamespaceConstraint.Any, Assert.IsType<TypeSelector>(SelectorList.Parse("*|rect").Selectors[0].Compounds[0].Selectors[0]).Namespace);
        Assert.Equal(NamespaceConstraint.None, Assert.IsType<TypeSelector>(SelectorList.Parse("|rect").Selectors[0].Compounds[0].Selectors[0]).Namespace);
    }

    [Fact]
    public void Model()
    {
        var list = SelectorList.Parse("div.a > #b + [href^=\"x\" i]::before");
        var complex = Assert.Single(list.Selectors);
        Assert.Null(complex.RelativeCombinator);
        Assert.Equal([Combinator.Child, Combinator.NextSibling], complex.Combinators);
        Assert.Equal(3, complex.Compounds.Length);

        var compound = complex.Compounds[0];
        Assert.False(compound.HasPseudoElement);
        Assert.Equal(2, compound.Selectors.Length);
        var type = Assert.IsType<TypeSelector>(compound.Selectors[0]);
        Assert.Equal("div", type.LocalName);
        Assert.False(type.IsUniversal);
        Assert.Null(type.Prefix);
        Assert.Equal(NamespaceConstraint.Any, type.Namespace);
        Assert.Equal("a", Assert.IsType<ClassSelector>(compound.Selectors[1]).ClassName);

        Assert.Equal("b", Assert.IsType<IdSelector>(Assert.Single(complex.Compounds[1].Selectors)).Id);

        compound = complex.Compounds[2];
        Assert.True(compound.HasPseudoElement);
        Assert.True(complex.HasPseudoElement);
        var attribute = Assert.IsType<AttributeSelector>(compound.Selectors[0]);
        Assert.Equal("href", attribute.Name);
        Assert.Equal(AttributeOperator.Prefix, attribute.Operator);
        Assert.Equal("x", attribute.Value);
        Assert.Equal(AttributeCaseSensitivity.Insensitive, attribute.CaseSensitivity);
        Assert.Equal(NamespaceConstraintKind.None, attribute.Namespace.Kind);
        var pseudoElement = Assert.IsType<PseudoElementSelector>(compound.Selectors[1]);
        Assert.Equal("before", pseudoElement.Name);
        Assert.False(pseudoElement.IsFunction);

        Assert.Equal(new Specificity(1, 2, 2), complex.Specificity);
    }

    [Fact]
    public void Model_RelativeAndUniversal()
    {
        var list = SelectorList.Parse("*:has(> .a, ~ .b)");
        var compound = Assert.Single(Assert.Single(list.Selectors).Compounds);
        Assert.True(Assert.IsType<TypeSelector>(compound.Selectors[0]).IsUniversal);
        var has = Assert.IsType<PseudoClassSelector>(compound.Selectors[1]);
        Assert.Equal(PseudoClass.Has, has.Kind);
        Assert.Equal("has", has.Name);
        Assert.Equal([Combinator.Child, Combinator.SubsequentSibling], has.Argument!.Selectors.Select(s => s.RelativeCombinator!.Value));
        Assert.Equal("> .a, ~ .b", has.Argument.ToString());
    }

    [Fact]
    public void Model_PseudoClassArguments()
    {
        var nthChild = GetSingleSimpleSelector<PseudoClassSelector>(":nth-child(2n+1 of .x, .y)");
        Assert.Equal(PseudoClass.NthChild, nthChild.Kind);
        Assert.Equal("nth-child", nthChild.Name);
        Assert.Equal(new AnB(2, 1), nthChild.Nth);
        Assert.Equal("2n+1", nthChild.Nth.ToString());
        Assert.True(nthChild.Nth.Matches(3));
        Assert.False(nthChild.Nth.Matches(2));
        Assert.Equal(".x, .y", nthChild.Argument!.ToString());

        var nthOfType = GetSingleSimpleSelector<PseudoClassSelector>(":nth-of-type(even)");
        Assert.Equal(new AnB(2, 0), nthOfType.Nth);
        Assert.Null(nthOfType.Argument);

        Assert.Equal(["en", "fr-*"], GetSingleSimpleSelector<PseudoClassSelector>(":lang(en, \"fr-*\")").Strings);
        Assert.Equal(["rtl"], GetSingleSimpleSelector<PseudoClassSelector>(":dir(RTL)").Strings);
        Assert.Equal([1, 3], GetSingleSimpleSelector<PseudoClassSelector>(":heading(1, 3)").Levels);

        var not = GetSingleSimpleSelector<PseudoClassSelector>(":NOT(.a, #b)");
        Assert.Equal(PseudoClass.Not, not.Kind);
        Assert.Equal("not", not.Name);
        Assert.Equal(2, not.Argument!.Selectors.Length);

        var hover = GetSingleSimpleSelector<PseudoClassSelector>(":hover");
        Assert.Equal(PseudoClass.Hover, hover.Kind);
        Assert.Null(hover.Argument);
        Assert.Empty(hover.Strings);
    }

    [Fact]
    public void Model_PseudoElementArguments()
    {
        var slotted = GetSingleSimpleSelector<PseudoElementSelector>("::slotted(span.a)");
        Assert.True(slotted.IsFunction);
        Assert.Equal("slotted", slotted.Name);
        Assert.Equal("span.a", slotted.Argument!.ToString());

        var part = GetSingleSimpleSelector<PseudoElementSelector>("::part(foo bar)");
        Assert.Equal("part", part.Name);
        Assert.Null(part.Argument);
        Assert.Equal("foo bar", part.ArgumentText);
        Assert.Equal(["foo", "bar"], part.PartNames);
    }

    [Fact]
    public void CollectClassNames()
    {
        // The class names used by a style sheet, including in the arguments of the pseudo-classes and pseudo-elements.
        // The escapes are resolved and the values of the attribute selectors are not class names.
        var list = SelectorList.Parse(@".a:is(.b, :not(.c)) :has(> .d), .e\.f, [class=""g.h""], ::slotted(.i), :nth-child(2 of .j)");
        var classNames = new SortedSet<string>(StringComparer.Ordinal);
        Collect(list);
        Assert.Equal(["a", "b", "c", "d", "e.f", "i", "j"], classNames);

        void Collect(SelectorList selectors)
        {
            foreach (var complex in selectors.Selectors)
            {
                foreach (var compound in complex.Compounds)
                {
                    foreach (var simple in compound.Selectors)
                    {
                        switch (simple)
                        {
                            case ClassSelector classSelector:
                                classNames.Add(classSelector.ClassName);
                                break;
                            case PseudoClassSelector { Argument: { } argument }:
                                Collect(argument);
                                break;
                            case PseudoElementSelector { Argument: { } argument }:
                                Collect(argument);
                                break;
                        }
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData("*", 0, 0, 0)]
    [InlineData("li", 0, 0, 1)]
    [InlineData("ul li", 0, 0, 2)]
    [InlineData("#a.b[c]:hover::before", 1, 3, 1)]
    [InlineData(":where(#a) .b", 0, 1, 0)]
    [InlineData(":is(#a, .b) span", 1, 0, 1)]
    [InlineData(":nth-child(2 of #a)", 1, 1, 0)]
    public void Specificity(string selector, int a, int b, int c)
    {
        var complex = Assert.Single(SelectorList.Parse(selector).Selectors);
        Assert.Equal(new Specificity(a, b, c), complex.Specificity);
        Assert.Equal($"({a}, {b}, {c})", complex.Specificity.ToString());
    }

    [Fact]
    public void Specificity_Comparison()
    {
        var list = SelectorList.Parse(".a .b, #c, div");
        Assert.Equal(new Specificity(1, 0, 0), list.MaxSpecificity);
        Assert.True(list.Selectors[0].Specificity < list.Selectors[1].Specificity);
        Assert.True(list.Selectors[0].Specificity > list.Selectors[2].Specificity);
    }

    [Fact]
    public void Serialization()
    {
        var list = SelectorList.Parse("A , b>C.x, [title='a b'], :IS(p) ~ *, *.y");
        Assert.Equal("A, b > C.x, [title=\"a b\"], :is(p) ~ *, .y", list.ToString());
        Assert.Equal("b > C.x", list.Selectors[1].ToString());
        Assert.Equal("C.x", list.Selectors[1].Compounds[1].ToString());
        Assert.Equal(".x", list.Selectors[1].Compounds[1].Selectors[1].ToString());
        Assert.Equal("[title=\"a b\"]", list.Selectors[2].Compounds[0].Selectors[0].ToString());
    }

    [Fact]
    public void Parse_UsesTheSharedCache()
    {
        Assert.Same(SelectorList.Parse(".cached-selector"), SelectorList.Parse(".cached-selector"));
    }

    [Fact]
    public void CssStyleRule_Selectors()
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync("@namespace svg url(http://www.w3.org/2000/svg); svg|rect.a, .b { color: red } .parent { & > .child { color: blue } }");
        var rule = Assert.IsType<CssStyleRule>(sheet.CssRules[1]);
        Assert.Equal(2, rule.Selectors.Selectors.Length);
        var type = Assert.IsType<TypeSelector>(rule.Selectors.Selectors[0].Compounds[0].Selectors[0]);
        Assert.Equal("rect", type.LocalName);
        Assert.Equal("svg", type.Prefix);
        Assert.Equal(NamespaceConstraint.Specific("http://www.w3.org/2000/svg"), type.Namespace);
        Assert.True(type.Namespace.Matches("http://www.w3.org/2000/svg"));
        Assert.False(type.Namespace.Matches(null));
        Assert.Equal(rule.SelectorText, rule.Selectors.ToString());

        rule.SelectorText = ".c";
        Assert.Equal("c", Assert.IsType<ClassSelector>(Assert.Single(Assert.Single(Assert.Single(rule.Selectors.Selectors).Compounds).Selectors)).ClassName);

        rule.SelectorText = "invalid[";
        Assert.Equal(".c", rule.Selectors.ToString());

        var nested = Assert.IsType<CssStyleRule>(Assert.IsType<CssStyleRule>(sheet.CssRules[2]).CssRules[0]);
        var complex = Assert.Single(nested.Selectors.Selectors);
        Assert.IsType<NestingSelector>(Assert.Single(complex.Compounds[0].Selectors));
        Assert.Equal([Combinator.Child], complex.Combinators);
    }

    [Fact]
    public void CssScopeRule_Selectors()
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync("@scope (.a) to (> .b) { div { color: red } } @scope { p { color: red } }");
        var rule = Assert.IsType<CssScopeRule>(sheet.CssRules[0]);
        Assert.Equal(".a", rule.StartSelectors!.ToString());
        Assert.Equal("> .b", rule.EndSelectors!.ToString());

        rule = Assert.IsType<CssScopeRule>(sheet.CssRules[1]);
        Assert.Null(rule.StartSelectors);
        Assert.Null(rule.EndSelectors);
    }

    [Fact]
    public void Matching()
    {
        var document = ParseDocument("<!DOCTYPE html><div id=a class=x><ul id=b><li id=c class=x>1<li id=d>2<li id=e class=x>3</ul></div>");
        var a = document.GetElementById("a")!;
        var b = document.GetElementById("b")!;
        var c = document.GetElementById("c")!;
        var d = document.GetElementById("d")!;

        var selector = SelectorList.Parse(".x");
        Assert.True(selector.Matches(c));
        Assert.False(selector.Matches(d));
        Assert.True(c.Matches(selector));
        Assert.False(d.Matches(selector));
        Assert.Same(a, d.Closest(selector));
        Assert.Same(c, c.Closest(selector));
        Assert.Null(c.Closest(SelectorList.Parse("span")));

        Assert.Equal(["a", "c", "e"], document.QuerySelectorAll(selector).Select(n => ((Element)n).Id));
        Assert.Equal(["c", "e"], b.QuerySelectorAll(selector).Select(n => ((Element)n).Id));
        Assert.Same(a, document.QuerySelector(selector));
        Assert.Same(c, b.QuerySelector(selector));
        Assert.Null(b.QuerySelector(SelectorList.Parse("div")));

        // The ID fast path of querySelector
        Assert.Same(c, document.QuerySelector(SelectorList.Parse("li#c")));
        Assert.Null(document.QuerySelector(SelectorList.Parse("div#c")));

        var fragment = document.CreateDocumentFragment();
        fragment.Append(a.CloneNode(true));
        Assert.Equal(3, fragment.QuerySelectorAll(selector).Count);
        Assert.Equal("a", fragment.QuerySelector(selector)!.Id);

        Assert.Equal(2, ((IParentNode)b).QuerySelectorAll(selector).Count);
    }

    [Fact]
    public void Matching_ScopeIsTheElement()
    {
        var document = ParseDocument("<!DOCTYPE html><div id=a><div id=b><div id=c></div></div></div>");
        var a = document.GetElementById("a")!;
        var b = document.GetElementById("b")!;
        var c = document.GetElementById("c")!;

        var scope = SelectorList.Parse(":scope");
        Assert.True(scope.Matches(c));
        Assert.True(c.Matches(scope));
        Assert.Same(c, c.Closest(scope));
        Assert.Equal(["b"], a.QuerySelectorAll(SelectorList.Parse(":scope > div")).Select(n => ((Element)n).Id));
        Assert.Same(b, c.Closest(SelectorList.Parse(":has(> :scope)")));
    }

    [Fact]
    public void Matching_Null_Throws()
    {
        var document = ParseDocument("<!DOCTYPE html><div></div>");
        var div = document.QuerySelector("div")!;
        Assert.Throws<ArgumentNullException>(() => SelectorList.Parse("div").Matches(null!));
        Assert.Throws<ArgumentNullException>(() => div.Matches((SelectorList)null!));
        Assert.Throws<ArgumentNullException>(() => div.Closest((SelectorList)null!));
        Assert.Throws<ArgumentNullException>(() => document.QuerySelector((SelectorList)null!));
        Assert.Throws<ArgumentNullException>(() => document.QuerySelectorAll((SelectorList)null!));
    }

    private static T GetSingleSimpleSelector<T>(string selector)
        where T : SimpleSelector
    {
        var list = SelectorList.Parse(selector);
        return Assert.IsType<T>(Assert.Single(Assert.Single(Assert.Single(list.Selectors).Compounds).Selectors));
    }
}
