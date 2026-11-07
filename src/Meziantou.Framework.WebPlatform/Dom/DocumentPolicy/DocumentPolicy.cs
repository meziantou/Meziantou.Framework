using System.Collections.Frozen;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A configuration point of Document Policy (https://wicg.github.io/document-policy/#configuration-point): a feature whose
/// value a document policy sets. The library supports the boolean configuration points that the browsers implement and
/// that it enforces: their default value is true, and false is stricter.
/// </summary>
internal sealed class DocumentPolicyFeature
{
    private DocumentPolicyFeature(string name) => Name = name;

    /// <summary>The token that identifies the feature in the policies (e.g. <c>sync-xhr</c>).</summary>
    public string Name { get; }

    /// <summary>The default value of the feature, when a policy does not declare it.</summary>
    public static bool DefaultValue => true;

    /// <summary>The synchronous requests of <c>XMLHttpRequest</c> (as in Chromium): <c>sync-xhr=?0</c> makes <c>send()</c> throw a <c>NetworkError</c>.</summary>
    public static DocumentPolicyFeature SyncXhr { get; } = new("sync-xhr");

    /// <summary>The scroll to the fragment or to the text directive on load (https://github.com/WICG/scroll-to-text-fragment/blob/main/document-policy-force-load-at-top.md).</summary>
    public static DocumentPolicyFeature ForceLoadAtTop { get; } = new("force-load-at-top");

    private static readonly FrozenDictionary<string, DocumentPolicyFeature> ByName = new[] { ForceLoadAtTop, SyncXhr }.ToFrozenDictionary(feature => feature.Name, StringComparer.Ordinal);

    /// <summary>The supported configuration point identified by a token, or null.</summary>
    public static DocumentPolicyFeature? Get(string name) => ByName.GetValueOrDefault(name);

    public override string ToString() => Name;
}

/// <summary>
/// A document policy (https://wicg.github.io/document-policy/#document-policy): the values of the configuration points
/// that a document declares (the <c>Document-Policy</c> and <c>Document-Policy-Report-Only</c> headers), or that a document
/// requires from the documents of its iframes (the <c>Require-Document-Policy</c> header and the <c>policy</c> attribute of
/// the <c>iframe</c> elements, sent in the <c>Sec-Required-Document-Policy</c> header).
/// </summary>
internal sealed class DocumentPolicyRecord
{
    public static DocumentPolicyRecord Empty { get; } = new(new SortedDictionary<string, (DocumentPolicyFeature Feature, bool Value)>(StringComparer.Ordinal), [], defaultEndpoint: null);

    private readonly SortedDictionary<string, (DocumentPolicyFeature Feature, bool Value)> _values;
    private readonly Dictionary<DocumentPolicyFeature, string> _endpoints;

    private DocumentPolicyRecord(SortedDictionary<string, (DocumentPolicyFeature Feature, bool Value)> values, Dictionary<DocumentPolicyFeature, string> endpoints, string? defaultEndpoint)
    {
        _values = values;
        _endpoints = endpoints;
        DefaultEndpoint = defaultEndpoint;
    }

    /// <summary>Whether the policy declares no value.</summary>
    public bool IsEmpty => _values.Count == 0;

    /// <summary>The reporting endpoint of the features without their own endpoint (the <c>*</c> member), or null.</summary>
    public string? DefaultEndpoint { get; }

    /// <summary>The value of a feature: its declared value, or its default value.</summary>
    public bool GetValue(DocumentPolicyFeature feature) => _values.TryGetValue(feature.Name, out var entry) ? entry.Value : DocumentPolicyFeature.DefaultValue;

    /// <summary>Whether the policy declares a value for a feature.</summary>
    public bool Declares(DocumentPolicyFeature feature) => _values.ContainsKey(feature.Name);

    /// <summary>The reporting endpoint of a feature (its <c>report-to</c> parameter, else the default endpoint), or null.</summary>
    public string? GetEndpoint(DocumentPolicyFeature feature)
    {
        var endpoint = _endpoints.GetValueOrDefault(feature) ?? DefaultEndpoint;
        return endpoint is "none" ? null : endpoint;
    }

    /// <summary>
    /// https://wicg.github.io/document-policy/#parse-document-policy: parses a policy (a structured field dictionary). The
    /// unsupported features are ignored; a value of the wrong type makes the parse fail (null).
    /// </summary>
    public static DocumentPolicyRecord? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Empty;

        if (StructuredFields.ParseDictionary(value) is not { } dictionary)
            return null;

        return FromDictionary(dictionary);
    }

    /// <summary>The policy of a header of a response (the empty policy when the header is absent or invalid).</summary>
    public static DocumentPolicyRecord FromHeaders(IReadOnlyList<KeyValuePair<string, string>>? headers, string name)
    {
        if (headers is null || StructuredFields.GetDictionary(headers, name) is not { } dictionary)
            return Empty;

        return FromDictionary(dictionary) ?? Empty;
    }

    private static DocumentPolicyRecord? FromDictionary(List<KeyValuePair<string, StructuredFieldMember>> dictionary)
    {
        var values = new SortedDictionary<string, (DocumentPolicyFeature, bool)>(StringComparer.Ordinal);
        var endpoints = new Dictionary<DocumentPolicyFeature, string>();
        string? defaultEndpoint = null;
        foreach (var (name, member) in dictionary)
        {
            if (name == "*")
            {
                if (member.GetParameter("report-to") is StructuredFieldToken endpoint)
                {
                    defaultEndpoint = endpoint.Value;
                }

                continue;
            }

            if (DocumentPolicyFeature.Get(name) is not { } feature || values.ContainsKey(name))
                continue;

            if (member.Item is not { Value: bool value })
                return null;

            values[name] = (feature, value);
            if (member.GetParameter("report-to") is StructuredFieldToken featureEndpoint)
            {
                endpoints[feature] = featureEndpoint.Value;
            }
        }

        return values.Count == 0 && defaultEndpoint is null ? Empty : new DocumentPolicyRecord(values, endpoints, defaultEndpoint);
    }

    /// <summary>
    /// https://wicg.github.io/document-policy/#algo-combine-policies: the policy that declares the values of both policies,
    /// the stricter one when both declare a feature.
    /// </summary>
    public static DocumentPolicyRecord Combine(DocumentPolicyRecord a, DocumentPolicyRecord b)
    {
        if (b.IsEmpty)
            return a;

        if (a.IsEmpty)
            return b;

        var values = new SortedDictionary<string, (DocumentPolicyFeature Feature, bool Value)>(a._values, StringComparer.Ordinal);
        foreach (var (name, entry) in b._values)
        {
            if (!values.TryGetValue(name, out var existing) || IsStricter(entry.Value, existing.Value))
            {
                values[name] = entry;
            }
        }

        return new DocumentPolicyRecord(values, [], defaultEndpoint: null);
    }

    /// <summary>
    /// https://wicg.github.io/document-policy/#algo-is-policy-compatible: whether a declared policy is compatible with a
    /// required policy: none of its values (declared or default) is less strict than a required value.
    /// </summary>
    public static bool IsCompatible(DocumentPolicyRecord required, DocumentPolicyRecord declared)
    {
        foreach (var (feature, requiredValue) in required._values.Values)
        {
            if (IsStricter(requiredValue, declared.GetValue(feature)))
                return false;
        }

        return true;
    }

    /// <summary>The canonical serialization (https://wicg.github.io/document-policy/#serialization): the features in ASCII order.</summary>
    public string Serialize() => string.Join(", ", _values.Select(entry => entry.Value.Value ? entry.Key : entry.Key + "=?0"));

    public override string ToString() => Serialize();

    /// <summary>Whether a boolean value is stricter than another one: false (disabled) is stricter than true.</summary>
    private static bool IsStricter(bool value, bool other) => !value && other;
}

