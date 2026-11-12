namespace Meziantou.Framework.WebPlatform.Dom;

public partial class Document
{
    /// <summary>Gets the tools of the document (https://webmachinelearning.github.io/webmcp/#dom-document-modelcontext): see <see cref="Dom.ModelContext"/>.</summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-document-modelcontext"/>
    [IdlMember("modelContext", "https://webmachinelearning.github.io/webmcp/#dom-document-modelcontext")]
    public ModelContext ModelContext => ModelContextOrNull ??= new ModelContext(this);

    /// <summary>The model context of the document, or null while it was not created (the document has no tool).</summary>
    internal ModelContext? ModelContextOrNull { get; private set; }
}
