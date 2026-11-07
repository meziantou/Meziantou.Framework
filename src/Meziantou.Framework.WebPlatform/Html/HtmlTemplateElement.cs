using Meziantou.Framework.WebPlatform.Html.Parsing;

namespace Meziantou.Framework.WebPlatform.Dom;

// https://html.spec.whatwg.org/#the-template-element
public partial class HtmlTemplateElement
{
    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(TemplateContents))]
    partial void OnCreated()
    {
        // https://html.spec.whatwg.org/#template-contents
        var contentsDocument = NodeDocument.GetAppropriateTemplateContentsOwnerDocument();
        TemplateContents = new DocumentFragment(contentsDocument) { Host = this };
    }

    /// <summary>The template contents (https://html.spec.whatwg.org/#template-contents). It is a shadow root for declarative shadow roots.</summary>
    internal DocumentFragment TemplateContents { get; set; }

    /// <summary>https://html.spec.whatwg.org/#template-insertion-target</summary>
    internal Node? InsertionTarget { get; set; }

    internal ProcessingInstruction? InsertionStartMarker { get; set; }

    internal ProcessingInstruction? InsertionEndMarker { get; set; }

    /// <summary>
    /// The state of the tentative extensions of the content patching of the template while the parser has it on its stack of
    /// open elements (see <see cref="ContentPatch"/>), or null.
    /// </summary>
    internal ContentPatch? Patch { get; set; }

    /// <summary>
    /// Gets or sets whether the template buffers its content (the tentative <c>buffer</c> attribute of the declarative partial
    /// updates, https://github.com/WICG/declarative-partial-updates/blob/main/fragment-include-explainer.md): the content of a
    /// template that patches content is parsed into its template contents and inserted at once when the template is closed.
    /// </summary>
    internal bool Buffer
    {
        get => HasAttributeWithLocalName("buffer");
        set => ToggleAttribute("buffer", value);
    }

    /// <summary>
    /// The template is removed while it buffers its content or fetches it (the tentative <c>buffer</c> and <c>src</c> attributes):
    /// the patch is canceled (its content is not inserted; the fetched content already inserted stays).
    /// </summary>
    internal override void RemovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.RemovingSteps(isSubtreeRoot, oldAncestor);
        if (Patch is { Buffered: true } patch)
        {
            patch.Canceled = true;
        }

        CancelSourcePatch();
    }

    /// <summary>Gets the template contents: the document fragment that holds the content of the template.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/scripting.html#dom-template-content"/>
    [IdlMember("content", "https://html.spec.whatwg.org/multipage/scripting.html#dom-template-content")]
    public DocumentFragment Content => TemplateContents;

    /// <summary>https://html.spec.whatwg.org/#template-adopting-steps</summary>
    internal override void AdoptingSteps(Document oldDocument)
    {
        base.AdoptingSteps(oldDocument);
        NodeDocument.GetAppropriateTemplateContentsOwnerDocument().Adopt(TemplateContents);
    }

    /// <summary>https://html.spec.whatwg.org/#template-cloning-steps</summary>
    internal override void CloningSteps(Node copy, bool subtree)
    {
        base.CloningSteps(copy, subtree);
        if (!subtree)
            return;

        var copyContents = ((HtmlTemplateElement)copy).TemplateContents;
        for (var child = TemplateContents.FirstChild; child is not null; child = child.NextSibling)
        {
            child.Clone(copyContents.NodeDocument, subtree: true, copyContents, fallbackRegistry: null);
        }
    }
}