/// <summary>
/// The body of the reports of type <c>document-policy-violation</c> (https://wicg.github.io/document-policy/#reporting): a
/// document used a feature that its document policy (or its report-only policy) disables.
/// </summary>
/// <seealso href="https://wicg.github.io/document-policy/#reporting"/>
[IdlInterface("DocumentPolicyViolationReportBody", "https://wicg.github.io/document-policy/#reporting")]
public sealed class DocumentPolicyViolationReportBody : ReportBody
{
    internal DocumentPolicyViolationReportBody()
    {
    }

    /// <summary>Gets the token of the configuration point whose policy is violated (e.g. <c>sync-xhr</c>).</summary>
    /// <seealso href="https://wicg.github.io/document-policy/#reporting"/>
    [IdlMember("featureId", "https://wicg.github.io/document-policy/#reporting")]
    public string FeatureId { get; init; } = "";

    /// <summary>Gets the URL of the script that used the feature, or null.</summary>
    /// <seealso href="https://wicg.github.io/document-policy/#reporting"/>
    [IdlMember("sourceFile", "https://wicg.github.io/document-policy/#reporting")]
    public string? SourceFile { get; init; }

    /// <summary>Gets the line of the script that used the feature, or null.</summary>
    /// <seealso href="https://wicg.github.io/document-policy/#reporting"/>
    [IdlMember("lineNumber", "https://wicg.github.io/document-policy/#reporting")]
    public int? LineNumber { get; init; }

