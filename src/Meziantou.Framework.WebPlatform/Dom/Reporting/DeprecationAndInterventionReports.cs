using System.Text.Json;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The body of the reports of type <c>deprecation</c> (https://wicg.github.io/deprecation-reporting/): the use of a
/// deprecated feature by a document (e.g. a synchronous <c>XMLHttpRequest</c> in a window).
/// </summary>
/// <seealso href="https://wicg.github.io/deprecation-reporting/#deprecationreportbody"/>
[IdlInterface("DeprecationReportBody", "https://wicg.github.io/deprecation-reporting/#deprecationreportbody")]
public sealed class DeprecationReportBody : ReportBody
{
    internal DeprecationReportBody()
    {
    }

    /// <summary>Gets the identifier of the deprecated feature (the name used by Chromium, e.g. <c>XMLHttpRequestSynchronousInNonWorkerOutsideBeforeUnload</c>).</summary>
    /// <seealso href="https://wicg.github.io/deprecation-reporting/#dom-deprecationreportbody-id"/>
    [IdlMember("id", "https://wicg.github.io/deprecation-reporting/#dom-deprecationreportbody-id")]
    public string Id { get; init; } = "";

    /// <summary>Gets the date (UTC) after which the feature is expected to be removed, or null when it is not known (as for every feature that the library reports).</summary>
    /// <seealso href="https://wicg.github.io/deprecation-reporting/#dom-deprecationreportbody-anticipatedremoval"/>
    [IdlMember("anticipatedRemoval", "https://wicg.github.io/deprecation-reporting/#dom-deprecationreportbody-anticipatedremoval")]
    public DateTime? AnticipatedRemoval { get; init; }

    /// <summary>Gets a message that describes the deprecation.</summary>
    /// <seealso href="https://wicg.github.io/deprecation-reporting/#dom-deprecationreportbody-message"/>
    [IdlMember("message", "https://wicg.github.io/deprecation-reporting/#dom-deprecationreportbody-message")]
    public string Message { get; init; } = "";

    /// <summary>Gets the URL of the script that used the feature, or null.</summary>
    /// <seealso href="https://wicg.github.io/deprecation-reporting/#dom-deprecationreportbody-sourcefile"/>
    [IdlMember("sourceFile", "https://wicg.github.io/deprecation-reporting/#dom-deprecationreportbody-sourcefile")]
    public string? SourceFile { get; init; }

    /// <summary>Gets the line of the script that used the feature, or null.</summary>
    /// <seealso href="https://wicg.github.io/deprecation-reporting/#dom-deprecationreportbody-linenumber"/>
    [IdlMember("lineNumber", "https://wicg.github.io/deprecation-reporting/#dom-deprecationreportbody-linenumber")]
    public int? LineNumber { get; init; }

    /// <summary>Gets the column of the script that used the feature, or null.</summary>
    /// <seealso href="https://wicg.github.io/deprecation-reporting/#dom-deprecationreportbody-columnnumber"/>
    [IdlMember("columnNumber", "https://wicg.github.io/deprecation-reporting/#dom-deprecationreportbody-columnnumber")]
    public int? ColumnNumber { get; init; }

    internal override void WriteJson(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("id", Id);
        if (AnticipatedRemoval is { } anticipatedRemoval)
        {
            writer.WriteString("anticipatedRemoval", anticipatedRemoval.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNull("anticipatedRemoval");
        }

        writer.WriteString("message", Message);
        InterventionReportBody.WriteSourceLocation(writer, SourceFile, LineNumber, ColumnNumber);
        writer.WriteEndObject();
    }
}

/// <summary>
/// The body of the reports of type <c>intervention</c> (https://wicg.github.io/intervention-reporting/): a request of a
/// document that the user agent did not honor (e.g. a <c>preventDefault()</c> call in a listener that is passive by
/// default).
/// </summary>
/// <seealso href="https://wicg.github.io/intervention-reporting/#interventionreportbody"/>
[IdlInterface("InterventionReportBody", "https://wicg.github.io/intervention-reporting/#interventionreportbody")]
public sealed class InterventionReportBody : ReportBody
{
    internal InterventionReportBody()
    {
    }

    /// <summary>Gets the identifier of the intervention (the name used by Chromium, e.g. <c>PreventDefaultPassive</c>).</summary>
    /// <seealso href="https://wicg.github.io/intervention-reporting/#dom-interventionreportbody-id"/>
    [IdlMember("id", "https://wicg.github.io/intervention-reporting/#dom-interventionreportbody-id")]
    public string Id { get; init; } = "";

    /// <summary>Gets a message that describes the intervention.</summary>
    /// <seealso href="https://wicg.github.io/intervention-reporting/#dom-interventionreportbody-message"/>
    [IdlMember("message", "https://wicg.github.io/intervention-reporting/#dom-interventionreportbody-message")]
    public string Message { get; init; } = "";

    /// <summary>Gets the URL of the script that caused the intervention, or null.</summary>
    /// <seealso href="https://wicg.github.io/intervention-reporting/#dom-interventionreportbody-sourcefile"/>
    [IdlMember("sourceFile", "https://wicg.github.io/intervention-reporting/#dom-interventionreportbody-sourcefile")]
    public string? SourceFile { get; init; }

    /// <summary>Gets the line of the script that caused the intervention, or null.</summary>
    /// <seealso href="https://wicg.github.io/intervention-reporting/#dom-interventionreportbody-linenumber"/>
    [IdlMember("lineNumber", "https://wicg.github.io/intervention-reporting/#dom-interventionreportbody-linenumber")]
    public int? LineNumber { get; init; }

    /// <summary>Gets the column of the script that caused the intervention, or null.</summary>
    /// <seealso href="https://wicg.github.io/intervention-reporting/#dom-interventionreportbody-columnnumber"/>
    [IdlMember("columnNumber", "https://wicg.github.io/intervention-reporting/#dom-interventionreportbody-columnnumber")]
    public int? ColumnNumber { get; init; }

    internal override void WriteJson(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("id", Id);
        writer.WriteString("message", Message);
        WriteSourceLocation(writer, SourceFile, LineNumber, ColumnNumber);
        writer.WriteEndObject();
    }

    internal static void WriteSourceLocation(Utf8JsonWriter writer, string? sourceFile, int? lineNumber, int? columnNumber)
    {
        CspViolationReportBody.WriteNullableString(writer, "sourceFile", sourceFile);
        WriteNullableNumber("lineNumber", lineNumber);
        WriteNullableNumber("columnNumber", columnNumber);

        void WriteNullableNumber(string name, int? value)
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
}

public partial class Document
{
    /// <summary>
    /// The identifiers of the deprecated features already reported by the document (see <see cref="Window.ReportDeprecation"/>):
    /// as in Chromium, a feature is reported once per document.
    /// </summary>
    internal HashSet<string>? ReportedDeprecations { get; set; }
}

// The deprecation reports (https://wicg.github.io/deprecation-reporting/) and the intervention reports
// (https://wicg.github.io/intervention-reporting/) of a window
public sealed partial class Window
{
    /// <summary>
    /// Generates a <c>deprecation</c> report (https://wicg.github.io/deprecation-reporting/#reporting), sent to the
    /// <c>default</c> endpoint, the first time the window uses the feature <paramref name="id"/>. Runs on the thread of the
    /// event loop of the window.
    /// </summary>
    internal void ReportDeprecation(string id, string message, DateTime? anticipatedRemoval = null)
    {
        // A window is reused by the document that replaces its initial about:blank document: the set is the document's
        var document = Document;
        document.ReportedDeprecations ??= new HashSet<string>(StringComparer.Ordinal);
        if (!document.ReportedDeprecations.Add(id))
            return;

        var (sourceFile, lineNumber, columnNumber) = GetReportSourceLocation();
        QueueReport(new DeprecationReportBody
        {
            Id = id,
            AnticipatedRemoval = anticipatedRemoval,
            Message = message,
            SourceFile = sourceFile,
            LineNumber = lineNumber,
            ColumnNumber = columnNumber,
        }, "deprecation", "default");
    }

    /// <summary>
    /// Generates an <c>intervention</c> report (https://wicg.github.io/intervention-reporting/#reporting), sent to the
    /// <c>default</c> endpoint. Runs on the thread of the event loop of the window.
    /// </summary>
    internal void ReportIntervention(string id, string message)
    {
        var (sourceFile, lineNumber, columnNumber) = GetReportSourceLocation();
        QueueReport(new InterventionReportBody
        {
            Id = id,
            Message = message,
            SourceFile = sourceFile,
            LineNumber = lineNumber,
            ColumnNumber = columnNumber,
        }, "intervention", "default");
    }

    /// <summary>The location of the running script, its URL stripped for use in reports, or nulls when no script runs.</summary>
    private (string? SourceFile, int? LineNumber, int? ColumnNumber) GetReportSourceLocation()
    {
        if (CurrentWorkerGlobal is not null || ScriptEngine?.GetCurrentScriptLocation() is not { } location)
            return (null, null, null);

        var sourceFile = UrlParser.Parse(location.Url, baseUrl: null) is { } sourceUrl ? ContentSecurityPolicyViolation.StripUrlForUseInReports(sourceUrl) : location.Url;
        return (sourceFile, location.LineNumber, location.ColumnNumber);
    }
}
