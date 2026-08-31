using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace Meziantou.Framework.Html.Tests;

public class HtmlNodeTests
{
    [Fact]
    public void HtmlNode_InnerText()
    {
        var doc = new HtmlDocument();
        doc.AppendChild(doc.CreateText("abc"));
        Assert.Equal("abc", doc.InnerText);
    }

    [Fact]
    public void HtmlNode_InnerText_CombineValues()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("abc<p>def</p>");
        Assert.Equal("abcdef", doc.InnerText);
    }

    [Fact]
    public void HtmlNode_CachedValuesAreRecomputedAfterADescendantChanges()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("<div><p><span>a</span></p></div>");
        Assert.Equal("<div><p><span>a</span></p></div>", doc.InnerHtml);
        Assert.Equal("a", doc.InnerText);

        doc.SelectSingleNode("//span")!.InnerText = "b";

        Assert.Equal("<div><p><span>b</span></p></div>", doc.InnerHtml);
        Assert.Equal("b", doc.InnerText);
    }

    [Fact]
    public void HtmlNode_CachedValuesAreRecomputedAfterADescendantIsRemoved()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("<div><p>a</p><p>b</p></div>");
        Assert.Equal("<div><p>a</p><p>b</p></div>", doc.InnerHtml);

        Assert.True(doc.SelectSingleNode("//p")!.Remove());

        Assert.Equal("<div><p>b</p></div>", doc.InnerHtml);
    }

    [Fact]
    public void HtmlNode_CachedValuesAreRecomputedAfterAnAttributeChanges()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("<div><p class='a'>x</p></div>");
        Assert.Equal("<div><p class='a'>x</p></div>", doc.InnerHtml);

        doc.SelectSingleNode("//p")!.Attributes[0].Value = "b";

        Assert.Equal("<div><p class='b'>x</p></div>", doc.InnerHtml);
    }

    // A node that has been removed no longer shares the version its descendants report their changes against,
    // so it must not keep serving what it computed before they changed.
    [Fact]
    public void HtmlNode_CachedValuesAreRecomputedOnADetachedSubtree()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("<div><p><span>a</span></p></div>");

        var p = doc.SelectSingleNode("//p")!;
        Assert.True(p.Remove());
        Assert.Equal("<p><span>a</span></p>", p.OuterHtml);

        p.SelectSingleNode("span")!.InnerText = "b";

        Assert.Equal("<p><span>b</span></p>", p.OuterHtml);
    }

    // Every change used to clear the cached serializations of each ancestor of the changed node. Parsing adds
    // one node per level of nesting, so loading a deeply nested document was quadratic in its depth: this input
    // took about 11 seconds. The budget is far below that and far above the tenth of a second it now needs.
    [Fact]
    public void HtmlNode_LoadHtml_DeeplyNestedDocumentIsNotQuadratic()
    {
        const int Depth = 20_000;
        var html = string.Concat(Enumerable.Repeat("<div>", Depth)) + "x" + string.Concat(Enumerable.Repeat("</div>", Depth));

        var stopwatch = Stopwatch.StartNew();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        stopwatch.Stop();

        Assert.Equal(html, doc.InnerHtml);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Loading {Depth} nested elements took {stopwatch.Elapsed}");
    }

    [Fact]
    public void HtmlNode_ParentElement_Null()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("<p>def</p>");
        var node = doc.SelectSingleNode("/p");
        Assert.NotNull(node);
        Assert.Null(node!.ParentElement);
    }

    [Fact]
    public void HtmlNode_ParentElement()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("<p>def</p>");
        var node = doc.SelectSingleNode("/p/node()");
        Assert.NotNull(node);
        Assert.Equal("p", node!.ParentElement!.Name);
    }

    // Detaching a node collects the namespaces declared by its ancestors. That walk used to recurse once per
    // ancestor, so removing a node from a deeply nested document overflowed the stack, which cannot be caught
    // and takes the whole process down. The tests below run on a thread with the stack size a server gives a
    // request, not the much larger stack of the test runner's main thread.

    [Fact]
    public void HtmlNode_Remove_DeeplyNestedDocument()
    {
        RunWithBoundedStack(() =>
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(Nest("<div>", "<span>x</span>", "</div>", DeepNestingLevels));

            var node = doc.SelectSingleNode("//span");
            Assert.NotNull(node);
            Assert.True(node!.Remove());
        });
    }

    [Fact]
    public void HtmlNode_RemoveAttribute_DeeplyNestedDocument()
    {
        RunWithBoundedStack(() =>
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(Nest("<div>", "<span class='x'>y</span>", "</div>", DeepNestingLevels));

            var node = doc.SelectSingleNode("//span");
            Assert.NotNull(node);
            node!.Attributes.RemoveAt(0);
            Assert.Empty(node.Attributes);
        });
    }

    [Fact]
    public void HtmlNode_GetAllNamespaces_DeeplyNestedDocument()
    {
        RunWithBoundedStack(() =>
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(Nest("<div>", "<span xmlns:x='urn:sample'>y</span>", "</div>", DeepNestingLevels));

            var node = doc.SelectSingleNode("//span");
            Assert.NotNull(node);
            Assert.Equal("urn:sample", Assert.Contains("x", node!.GetAllNamespaces()));
        });
    }

    [Fact]
    public void HtmlNode_GetNamespaceOfPrefix_DeeplyNestedDocument()
    {
        RunWithBoundedStack(() =>
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(Nest("<div xmlns:x='urn:sample'>", "<span>y</span>", "</div>", DeepNestingLevels));

            var node = doc.SelectSingleNode("//span");
            Assert.NotNull(node);
            Assert.Equal("urn:sample", node!.GetNamespaceOfPrefix("x"));
            Assert.Equal("x", node.GetPrefixOfNamespace("urn:sample"));
        });
    }

    private const int DeepNestingLevels = 8_000;

    private static string Nest(string open, string content, string close, int levels)
        => string.Concat(Enumerable.Repeat(open, levels)) + content + string.Concat(Enumerable.Repeat(close, levels));

    // 1 MB is the stack an ASP.NET Core request runs on; the test runner's main thread has several times more,
    // which is enough to hide a per-ancestor recursion at these depths.
    private static void RunWithBoundedStack(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        }, maxStackSize: 1024 * 1024);

        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
