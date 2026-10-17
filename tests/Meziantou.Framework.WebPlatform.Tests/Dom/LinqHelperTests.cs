using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>The LINQ helpers: tree walks, typed queries, queries on node sequences and <c>CreateElement&lt;T&gt;</c>.</summary>
public sealed class LinqHelperTests
{
    private static Document ParseDocument(string html) => HtmlDocumentParser.ParseDocument(html);

    private static string Ids(IEnumerable<Node> nodes) => string.Join(",", nodes.Select(node => node is Element { Id: not "" } element ? element.Id : node.NodeName));

    [Fact]
    public void Descendants_TreeOrder()
    {
        var document = ParseDocument("<div id=root><p id=a>text<span id=b></span></p><p id=c></p></div>");
        var root = document.GetElementById("root")!;

        Assert.Equal("a,#text,b,c", Ids(root.Descendants()));
        Assert.Equal("root,a,#text,b,c", Ids(root.DescendantsAndSelf()));
        Assert.Equal("a,c", Ids(root.Descendants<HtmlParagraphElement>()));
        Assert.Equal("root,a,b,c", Ids(root.DescendantsAndSelf<Element>()));
        Assert.Empty(root.DescendantsAndSelf<HtmlAnchorElement>());

        var leaf = document.GetElementById("c")!;
        Assert.Empty(leaf.Descendants());
        Assert.Equal("c", Ids(leaf.DescendantsAndSelf()));
    }

    [Fact]
    public void Descendants_IsLazy()
    {
        var document = ParseDocument("<div id=root><p id=a></p><p id=b></p></div>");
        var root = document.GetElementById("root")!;

        using var enumerator = root.Descendants().GetEnumerator();
        Assert.True(enumerator.MoveNext());
        Assert.Equal("a", ((Element)enumerator.Current).Id);

        // The iterator walks the live tree: a node appended during the enumeration is returned
        var added = document.CreateElement("p");
        added.Id = "c";
        root.AppendChild(added);
        Assert.True(enumerator.MoveNext());
        Assert.True(enumerator.MoveNext());
        Assert.Equal("c", ((Element)enumerator.Current).Id);
        Assert.False(enumerator.MoveNext());
    }

    [Fact]
    public void Ancestors_NearestFirst()
    {
        var document = ParseDocument("<div id=a><section id=b><p id=c><span id=d></span></p></section></div>");
        var span = document.GetElementById("d")!;

        Assert.Equal("c,b,a,BODY,HTML,#document", Ids(span.Ancestors()));
        Assert.Equal("d,c,b,a,BODY,HTML,#document", Ids(span.AncestorsAndSelf()));
        Assert.Equal("c,b,a,BODY,HTML", Ids(span.Ancestors<Element>()));
        Assert.Equal("d,c,b,a,BODY,HTML", Ids(span.AncestorsAndSelf<Element>()));
        Assert.Same(document.GetElementById("a"), span.Ancestors<HtmlDivElement>().Single());
        Assert.Empty(document.Ancestors());
        Assert.Same(document, document.AncestorsAndSelf().Single());
    }

    [Fact]
    public void Ancestors_DoNotCrossShadowRoots()
    {
        var document = ParseDocument("<div id=host></div>");
        var host = document.GetElementById("host")!;
        var shadowRoot = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        var child = shadowRoot.AppendChild(document.CreateElement("span"));

        Assert.Same(shadowRoot, child.Ancestors().Single());
    }

    [Fact]
    public void QuerySelector_Typed_SkipsOtherTypes()
    {
        var document = ParseDocument("<div class=x id=div></div><a class=x id=link1></a><a id=link2></a><a class=x id=link3></a>");

        Assert.Equal("div", document.QuerySelector(".x")!.Id);
        Assert.Equal("link1", document.QuerySelector<HtmlAnchorElement>(".x")!.Id);
        Assert.Null(document.QuerySelector<HtmlImageElement>(".x"));

        IReadOnlyList<HtmlAnchorElement> links = document.QuerySelectorAll<HtmlAnchorElement>(".x");
        Assert.Equal("link1,link3", Ids(links));

        var selectors = SelectorList.Parse(".x");
        Assert.Equal("link1", document.QuerySelector<HtmlAnchorElement>(selectors)!.Id);
        Assert.Equal("link1,link3", Ids(document.QuerySelectorAll<HtmlAnchorElement>(selectors)));
    }

    [Fact]
    public void QuerySelector_Typed_IdFastPath()
    {
        // The first element with the ID is not of the requested type: the other elements are searched
        var document = ParseDocument("<div id=x></div><a id=x></a>");
        Assert.IsType<HtmlAnchorElement>(document.QuerySelector<HtmlAnchorElement>("#x"));
        Assert.IsType<HtmlDivElement>(document.QuerySelector<Element>("#x"));
    }

