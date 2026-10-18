using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>The type of a module (https://html.spec.whatwg.org/multipage/webappapis.html#module-type-from-module-request).</summary>
public enum ModuleType
{
    /// <summary>A JavaScript module.</summary>
    JavaScript,

    /// <summary>A JSON module (<c>with { type: "json" }</c>): its default export is the parsed JSON value.</summary>
    Json,

    /// <summary>A CSS module (<c>with { type: "css" }</c>): its default export is a constructed <c>CSSStyleSheet</c>.</summary>
    Css,

    /// <summary>A text module (<c>with { type: "text" }</c>): its default export is the response decoded as UTF-8, whatever its MIME type.</summary>
    Text,
}

/// <summary>
/// A module script (https://html.spec.whatwg.org/multipage/webappapis.html#module-script): the root of a module graph, the
/// script of a <c>&lt;script type="module"&gt;</c> element, whose descendants are loaded and that is run by the
/// <see cref="ScriptEngine"/> (see <see cref="ScriptEngine.LoadModuleScriptGraph"/> and <see cref="ScriptEngine.ExecuteModuleScript"/>).
/// </summary>
public sealed class ModuleScript
{
    /// <summary>Creates a module script.</summary>
    /// <param name="global">The global object of the realm in which the script runs.</param>
    /// <param name="sourceText">The source text of the script.</param>
    /// <param name="baseUrl">The base URL of the script: the URL of an external script, or the base URL of the document of an inline script.</param>
    public ModuleScript(GlobalScope global, string sourceText, string baseUrl)
    {
        ArgumentNullException.ThrowIfNull(global);
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentNullException.ThrowIfNull(baseUrl);
        Global = global;
        SourceText = sourceText;
        BaseUrl = baseUrl;
        Url = baseUrl;
    }

    /// <summary>Gets the global object of the realm of the script.</summary>
    public GlobalScope Global { get; }

    /// <summary>Gets the source text of the script.</summary>
    public string SourceText { get; }

    /// <summary>Gets the base URL of the script, used to resolve its module specifiers and as <c>import.meta.url</c>.</summary>
    public string BaseUrl { get; }

    /// <summary>Gets the URL of the script, used as the file name of the errors: the URL of an external script, or the URL of the document of an inline script.</summary>
    public string Url { get; init; }

    /// <summary>Gets whether the script is an inline script (its source text is the child text content of the element).</summary>
    public bool IsInline { get; init; }

    /// <summary>
    /// Gets the line (1-based) of the start of the source text in the document of an inline script: the line numbers of its
    /// errors are the lines in the document. The default value is 1.
    /// </summary>
    public int SourceTextLine { get; init; } = 1;

    /// <summary>Gets the column (0-based, in UTF-16 code units) of the start of the source text in its line (see <see cref="SourceTextLine"/>).</summary>
    public int SourceTextColumn { get; init; }

    /// <summary>Gets the script element whose script this is.</summary>
    public Element? Element { get; init; }

    /// <summary>Gets the options used to fetch the descendants of the script (https://html.spec.whatwg.org/multipage/webappapis.html#descendant-script-fetch-options).</summary>
    public ModuleFetchOptions FetchOptions { get; init; } = new();

    /// <summary>
    /// Gets or sets the error to rethrow (https://html.spec.whatwg.org/multipage/webappapis.html#concept-script-error-to-rethrow):
    /// a parse error, or an error of the module graph, reported when the script runs. Set by the engine.
    /// </summary>
    public object? ErrorToRethrow { get; set; }

    /// <summary>Gets or sets the record of the script in the engine (e.g. its module record). Set by the engine.</summary>
    public object? Record { get; set; }

    /// <summary>
    /// The state of the evaluation promise of the script (https://html.spec.whatwg.org/multipage/webappapis.html#run-a-module-script)
    /// once the script ran and the microtasks were processed: a pending promise is the one of an asynchronous module (a
    /// top-level <c>await</c>). Set by the engine; <see cref="ModuleEvaluationState.Unknown"/> when the engine does not report it.
    /// </summary>
    internal ModuleEvaluationState EvaluationState { get; set; }
}

/// <summary>The state of the evaluation promise of a module script (see <see cref="ModuleScript.EvaluationState"/>).</summary>
internal enum ModuleEvaluationState
{
    Unknown,
    Pending,
    Fulfilled,
    Rejected,
}

/// <summary>The options of the fetches of the module scripts (https://html.spec.whatwg.org/multipage/webappapis.html#script-fetch-options).</summary>
public sealed class ModuleFetchOptions
{
    /// <summary>Gets whether the credentials are included in the cross-origin requests (the <c>use-credentials</c> state of the <c>crossorigin</c> attribute).</summary>
    public bool IncludeCredentials { get; init; }

    /// <summary>Gets the referrer policy of the requests (the <c>referrerpolicy</c> attribute), or the empty string.</summary>
    public string ReferrerPolicy { get; init; } = "";

    /// <summary>Gets the cryptographic nonce metadata of the requests (the nonce of the <c>script</c> element), or the empty string.</summary>
    public string Nonce { get; init; } = "";

    /// <summary>Gets whether the parser metadata of the requests is "parser-inserted" (the <c>script</c> element was inserted by the parser).</summary>
    public bool ParserInserted { get; init; }

    /// <summary>
    /// Gets whether the options are the ones of a referencing script that the engine cannot determine (e.g. an <c>import()</c>
    /// after an <c>await</c>, when the engine does not track the active script in the promise jobs): the content security
    /// policies assume that the nonce of the requests matches (rather than blocking the imports of a script allowed by its nonce).
    /// </summary>
    internal bool IsUnknownReferrer { get; init; }

    /// <summary>
    /// The credentials mode of the fetches when it is not given by <see cref="IncludeCredentials"/>: the mode of the
    /// <c>credentials</c> option of a worklet (https://html.spec.whatwg.org/multipage/worklets.html#dom-workletoptions-credentials), which can be "omit".
    /// </summary>
    internal FetchCredentialsMode? CredentialsMode { get; init; }

    /// <summary>
    /// Returns the options of a fetched module whose response has a <c>Referrer-Policy</c> header
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-single-module-script): the policy of the header, when
    /// it is not empty, is the referrer policy of the fetches of its descendants.
    /// </summary>
    internal ModuleFetchOptions WithResponseReferrerPolicy(string referrerPolicy) => referrerPolicy.Length == 0 ? this : new ModuleFetchOptions
    {
        IncludeCredentials = IncludeCredentials,
        ReferrerPolicy = referrerPolicy,
        Nonce = Nonce,
        ParserInserted = ParserInserted,
        IsUnknownReferrer = IsUnknownReferrer,
        CredentialsMode = CredentialsMode,
        RenderBlocking = RenderBlocking,
    };

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#concept-script-fetch-options-render-blocking: the script element of the module script was render-blocking when it was prepared.</summary>
    internal bool RenderBlocking { get; init; }
}
