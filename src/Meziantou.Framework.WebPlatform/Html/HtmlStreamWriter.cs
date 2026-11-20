using Meziantou.Framework.WebPlatform.Html.Parsing;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The writer of the HTML streamed into an element or a shadow root (https://whatpr.org/html/12758/dynamic-markup-insertion.html#stream-html),
/// returned by <see cref="Element.StreamAppendHtmlUnsafe"/> and the other streaming methods: the chunks are parsed as they
/// are written, by a fragment parser that inserts the nodes directly into the target (before the reference child), so the
/// <c>template for</c> elements patch the content as it arrives. In JavaScript, the methods return a <c>WritableStream</c>
/// whose sink is this writer.
/// </summary>
/// <remarks>
/// With <c>runScripts</c>, the streamed scripts are parser-inserted: an inline script runs when its end tag is parsed, an
/// async script runs as soon as it is fetched, a parser-blocking script (an external classic script without <c>async</c>
/// or <c>defer</c>) pauses the parser until it is fetched and run from a task (the chunks written meanwhile are parsed
/// after it), and the <c>defer</c> and module scripts run from a task once the stream is closed.
/// </remarks>
public sealed class HtmlStreamWriter
{
    private readonly Node _target;
    private readonly Node? _referenceChild;
    private readonly HtmlTreeBuilder _builder;
    private readonly HtmlTokenizer _tokenizer;

    // https://whatpr.org/html/12758/parsing.html#fragment-parser-running: the parser is not reentrant, the input written by a
    // script that the parser runs is consumed by the running parser
    private bool _running;

    // The parsing-blocking script for which the parser waits (it is executed from a task), or null
    private ScriptElementCore? _parsingBlockingScript;
    private bool _blocked;

    // The fragment parser close promise, and the error of the stream (the reference child was removed from the target)
    private TaskCompletionSource? _closeCompletion;
    private DomException? _writeError;

    internal HtmlStreamWriter(Node target, Node? referenceChild, HtmlTreeBuilder builder, HtmlTokenizer tokenizer)
    {
        _target = target;
        _referenceChild = referenceChild;
        _builder = builder;
        _tokenizer = tokenizer;
        if (referenceChild is not null)
        {
            builder.InvalidReferenceChildSteps = RunInvalidReferenceChildSteps;
        }
    }

    /// <summary>Gets whether the stream is closed or aborted: the writer cannot be written to anymore.</summary>
    public bool IsCompleted { get; private set; }

    /// <summary>
    /// Gets or sets the callback that errors the stream of the scripts (the <c>WritableStream</c> of the binding) when the
    /// reference child is no longer a child of the target.
    /// </summary>
    internal Action<Exception>? ErrorStream { get; set; }

    /// <summary>Parses a chunk of HTML, after the previous chunks (the write algorithm of https://whatpr.org/html/12758/dynamic-markup-insertion.html#stream-html).</summary>
    /// <remarks>
    /// The method returns without waiting for a parsing-blocking script: the chunk is parsed once the script has run. A chunk
    /// written by a script that the parser runs is parsed after the rest of the current chunk.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The writer is closed or aborted.</exception>
    /// <exception cref="DomException">The reference child is no longer a child of the target: the parser is aborted.</exception>
    public void Write(string chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ThrowIfCompleted();
        if (_referenceChild is not null && !ReferenceEquals(_referenceChild.ParentNode, _target) && _builder.InvalidReferenceChildSteps is { } invalidReferenceChildSteps)
        {
            invalidReferenceChildSteps(_referenceChild);
        }

        if (_writeError is not null)
            throw _writeError;

        _tokenizer.Insert(0, chunk);
        RunFragmentParser();
        if (_writeError is not null)
            throw _writeError;
    }

    /// <summary>
    /// Parses the end of the input (the close algorithm of https://whatpr.org/html/12758/dynamic-markup-insertion.html#stream-html).
    /// When the parser waits for a parsing-blocking script, the rest of the input is parsed once the script has run.
    /// </summary>
    /// <exception cref="InvalidOperationException">The writer is closed or aborted.</exception>
    public void Close() => _ = CloseAsync();

    /// <summary>
    /// The close algorithm: returns a task completed when the parser stops parsing (after a pending parsing-blocking script
    /// and the rest of the input, before the <c>defer</c> scripts run), or faulted when the reference child is removed from
    /// the target before.
    /// </summary>
    internal Task CloseAsync()
    {
        ThrowIfCompleted();
        IsCompleted = true;
        if (_writeError is not null)
            return Task.FromException(_writeError);

        var completion = new TaskCompletionSource();
        _closeCompletion = completion;
        _tokenizer.EofInserted = true;
        RunFragmentParser();
        return completion.Task;
    }

    /// <summary>Aborts the parser: the rest of the input is not parsed, and the script elements that have not run yet never run.</summary>
    public void Abort()
    {
        if (IsCompleted && _closeCompletion is null)
            return;

        IsCompleted = true;
        AbortParser();
    }

    /// <summary>https://whatpr.org/html/12758/parsing.html#abort-a-fragment-parser</summary>
    private void AbortParser()
    {
        // A parser that waits for a script does not run: it is aborted at once
        if (_parsingBlockingScript is { } script)
        {
            _parsingBlockingScript = null;
            script.ReadyToBeParserExecutedCallback = null;
            script.DiscardScriptOfAbortedParser();
        }

        _builder.Abort();
        CompleteAbortedClose();
    }

    /// <summary>A close that waits for the parser completes when the parser is aborted (the stream is aborted or errored).</summary>
    private void CompleteAbortedClose()
    {
        if (_builder.IsAborted && _closeCompletion is { } closeCompletion)
        {
            _closeCompletion = null;
            closeCompletion.TrySetResult();
        }
    }

    /// <summary>https://whatpr.org/html/12758/dynamic-markup-insertion.html#stream-html (the invalid reference child steps)</summary>
    private void RunInvalidReferenceChildSteps(Node child)
    {
        if (!ReferenceEquals(child, _referenceChild) || ReferenceEquals(child.ParentNode, _target))
            return;

        _builder.InvalidReferenceChildSteps = null;
        IsCompleted = true;
        var error = new DomException("The node before which the HTML is inserted is no longer a child of the target", "HierarchyRequestError");
        _writeError = error;
        ErrorStream?.Invoke(error);
        if (_closeCompletion is { } closeCompletion)
        {
            _closeCompletion = null;
            closeCompletion.TrySetException(error);
        }

        AbortParser();
    }

    /// <summary>https://whatpr.org/html/12758/parsing.html#run-a-fragment-parser</summary>
    private void RunFragmentParser()
    {
        if (_running || _blocked || _builder.IsAborted)
            return;

        _running = true;
        bool stopped;
        try
        {
            using var ceReactions = CEReactionsScope.Enter();
            stopped = _builder.Run();
        }
        finally
        {
            _running = false;
        }

        if (_builder.IsAborted)
        {
            CompleteAbortedClose();
            return;
        }

        // https://whatpr.org/html/12758/parsing.html#scriptEndTag: the pending parsing-blocking script is executed from a task
        var parserDocument = _builder.Document;
        if (parserDocument.PendingParsingBlockingScript is { } script)
        {
            parserDocument.PendingParsingBlockingScript = null;
            _parsingBlockingScript = script;
            _blocked = true;
            if (_builder.StreamingScriptDocument?.RelevantWindow is { } window)
            {
                window.QueueGlobalTask(ExecutePendingParsingBlockingScripts);
            }

            return;
        }

        if (stopped)
        {
            TheEnd();
        }
    }

    /// <summary>https://whatpr.org/html/12758/parsing.html#execute-the-pending-parsing-blocking-scripts</summary>
    private void ExecutePendingParsingBlockingScripts()
    {
        while (_parsingBlockingScript is { } script)
        {
            if (_builder.IsAborted)
                return;

            if (!script.ReadyToBeParserExecuted)
            {
                script.ReadyToBeParserExecutedCallback = ExecutePendingParsingBlockingScripts;
                return;
            }

            _parsingBlockingScript = null;

            // The characters written by the script are inserted just before the next input character
            _builder.InsertionPoint = _tokenizer.PositionFromEnd;
            _builder.ScriptNestingLevel++;
            script.Execute();
            _builder.ScriptNestingLevel--;
            if (_builder.ScriptNestingLevel == 0)
            {
                _builder.ParserPauseFlag = false;
            }

            _builder.InsertionPoint = null;
            if (_builder.Document.PendingParsingBlockingScript is { } next)
            {
                _builder.Document.PendingParsingBlockingScript = null;
                _parsingBlockingScript = next;
            }
        }

        _blocked = false;
        RunFragmentParser();
    }

    /// <summary>
    /// https://whatpr.org/html/12758/parsing.html#the-end (fragment case): the close promise is resolved, then the scripts that
    /// will execute when the stream is closed run from a task, one per task.
    /// </summary>
    private void TheEnd()
    {
        if (_closeCompletion is { } closeCompletion)
        {
            _closeCompletion = null;
            closeCompletion.TrySetResult();
        }

        if (_builder.Document.ScriptsToExecuteWhenParsingFinished.Count > 0 && _builder.StreamingScriptDocument?.RelevantWindow is { } window)
        {
            window.QueueGlobalTask(ExecuteDeferredScripts);
        }
    }

    /// <summary>https://whatpr.org/html/12758/parsing.html#execute-the-deferred-scripts</summary>
    private void ExecuteDeferredScripts()
    {
        var scripts = _builder.Document.ScriptsToExecuteWhenParsingFinished;
        while (scripts.Count > 0)
        {
            var script = scripts[0];
            if (!script.ReadyToBeParserExecuted)
            {
                script.ReadyToBeParserExecutedCallback = ExecuteDeferredScripts;
                return;
            }

            scripts.RemoveAt(0);
            script.Execute();
        }
    }

    private void ThrowIfCompleted()
    {
        if (IsCompleted)
            throw new InvalidOperationException("The HTML stream is closed");
    }
}