    [Fact]
    public void QuerySelector_Typed_ElementAndFragment()
    {
        var document = ParseDocument("<div id=root><a id=a></a><img id=b></div><a id=c></a>");
        var root = document.GetElementById("root")!;

        Assert.Equal("a", Ids(root.QuerySelectorAll<HtmlAnchorElement>("*")));
        Assert.Equal("b", root.QuerySelector<HtmlImageElement>(":scope > *")!.Id);

        var fragment = document.CreateDocumentFragment();
        fragment.AppendChild(root);
        Assert.Equal("a", fragment.QuerySelector<HtmlAnchorElement>("a")!.Id);
        Assert.Equal("b", Ids(fragment.QuerySelectorAll<HtmlImageElement>(SelectorList.Parse("*"))));
    }

    [Fact]
    public void QuerySelector_Typed_InvalidSelector_ThrowsSyntaxError()
    {
        var document = ParseDocument("");
        Assert.Equal("SyntaxError", Assert.Throws<DomException>(() => document.QuerySelector<Element>("a[")).Name);
        Assert.Equal("SyntaxError", Assert.Throws<DomException>(() => document.QuerySelectorAll<Element>("a[")).Name);
    }

    [Fact]
    public void Sequence_QuerySelectorAll_SearchesDescendants()
    {
        var document = ParseDocument("""
            <article id=a1 class=item><a id=l1></a><p class=item id=p1><a id=l2></a></p></article>
            <article id=a2 class=item><a id=l3></a></article>
            """);
        var articles = document.QuerySelectorAll("article");

        // The nodes of the sequence are not part of the result
        Assert.Equal("p1", Ids(articles.QuerySelectorAll(".item")));
        Assert.Equal("l1,l2,l3", Ids(articles.QuerySelectorAll("a")));
        Assert.Equal("l1,l3", Ids(articles.QuerySelectorAll(":scope > a")));
        Assert.Equal("l1", articles.QuerySelector("a")!.Id);
        Assert.Equal("l3", articles.QuerySelector(":scope > a:not(#l1)")!.Id);
        Assert.Null(articles.QuerySelector("img"));

        IReadOnlyList<HtmlAnchorElement> links = articles.QuerySelectorAll<HtmlAnchorElement>(SelectorList.Parse("*"));
        Assert.Equal("l1,l2,l3", Ids(links));
        Assert.Equal("p1", articles.QuerySelector<HtmlParagraphElement>("*")!.Id);

        // HtmlCollection, and LINQ sequences
        Assert.Equal("l1,l2,l3", Ids(document.Body!.Children.QuerySelectorAll("a")));
        Assert.Equal("l3", Ids(articles.Skip(1).QuerySelectorAll<HtmlAnchorElement>("a")));
    }

    [Fact]
    public void Sequence_QuerySelectorAll_RemovesDuplicates()
    {
        var document = ParseDocument("<div id=outer><div id=inner><a id=l1></a></div><a id=l2></a></div>");
        var divs = new Node[] { document.GetElementById("inner")!, document.GetElementById("outer")!, document.CreateTextNode("text"), document };

        Assert.Equal("l1,l2", Ids(divs.QuerySelectorAll("a")));
        Assert.Equal("l1,inner,l2,outer", Ids(divs.QuerySelectorAll("a, div")));
    }

    [Fact]
    public void Sequence_InvalidSelector_ThrowsSyntaxError()
    {
        var nodes = ParseDocument("").Body!.Children;
        Assert.Equal("SyntaxError", Assert.Throws<DomException>(() => nodes.QuerySelectorAll("a[")).Name);
        Assert.Equal("SyntaxError", Assert.Throws<DomException>(() => nodes.QuerySelector<Element>("a[")).Name);
        Assert.Throws<ArgumentNullException>(() => ((IEnumerable<Node>)null!).QuerySelectorAll("a"));
        Assert.Throws<ArgumentNullException>(() => nodes.QuerySelectorAll((string)null!));
    }

    [Fact]
    public void CreateElement_Typed()
    {
        var document = ParseDocument("");

        Assert.Equal("a", document.CreateElement<HtmlAnchorElement>().LocalName);
        Assert.Equal("picture", document.CreateElement<HtmlPictureElement>().LocalName);
        Assert.Equal("source", document.CreateElement<HtmlSourceElement>().LocalName);
        Assert.Equal("h2", document.CreateElement<HtmlHeadingElement>("h2").LocalName);
        Assert.Equal("section", document.CreateElement<HtmlElement>("section").LocalName);

        var heading = Assert.Throws<InvalidOperationException>(() => document.CreateElement<HtmlHeadingElement>());
        Assert.Contains("h1, h2, h3, h4, h5, h6", heading.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => document.CreateElement<HtmlElement>());
        Assert.Throws<InvalidOperationException>(() => document.CreateElement<HtmlUnknownElement>());
        Assert.Throws<ArgumentException>(() => document.CreateElement<HtmlAnchorElement>("div"));
    }

    [Fact]
    public void CreateElement_Typed_XmlDocument_Throws()
    {
        var document = ParseDocument("").Implementation.CreateDocument(@namespace: null, "root");
        Assert.Throws<ArgumentException>(() => document.CreateElement<HtmlAnchorElement>());
    }
}
