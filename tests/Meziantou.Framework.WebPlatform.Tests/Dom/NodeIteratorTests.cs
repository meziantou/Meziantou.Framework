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
}
