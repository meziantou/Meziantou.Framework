using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

public sealed class SanitizerTests
{
    private static Element CreateContainer()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><div></div>");
        return document.QuerySelector("div")!;
    }

    [Theory]
    [InlineData("<p>Hello<script>alert(1)</script></p>", "<p>Hello</p>")]
    [InlineData("<img src=x onerror=alert(1)>", "")]
    [InlineData("<a href='javascript:alert(1)' title=t>x</a>", "<a title=\"t\">x</a>")]
    [InlineData("<a href='https://example.com'>x</a>", "<a href=\"https://example.com\">x</a>")]
    [InlineData("<div data-x=1 class=c id=i>x</div>", "<div>x</div>")]
    [InlineData("a <!-- comment --> b", "a  b")]
    [InlineData("<iframe></iframe><b>b</b>", "<b>b</b>")]
    [InlineData("<svg><use href='#a'/><circle r=1 onclick=x /></svg>", "<svg><circle r=\"1\"></circle></svg>")]
    public void SetHtml(string html, string expected)
    {
        var container = CreateContainer();
        container.SetHtml(html);
        Assert.Equal(expected, container.InnerHtml);
    }

    [Fact]
    public void SetHtmlUnsafeWithoutSanitizerKeepsEverything()
    {
        var container = CreateContainer();
        container.SetHtmlUnsafe("<p onclick=x>a<script>b</script><!--c--></p>");
        Assert.Equal("<p onclick=\"x\">a<script>b</script><!--c--></p>", container.InnerHtml);
    }

    [Fact]
    public void SetHtmlUnsafeWithSanitizer()
    {
        var container = CreateContainer();
        container.SetHtmlUnsafe("<p onclick=x>a<script>b</script><em>c</em></p>", new SetHtmlUnsafeOptions
        {
            Sanitizer = new SanitizerConfig { RemoveElements = ["em"], RemoveAttributes = ["onclick"] },
        });

        Assert.Equal("<p>a<script>b</script></p>", container.InnerHtml);
    }

    [Fact]
    public void SetHtmlRemovesUnsafeContentFromCustomConfiguration()
    {
        var container = CreateContainer();
        container.SetHtml("<p onclick=x>a<script>b</script></p>", new SetHtmlOptions { Sanitizer = new SanitizerConfig() });
        Assert.Equal("<p>a</p>", container.InnerHtml);
    }

    [Fact]
    public void ReplaceWithChildren()
    {
        var container = CreateContainer();
        container.SetHtml("<p>a<b>b</b>c</p>", new SetHtmlOptions { Sanitizer = new SanitizerConfig { ReplaceWithChildrenElements = ["b"] } });
        Assert.Equal("<p>abc</p>", container.InnerHtml);
        Assert.Single(container.FirstChild!.ChildNodes);
    }

    [Theory]
    [InlineData("<b><div>Text</b>", "div", "<b>Text</b><b></b>")]
    [InlineData("<b>1<p>foo</b>bar", "b", "1<p>foobar</p>")]
    [InlineData("<b><em><div>Text</b>after", "div", "<b><em>Text</em></b><em><b></b>after</em>")]
    [InlineData("<div><template><b>Hello</b></template>after</div>", "template", "<div>after</div>")]
    public void ReplaceWithChildrenWhileParsing(string html, string element, string expected)
    {
        var container = CreateContainer();
        container.SetHtml(html, new SetHtmlOptions { Sanitizer = new SanitizerConfig { ReplaceWithChildrenElements = [element] } });
        Assert.Equal(expected, container.InnerHtml);
    }

    [Fact]
    public void RemoveElementWhileParsing()
    {
        var container = CreateContainer();
        container.SetHtml("<b><div>Text</b><p>x</p>", new SetHtmlOptions { Sanitizer = new SanitizerConfig { RemoveElements = ["div"] } });
        Assert.Equal("<b></b>", container.InnerHtml);
    }

    [Fact]
    public void DeclarativeShadowRootWithRemovedTemplate()
    {
        var container = CreateContainer();
        container.SetHtml("<div><template shadowrootmode=open><p>x</p></template></div>");
        var host = (Element)container.FirstChild!;
        Assert.Null(host.ShadowRoot);
        Assert.Null(host.FirstChild);
    }

    [Fact]
    public void DeclarativeShadowRootWithRemovedAttributes()
    {
        var container = CreateContainer();
        container.SetHtmlUnsafe("<div><template shadowrootmode=open shadowrootdelegatesfocus><p>x</p></template></div>", new SetHtmlUnsafeOptions
        {
            Sanitizer = new SanitizerConfig { RemoveAttributes = ["shadowrootdelegatesfocus"] },
        });

        var shadowRoot = ((Element)container.FirstChild!).ShadowRoot;
        Assert.NotNull(shadowRoot);
        Assert.False(shadowRoot.DelegatesFocus);
        Assert.Equal("<p>x</p>", shadowRoot.InnerHtml);
    }

    [Fact]
    public void DeclarativeShadowRootWithRemovedModeAttribute()
    {
        var container = CreateContainer();
        container.SetHtmlUnsafe("<div><template shadowrootmode=open><p>x</p></template></div>", new SetHtmlUnsafeOptions
        {
            Sanitizer = new SanitizerConfig { RemoveAttributes = ["shadowrootmode"] },
        });

        var host = (Element)container.FirstChild!;
        Assert.Null(host.ShadowRoot);
        Assert.Equal("<template><p>x</p></template>", host.InnerHtml);
    }

    [Fact]
    public void ParseHtml()
    {
        var document = Document.ParseHtml("<p>x<script>y</script><style>z</style></p>");
        Assert.Equal("<p>x</p>", document.Body!.InnerHtml);
    }

    [Fact]
    public void Modifiers()
    {
        var sanitizer = new Sanitizer(new SanitizerConfig { Elements = ["p"], Attributes = ["title"] });
        Assert.True(sanitizer.AllowElement("b"));
        Assert.False(sanitizer.AllowElement("b"));
        Assert.True(sanitizer.RemoveElement("p"));
        Assert.True(sanitizer.ReplaceElementWithChildren("i"));
        Assert.False(sanitizer.ReplaceElementWithChildren("html"));
        Assert.True(sanitizer.AllowAttribute("lang"));
        Assert.True(sanitizer.RemoveAttribute("title"));
        // The constructor uses the permissive defaults: comments and data attributes are allowed
        Assert.False(sanitizer.SetComments(allow: true));
        Assert.True(sanitizer.SetComments(allow: false));
        Assert.False(sanitizer.SetDataAttributes(allow: true));
        Assert.True(sanitizer.SetDataAttributes(allow: false));

        var config = sanitizer.Get();
        Assert.Equal(["b"], config.Elements!.Select(e => e.ElementNamespaceWithAttributes!.Name));
        Assert.Equal(["i"], config.ReplaceWithChildrenElements!.Select(e => e.ElementNamespace!.Name));
        Assert.Equal(["lang"], config.Attributes!.Select(a => a.AttributeNamespace!.Name));
        Assert.Null(config.RemoveElements);
        Assert.False(config.Comments);
        Assert.False(config.DataAttributes);
    }

    [Fact]
    public void DefaultConfiguration()
    {
        var config = new Sanitizer().Get();
        Assert.Contains(config.Elements!, e => e.ElementNamespaceWithAttributes!.Name == "div");
        Assert.DoesNotContain(config.Elements!, e => e.ElementNamespaceWithAttributes!.Name == "script");
        Assert.Contains(config.Attributes!, a => a.AttributeNamespace!.Name == "title");
        Assert.Empty(config.ProcessingInstructions!);
        Assert.False(config.Comments);
        Assert.False(config.JavaScriptUrls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvalidConfigurationThrows(bool bothLists)
    {
        var config = bothLists
            ? new SanitizerConfig { Elements = ["p"], RemoveElements = ["b"] }
            : new SanitizerConfig { Elements = ["p", "p"] };
        Assert.Throws<TypeErrorException>(() => new Sanitizer(config));
    }

    [Fact]
    public void SanitizerIsNotModifiedBySafeMethods()
    {
        var sanitizer = new Sanitizer(new SanitizerConfig());
        var before = sanitizer.Get();
        CreateContainer().SetHtml("<script></script>", new SetHtmlOptions { Sanitizer = sanitizer });
        var after = sanitizer.Get();
        Assert.Equal(before.RemoveElements!.Count, after.RemoveElements!.Count);
    }

    [Fact]
    public void Documentation_SanitizeHtml()
    {
        #region WebPlatform_SanitizeHtml
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><div id=comment></div>");
        var comment = document.GetElementById("comment")!;

        // SetHtml removes what can run scripts: script elements, event handlers, javascript: URLs...
        comment.SetHtml("""<p onclick="steal()">Nice post! <a href="javascript:steal()">Link</a><script>steal()</script></p>""");
        Assert.Equal("<p>Nice post! <a>Link</a></p>", comment.InnerHtml);

        // A configuration restricts the elements and the attributes further
        comment.SetHtml("<h1>Title</h1><p title=t>Some <b>bold</b> and <i>italic</i> text</p>", new SetHtmlOptions
        {
            Sanitizer = new SanitizerConfig { Elements = ["p", "b"], Attributes = [] },
        });
        Assert.Equal("<p>Some <b>bold</b> and  text</p>", comment.InnerHtml);

        // Parse a whole document with the same rules
        var page = Document.ParseHtml("<title>Post</title><p onclick=steal()>Hello</p>");
        Assert.Equal("<p>Hello</p>", page.Body!.InnerHtml);
        #endregion
    }
}
