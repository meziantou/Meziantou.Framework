using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// A classic script (https://html.spec.whatwg.org/multipage/webappapis.html#classic-script) that the library asks the
/// <see cref="ScriptEngine"/> to run: the script of a <c>script</c> element, a <c>javascript:</c> URL, or a string passed
/// to <c>setTimeout()</c> or <c>setInterval()</c>.
/// </summary>
public sealed class ClassicScript
{
    /// <summary>Creates a classic script.</summary>
    /// <param name="global">The global object of the realm in which the script runs.</param>
    /// <param name="sourceText">The source text of the script.</param>
    /// <param name="baseUrl">The base URL of the script, used to resolve the module specifiers.</param>
    public ClassicScript(GlobalScope global, string sourceText, string baseUrl)
    {
        ArgumentNullException.ThrowIfNull(global);
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentNullException.ThrowIfNull(baseUrl);
        Global = global;
        SourceText = sourceText;
        _baseUrl = baseUrl;
        Url = baseUrl;
    }

    /// <summary>Gets the global object of the realm of the script (the global object of its settings object).</summary>
    public GlobalScope Global { get; }

    /// <summary>Gets the source text of the script.</summary>
    public string SourceText { get; }

    private readonly string _baseUrl;

    /// <summary>
    /// Gets the base URL of the script (https://html.spec.whatwg.org/multipage/webappapis.html#concept-script-base-url):
    /// <c>about:blank</c> for a script whose errors are muted (https://html.spec.whatwg.org/multipage/webappapis.html#creating-a-classic-script).
    /// </summary>
    public string BaseUrl => MutedErrors ? "about:blank" : _baseUrl;

    /// <summary>
    /// Gets the URL of the script, used as the file name of the errors and of the stack traces: the URL of an external
    /// script, or the URL of the document of an inline script.
    /// </summary>
    public string Url { get; init; }

    /// <summary>
    /// Gets whether the errors of the script are muted (https://html.spec.whatwg.org/multipage/webappapis.html#muted-errors):
    /// the script was fetched from another origin without CORS. The library replaces the information of the errors it
    /// reports with <c>"Script error."</c>.
    /// </summary>
    public bool MutedErrors { get; init; }

    /// <summary>Gets the script element whose script this is, or null (e.g. a <c>javascript:</c> URL).</summary>
    public Element? Element { get; init; }

    /// <summary>
    /// Gets the line (1-based) of the start of the source text in the document of an inline script created by the HTML
    /// parser: the locations in the script (e.g. of its errors and of the violations of the content security policies) are
    /// the locations in the document. The default value is 1.
    /// </summary>
    public int SourceTextLine { get; init; } = 1;

    /// <summary>Gets the column (0-based, in UTF-16 code units) of the start of the source text in its line (see <see cref="SourceTextLine"/>).</summary>
    public int SourceTextColumn { get; init; }

    /// <summary>
    /// Gets the options used to fetch the modules imported by the script (https://html.spec.whatwg.org/multipage/webappapis.html#descendant-script-fetch-options):
    /// the <c>crossorigin</c> and <c>referrerpolicy</c> attributes, the nonce and the parser metadata of its <c>script</c>
    /// element, else the default options.
    /// </summary>
    public ModuleFetchOptions FetchOptions
    {
        get => field ??= Element is HtmlScriptElement script
            ? new ModuleFetchOptions { IncludeCredentials = Infra.InfraAscii.EqualsIgnoreAsciiCase(script.GetAttributeValue("crossorigin") ?? "", "use-credentials"), ReferrerPolicy = script.ReferrerPolicy, Nonce = ContentSecurityPolicy.GetNonceMetadata(script), ParserInserted = script.IsParserInserted }
            : new ModuleFetchOptions();
        init;
    }
}
