namespace Meziantou.Framework.WebPlatform.Dom;

// Document Policy (https://wicg.github.io/document-policy/): the policies of the document, set when it is created from the
// headers of its response and the required policy of its navigable.
public partial class Document
{
    /// <summary>The document policy of the document (https://wicg.github.io/document-policy/#document-policy): the <c>Document-Policy</c> header of its response.</summary>
    internal DocumentPolicyRecord DocumentPolicy { get; set; } = DocumentPolicyRecord.Empty;

    /// <summary>The report-only document policy of the document: the <c>Document-Policy-Report-Only</c> header of its response.</summary>
    internal DocumentPolicyRecord ReportOnlyDocumentPolicy { get; set; } = DocumentPolicyRecord.Empty;

    /// <summary>
    /// The required document policy of the documents of the child navigables of the document
    /// (https://wicg.github.io/document-policy/#nested-context-required-document-policy): the required policy of the
    /// document combined with the <c>Require-Document-Policy</c> header of its response.
    /// </summary>
    internal DocumentPolicyRecord NestedRequiredDocumentPolicy { get; set; } = DocumentPolicyRecord.Empty;

    /// <summary>
    /// The integrity policy of the policy container of the document (https://w3c.github.io/webappsec-subresource-integrity/#integrity-policy-section):
    /// the <c>Integrity-Policy</c> header of its response.
    /// </summary>
    internal IntegrityPolicy IntegrityPolicy { get; set; } = IntegrityPolicy.Empty;

    /// <summary>The report-only integrity policy of the policy container of the document: the <c>Integrity-Policy-Report-Only</c> header of its response.</summary>
    internal IntegrityPolicy ReportOnlyIntegrityPolicy { get; set; } = IntegrityPolicy.Empty;
}
