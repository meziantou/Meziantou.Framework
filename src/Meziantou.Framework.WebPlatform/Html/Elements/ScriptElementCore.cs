using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The state and the processing model of a script element (https://html.spec.whatwg.org/multipage/scripting.html#script-processing-model),
/// shared by the HTML and the SVG script elements: as in the browsers, an SVG <c>script</c> element is prepared and executed
/// as an HTML one (https://w3c.github.io/svgwg/svg2-draft/interact.html#ScriptElement): its classic, module and import map
/// scripts, with the <c>async</c> and <c>defer</c> attributes. The source of an SVG script is its <c>href</c> attribute,
/// and its <c>language</c>, <c>nomodule</c>, <c>event</c>, <c>for</c>, <c>charset</c>, <c>integrity</c> and
/// <c>blocking</c> attributes are ignored.
/// </summary>
internal sealed class ScriptElementCore(Element element)
{
    /// <summary>The <c>script</c> element (an <see cref="HtmlScriptElement"/> or an <see cref="SvgScriptElement"/>).</summary>
    public Element Element { get; } = element;

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#script-force-async: unset by the parsers.</summary>
    public bool ForceAsync { get; set; } = true;

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#parser-document: set by the HTML and XML parsers on the script elements they insert.</summary>
    public Document? ParserDocument { get; set; }

    /// <summary>
    /// The line (1-based) and the column (0-based) in its document of the source text of a script created by the HTML parser,
    /// for the positions of the errors of an inline script; (1, 0) for the other scripts.
    /// </summary>
    public (int Line, int Column) SourceTextPosition
    {
        get => _sourceTextPosition ?? (1, 0);
        set => _sourceTextPosition = value;
    }

    private (int Line, int Column)? _sourceTextPosition;

    /// <summary>Whether <see cref="SourceTextPosition"/> is the position of the source text in the document (the script was created by the HTML parser).</summary>
    public bool HasSourceTextPosition => _sourceTextPosition is not null;

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#parser-inserted</summary>
    public bool IsParserInserted => ParserDocument is not null;

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#preparation-time-document</summary>
    public Document? PreparationTimeDocument { get; private set; }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#already-started</summary>
    public bool AlreadyStarted { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#concept-script-external</summary>
    public bool FromExternalFile { get; private set; }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#concept-script-type: determined when the element is prepared.</summary>
    public ScriptElementType? ScriptType { get; private set; }

    /// <summary>
    /// The script text (https://w3c.github.io/trusted-types/dist/spec/#htmlscriptelement-script-text and
    /// https://w3c.github.io/trusted-types/dist/spec/#svgscriptelement-script-text): the child text content set by a Trusted
    /// Types compliant sink or by the parser. When it differs from the child text content, the default policy is applied to the
    /// child text content when the element is prepared.
    /// </summary>
    public string ScriptText { get; set; } = "";

    /// <summary>
    /// The result of the script element (https://html.spec.whatwg.org/multipage/scripting.html#concept-script-result): the
    /// script (a <see cref="Scripting.ClassicScript"/> or a <see cref="Scripting.ModuleScript"/>), a speculation rules parse
    /// result, or null when it could not be fetched. It is "uninitialized" until <see cref="HasResult"/> is true.
    /// </summary>
    public object? Result { get; set; }

    /// <summary>Whether the result is not "uninitialized".</summary>
    public bool HasResult { get; private set; }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#steps-to-run-when-the-result-is-ready</summary>
    private Action? _stepsToRunWhenTheResultIsReady;

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#ready-to-be-parser-executed</summary>
    public bool ReadyToBeParserExecuted { get; private set; }

    /// <summary>Called when <see cref="ReadyToBeParserExecuted"/> becomes true (the parser waiting for the script resumes).</summary>
    public Action? ReadyToBeParserExecutedCallback { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#concept-script-delay-load</summary>
    private bool _delayingTheLoadEvent;

    /// <summary>Whether the script was streamed by a parser that was aborted before it ran (see <see cref="DiscardScriptOfAbortedParser"/>).</summary>
    private bool _discardedByAbortedParser;

    /// <summary>The document whose rendering is blocked by the element (https://html.spec.whatwg.org/multipage/dom.html#block-rendering).</summary>
    private Document? _documentWhoseRenderingIsBlocked;

    private HtmlScriptElement? HtmlElement => Element as HtmlScriptElement;

    /// <summary>The name of the Trusted Types sink of the script text of the element.</summary>
    private string TextSinkName => Element is SvgScriptElement ? "SVGScriptElement text" : "HTMLScriptElement text";

    /// <summary>The source of the element: the <c>src</c> attribute of an HTML script, the <c>href</c> (or <c>xlink:href</c>) attribute of an SVG script.</summary>
    public string? Source => Element is SvgScriptElement ? Element.GetAttributeValue("href") ?? Element.GetAttributeValue(Namespaces.XLink, "href") : Element.GetAttributeValue("src");

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#dom-script-async</summary>
    public bool Async
    {
        get => ForceAsync || Element.HasAttributeWithLocalName("async");
        set
        {
            ForceAsync = false;
            Reflection.SetBoolean(Element, "async", value);
        }
    }

    /// <summary>
    /// https://w3c.github.io/trusted-types/dist/spec/#prepare-script-text: applies the default policy to the child text content
    /// when it was not set by a Trusted Types compliant sink. Returns false when the script must not run.
    /// </summary>
    private bool PrepareScriptText()
    {
        var childTextContent = Element.GetChildTextContent();
        if (ScriptText == childTextContent)
            return true;

        try
        {
            ScriptText = TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedScript, Element.NodeDocument, childTextContent, TextSinkName);
            return true;
        }
#pragma warning disable CA1031 // The exceptions of the default policy are reported and the script does not run
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // "If that algorithm threw an error, then return": the error of the default policy is reported
            if (ex is not TypeErrorException)
            {
                Scripting.ScriptErrors.Report(ex);
            }

            return false;
        }
    }

    /// <summary>
    /// The attribute change steps of the processing model: setting <c>async</c> unsets force async, and setting the source of
    /// a connected script prepares it.
    /// </summary>
    public void AttributeChangeSteps(string localName, string? value, string? @namespace)
    {
        if (localName == "async" && @namespace is null)
        {
            ForceAsync = false;
        }
        else if (value is not null && Element.IsConnected && (Element is SvgScriptElement ? localName == "href" && @namespace is null or Namespaces.XLink : localName == "src" && @namespace is null))
        {
            PostConnectionSteps();
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/urls-and-fetching.html#potentially-render-blocking: the blocking attribute of the
    /// element contains "render", or the element is implicitly potentially render-blocking
    /// (https://html.spec.whatwg.org/multipage/scripting.html#the-script-element: a parser-inserted classic script without
    /// async or defer attribute).
    /// </summary>
    public bool IsPotentiallyRenderBlocking => (Element is HtmlScriptElement && Document.HasRenderBlockingToken(Element))
        || (ScriptType == ScriptElementType.Classic && IsParserInserted && !Element.HasAttributeWithLocalName("async") && !Element.HasAttributeWithLocalName("defer"));

    private void BlockRendering(Document document)
    {
        _documentWhoseRenderingIsBlocked = document;
        document.BlockRendering(Element);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#unblock-rendering</summary>
    public void StopBlockingRendering()
    {
        if (_documentWhoseRenderingIsBlocked is not { } document)
            return;

        _documentWhoseRenderingIsBlocked = null;
        document.UnblockRendering(Element);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#script-processing-model: the script HTML element post-connection steps.</summary>
    public void PostConnectionSteps()
    {
        if (IsParserInserted)
            return;

        Prepare();
    }

    private void SetDelayingTheLoadEvent(bool value)
    {
        if (_delayingTheLoadEvent == value || PreparationTimeDocument is not { } document)
            return;

        _delayingTheLoadEvent = value;
        document.ScriptsDelayingTheLoadEvent += value ? 1 : -1;
        if (!value)
        {
            document.TryQueueLoadEvent();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#mark-as-ready</summary>
    private void MarkAsReady(object? result)
    {
        Result = result;
        HasResult = true;
        var steps = _stepsToRunWhenTheResultIsReady;
        _stepsToRunWhenTheResultIsReady = null;
        steps?.Invoke();
        SetDelayingTheLoadEvent(false);
    }

    /// <summary>
    /// Discards a streamed script that has not run when its parser is aborted: it is the pending parsing-blocking script of the
    /// parser, which never runs, and on which the rendering is unblocked (https://whatpr.org/html/12758/parsing.html#abort-a-parser).
    /// </summary>
    public void DiscardScriptOfAbortedParser()
    {
        _discardedByAbortedParser = true;
        StopBlockingRendering();
    }

    private void SetReadyToBeParserExecuted()
    {
        ReadyToBeParserExecuted = true;
        var callback = ReadyToBeParserExecutedCallback;
        ReadyToBeParserExecutedCallback = null;
        callback?.Invoke();
    }

    /// <summary>The options of the module fetches of the element (https://html.spec.whatwg.org/multipage/webappapis.html#descendant-script-fetch-options).</summary>
    /// <param name="renderBlocking">Whether the render-blocking flag of the options is the one of the element (else it is false).</param>
    public Scripting.ModuleFetchOptions CreateModuleFetchOptions(bool renderBlocking = true) => new()
    {
        IncludeCredentials = InfraAscii.EqualsIgnoreAsciiCase(Element.GetAttributeValue("crossorigin") ?? "", "use-credentials"),
        ReferrerPolicy = Element switch
        {
            HtmlScriptElement script => script.ReferrerPolicy,
            SvgScriptElement script => script.ReferrerPolicy,
            _ => "",
        },
        Nonce = ContentSecurityPolicy.GetNonceMetadata(Element),
        ParserInserted = IsParserInserted,
        RenderBlocking = renderBlocking && Element.NodeDocument.IsRenderBlocking(Element),
    };

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/scripting.html#prepare-the-script-element. The script is fetched by the
    /// <see cref="BrowsingContextOptions.DocumentLoader"/> and run by the <see cref="BrowsingContextOptions.ScriptEngine"/>:
    /// nothing is fetched or run when the browsing context has no script engine.
    /// </summary>
    public void Prepare()
    {
        if (AlreadyStarted)
            return;

        var parserDocument = ParserDocument;
        ParserDocument = null;
        if (parserDocument is not null && !Element.HasAttributeWithLocalName("async"))
        {
            ForceAsync = true;
        }

        // https://w3c.github.io/trusted-types/dist/spec/#slot-value-verification
        if (!PrepareScriptText())
            return;

        var sourceText = ScriptText;
        var src = Source;
        if (src is null && sourceText.Length == 0)
            return;

        if (!Element.IsConnected)
            return;

        if (GetScriptTypeFromAttributes() is not { } scriptType)
            return;

        ScriptType = scriptType;

        if (parserDocument is not null)
        {
            ParserDocument = parserDocument;
            ForceAsync = false;
        }

        AlreadyStarted = true;
        var document = Element.NodeDocument;
        PreparationTimeDocument = document;
        if (parserDocument is not null && !ReferenceEquals(parserDocument, document))
            return;

        // https://html.spec.whatwg.org/multipage/webappapis.html#concept-n-noscript: scripting is disabled for the documents
        // that have no browsing context (e.g. the documents created by DOMParser or createHTMLDocument()), and for the
        // sandboxed documents without allow-scripts
        if (document.BrowsingContext is not { Options.ScriptingEnabled: true } browsingContext || !document.ScriptingEnabled)
            return;

        var htmlElement = HtmlElement;
        if (ScriptType == ScriptElementType.Classic && htmlElement is not null && htmlElement.HasAttributeWithLocalName("nomodule"))
            return;

        // https://w3c.github.io/webappsec-csp/#should-block-inline
        if (src is null && ContentSecurityPolicy.ShouldBlockInline(Element, ContentSecurityPolicyInlineType.Script, sourceText, IsParserInserted))
            return;

        if (ScriptType == ScriptElementType.Classic && htmlElement is not null && htmlElement.GetAttributeValue("event") is { } eventValue && htmlElement.GetAttributeValue("for") is { } forValue)
        {
            if (!InfraAscii.StripLeadingAndTrailingAsciiWhitespace(forValue).Equals("window", StringComparison.OrdinalIgnoreCase))
                return;

            var @event = InfraAscii.StripLeadingAndTrailingAsciiWhitespace(eventValue);
            if (!@event.Equals("onload", StringComparison.OrdinalIgnoreCase) && !@event.Equals("onload()", StringComparison.OrdinalIgnoreCase))
                return;
        }

        string? url = null;
        Urls.UrlRecord? parsedUrl = null;
        if (src is not null)
        {
            // External import maps and speculation rules are not supported
            if (ScriptType is ScriptElementType.ImportMap or ScriptElementType.SpeculationRules || src.Length == 0)
            {
                QueueErrorEvent();
                return;
            }

            // The URL record is fetched, rather than its serialization: the serialization does not keep its flags (e.g. the
            // potentially dangling markup flag, which blocks the fetch of a URL whose input had a newline and a "<")
            FromExternalFile = true;
            parsedUrl = Reflection.EncodingParseUrl(document, src);
            if (parsedUrl is null)
            {
                QueueErrorEvent();
                return;
            }

            url = parsedUrl.Serialize();
        }

        // https://html.spec.whatwg.org/multipage/scripting.html#prepare-the-script-element, step 32: the result of an inline
        // speculation rules script is ready immediately. No script engine is needed.
        if (ScriptType == ScriptElementType.SpeculationRules)
        {
            if (!browsingContext.Options.EffectiveFeatures.SpeculationRulesEnabled || document.RelevantWindow is null)
                return;

            MarkAsReady(HtmlScriptElement.CreateSpeculationRulesResult(sourceText, document));
            if (parserDocument is not null && parserDocument.HasStyleSheetThatIsBlockingScripts && parserDocument.ActiveParser is not Html.Parsing.HtmlScriptingParser { TreeBuilder.ScriptNestingLevel: > 1 })
            {
                parserDocument.PendingParsingBlockingScript = this;
                ReadyToBeParserExecuted = true;
                return;
            }

            Execute();
            return;
        }

        // The module scripts need an engine that supports them
        if (browsingContext.Options.ScriptEngine is not { } engine || document.RelevantWindow is not { } window)
            return;

        if (ScriptType == ScriptElementType.Module && !engine.SupportsModuleScripts)
            return;

        // https://html.spec.whatwg.org/multipage/webappapis.html#register-an-import-map: an inline import map is registered immediately
        if (ScriptType == ScriptElementType.ImportMap)
        {
            RegisterImportMap(window, sourceText);
            return;
        }

        if (url is null && ScriptType == ScriptElementType.Classic)
        {
            // An inline classic script is ready immediately. A script of the parser waits for the style sheets that are
            // blocking scripts (the style sheets of the link and style elements and of the xml-stylesheet processing
            // instructions created by the parser).
            MarkAsReady(new Scripting.ClassicScript(window, sourceText, document.BaseUri) { Url = document.Url, Element = Element, SourceTextLine = SourceTextPosition.Line, SourceTextColumn = SourceTextPosition.Column });
            if (parserDocument is not null && parserDocument.HasStyleSheetThatIsBlockingScripts && parserDocument.ActiveParser is not Html.Parsing.HtmlScriptingParser { TreeBuilder.ScriptNestingLevel: > 1 })
            {
                parserDocument.PendingParsingBlockingScript = this;
                ReadyToBeParserExecuted = true;
                return;
            }

            Execute();
            return;
        }

        // https://html.spec.whatwg.org/multipage/scripting.html#prepare-the-script-element: "If el is potentially
        // render-blocking, then block rendering on el" (an external script, or an inline module script)
        if (IsPotentiallyRenderBlocking)
        {
            BlockRendering(document);
        }

        SetDelayingTheLoadEvent(true);
        var completedSynchronously = false;
        Scripting.ClassicScript? fetchedScript = null;
        if (ScriptType == ScriptElementType.Classic)
        {
            // A script that is not run by the parser is marked as ready by a task (see below)
            var markedAsReadyByTheParser = parserDocument is not null && !Element.HasAttributeWithLocalName("async") && !ForceAsync && parserDocument.ActiveParser is not Html.Parsing.ScriptingParser { IsScriptCreated: true };
            completedSynchronously = Scripting.ScriptFetching.FetchClassicScript(Element, browsingContext, window, parsedUrl!, MarkAsReady, out fetchedScript, reportTimingFromTask: !markedAsReadyByTheParser);
        }

        bool executedByTheParser;
        if (Element.HasAttributeWithLocalName("async") || ForceAsync)
        {
            // The set of scripts that will execute as soon as possible
            var scripts = document.ScriptsToExecuteAsSoonAsPossible;
            scripts.Add(this);
            _stepsToRunWhenTheResultIsReady = () =>
            {
                Execute();
                scripts.Remove(this);
                document.TryQueueLoadEvent();
            };
            executedByTheParser = false;
        }
        else if (parserDocument is null)
        {
            // The list of scripts that will execute in order as soon as possible
            var scripts = document.ScriptsToExecuteInOrderAsSoonAsPossible;
            scripts.Add(this);
            _stepsToRunWhenTheResultIsReady = () =>
            {
                if (!ReferenceEquals(scripts[0], this))
                    return;

                while (scripts.Count > 0 && scripts[0].HasResult)
                {
                    var script = scripts[0];
                    script.Execute();
                    scripts.RemoveAt(0);
                }

                document.TryQueueLoadEvent();
            };
            executedByTheParser = false;
        }
        else if (Element.HasAttributeWithLocalName("defer") || ScriptType == ScriptElementType.Module)
        {
            // The list of scripts that will execute when the document has finished parsing (the module scripts are deferred)
            parserDocument.ScriptsToExecuteWhenParsingFinished.Add(this);
            _stepsToRunWhenTheResultIsReady = SetReadyToBeParserExecuted;
            executedByTheParser = true;
        }
        else
        {
            parserDocument.PendingParsingBlockingScript = this;
            BlockRendering(document);
            _stepsToRunWhenTheResultIsReady = SetReadyToBeParserExecuted;
            executedByTheParser = true;
        }

        // The module scripts are fetched once they are scheduled: the result is ready from a task or a microtask
        if (ScriptType == ScriptElementType.Module)
        {
            FetchModuleScriptGraph(engine, window, parsedUrl, sourceText);
            return;
        }

        if (completedSynchronously)
        {
            // A fetch completes from a task. The scripts executed by the parser are marked as ready immediately, so that
            // the parser does not pause when the loader completes synchronously, except the ones of a script-created parser
            // (document.open()): the call to document.write() that inserted the script returns before the script runs.
            if (executedByTheParser && parserDocument?.ActiveParser is not Html.Parsing.ScriptingParser { IsScriptCreated: true })
            {
                MarkAsReady(fetchedScript);
            }
            else
            {
                browsingContext.EventLoop.QueueTask(() => MarkAsReady(fetchedScript));
            }
        }

        void QueueErrorEvent() => Element.NodeDocument.QueueElementTask(() => Element.FireEvent(new Event("error")));
    }

    /// <summary>
    /// Fetches the graph of a module script (https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-module-script-tree
    /// and https://html.spec.whatwg.org/multipage/webappapis.html#fetch-an-inline-module-script-graph): the engine fetches
    /// the root of an external script (with its module map), and loads the descendants. The result is null when a module of
    /// the graph cannot be fetched.
    /// </summary>
    private void FetchModuleScriptGraph(Scripting.ScriptEngine engine, Window window, Urls.UrlRecord? url, string sourceText)
    {
        var document = Element.NodeDocument;
        var options = CreateModuleFetchOptions();

        try
        {
            // The script is marked as ready from a task (https://github.com/whatwg/html/issues/9864), as in the browsers:
            // the graph is loaded by promise jobs (or synchronously for an inline script, or a root already in the module
            // map), and the script runs (then the microtasks it queues, before its load event) outside of the microtask checkpoint
            if (url is null)
            {
                var script = new Scripting.ModuleScript(window, sourceText, document.DocumentBaseUrl.Serialize()) { Url = document.Url, IsInline = true, Element = Element, FetchOptions = options, SourceTextLine = SourceTextPosition.Line, SourceTextColumn = SourceTextPosition.Column };
                engine.LoadModuleScriptGraph(script, success => window.QueueGlobalTask(() => MarkAsReady(success ? script : null)));
            }
            else
            {
                engine.FetchModuleScriptGraph(window, url.Serialize(), options.WithRootUrl(url), Element, script => window.QueueGlobalTask(() => MarkAsReady(script)));
            }
        }
#pragma warning disable CA1031 // An exception thrown by the engine is a failure of the graph
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Scripting.ScriptErrors.Report(ex, window);
            MarkAsReady(null);
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/webappapis.html#register-an-import-map: the import map is parsed and merged in
    /// the import map of the window; its errors are reported.
    /// </summary>
    private void RegisterImportMap(Window window, string sourceText)
    {
        Scripting.ImportMap importMap;
        try
        {
            importMap = Scripting.ModuleSpecifierResolution.Parse(sourceText, Element.NodeDocument.DocumentBaseUrl);
        }
        catch (System.Text.Json.JsonException ex)
        {
            Scripting.ScriptErrors.Report(new JavaScriptSyntaxErrorException("The import map is not valid JSON: " + ex.Message), window);
            return;
        }
        catch (TypeErrorException ex)
        {
            Scripting.ScriptErrors.Report(ex, window);
            return;
        }

        Scripting.ModuleSpecifierResolution.Merge(window.ImportMap, importMap);
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/scripting.html#execute-the-script-element
    /// </summary>
    public void Execute()
    {
        var document = Element.NodeDocument;
        if (!ReferenceEquals(PreparationTimeDocument, document) || _discardedByAbortedParser)
            return;

        StopBlockingRendering();

        if (Result is SpeculationRulesParseResult speculationRules)
        {
            if (document.RelevantWindow is { } rulesWindow)
            {
                HtmlElement?.RegisterSpeculationRules(speculationRules, rulesWindow);
            }

            return;
        }

        if (Result is not { } result || document.BrowsingContext?.Options.ScriptEngine is not { } engine)
        {
            Element.FireEvent(new Event("error"));
            return;
        }

        // A module script runs without currentScript, and cannot blow away the document with document.write() (the
        // ignore-destructive-writes counter)
        if (result is Scripting.ModuleScript moduleScript)
        {
            var previousCurrentScript = document.CurrentScript;
            document.CurrentScript = null;
            document.IgnoreDestructiveWritesCounter++;
            try
            {
                Scripting.ScriptExecution.RunModuleScript(engine, moduleScript);
            }
            finally
            {
                document.CurrentScript = previousCurrentScript;
                document.IgnoreDestructiveWritesCounter--;
            }

            if (FromExternalFile)
            {
                Element.FireEvent(new Event("load"));
            }

            return;
        }

        var script = (Scripting.ClassicScript)result;

        // https://html.spec.whatwg.org/multipage/scripting.html#execute-the-script-element: an external script cannot blow
        // away the document with document.write() (the ignore-destructive-writes counter)
        if (FromExternalFile)
        {
            document.IgnoreDestructiveWritesCounter++;
        }

        var oldCurrentScript = document.CurrentScript;
        document.CurrentScript = Element.GetRoot() is ShadowRoot ? null : Element;
        try
        {
            Scripting.ScriptExecution.RunClassicScript(engine, script);
        }
        finally
        {
            document.CurrentScript = oldCurrentScript;
            if (FromExternalFile)
            {
                document.IgnoreDestructiveWritesCounter--;
            }
        }

        if (FromExternalFile)
        {
            Element.FireEvent(new Event("load"));
        }
    }

    /// <summary>
    /// The type of the script given its type and language attributes (https://html.spec.whatwg.org/multipage/scripting.html#prepare-the-script-element,
    /// steps 8 to 13), or null when the script block's type string is not supported. An SVG script has no <c>language</c>
    /// attribute, and no speculation rules.
    /// </summary>
    public ScriptElementType? GetScriptTypeFromAttributes()
    {
        var isSvg = Element is SvgScriptElement;
        var type = Element.GetAttributeValue("type");
        var language = isSvg ? null : Element.GetAttributeValue("language");
        string typeString;
        if (type is { Length: 0 } || (type is null && language is null or { Length: 0 }))
        {
            typeString = "text/javascript";
        }
        else if (type is not null)
        {
            typeString = type;
        }
        else
        {
            typeString = "text/" + language;
        }

        // Only the JavaScript MIME type check strips the whitespace of the type attribute: " module " is not a module.
        if (HtmlScriptElement.IsJavaScriptMimeTypeEssenceMatch(typeString) || (type is not null && HtmlScriptElement.IsJavaScriptMimeTypeEssenceMatch(InfraAscii.StripLeadingAndTrailingAsciiWhitespace(type).ToString())))
            return ScriptElementType.Classic;

        if (typeString.Equals("module", StringComparison.OrdinalIgnoreCase))
            return ScriptElementType.Module;

        if (typeString.Equals("importmap", StringComparison.OrdinalIgnoreCase))
            return ScriptElementType.ImportMap;

        if (!isSvg && typeString.Equals("speculationrules", StringComparison.OrdinalIgnoreCase))
            return ScriptElementType.SpeculationRules;

        return null;
    }
}