    /// <summary>Gets the column of the script that used the feature, or null.</summary>
    /// <seealso href="https://wicg.github.io/document-policy/#reporting"/>
    [IdlMember("columnNumber", "https://wicg.github.io/document-policy/#reporting")]
    public int? ColumnNumber { get; init; }

    /// <summary>Gets <c>enforce</c> when the policy was enforced, or <c>report</c> for a violation of the report-only policy.</summary>
    /// <seealso href="https://wicg.github.io/document-policy/#reporting"/>
    [IdlMember("disposition", "https://wicg.github.io/document-policy/#reporting")]
    public string Disposition { get; init; } = "";

    /// <summary>Gets a message that describes the violation, or null.</summary>
    /// <seealso href="https://wicg.github.io/document-policy/#reporting"/>
    [IdlMember("message", "https://wicg.github.io/document-policy/#reporting")]
    public string? Message { get; init; }

    internal override void WriteJson(System.Text.Json.Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("featureId", FeatureId);
        CspViolationReportBody.WriteNullableString(writer, "sourceFile", SourceFile);
        WriteNullableNumber(writer, "lineNumber", LineNumber);
        WriteNullableNumber(writer, "columnNumber", ColumnNumber);
        writer.WriteString("disposition", Disposition);
        CspViolationReportBody.WriteNullableString(writer, "message", Message);
        writer.WriteEndObject();
    }

    private static void WriteNullableNumber(System.Text.Json.Utf8JsonWriter writer, string name, int? value)
    {
        if (value is { } number)
        {
            writer.WriteNumber(name, number);
        }
        else
        {
            writer.WriteNull(name);
        }
    }
}

/// <summary>The algorithms of Document Policy that the features use (https://wicg.github.io/document-policy/#algorithms).</summary>
internal static class DocumentPolicyAlgorithms
{
    /// <summary>
    /// https://wicg.github.io/document-policy/#algo-should-request-be-allowed-to-use-feature: whether a document can use a
    /// feature. A violation of its policy, or of its report-only policy, is reported to the document.
    /// </summary>
    public static bool IsFeatureEnabled(Document document, DocumentPolicyFeature feature, string message)
    {
        if (document.RelevantWindow is not { } window || !window.Options.EffectiveFeatures.DocumentPolicyEnabled)
            return true;

        var policy = document.DocumentPolicy;
        if (!policy.GetValue(feature))
        {
            ReportViolation(window, feature, "enforce", policy, message);
            return false;
        }

        var reportOnlyPolicy = document.ReportOnlyDocumentPolicy;
        if (!reportOnlyPolicy.GetValue(feature))
        {
            ReportViolation(window, feature, "report", reportOnlyPolicy, message);
        }

        return true;
    }

    private static void ReportViolation(Window window, DocumentPolicyFeature feature, string disposition, DocumentPolicyRecord policy, string message)
    {
        // The location of the running script (the engine is only queried from the thread of the event loop of the window)
        var location = GlobalScope.CurrentWorkerGlobal is null ? window.ScriptEngine?.GetCurrentScriptLocation() : null;
        var body = new DocumentPolicyViolationReportBody
        {
            FeatureId = feature.Name,
            SourceFile = location is not { } scriptLocation ? null : UrlParser.Parse(scriptLocation.Url, baseUrl: null) is { } sourceUrl ? ContentSecurityPolicyViolation.StripUrlForUseInReports(sourceUrl) : scriptLocation.Url,
            LineNumber = location?.LineNumber,
            ColumnNumber = location?.ColumnNumber,
            Disposition = disposition,
            Message = message,
        };

        window.QueueReport(body, "document-policy-violation", policy.GetEndpoint(feature));
    }
}
