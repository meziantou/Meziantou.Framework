using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

public sealed partial class HtmlMetaElement
{
    internal override void PostConnectionSteps()
    {
        base.PostConnectionSteps();

        // https://html.spec.whatwg.org/multipage/semantics.html#pragma-directives: the pragma is processed when the element is inserted into the document
        var document = NodeDocument;
        if (!ReferenceEquals(GetRoot(), document))
            return;

        ProcessReferrerMetadata(document);
        document.InvalidatePageColorSchemes(this);
        if (GetAttributeValue("http-equiv") is not { } httpEquiv)
            return;

        if (InfraAscii.EqualsIgnoreAsciiCase(httpEquiv, "content-language"))
        {
            ProcessContentLanguagePragma(document);
        }
        else if (InfraAscii.EqualsIgnoreAsciiCase(httpEquiv, "content-security-policy"))
        {
            ProcessContentSecurityPolicyPragma(document);
        }
        else if (InfraAscii.EqualsIgnoreAsciiCase(httpEquiv, "refresh") && GetAttributeValue("content") is { Length: > 0 } content)
        {
            // https://html.spec.whatwg.org/multipage/semantics.html#attr-meta-http-equiv-refresh
            document.RunSharedDeclarativeRefreshSteps(content);
        }
        else if (InfraAscii.EqualsIgnoreAsciiCase(httpEquiv, "delegate-ch") || InfraAscii.EqualsIgnoreAsciiCase(httpEquiv, "accept-ch"))
        {
            // https://wicg.github.io/client-hints-infrastructure/#delegate-ch-algo
            ClientHints.ProcessPragma(this, document, delegates: InfraAscii.EqualsIgnoreAsciiCase(httpEquiv, "delegate-ch"));
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/semantics.html#attr-meta-http-equiv-content-security-policy: the library
    /// implements the fetch directives (see <see cref="ContentSecurityPolicy"/>) and the Trusted Types directives (see
    /// <see cref="Document.TrustedTypesDirectives"/>). The policies of the documents that have no browsing context (e.g.
    /// parsed by <see cref="HtmlDocumentParser"/>) are not enforced.
    /// </summary>
    private void ProcessContentSecurityPolicyPragma(Document document)
    {
        if (document.BrowsingContext is null || ParentNode is not HtmlHeadElement || GetAttributeValue("content") is not { Length: > 0 } content)
            return;

        document.AddContentSecurityPolicy(content, reportOnly: false, fromMeta: true);
    }

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        if (@namespace is null && localName is "name" or "content" && ReferenceEquals(GetRoot(), NodeDocument))
        {
            ProcessReferrerMetadata(NodeDocument);
            NodeDocument.InvalidatePageColorSchemes(this, oldName: localName == "name" ? oldValue : null);
        }
    }

    internal override void RemovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.RemovingSteps(isSubtreeRoot, oldAncestor);
        if (oldAncestor.GetRoot() is Document document)
        {
            document.InvalidatePageColorSchemes(this);
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/semantics.html#meta-referrer: processed when the element is inserted into the
    /// document, or when its name or content attribute changes.
    /// </summary>
    private void ProcessReferrerMetadata(Document document)
    {
        if (!InfraAscii.EqualsIgnoreAsciiCase(GetAttributeValue("name") ?? "", "referrer") || GetAttributeValue("content") is not { Length: > 0 } content)
            return;

        if (ReferrerPolicies.ParseMetaContent(content) is { } policy)
        {
            document.ReferrerPolicy = policy;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/semantics.html#attr-meta-http-equiv-content-language</summary>
    private void ProcessContentLanguagePragma(Document document)
    {
        if (GetAttributeValue("content") is not { } input || input.Contains(',', StringComparison.Ordinal))
            return;

        var candidate = input.AsSpan().TrimStart("\t\n\f\r ");
        var end = candidate.IndexOfAny("\t\n\f\r ");
        if (end >= 0)
        {
            candidate = candidate[..end];
        }

        if (candidate.IsEmpty)
            return;

        document.PragmaSetDefaultLanguage = candidate.ToString();
    }
}
