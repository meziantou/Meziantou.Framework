using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>The named properties of a document (https://html.spec.whatwg.org/multipage/dom.html#dom-document-nameditem).</summary>
public sealed class DocumentNamedItemTests
{
    [Fact]
    public void NamedElements()
    {
        var document = HtmlDocumentParser.ParseDocument("""
            <!DOCTYPE html>
            <form name=f id=fid></form><embed name=e><img name=i id=iid><img id=noname><object name=o id=oid></object>
            <div name=d id=did></div><a name=a></a>
            """);

        Assert.Same(document.QuerySelector("form"), document["f"]);
        Assert.Same(document.QuerySelector("embed"), document["e"]);
        Assert.Same(document.QuerySelector("img"), document["i"]);
        Assert.Same(document.QuerySelector("object"), document["o"]);

        // The id of an object element, and of an img element that also has a name
        Assert.Same(document.QuerySelector("object"), document["oid"]);
        Assert.Same(document.QuerySelector("img"), document["iid"]);
        Assert.Null(document["noname"]);
        Assert.Null(document["fid"]);

        // The other elements are not named elements
        Assert.Null(document["d"]);
        Assert.Null(document["did"]);
        Assert.Null(document["a"]);
        Assert.Null(document[""]);
        Assert.Equal(["f", "e", "iid", "i", "oid", "o"], document.GetSupportedPropertyNames());
    }

    [Fact]
    public void SeveralElements_ReturnsALiveCollectionInTreeOrder()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><img name=x class=a><object id=x class=b></object>");
        var collection = Assert.IsType<HtmlCollection>(document["x"]);
        Assert.Equal(["a", "b"], collection.Select(element => element.ClassName));
        Assert.Same(collection, document["x"]);

        var form = document.CreateElement("form");
        form.SetAttribute("name", "x");
        document.Body!.Prepend(form);
        Assert.Equal(["form", "img", "object"], collection.Select(element => element.LocalName));

        form.Remove();
        document.QuerySelector("img")!.Remove();
        Assert.Same(document.QuerySelector("object"), document["x"]);
        Assert.Equal(["x"], document.GetSupportedPropertyNames());
    }

    [Fact]
    public void AttributeChanges()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><img id=image><object></object>");
        var image = document.QuerySelector("img")!;
        var obj = document.QuerySelector("object")!;
        Assert.Null(document["image"]);

        // An img element is named by its id once it has a name
        image.SetAttribute("name", "n");
        Assert.Same(image, document["image"]);
        Assert.Same(image, document["n"]);

        image.SetAttribute("name", "renamed");
        Assert.Null(document["n"]);
        Assert.Same(image, document["renamed"]);

        image.SetAttribute("id", "other");
        Assert.Null(document["image"]);
        Assert.Same(image, document["other"]);

        image.SetAttribute("name", "");
        Assert.Null(document["other"]);
        Assert.Null(document["renamed"]);

        obj.Id = "o";
        Assert.Same(obj, document["o"]);
        obj.SetAttribute("name", "o");
        Assert.Same(obj, document["o"]);
        obj.RemoveAttribute("id");
        Assert.Same(obj, document["o"]);
        obj.RemoveAttribute("name");
        Assert.Null(document["o"]);
        Assert.Empty(document.GetSupportedPropertyNames());
    }

    [Fact]
    public void OnlyTheElementsOfTheDocumentTree()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><div id=host></div><div id=container><form name=f></form></div>");
        var form = document.QuerySelector("form")!;
        var container = document.GetElementById("container")!;
        Assert.Same(form, document["f"]);

        // A subtree that is removed, then inserted again
        container.Remove();
        Assert.Null(document["f"]);
        document.Body!.AppendChild(container);
        Assert.Same(form, document["f"]);

        // The elements of a shadow tree are not in the document tree
        var shadowRoot = document.GetElementById("host")!.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadowRoot.AppendChild(form);
        Assert.Null(document["f"]);
        form.SetAttribute("name", "g");
        Assert.Null(document["g"]);
        container.AppendChild(form);
        Assert.Same(form, document["g"]);

        // An adopted element is a named element of its new document once it is inserted
        var other = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><body>");
        Assert.Null(other["g"]);
        other.AdoptNode(form);
        Assert.Null(document["g"]);
        Assert.Null(other["g"]);
        other.Body!.AppendChild(form);
        Assert.Same(form, other["g"]);
    }
}
