namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The disposition of a content security policy (https://w3c.github.io/webappsec-csp/#enumdef-securitypolicyviolationeventdisposition).</summary>
/// <seealso href="https://w3c.github.io/webappsec-csp/#enumdef-securitypolicyviolationeventdisposition"/>
[IdlInterface("SecurityPolicyViolationEventDisposition", "https://w3c.github.io/webappsec-csp/#enumdef-securitypolicyviolationeventdisposition")]
public enum SecurityPolicyViolationEventDisposition
{
    /// <summary>The policy is enforced (<c>Content-Security-Policy</c>): a violation is blocked and reported.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventdisposition-enforce"/>
    [IdlMember("enforce", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventdisposition-enforce")]
    Enforce,

    /// <summary>The policy only reports its violations (<c>Content-Security-Policy-Report-Only</c>).</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventdisposition-report"/>
    [IdlMember("report", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventdisposition-report")]
    Report,
}

/// <summary>The initialization dictionary of a <see cref="SecurityPolicyViolationEvent"/> (https://w3c.github.io/webappsec-csp/#dictdef-securitypolicyviolationeventinit).</summary>
/// <seealso href="https://w3c.github.io/webappsec-csp/#dictdef-securitypolicyviolationeventinit"/>
[IdlInterface("SecurityPolicyViolationEventInit", "https://w3c.github.io/webappsec-csp/#dictdef-securitypolicyviolationeventinit")]
public sealed class SecurityPolicyViolationEventInit : EventInit
{
    /// <summary>Gets the URL of the document or worker whose policy is violated.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventinit-documenturi"/>
    [IdlMember("documentURI", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventinit-documenturi")]
    public string DocumentUri { get; init; } = "";

    /// <summary>Gets the referrer of the document whose policy is violated.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventinit-referrer"/>
    [IdlMember("referrer", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventinit-referrer")]
    public string Referrer { get; init; } = "";

    /// <summary>Gets the URL of the blocked resource, or a keyword (<c>inline</c>, <c>eval</c>...).</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventinit-blockeduri"/>
    [IdlMember("blockedURI", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventinit-blockeduri")]
    public string BlockedUri { get; init; } = "";

    /// <summary>Gets the name of the violated directive (a historical alias of <see cref="EffectiveDirective"/>).</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventinit-violateddirective"/>
    [IdlMember("violatedDirective", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventinit-violateddirective")]
    public string ViolatedDirective { get; init; } = "";

    /// <summary>Gets the name of the directive whose enforcement caused the violation.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventinit-effectivedirective"/>
    [IdlMember("effectiveDirective", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventinit-effectivedirective")]
    public string EffectiveDirective { get; init; } = "";

    /// <summary>Gets the serialization of the violated policy.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventinit-originalpolicy"/>
    [IdlMember("originalPolicy", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventinit-originalpolicy")]
    public string OriginalPolicy { get; init; } = "";

    /// <summary>Gets the URL of the script that caused the violation.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventinit-sourcefile"/>
    [IdlMember("sourceFile", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventinit-sourcefile")]
    public string SourceFile { get; init; } = "";

    /// <summary>Gets the sample of the code or the value that caused the violation.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventinit-sample"/>
    [IdlMember("sample", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventinit-sample")]
    public string Sample { get; init; } = "";

    /// <summary>Gets the disposition of the violated policy.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventinit-disposition"/>
    [IdlMember("disposition", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventinit-disposition")]
    public SecurityPolicyViolationEventDisposition Disposition { get; init; }

    /// <summary>Gets the HTTP status code of the response of the document or worker whose policy is violated.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventinit-statuscode"/>
    [IdlMember("statusCode", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventinit-statuscode")]
    public ushort StatusCode { get; init; }

    /// <summary>Gets the line of the script that caused the violation.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventinit-linenumber"/>
    [IdlMember("lineNumber", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventinit-linenumber")]
    public uint LineNumber { get; init; }

    /// <summary>Gets the column of the script that caused the violation.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventinit-columnnumber"/>
    [IdlMember("columnNumber", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationeventinit-columnnumber")]
    public uint ColumnNumber { get; init; }
}

/// <summary>
/// The event fired when a content security policy is violated (https://w3c.github.io/webappsec-csp/#violation-events): at
/// the element that caused the violation, else at the document or at the global object of the worker, when the violation
/// is reported (https://w3c.github.io/webappsec-csp/#report-violation).
/// </summary>
/// <seealso href="https://w3c.github.io/webappsec-csp/#securitypolicyviolationevent"/>
[IdlInterface("SecurityPolicyViolationEvent", "https://w3c.github.io/webappsec-csp/#securitypolicyviolationevent")]
public sealed class SecurityPolicyViolationEvent : Event
{
    /// <summary>Creates a security policy violation event with the given type and initialization dictionary.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-securitypolicyviolationevent"/>
    [IdlMember("constructor", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-securitypolicyviolationevent")]
    public SecurityPolicyViolationEvent(string type, SecurityPolicyViolationEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        DocumentUri = eventInitDict?.DocumentUri ?? "";
        Referrer = eventInitDict?.Referrer ?? "";
        BlockedUri = eventInitDict?.BlockedUri ?? "";
        ViolatedDirective = eventInitDict?.ViolatedDirective ?? "";
        EffectiveDirective = eventInitDict?.EffectiveDirective ?? "";
        OriginalPolicy = eventInitDict?.OriginalPolicy ?? "";
        SourceFile = eventInitDict?.SourceFile ?? "";
        Sample = eventInitDict?.Sample ?? "";
        Disposition = eventInitDict?.Disposition ?? SecurityPolicyViolationEventDisposition.Enforce;
        StatusCode = eventInitDict?.StatusCode ?? 0;
        LineNumber = eventInitDict?.LineNumber ?? 0;
        ColumnNumber = eventInitDict?.ColumnNumber ?? 0;
    }

    /// <summary>Gets the URL of the document or worker whose policy is violated, without its fragment and credentials.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-documenturi"/>
    [IdlMember("documentURI", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-documenturi")]
    public string DocumentUri { get; }

    /// <summary>Gets the referrer of the document whose policy is violated, without its fragment and credentials.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-referrer"/>
    [IdlMember("referrer", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-referrer")]
    public string Referrer { get; }

    /// <summary>
    /// Gets the URL of the blocked resource (the URL of the request, not the URL of a redirect), without its fragment and
    /// credentials (only the scheme for a URL whose scheme is not HTTP(S)), or a keyword: <c>inline</c>, <c>eval</c>,
    /// <c>wasm-eval</c>, <c>trusted-types-policy</c> or <c>trusted-types-sink</c>.
    /// </summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-blockeduri"/>
    [IdlMember("blockedURI", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-blockeduri")]
    public string BlockedUri { get; }

    /// <summary>Gets the name of the violated directive: a historical alias of <see cref="EffectiveDirective"/>.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-violateddirective"/>
    [IdlMember("violatedDirective", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-violateddirective")]
    public string ViolatedDirective { get; }

    /// <summary>Gets the name of the directive whose enforcement caused the violation (e.g. <c>img-src</c> when <c>default-src</c> applies to an image).</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-effectivedirective"/>
    [IdlMember("effectiveDirective", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-effectivedirective")]
    public string EffectiveDirective { get; }

    /// <summary>Gets the serialization of the violated policy.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-originalpolicy"/>
    [IdlMember("originalPolicy", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-originalpolicy")]
    public string OriginalPolicy { get; }

    /// <summary>Gets the URL of the script that caused the violation (stripped as <see cref="BlockedUri"/>), or the empty string.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-sourcefile"/>
    [IdlMember("sourceFile", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-sourcefile")]
    public string SourceFile { get; }

    /// <summary>Gets the sample of the code or the value that caused the violation (e.g. the first 40 characters of an inline script), or the empty string.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-sample"/>
    [IdlMember("sample", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-sample")]
    public string Sample { get; }

    /// <summary>Gets the disposition of the violated policy.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-disposition"/>
    [IdlMember("disposition", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-disposition")]
    public SecurityPolicyViolationEventDisposition Disposition { get; }

    /// <summary>Gets the HTTP status code of the response of the document or worker whose policy is violated.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-statuscode"/>
    [IdlMember("statusCode", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-statuscode")]
    public ushort StatusCode { get; }

    /// <summary>Gets the line (1-based) of the script that caused the violation, or 0.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-linenumber"/>
    [IdlMember("lineNumber", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-linenumber")]
    public uint LineNumber { get; }

    /// <summary>Gets the column (1-based) of the script that caused the violation, or 0.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-columnnumber"/>
    [IdlMember("columnNumber", "https://w3c.github.io/webappsec-csp/#dom-securitypolicyviolationevent-columnnumber")]
    public uint ColumnNumber { get; }
}
