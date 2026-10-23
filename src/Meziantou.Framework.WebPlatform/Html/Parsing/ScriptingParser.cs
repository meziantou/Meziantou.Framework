using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Html.Parsing;

/// <summary>
/// Drives the HTML or XML parser of a document: the tree construction pauses at a parser-blocking script until the
/// script is fetched and the document has no style sheet that is blocking scripts
/// (https://html.spec.whatwg.org/multipage/parsing.html#scriptEndTag), then the parser runs the steps
/// of "the end" (https://html.spec.whatwg.org/multipage/parsing.html#the-end): the scripts that will execute when the
/// document has finished parsing, then the DOMContentLoaded and load events. When the parser waits for a script, it
/// resumes from the task that marks the script as ready: the rest of the document is parsed by the tasks of the
/// <see cref="DomEventLoop"/>.
/// </summary>
internal abstract class ScriptingParser
{
    private readonly bool _runTheEnd;
    private HtmlScriptElement? _parsingBlockingScript;
    private bool _stopped;
    private bool _running;
    private TaskCompletionSource? _completion;

    /// <param name="document">The document being parsed.</param>
    /// <param name="runTheEnd">Whether the parser runs the steps of "the end" (the document has a browsing context).</param>
    protected ScriptingParser(Document document, bool runTheEnd)
    {
        Document = document;
        _runTheEnd = runTheEnd;
    }

    public Document Document { get; }

    /// <summary>Gets whether the parser has finished (or was aborted).</summary>
    public bool IsFinished { get; private set; }

    /// <summary>Gets whether the parser paused at least once, waiting for a script.</summary>
    public bool HasPaused { get; private set; }

    /// <summary>
    /// Gets or sets whether the parser was created by <c>document.open()</c> (a script-created parser): its input ends when
    /// <c>document.close()</c> is called.
    /// </summary>
    public bool IsScriptCreated { get; init; }

    /// <summary>Gets whether the tree construction stopped at the end of the input before the end of the file (a script-created parser waits for more input).</summary>
    protected virtual bool IsWaitingForInput => false;

    /// <summary>
    /// Runs the tree construction until the end of the input, or until a parser-blocking script is pending.
    /// </summary>
    /// <returns>True when the input is consumed (the parser stopped).</returns>
    protected abstract bool RunTreeConstruction();

    /// <summary>Called when the parser starts waiting for the pending parsing-blocking script to be fetched.</summary>
    protected virtual void OnWaitingForParsingBlockingScript()
    {
    }

    /// <summary>Executes the pending parsing-blocking script, with the script nesting level of the parser incremented.</summary>
    protected virtual void ExecuteParsingBlockingScript(HtmlScriptElement script) => script.Execute();

    /// <summary>Starts the parser. It returns when the document is parsed, or when the parser waits for a script.</summary>
    public void Start()
    {
        if (_runTheEnd || IsScriptCreated)
        {
            Document.ActiveParser = this;
        }

        Continue();
    }

    /// <summary>Gets whether the parser is executing a script (its script nesting level is not zero).</summary>
    protected virtual bool IsExecutingScript => false;

    /// <summary>
    /// Runs the parser after <c>document.write()</c> or <c>document.close()</c> (e.g. to wait for a parsing-blocking script,
    /// or to reach the end of the file). A script executed by the parser does not run the parser: the parser continues
    /// when the script returns.
    /// </summary>
    public void Resume()
    {
        if (!IsExecutingScript)
        {
            Continue();
        }
    }

    /// <summary>Returns a task completed when the parser has finished.</summary>
    public Task WaitForCompletionAsync()
    {
        if (IsFinished)
            return Task.CompletedTask;

        _completion ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _completion.Task;
    }

