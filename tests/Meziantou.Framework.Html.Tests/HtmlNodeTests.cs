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
