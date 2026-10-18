namespace Meziantou.Framework.WebPlatform.Dom;

public partial class Document
{
    /// <summary>
    /// The client hints set of the environment settings object of the document
    /// (https://wicg.github.io/client-hints-infrastructure/#environment-settings-object-client-hints-set): the hints added by
    /// its <c>meta</c> elements (see <see cref="ClientHints.ProcessPragma"/>). The hints that its origin opted into with
    /// the <c>Accept-CH</c> header are in the Accept-CH cache (see <see cref="ClientHints.GetEnvironmentHints"/>).
    /// </summary>
    internal ClientHintsSet ClientHintsSet { get; set; }

    /// <summary>Whether the <c>meta</c> elements no longer change the client hints of the document: an element that fetches or executes was inserted.</summary>
    internal bool ClientHintsPragmasAreIgnored { get; set; }
}

public partial class HtmlImageElement
{
    /// <summary>
    /// The source size of the element (https://html.spec.whatwg.org/multipage/images.html#source-size-2) when it comes from a
    /// <c>sizes</c> attribute (of the selected <c>source</c> element, else of the element), in CSS pixels, or null: the width
    /// of the <c>Sec-CH-Width</c> client hint (https://wicg.github.io/responsive-image-client-hints/#sec-ch-width).
    /// </summary>
    internal double? GetSourceSizeOfSizesAttribute()
    {
        var environment = Css.Cascade.StyleEngine.CreateEnvironment(NodeDocument);
        var sizes = SelectSourceElement(environment) is ({ } source, _)
            ? source.GetAttributeValue("sizes") ?? (SrcsetParser.AllowsAutoSizes(this) ? "auto" : null)
            : GetAttributeValue("sizes");

        return sizes is null ? null : SrcsetParser.ParseSizes(sizes, environment, this);
    }
}

internal sealed partial class FetchRequest
{
    /// <summary>
    /// The method of the request of a navigation before its redirects, recorded when its first response is processed: a
    /// navigation is only restarted for the critical client hints when its method is safe.
    /// </summary>
    public string? NavigationInitialMethod { get; set; }
}
