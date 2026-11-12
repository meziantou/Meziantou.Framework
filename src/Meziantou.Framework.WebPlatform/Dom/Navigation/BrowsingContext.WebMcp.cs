namespace Meziantou.Framework.WebPlatform.Dom;

// The browser agent of WebMCP (https://webmachinelearning.github.io/webmcp/#interaction-with-agents): the application
// discovers and executes the tools that the documents registered with document.modelContext.registerTool()
public sealed partial class BrowsingContext
{
    /// <summary>
    /// Returns the tools registered by the active document of this browsing context and by the active documents of its
    /// descendant browsing contexts (https://webmachinelearning.github.io/webmcp/#perform-an-observation), in tree order, then
    /// in the order of their registration: the application sees the tools of every origin, whatever their
    /// <see cref="ModelContextRegisterToolOptions.ExposedTo"/> option. Call it on the thread of the event loop.
    /// </summary>
    /// <returns>A snapshot of the tools: <see cref="Tool.Document"/> and <see cref="Tool.Origin"/> tell which document registered each one.</returns>
    public IReadOnlyList<Tool> GetModelContextTools()
    {
        var tools = new List<Tool>();
        foreach (var navigable in GetInclusiveDescendantNavigables())
        {
            var document = navigable.Document;
            if (document.ModelContextOrNull is not { } modelContext || document.RelevantWindow is not { } window)
                continue;

            foreach (var definition in modelContext.Tools)
            {
                tools.Add(new Tool(definition, document, window, document.Origin));
            }
        }

        return tools;
    }

    /// <summary>
    /// Gets whether the tools of the active documents of this browsing context and of its descendant browsing contexts are ready
    /// to be read: false while a promise given to <c>document.modelContext.waitForToolsUntil()</c> is pending
    /// (webmachinelearning/webmcp#373), e.g. while a page registers its tools after fetching their configuration.
    /// </summary>
    public bool ModelContextToolsReady
    {
        get
        {
            foreach (var navigable in GetInclusiveDescendantNavigables())
            {
#pragma warning disable WEBPLATEXP0018 // The application reads the state that the proposal defines
                if (navigable.Document.ModelContextOrNull is { ToolsReady: false })
                    return false;
#pragma warning restore WEBPLATEXP0018
            }

            return true;
        }
    }

    /// <summary>
    /// Executes a tool returned by <see cref="GetModelContextTools"/>, as the browser agent of the specification: a task of the
    /// event loop invokes the execute callback of the tool in its document (firing the <c>toolactivated</c> event), and the
    /// returned task is completed with the JSON serialization of its result once it settles. Call it on the thread of the event
    /// loop, and run the event loop (e.g. <see cref="DomEventLoop.RunUntilIdleAsync(CancellationToken)"/>) until the task is completed.
    /// </summary>
    /// <param name="tool">The tool.</param>
    /// <param name="inputArguments">The input arguments: the JSON text of an object. The default is <c>{}</c>.</param>
    /// <param name="cancellationToken">
    /// A token that cancels the execution: the task is canceled, the signal given to the execute callback is aborted, and the
    /// <c>toolcancel</c> event is fired.
    /// </param>
    /// <returns>
    /// The JSON text of the result of the tool. The task fails with a <see cref="DomException"/>: an <c>InvalidStateError</c>
    /// when the document of the tool was destroyed or belongs to another frame tree, a <c>NotFoundError</c> when the tool was
    /// unregistered, a <c>DataError</c> when <paramref name="inputArguments"/> is not the JSON text of an object, an
    /// <c>OperationError</c> when the tool fails or its result cannot be serialized to JSON, and an <c>AbortError</c> when the
    /// document of the tool is unloaded during the execution.
    /// </returns>
    public Task<string> ExecuteModelContextToolAsync(Tool tool, string? inputArguments = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tool);
        inputArguments ??= "{}";
        var targetWindow = tool.TargetWindow;
        var targetDocument = targetWindow.Document;
        if (targetDocument.BrowsingContext is not { } targetNavigable)
            return Task.FromException<string>(DomException.InvalidState("The document of the tool was destroyed"));

        if (!ReferenceEquals(targetNavigable.Top, Top))
            return Task.FromException<string>(DomException.InvalidState("The tool belongs to another frame tree"));

        if (!OriginRecord.IsSameOrigin(targetDocument.Origin, tool.OriginRecord) || targetDocument.ModelContextOrNull?.FindTool(tool.Id) is not { } definition)
            return Task.FromException<string>(DomException.NotFound($"The tool '{tool.Name}' is not registered"));

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<string>(cancellationToken);

        var promise = new TaskCompletionSource<string>();
        var uuid = new object();
        var eventLoop = targetWindow.EventLoop;
        var pendingExecutions = ModelContext.GetPendingExecutions(eventLoop);
        CancellationTokenRegistration registration = default;
        if (cancellationToken.CanBeCanceled)
        {
            // The token can be canceled from another thread: the execution is canceled by a task of the event loop
            registration = cancellationToken.Register(() =>
            {
                promise.TrySetCanceled(cancellationToken);
                eventLoop.QueueTask(() => ModelContext.CancelPendingToolExecution(pendingExecutions, uuid));
            });
        }

        void CompletionSteps(string? result, ToolExecutionError? error)
        {
            if (!pendingExecutions.Remove(uuid))
                return;

            registration.Dispose();
            if (error is { } executionError)
            {
                promise.TrySetException(ModelContext.CreateException(executionError));
            }
            else
            {
                promise.TrySetResult(result!);
            }
        }

        pendingExecutions[uuid] = new PendingToolExecution(CallerDocument: null, targetDocument, definition.Name, CompletionSteps);
        targetWindow.QueueGlobalTask(() => ModelContext.RunToolExecuteSteps(definition.Id, targetDocument, inputArguments, CompletionSteps, uuid));
        return promise.Task;
    }
}
