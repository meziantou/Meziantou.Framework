using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

public sealed class MutationObserverTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObservedNode_AdoptedIntoADocumentWithoutObservers_QueuesRecords(bool explicitAdoption)
    {
        var source = HtmlDocumentParser.ParseDocument("<div></div>");
        var root = source.QuerySelector("div")!;
        var observer = new MutationObserver((_, _) => { });
        observer.Observe(root, new MutationObserverInit { ChildList = true, Subtree = true, Attributes = true });

        var destination = HtmlDocumentParser.ParseDocument("");
        if (explicitAdoption)
        {
            destination.AdoptNode(root);
        }
        else
        {
            destination.Body!.AppendChild(root);
        }

        var child = destination.CreateElement("p");
        root.AppendChild(child);
        child.SetAttribute("id", "a");
        Assert.Equal(["childList", "attributes"], observer.TakeRecords().Select(record => record.Type));
    }

    [Fact]
    public void ObserverOfAnotherDocument_DoesNotObserveTheDocument()
    {
        var observed = HtmlDocumentParser.ParseDocument("<div></div>");
        var observer = new MutationObserver((_, _) => { });
        observer.Observe(observed, new MutationObserverInit { ChildList = true, Subtree = true });

        var document = HtmlDocumentParser.ParseDocument("<div></div>");
        document.Body!.AppendChild(document.CreateElement("p"));
        Assert.Empty(observer.TakeRecords());

        observed.Body!.AppendChild(observed.CreateElement("p"));
        Assert.Single(observer.TakeRecords());
    }
}
