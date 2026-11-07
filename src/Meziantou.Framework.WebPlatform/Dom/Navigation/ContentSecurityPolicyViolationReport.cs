namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A violation of a content security policy (https://w3c.github.io/webappsec-csp/#violation), given to
/// <see cref="BrowsingContextOptions.ContentSecurityPolicyViolation"/>. The URLs are stripped as in the reports sent to the
/// endpoints of the policy (https://w3c.github.io/webappsec-csp/#strip-url-for-use-in-reports).
/// </summary>
public sealed class ContentSecurityPolicyViolationReport
{
    internal ContentSecurityPolicyViolationReport(string documentUrl, string referrer, string blockedUrl, string effectiveDirective, string originalPolicy, SecurityPolicyViolationEventDisposition disposition, string? sourceFile, int statusCode, int lineNumber, int columnNumber, string sample)
    {
        DocumentUrl = documentUrl;
        Referrer = referrer;
        BlockedUrl = blockedUrl;
        EffectiveDirective = effectiveDirective;
        OriginalPolicy = originalPolicy;
        Disposition = disposition;
        SourceFile = sourceFile;
        StatusCode = statusCode;
        LineNumber = lineNumber;
        ColumnNumber = columnNumber;
        Sample = sample;
    }

    /// <summary>Gets the URL of the document (or of the response, for a violation of a navigation response check).</summary>
    public string DocumentUrl { get; }

    /// <summary>Gets the referrer of the document, or the empty string.</summary>
    public string Referrer { get; }

    /// <summary>Gets the URL of the blocked resource, or a keyword such as <c>inline</c>, <c>eval</c>, <c>wasm-eval</c>, <c>trusted-types-policy</c> or <c>trusted-types-sink</c>.</summary>
    public string BlockedUrl { get; }

    /// <summary>Gets the directive whose enforcement caused the violation (e.g. <c>img-src</c>).</summary>
    public string EffectiveDirective { get; }

    /// <summary>Gets the serialization of the violated policy.</summary>
    public string OriginalPolicy { get; }

    /// <summary>Gets whether the violated policy is enforced (the resource is blocked) or report-only.</summary>
    public SecurityPolicyViolationEventDisposition Disposition { get; }

    /// <summary>Gets the URL of the script that caused the violation, or null.</summary>
    public string? SourceFile { get; }

    /// <summary>Gets the HTTP status code of the response of the document.</summary>
    public int StatusCode { get; }

    /// <summary>Gets the line number in <see cref="SourceFile"/>, or 0.</summary>
    public int LineNumber { get; }

    /// <summary>Gets the column number in <see cref="SourceFile"/>, or 0.</summary>
    public int ColumnNumber { get; }

    /// <summary>Gets the sample of the code or of the value that caused the violation (only with the <c>'report-sample'</c> source), or the empty string.</summary>
    public string Sample { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        var verb = Disposition is SecurityPolicyViolationEventDisposition.Enforce ? "Refused" : "Reported";
        return $"{verb} to load or execute '{BlockedUrl}' because it violates the '{EffectiveDirective}' directive of the policy \"{OriginalPolicy}\".";
    }
}