    /// <summary>
    /// Aborts the parser (https://html.spec.whatwg.org/multipage/parsing.html#abort-a-parser): it stops parsing, and does not
    /// run the steps of "the end".
    /// </summary>
    /// <param name="updateReadiness">
    /// Whether the readiness of the document is updated to "interactive", then to "complete" (the parser of a document that is
    /// aborted, e.g. by <c>window.stop()</c>). The parser replaced by the one of <c>document.open()</c> does not update it.
    /// </param>
    public void Abort(bool updateReadiness = false)
    {
        if (IsFinished)
            return;

        if (_parsingBlockingScript is not null)
        {
            _parsingBlockingScript.ReadyToBeParserExecutedCallback = null;
            _parsingBlockingScript = null;
        }

        Document.NoStyleSheetBlockingScriptsCallback = null;

        Finish();
        if (updateReadiness && _runTheEnd)
        {
            // The pending content is thrown away (the parser may be running a script), and the open elements are popped
            Document.UpdateReadiness(DocumentReadyState.Interactive);
            AbortTreeConstruction();
            Document.UpdateReadiness(DocumentReadyState.Complete);
        }
    }

    /// <summary>Stops the tree construction: the pending content is thrown away, and the open elements are popped.</summary>
    protected virtual void AbortTreeConstruction()
    {
    }

    private void Continue()
    {
        // The parser is not reentrant: a script ready while the parser runs is handled by the running loop
        if (_running)
            return;

        _running = true;
        try
        {
            while (!IsFinished)
            {
                if (!_stopped)
                {
                    // https://html.spec.whatwg.org/multipage/parsing.html#scriptEndTag: the pending parsing-blocking script
                    if (_parsingBlockingScript is null && Document.PendingParsingBlockingScript is { } pending)
                    {
                        Document.PendingParsingBlockingScript = null;
                        _parsingBlockingScript = pending;
                    }

                    if (_parsingBlockingScript is { } script)
                    {
                        if (!script.ReadyToBeParserExecuted)
                        {
                            HasPaused = true;
                            script.ReadyToBeParserExecutedCallback = Continue;
                            OnWaitingForParsingBlockingScript();
                            return;
                        }

                        if (Document.HasStyleSheetThatIsBlockingScripts)
                        {
                            HasPaused = true;
                            Document.NoStyleSheetBlockingScriptsCallback = Continue;
                            return;
                        }

                        _parsingBlockingScript = null;
                        if (IsAborted())
                            return;

                        ExecuteParsingBlockingScript(script);
                        continue;
                    }

                    var consumed = RunTreeConstruction();

                    // A script run by the tree construction aborted the parser (e.g. window.stop())
                    if (IsFinished)
                        return;

                    if (!consumed)
                    {
                        // A script-created parser waits for document.write() or document.close()
                        if (IsWaitingForInput)
                            return;

                        continue;
                    }

                    // https://html.spec.whatwg.org/multipage/parsing.html#the-end
                    _stopped = true;
                    if (!_runTheEnd)
                    {
                        Finish();
                        return;
                    }

                    Document.UpdateReadiness(DocumentReadyState.Interactive);
                }

                if (IsAborted())
                    return;

                // The scripts that will execute when the document has finished parsing: each one waits until it is ready to be
                // parser-executed and the document has no style sheet that is blocking scripts
                var deferredScripts = Document.ScriptsToExecuteWhenParsingFinished;
                if (deferredScripts.Count > 0)
                {
                    var script = deferredScripts[0];
                    if (!script.ReadyToBeParserExecuted)
                    {
                        HasPaused = true;
                        script.ReadyToBeParserExecutedCallback = Continue;
                        return;
                    }

                    if (Document.HasStyleSheetThatIsBlockingScripts)
                    {
                        HasPaused = true;
                        Document.NoStyleSheetBlockingScriptsCallback = Continue;
                        return;
                    }

                    script.Execute();

                    // The script may have changed the list (e.g. document.open() aborts the parser)
                    deferredScripts.Remove(script);
                    continue;
                }

                Finish();
                Document.FinishParsingAfterDeferredScripts();
            }
        }
        finally
        {
            _running = false;
        }
    }

    /// <summary>Whether the document was unloaded while the parser was waiting: the parser is aborted.</summary>
    private bool IsAborted()
    {
        if (!_runTheEnd || Document.BrowsingContext is not null)
            return false;

        // https://html.spec.whatwg.org/multipage/document-lifecycle.html#abort-a-document
        Document.ActiveParserWasAborted = true;
        Finish();
        return true;
    }

