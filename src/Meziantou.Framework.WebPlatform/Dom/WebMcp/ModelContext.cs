using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The tools of a document (https://webmachinelearning.github.io/webmcp/#modelcontext, <c>document.modelContext</c>): the
/// functions that a page registers with a natural language description and a JSON Schema of their input, so that an agent
/// (a script of the page or of another frame, or the application with <see cref="BrowsingContext.GetModelContextTools"/> and
/// <see cref="BrowsingContext.ExecuteModelContextToolAsync"/>) can discover and execute them.
/// </summary>
/// <remarks>
/// The tools are visible to the documents of the same frame tree that are same origin with the document that registered
/// them, and to the origins listed in <see cref="ModelContextRegisterToolOptions.ExposedTo"/>. The documents that are not
/// allowed to use the <c>tools</c> permissions policy feature (its default allowlist is <c>'self'</c>) cannot use the API.
/// </remarks>
/// <seealso href="https://webmachinelearning.github.io/webmcp/#modelcontext"/>
[IdlInterface("ModelContext", "https://webmachinelearning.github.io/webmcp/#modelcontext")]
public sealed class ModelContext : EventTarget
{
    /// <summary>
    /// The pending tool executions map of the traversable navigables (https://webmachinelearning.github.io/webmcp/#pending-tool-executions),
    /// by event loop: the documents of a traversable navigable share the event loop of its browsing context group, and the
    /// executions across traversable navigables are rejected, so the keys (unique values) never collide.
    /// </summary>
    private static readonly ConditionalWeakTable<DomEventLoop, Dictionary<object, PendingToolExecution>> PendingExecutions = [];

    private readonly Document _document;

    /// <summary>The tool map (https://webmachinelearning.github.io/webmcp/#model-context-tool-map), in the order of registration.</summary>
    private readonly List<ModelContextToolDefinition> _tools = [];

    /// <summary>The local pending tool executions map (https://webmachinelearning.github.io/webmcp/#model-context-local-pending-tool-executions-map).</summary>
    private readonly Dictionary<object, AbortController> _localPendingExecutions = [];

    /// <summary>The pending tool readiness promises (webmachinelearning/webmcp#373).</summary>
    private readonly HashSet<Task> _pendingReadinessPromises = [];

    internal ModelContext(Document document)
    {
        _document = document;
    }

    internal override GlobalScope? RelevantGlobal => _document.RelevantWindow;

    /// <summary>The tools registered by the document, in the order of registration.</summary>
    internal IReadOnlyList<ModelContextToolDefinition> Tools => _tools;

    /// <summary>
    /// Registers a tool (https://webmachinelearning.github.io/webmcp/#dom-modelcontext-registertool). The returned task fails
    /// with an <c>InvalidStateError</c> when the document is not fully active, when a tool with the same name is registered,
    /// when the name is not valid or when the description is empty; with a <c>NotAllowedError</c> when the document is not
    /// allowed to use the <c>tools</c> feature; with a <c>SecurityError</c> when an origin of
    /// <see cref="ModelContextRegisterToolOptions.ExposedTo"/> is not a potentially trustworthy URL; and with a
    /// <c>TypeError</c> when a schema cannot be serialized to JSON. The tool is unregistered when the signal of the options is
    /// aborted.
    /// </summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-modelcontext-registertool"/>
    [IdlMember("registerTool", "https://webmachinelearning.github.io/webmcp/#dom-modelcontext-registertool")]
    public Task RegisterTool(ModelContextTool tool, ModelContextRegisterToolOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var global = _document.RelevantWindow;
        if (!_document.IsFullyActive || global is null)
            return Task.FromException(InvalidState("The document is not fully active"));

        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(_document, PolicyControlledFeature.Tools))
            return Task.FromException(NotAllowed());

        var toolName = tool.Name;
        if (FindTool(toolName) is not null)
            return Task.FromException(InvalidState($"A tool named '{toolName}' is already registered"));

        if (!IsValidToolName(toolName))
            return Task.FromException(InvalidState($"The tool name '{toolName}' is not valid: it must have 1 to 128 ASCII alphanumeric characters, '_', '-' or '.'"));

        if (tool.Description.Length == 0)
            return Task.FromException(InvalidState("The description of the tool is empty"));

        // The serialization of the schemas can run script (toJSON() methods), which can detach the document or register a
        // tool with the same name
        string inputSchema;
        string outputSchema;
        try
        {
            inputSchema = SerializeToolSchema(global, tool.InputSchema);
            outputSchema = SerializeToolSchema(global, tool.OutputSchema);
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }

        if (!_document.IsFullyActive)
            return Task.FromException(InvalidState("The document is not fully active"));

        if (FindTool(toolName) is not null)
            return Task.FromException(InvalidState($"A tool named '{toolName}' is already registered"));

        var signal = options?.Signal;
        if (signal is { Aborted: true })
            return Task.FromException(GetAbortException(signal));

        var exposedOrigins = new List<OriginRecord>();
        if (options?.ExposedTo is { } exposedTo)
        {
            foreach (var origin in exposedTo)
            {
                if (ParsePotentiallyTrustworthyOrigin(origin) is not { } parsedOrigin)
                    return Task.FromException(new DomException($"The origin '{origin}' is not a potentially trustworthy URL", "SecurityError") { RealmWindow = global });

                exposedOrigins.Add(parsedOrigin);
            }
        }

        var promise = new TaskCompletionSource();
        var definition = new ModelContextToolDefinition
        {
            Id = new object(),
            Name = toolName,
            Title = tool.Title,
            Description = tool.Description,
            InputSchema = inputSchema,
            OutputSchema = outputSchema,
            Execute = tool.Execute,
            Annotations = tool.Annotations is { } annotations
                ? new ToolAnnotations { ReadOnlyHint = annotations.ReadOnlyHint, UntrustedContentHint = annotations.UntrustedContentHint, ConsequentialHint = annotations.ConsequentialHint, Debugging = annotations.Debugging }
                : null,
            ExposedOrigins = exposedOrigins,
        };

        if (signal is not null)
        {
            signal.AddAbortAlgorithm(() =>
            {
                UnregisterTool(definition.Id);
                promise.TrySetException(GetAbortException(signal));
            });
        }

        _tools.Add(definition);
        NotifyDocumentsOfToolChange(_document, exposedOrigins);
        global.QueueGlobalTask(() => promise.TrySetResult());
        return promise.Task;
    }

    /// <summary>
    /// Returns the tools exposed to the document by the documents of its frame tree (https://webmachinelearning.github.io/webmcp/#dom-modelcontext-gettools),
    /// sorted by name: the tools of the documents same origin with this document, and the tools of the documents whose origin
    /// is listed in <see cref="ModelContextGetToolOptions.FromOrigins"/> that expose them to the origin of this document.
    /// </summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-modelcontext-gettools"/>
    [IdlMember("getTools", "https://webmachinelearning.github.io/webmcp/#dom-modelcontext-gettools")]
    public Task<IReadOnlyList<Tool>> GetTools(ModelContextGetToolOptions? options = null)
    {
        var global = _document.RelevantWindow;
        if (!_document.IsFullyActive || global is null)
            return Task.FromException<IReadOnlyList<Tool>>(InvalidState("The document is not fully active"));

        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(_document, PolicyControlledFeature.Tools))
            return Task.FromException<IReadOnlyList<Tool>>(NotAllowed());

        var fromOrigins = new List<OriginRecord>();
        if (options?.FromOrigins is { } origins)
        {
            foreach (var origin in origins)
            {
                if (ParsePotentiallyTrustworthyOrigin(origin) is not { } parsedOrigin)
                    return Task.FromException<IReadOnlyList<Tool>>(new DomException($"The origin '{origin}' is not a potentially trustworthy URL", "SecurityError") { RealmWindow = global });

                fromOrigins.Add(parsedOrigin);
            }
        }

        var discoveredTools = new List<(ModelContextToolDefinition Definition, Document Document, Window Window, OriginRecord Origin)>();
        var callerOrigin = _document.Origin;
        foreach (var navigable in _document.BrowsingContext!.Top.GetInclusiveDescendantNavigables())
        {
            var targetDocument = navigable.Document;
            if (targetDocument.ModelContextOrNull is not { } targetContext || targetDocument.RelevantWindow is not { } targetWindow)
                continue;

            if (!PermissionsPolicyAlgorithms.IsAllowedToUse(targetDocument, PolicyControlledFeature.Tools, report: false))
                continue;

            var targetOrigin = targetDocument.Origin;
            if (!OriginRecord.IsSameOrigin(targetOrigin, callerOrigin) && !fromOrigins.Exists(origin => OriginRecord.IsSameOrigin(origin, targetOrigin)))
                continue;

            foreach (var definition in targetContext._tools)
            {
                if (IsToolExposedToOrigin(targetOrigin, definition.ExposedOrigins, callerOrigin))
                {
                    discoveredTools.Add((definition, targetDocument, targetWindow, targetOrigin));
                }
            }
        }

        var promise = new TaskCompletionSource<IReadOnlyList<Tool>>();
        global.QueueGlobalTask(() =>
        {
            var tools = discoveredTools.ConvertAll(tool => new Tool(tool.Definition, tool.Document, tool.Window, tool.Origin));
            tools.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            promise.TrySetResult(tools);
        });
        return promise.Task;
    }

    /// <summary>
    /// Executes a tool returned by <see cref="GetTools"/> (https://webmachinelearning.github.io/webmcp/#dom-modelcontext-executetool):
    /// the input arguments are serialized to JSON, the execute callback of the tool is invoked in its document, and the task
    /// is completed with the JSON serialization of its result. The task fails with an <c>InvalidStateError</c> when this
    /// document is not fully active, when the document of the tool was destroyed or belongs to another frame tree; with a
    /// <c>NotFoundError</c> when the tool was unregistered or is no longer exposed to this document; with an
    /// <c>OperationError</c> when the tool fails or its result cannot be serialized to JSON; with an <c>AbortError</c> when the
    /// document of the tool is unloaded; and with the abort reason when the signal of the options is aborted (the signal given
    /// to the execute callback is then aborted, and a <c>toolcancel</c> event is fired at the model context of the tool).
    /// </summary>
    /// <param name="tool">The tool.</param>
    /// <param name="inputObject">The input arguments: a JavaScript object, or a <see cref="System.Text.Json.Nodes.JsonNode"/>; null is an empty object.</param>
    /// <param name="options">The options.</param>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-modelcontext-executetool"/>
    [IdlMember("executeTool", "https://webmachinelearning.github.io/webmcp/#dom-modelcontext-executetool")]
    public Task<string> ExecuteTool(Tool tool, object? inputObject = null, ModelContextExecuteToolOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var callerDocument = _document;
        var global = callerDocument.RelevantWindow;
        if (!callerDocument.IsFullyActive || global is null)
            return Task.FromException<string>(InvalidState("The document is not fully active"));

        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(callerDocument, PolicyControlledFeature.Tools))
            return Task.FromException<string>(NotAllowed());

        // https://github.com/webmachinelearning/webmcp/issues/349: the tools of the documents whose origin is opaque cannot be executed
        var expectedTargetOrigin = tool.OriginRecord;
        if (expectedTargetOrigin.IsOpaque)
            return Task.FromException<string>(new DomException("The tools of a document whose origin is opaque cannot be executed", "NotSupportedError") { RealmWindow = global });

        string inputArguments;
        try
        {
            inputArguments = inputObject is null ? "{}" : SerializeJson(global, inputObject) ?? throw new ArgumentException("The input arguments cannot be serialized to JSON", nameof(inputObject));
        }
        catch (Exception ex)
        {
            return Task.FromException<string>(ex);
        }

        // The serialization can run script (toJSON() methods), which can detach the document
        if (!callerDocument.IsFullyActive || callerDocument.BrowsingContext is not { } callerNavigable)
            return Task.FromException<string>(InvalidState("The document is not fully active"));

        var targetWindow = tool.TargetWindow;
        var targetDocument = targetWindow.Document;
        if (targetDocument.BrowsingContext is not { } targetNavigable)
            return Task.FromException<string>(InvalidState("The document of the tool was destroyed"));

        var promise = new TaskCompletionSource<string>();
        var uuid = new object();
        var pendingExecutions = GetPendingExecutions(targetWindow.EventLoop);
        if (options?.Signal is { } signal)
        {
            if (signal.Aborted)
                return Task.FromException<string>(GetAbortException(signal));

            signal.AddAbortAlgorithm(() =>
            {
                promise.TrySetException(GetAbortException(signal));
                CancelPendingToolExecution(pendingExecutions, uuid);
            });
        }

        // https://github.com/webmachinelearning/webmcp/issues/227: the tools of another frame tree cannot be executed
        if (!ReferenceEquals(targetNavigable.Top, callerNavigable.Top))
        {
            global.QueueGlobalTask(() => promise.TrySetException(InvalidState("The tool belongs to another frame tree")));
            return promise.Task;
        }

        // The same error is reported when the origin of the document changed, when the tool was unregistered, and when it is
        // not exposed to the caller, so that a cross-origin caller cannot tell which one happened
        var targetOrigin = targetDocument.Origin;
        if (!OriginRecord.IsSameOrigin(targetOrigin, expectedTargetOrigin)
            || targetDocument.ModelContextOrNull?.FindTool(tool.Id) is not { } definition
            || !IsToolExposedToOrigin(targetOrigin, definition.ExposedOrigins, callerDocument.Origin))
        {
            global.QueueGlobalTask(() => promise.TrySetException(DomException.NotFound($"The tool '{tool.Name}' is not registered")));
            return promise.Task;
        }

        void CompletionSteps(string? result, ToolExecutionError? error)
        {
            if (!pendingExecutions.Remove(uuid))
                return;

            callerDocument.QueueGlobalTask(() =>
            {
                if (error is { } executionError)
                {
                    promise.TrySetException(CreateException(executionError));
                }
                else
                {
                    promise.TrySetResult(result!);
                }
            });
        }

        pendingExecutions[uuid] = new PendingToolExecution(callerDocument, targetDocument, definition.Name, CompletionSteps);
        targetWindow.QueueGlobalTask(() => RunToolExecuteSteps(definition.Id, targetDocument, inputArguments, CompletionSteps, uuid));
        return promise.Task;
    }

    /// <summary>
    /// Gets whether the tools of the document are ready to be read: false while a promise given to <see cref="WaitForToolsUntil"/>
    /// is pending (webmachinelearning/webmcp#373).
    /// </summary>
    /// <seealso href="https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/373.html#dom-modelcontext-toolsready"/>
    [Experimental(ExperimentalApis.WebMcpToolReadiness, UrlFormat = ExperimentalApis.UrlFormat)]
    [IdlMember("toolsReady", "https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/373.html#dom-modelcontext-toolsready")]
    public bool ToolsReady => _pendingReadinessPromises.Count == 0;

    /// <summary>
    /// Signals that the document is registering or updating its tools until <paramref name="promise"/> is settled
    /// (webmachinelearning/webmcp#373): <see cref="ToolsReady"/> is false meanwhile, and
    /// <see cref="BrowsingContext.ModelContextToolsReady"/> tells the application whether the tools of a frame tree are ready.
    /// Throws an <c>InvalidStateError</c> when the document is not fully active, and a <c>NotAllowedError</c> when it is not
    /// allowed to use the <c>tools</c> feature.
    /// </summary>
    /// <seealso href="https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/373.html#dom-modelcontext-waitfortoolsuntil"/>
    [Experimental(ExperimentalApis.WebMcpToolReadiness, UrlFormat = ExperimentalApis.UrlFormat)]
    [IdlMember("waitForToolsUntil", "https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/373.html#dom-modelcontext-waitfortoolsuntil")]
    public void WaitForToolsUntil(Task promise)
    {
        ArgumentNullException.ThrowIfNull(promise);
        if (!_document.IsFullyActive)
            throw InvalidState("The document is not fully active");

        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(_document, PolicyControlledFeature.Tools))
            throw NotAllowed();

        if (promise.IsCompleted)
        {
            _ = promise.Exception;
            return;
        }

        // The task can be completed on another thread (e.g. a task of the application): the state of the model context is
        // updated by a task of the event loop
        _pendingReadinessPromises.Add(promise);
        var document = _document;
        _ = promise.ContinueWith(settled =>
        {
            _ = settled.Exception;
            document.QueueGlobalTask(() => _pendingReadinessPromises.Remove(settled));
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>Gets or sets the handler of the <c>toolchange</c> event, fired when a tool exposed to the document is registered or unregistered.</summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-modelcontext-ontoolchange"/>
    [IdlMember("ontoolchange", "https://webmachinelearning.github.io/webmcp/#dom-modelcontext-ontoolchange")]
    public Action<Event>? OnToolChange
    {
        get => GetEventHandler("toolchange");
        set => SetEventHandler("toolchange", value);
    }

    /// <summary>Gets or sets the handler of the <c>toolactivated</c> event (<see cref="ToolActivatedEvent"/>).</summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-modelcontext-ontoolactivated"/>
    [IdlMember("ontoolactivated", "https://webmachinelearning.github.io/webmcp/#dom-modelcontext-ontoolactivated")]
    public Action<Event>? OnToolActivated
    {
        get => GetEventHandler("toolactivated");
        set => SetEventHandler("toolactivated", value);
    }

    /// <summary>Gets or sets the handler of the <c>toolcancel</c> event (<see cref="ToolCancelEvent"/>).</summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-modelcontext-ontoolcancel"/>
    [IdlMember("ontoolcancel", "https://webmachinelearning.github.io/webmcp/#dom-modelcontext-ontoolcancel")]
    public Action<Event>? OnToolCancel
    {
        get => GetEventHandler("toolcancel");
        set => SetEventHandler("toolcancel", value);
    }

    internal ModelContextToolDefinition? FindTool(object id) => _tools.Find(tool => ReferenceEquals(tool.Id, id));

    private ModelContextToolDefinition? FindTool(string name) => _tools.Find(tool => tool.Name == name);

    /// <summary>https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/348.html#model-context-unregister-a-tool</summary>
    private void UnregisterTool(object id)
    {
        var index = _tools.FindIndex(tool => ReferenceEquals(tool.Id, id));
        if (index < 0)
            return;

        var exposedOrigins = _tools[index].ExposedOrigins;
        _tools.RemoveAt(index);

        // The signal can be aborted after the document was removed from its navigable (e.g. its iframe was removed): there is
        // no navigable from which to find the documents to notify
        if (!_document.IsFullyActive)
            return;

        NotifyDocumentsOfToolChange(_document, exposedOrigins);
    }

    /// <summary>https://webmachinelearning.github.io/webmcp/#notify-documents-of-a-tool-change</summary>
    private static void NotifyDocumentsOfToolChange(Document toolOwner, IReadOnlyList<OriginRecord> exposedOrigins)
    {
        if (toolOwner.BrowsingContext?.Top is not { } traversable)
            return;

        var toolOwnerOrigin = toolOwner.Origin;
        foreach (var navigable in traversable.GetInclusiveDescendantNavigables().ToArray())
        {
            var targetDocument = navigable.Document;
            if (!PermissionsPolicyAlgorithms.IsAllowedToUse(targetDocument, PolicyControlledFeature.Tools, report: false))
                continue;

            if (!IsToolExposedToOrigin(toolOwnerOrigin, exposedOrigins, targetDocument.Origin))
                continue;

            // The event cannot have listeners while the model context of the document was not created
            targetDocument.QueueGlobalTask(() => targetDocument.ModelContextOrNull?.FireEvent(new Event("toolchange")));
        }
    }

    /// <summary>https://webmachinelearning.github.io/webmcp/#tool-is-exposed-to-an-origin</summary>
    private static bool IsToolExposedToOrigin(OriginRecord toolOwnerOrigin, IReadOnlyList<OriginRecord> exposedOrigins, OriginRecord accessingOrigin)
    {
        if (OriginRecord.IsSameOrigin(toolOwnerOrigin, accessingOrigin))
            return true;

        foreach (var allowedOrigin in exposedOrigins)
        {
            if (OriginRecord.IsSameOrigin(accessingOrigin, allowedOrigin))
                return true;
        }

        return false;
    }

    /// <summary>The origin of a URL whose origin is potentially trustworthy (the origins of <c>exposedTo</c> and <c>fromOrigins</c>), or null.</summary>
    private static OriginRecord? ParsePotentiallyTrustworthyOrigin(string origin)
    {
        if (Urls.UrlParser.Parse(origin, baseUrl: null) is not { } url)
            return null;

        var record = OriginRecord.FromUrl(url);
        return record.IsOpaque || !ReferrerPolicies.IsOriginPotentiallyTrustworthy(url) ? null : record;
    }

    /// <summary>
    /// https://webmachinelearning.github.io/webmcp/#serialize-a-tool-schema: the empty string when there is no schema, a
    /// <c>TypeError</c> when it serializes to <c>undefined</c>; the exceptions of the serialization propagate.
    /// </summary>
    private static string SerializeToolSchema(GlobalScope global, object? schema)
    {
        if (schema is null)
            return "";

        return SerializeJson(global, schema) ?? throw new ArgumentException("The schema cannot be serialized to JSON", nameof(schema));
    }

    /// <summary>The tool name must have 1 to 128 ASCII alphanumeric characters, '_', '-' or '.' (https://webmachinelearning.github.io/webmcp/#tool-definition-name).</summary>
    internal static bool IsValidToolName(string name)
    {
        if (name.Length is 0 or > 128)
            return false;

        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.'))
                return false;
        }

        return true;
    }

    internal static string? SerializeJson(GlobalScope global, object? value) => global.ScriptEngine is { } engine ? engine.SerializeJson(global, value) : DotNetJson.Serialize(value);

    private static object? ParseJson(GlobalScope global, string json, out bool isObject) => global.ScriptEngine is { } engine ? engine.ParseJson(global, json, out isObject) : DotNetJson.Parse(json, out isObject);

    private static Exception GetAbortException(AbortSignal signal) => signal.AbortReason as Exception ?? new AbortSignalException(signal.AbortReason);

    private DomException InvalidState(string message) => new(message, "InvalidStateError") { RealmWindow = _document.RelevantWindow };

    private DomException NotAllowed() => new("The document is not allowed to use the 'tools' feature", "NotAllowedError") { RealmWindow = _document.RelevantWindow };

    /// <summary>The exception of a tool execution error (https://webmachinelearning.github.io/webmcp/#tool-execution-error).</summary>
    internal static DomException CreateException(ToolExecutionError error) => error switch
    {
        ToolExecutionError.ToolNotFound => DomException.NotFound("The tool is not registered in its document"),
        ToolExecutionError.InvalidInput => new DomException("The input arguments are not a JSON object", "DataError"),
        ToolExecutionError.ToolRejected => new DomException("The execution of the tool failed", "OperationError"),
        ToolExecutionError.UnserializableResult => new DomException("The result of the tool cannot be serialized to JSON", "OperationError"),
        _ => new DomException("The document of the tool was unloaded", "AbortError"),
    };

    internal static Dictionary<object, PendingToolExecution> GetPendingExecutions(DomEventLoop eventLoop) => PendingExecutions.GetValue(eventLoop, static _ => []);

    /// <summary>https://webmachinelearning.github.io/webmcp/#cancel-a-pending-tool-execution</summary>
    internal static void CancelPendingToolExecution(Dictionary<object, PendingToolExecution> pendingExecutions, object uuid)
    {
        if (!pendingExecutions.Remove(uuid, out var execution))
            return;

        var targetDocument = execution.TargetDocument;
        targetDocument.QueueGlobalTask(() =>
        {
            if (targetDocument.ModelContextOrNull is not { } modelContext || !modelContext._localPendingExecutions.Remove(uuid, out var controller))
                return;

            controller.Signal.SignalAbort(new DomException("The execution of the tool was canceled", "AbortError") { RealmWindow = targetDocument.RelevantWindow });

            // An abort handler can detach the document
            if (targetDocument.IsFullyActive)
            {
                modelContext.FireEvent(new ToolCancelEvent("toolcancel", new ToolCancelEventInit { ToolName = execution.ToolName }));
            }
        });
    }

    /// <summary>
    /// The unloading document cleanup steps (https://webmachinelearning.github.io/webmcp/#unloading-document-cleanup-steps):
    /// the executions of the tools of the document fail with an <c>AbortError</c>, and the executions that the document
    /// started are canceled.
    /// </summary>
    internal static void RunUnloadingDocumentCleanupSteps(Document document)
    {
        if (document.RelevantWindow is not { } window || !PendingExecutions.TryGetValue(window.EventLoop, out var pendingExecutions) || pendingExecutions.Count == 0)
            return;

        var executionsToRemove = new List<object>();
        foreach (var (uuid, execution) in pendingExecutions)
        {
            if (ReferenceEquals(execution.TargetDocument, document) || ReferenceEquals(execution.CallerDocument, document))
            {
                executionsToRemove.Add(uuid);
            }
        }

        foreach (var uuid in executionsToRemove)
        {
            if (!pendingExecutions.TryGetValue(uuid, out var execution))
                continue;

            var isTarget = ReferenceEquals(execution.TargetDocument, document);
            var isCaller = ReferenceEquals(execution.CallerDocument, document);
            if (isTarget && !isCaller)
            {
                execution.CompletionSteps(null, ToolExecutionError.TargetDocumentUnloaded);
            }
            else if (isCaller && !isTarget)
            {
                CancelPendingToolExecution(pendingExecutions, uuid);
            }
            else
            {
                pendingExecutions.Remove(uuid);
            }
        }
    }

    /// <summary>https://webmachinelearning.github.io/webmcp/#tool-execute-steps</summary>
    internal static void RunToolExecuteSteps(object toolId, Document targetDocument, string inputArguments, Action<string?, ToolExecutionError?> completionSteps, object uuid)
    {
        if (targetDocument.ModelContextOrNull is not { } modelContext || modelContext.FindTool(toolId) is not { } tool)
        {
            completionSteps(null, ToolExecutionError.ToolNotFound);
            return;
        }

        modelContext.InvokeExecuteCallback(tool, targetDocument, inputArguments, completionSteps, uuid);
    }

    /// <summary>https://webmachinelearning.github.io/webmcp/#invoke-the-execute-callback</summary>
    private void InvokeExecuteCallback(ModelContextToolDefinition tool, Document targetDocument, string inputArguments, Action<string?, ToolExecutionError?> completionSteps, object uuid)
    {
        if (targetDocument.RelevantWindow is not { } window)
        {
            completionSteps(null, ToolExecutionError.TargetDocumentUnloaded);
            return;
        }

        object? inputObject;
        try
        {
            inputObject = ParseJson(window, inputArguments, out var isObject);
            if (!isObject)
            {
                completionSteps(null, ToolExecutionError.InvalidInput);
                return;
            }
        }
        catch (Exception)
        {
            completionSteps(null, ToolExecutionError.InvalidInput);
            return;
        }

        var controller = new AbortController();
        controller.Signal.SetRelevantGlobalCore(window);
        _localPendingExecutions[uuid] = controller;
        var options = new ToolExecuteCallbackOptions { Signal = controller.Signal };

        // A callback that throws returns a rejected promise (https://webidl.spec.whatwg.org/#invoke-a-callback-function), whose
        // exception is not reported
        Task<object?> toolPromise;
        try
        {
            toolPromise = tool.Execute(inputObject!, options) ?? Task.FromResult<object?>(null);
        }
        catch (Exception ex)
        {
            toolPromise = Task.FromException<object?>(ex);
        }

        // The reactions to the promise run after the toolactivated event. The task of a callback of the application can be
        // completed on another thread: the state of the executions is only used by the tasks of the event loop.
        var settledSynchronously = toolPromise.IsCompleted;
        if (!settledSynchronously)
        {
            _ = toolPromise.ContinueWith(settled => targetDocument.QueueGlobalTask(() => OnToolPromiseSettled(settled, window, completionSteps, uuid)), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        // The callback can detach the document: the event is not fired at a document that is not fully active
        if (targetDocument.IsFullyActive)
        {
            FireEvent(new ToolActivatedEvent("toolactivated", new ToolActivatedEventInit { ToolName = tool.Name }));
        }

        if (settledSynchronously)
        {
            OnToolPromiseSettled(toolPromise, window, completionSteps, uuid);
        }
    }

    private void OnToolPromiseSettled(Task<object?> toolPromise, Window window, Action<string?, ToolExecutionError?> completionSteps, object uuid)
    {
        // The execution was canceled before the promise of the tool settled
        if (!_localPendingExecutions.Remove(uuid))
        {
            _ = toolPromise.Exception;
            return;
        }

        if (!toolPromise.IsCompletedSuccessfully)
        {
            _ = toolPromise.Exception;
            completionSteps(null, ToolExecutionError.ToolRejected);
            return;
        }

        // https://github.com/webmachinelearning/webmcp/issues/361: undefined (null in .NET) is serialized as null
        string? serializedResult;
        try
        {
            serializedResult = toolPromise.Result is null ? "null" : SerializeJson(window, toolPromise.Result);
        }
        catch (Exception)
        {
            serializedResult = null;
        }

        if (serializedResult is null)
        {
            completionSteps(null, ToolExecutionError.UnserializableResult);
            return;
        }

        completionSteps(serializedResult, null);
    }
}

/// <summary>A tool definition (https://webmachinelearning.github.io/webmcp/#tool-definition).</summary>
internal sealed class ModelContextToolDefinition
{
    /// <summary>The tool id (webmachinelearning/webmcp#348): identifies this registration of the tool.</summary>
    public required object Id { get; init; }

    public required string Name { get; init; }

    public string? Title { get; init; }

    public required string Description { get; init; }

    /// <summary>The JSON of the input schema, or the empty string.</summary>
    public required string InputSchema { get; init; }

    /// <summary>The JSON of the output schema, or the empty string.</summary>
    public required string OutputSchema { get; init; }

    public required ToolExecuteCallback Execute { get; init; }

    public ToolAnnotations? Annotations { get; init; }

    public required IReadOnlyList<OriginRecord> ExposedOrigins { get; init; }
}

/// <summary>A pending tool execution (https://webmachinelearning.github.io/webmcp/#pending-tool-execution).</summary>
/// <param name="CallerDocument">The caller document, or null for an execution of the application (<see cref="BrowsingContext.ExecuteModelContextToolAsync"/>).</param>
internal sealed record PendingToolExecution(Document? CallerDocument, Document TargetDocument, string ToolName, Action<string?, ToolExecutionError?> CompletionSteps);

/// <summary>A tool execution error (https://webmachinelearning.github.io/webmcp/#tool-execution-error).</summary>
internal enum ToolExecutionError
{
    ToolNotFound,
    InvalidInput,
    ToolRejected,
    UnserializableResult,
    TargetDocumentUnloaded,
}
