using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

public sealed class NodeIteratorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreRemovingSteps_AfterTheRootIsAdopted(bool explicitAdoption)
    {
        var source = HtmlDocumentParser.ParseDocument("<div><i></i><b></b></div>");
        var root = source.QuerySelector("div")!;
        var iterator = source.CreateNodeIterator(root);
        Assert.Same(root, iterator.NextNode());
        var removed = (Element)iterator.NextNode()!;
        Assert.Equal("I", removed.NodeName);

        var destination = HtmlDocumentParser.ParseDocument("");
        if (explicitAdoption)
        {
            destination.AdoptNode(root);
            Assert.Same(destination, root.OwnerDocument);
        }
        else
        {
            destination.Body!.AppendChild(root);
        }

        removed.Remove();
        Assert.Same(root, iterator.ReferenceNode);
        Assert.Equal("B", iterator.NextNode()?.NodeName);
    }

    [Fact]
    public void LiveRanges_UpdatedAfterAnUnaffectedRange()
    {
        // The range of another document is registered first: the mutations do not change it, and the index of the child is
        // computed for the second range only
        var other = HtmlDocumentParser.ParseDocument("<p></p>");
        var otherRange = other.CreateRange();
        otherRange.SetStart(other.Body!, 0);

        var document = HtmlDocumentParser.ParseDocument("<div><i></i><b></b><u></u></div>");
        var div = document.QuerySelector("div")!;
        var range = document.CreateRange();
        range.SetStart(div, 2);
        range.SetEnd(div, 3);

        div.InsertBefore(document.CreateElement("s"), div.FirstChild);
        Assert.Equal(3u, range.StartOffset);
        Assert.Equal(4u, range.EndOffset);

        div.QuerySelector("b")!.Remove();
        Assert.Same(div, range.StartContainer);
        Assert.Equal(2u, range.StartOffset);
        Assert.Equal(3u, range.EndOffset);

        var u = div.QuerySelector("u")!;
        range.SetStart(u, 0);
        u.Remove();
        Assert.Same(div, range.StartContainer);
        Assert.Equal(2u, range.StartOffset);
        Assert.Equal(2u, range.EndOffset);

        Assert.Same(other.Body, otherRange.StartContainer);
        Assert.Equal(0u, otherRange.StartOffset);
    }
}