    private void Finish()
    {
        IsFinished = true;
        if (ReferenceEquals(Document.ActiveParser, this))
        {
            Document.ActiveParser = null;
        }

        _completion?.TrySetResult();
    }
}

/// <summary>The <see cref="ScriptingParser"/> of the HTML parser.</summary>
internal sealed class HtmlScriptingParser(Document document, HtmlTreeBuilder builder, bool runTheEnd) : ScriptingParser(document, runTheEnd)
{
    public HtmlTreeBuilder TreeBuilder { get; } = builder;

    protected override bool RunTreeConstruction() => TreeBuilder.Run();

    protected override bool IsWaitingForInput => TreeBuilder.IsWaitingForInput;

    protected override bool IsExecutingScript => TreeBuilder.ScriptNestingLevel > 0;

    protected override void AbortTreeConstruction() => TreeBuilder.Abort();

    // The speculative parser that runs while the parser waits for the pending parsing-blocking script
    private SpeculativeHtmlParser? _speculativeParser;

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#speculative-html-parsing</summary>
    protected override void OnWaitingForParsingBlockingScript() => _speculativeParser ??= SpeculativeHtmlParser.Start(Document, TreeBuilder);

    /// <summary>Gives the characters appended to the input stream (or the end of the file) to the speculative parser, if any.</summary>
    public void AppendSpeculativeInput(string text, bool endOfFile) => _speculativeParser?.Append(text, endOfFile);

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#scriptEndTag (the steps for the pending parsing-blocking script)</summary>
    protected override void ExecuteParsingBlockingScript(HtmlScriptElement script)
    {
        // https://html.spec.whatwg.org/multipage/parsing.html#stop-the-speculative-html-parser
        _speculativeParser = null;

        // The characters written by the script are inserted just before the next input character; then the insertion point is undefined again
        TreeBuilder.InsertionPoint = TreeBuilder.Tokenizer.PositionFromEnd;
        TreeBuilder.ScriptNestingLevel++;
        script.Execute();
        TreeBuilder.ScriptNestingLevel--;
        TreeBuilder.InsertionPoint = IsScriptCreated ? 0 : null;
        if (TreeBuilder.ScriptNestingLevel == 0)
        {
            TreeBuilder.ParserPauseFlag = false;
            TreeBuilder.RunPendingSvgScripts();
        }
    }
}

/// <summary>
/// The <see cref="ScriptingParser"/> of the XML parser (https://html.spec.whatwg.org/multipage/xhtml.html#parsing-xhtml-documents).
/// A document that is not well-formed is replaced by a <c>parsererror</c> element, unless <see cref="ThrowOnError"/> is true and
/// the parser did not pause.
/// </summary>
internal sealed class XmlScriptingParser(Document document, string input, bool runTheEnd) : ScriptingParser(document, runTheEnd)
{
    private readonly Xml.XmlTreeBuilder _builder = Xml.XmlTreeBuilder.CreateResumable(document, input);

    /// <summary>Whether a well-formedness error is thrown when the parser did not pause (the API of <see cref="XmlDocumentParser"/>).</summary>
    public bool ThrowOnError { get; init; }

    protected override bool RunTreeConstruction()
    {
        try
        {
            return _builder.RunUntilPause();
        }
        catch (System.Xml.XmlException ex) when (!ThrowOnError || HasPaused)
        {
            ShowError(Document, ex);
            return true;
        }
    }

    /// <summary>The document shows the error, as the document returned by DOMParser.</summary>
    public static void ShowError(Document document, System.Xml.XmlException error)
    {
        while (document.FirstChild is { } child)
        {
            document.RemoveChildCore(child, suppressObservers: false);
        }

        var root = document.CreateElementNS(DomParser.ParserErrorNamespace, "parsererror");
        root.AppendNode(new Text(document, error.Message));
        document.AppendNode(root);
    }
}
