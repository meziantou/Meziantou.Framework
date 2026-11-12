namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The hints that describe the behavior of a tool (https://webmachinelearning.github.io/webmcp/#dictdef-toolannotations).</summary>
/// <seealso href="https://webmachinelearning.github.io/webmcp/#dictdef-toolannotations"/>
[IdlInterface("ToolAnnotations", "https://webmachinelearning.github.io/webmcp/#dictdef-toolannotations")]
public sealed class ToolAnnotations
{
    /// <summary>Gets whether the tool does not modify any state and only reads data.</summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-toolannotations-readonlyhint"/>
    [IdlMember("readOnlyHint", "https://webmachinelearning.github.io/webmcp/#dom-toolannotations-readonlyhint")]
    public bool ReadOnlyHint { get; init; }

    /// <summary>Gets whether the output of the tool contains data that the author of the tool does not trust.</summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-toolannotations-untrustedcontenthint"/>
    [IdlMember("untrustedContentHint", "https://webmachinelearning.github.io/webmcp/#dom-toolannotations-untrustedcontenthint")]
    public bool UntrustedContentHint { get; init; }

    /// <summary>Gets whether executing the tool has significant, real-world or non-reversible consequences (e.g. a payment).</summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-toolannotations-consequentialhint"/>
    [IdlMember("consequentialHint", "https://webmachinelearning.github.io/webmcp/#dom-toolannotations-consequentialhint")]
    public bool ConsequentialHint { get; init; }

    /// <summary>Gets whether the tool is intended for debugging and developer tooling rather than for the end users.</summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-toolannotations-debugging"/>
    [IdlMember("debugging", "https://webmachinelearning.github.io/webmcp/#dom-toolannotations-debugging")]
    public bool Debugging { get; init; }

    /// <summary>The JSON object of the annotations: the members are in the order of their conversion to a JavaScript value (lexicographic order).</summary>
    internal string ToJson() => $$"""{"consequentialHint":{{ToJson(ConsequentialHint)}},"debugging":{{ToJson(Debugging)}},"readOnlyHint":{{ToJson(ReadOnlyHint)}},"untrustedContentHint":{{ToJson(UntrustedContentHint)}}}""";

    private static string ToJson(bool value) => value ? "true" : "false";
}

/// <summary>The options given to the <see cref="ToolExecuteCallback"/> of a tool (https://webmachinelearning.github.io/webmcp/#dictdef-toolexecutecallbackoptions).</summary>
/// <seealso href="https://webmachinelearning.github.io/webmcp/#dictdef-toolexecutecallbackoptions"/>
[IdlInterface("ToolExecuteCallbackOptions", "https://webmachinelearning.github.io/webmcp/#dictdef-toolexecutecallbackoptions")]
public sealed class ToolExecuteCallbackOptions
{
    /// <summary>Gets the signal that is aborted when the execution of the tool is canceled (by the caller, or because the document of the caller was unloaded).</summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-toolexecutecallbackoptions-signal"/>
    [IdlMember("signal", "https://webmachinelearning.github.io/webmcp/#dom-toolexecutecallbackoptions-signal")]
    public required AbortSignal Signal { get; init; }
}

/// <summary>
/// The function that executes a tool (https://webmachinelearning.github.io/webmcp/#callbackdef-toolexecutecallback): it
/// receives the input arguments (an object created from the JSON arguments of the caller) and returns the result, which is
/// serialized to JSON for the caller.
/// </summary>
/// <param name="inputObject">The input arguments: a JavaScript object of the realm of the document of the tool.</param>
/// <param name="options">The options of the execution.</param>
/// <seealso href="https://webmachinelearning.github.io/webmcp/#callbackdef-toolexecutecallback"/>
[IdlInterface("ToolExecuteCallback", "https://webmachinelearning.github.io/webmcp/#callbackdef-toolexecutecallback")]
#pragma warning disable CA1711 // The name of the IDL callback function
public delegate Task<object?> ToolExecuteCallback(object inputObject, ToolExecuteCallbackOptions options);
#pragma warning restore CA1711

/// <summary>A tool registered with <see cref="ModelContext.RegisterTool"/> (https://webmachinelearning.github.io/webmcp/#dictdef-modelcontexttool).</summary>
/// <seealso href="https://webmachinelearning.github.io/webmcp/#dictdef-modelcontexttool"/>
[IdlInterface("ModelContextTool", "https://webmachinelearning.github.io/webmcp/#dictdef-modelcontexttool")]
public sealed class ModelContextTool
{
    /// <summary>Gets the name of the tool: 1 to 128 ASCII alphanumeric characters, <c>_</c>, <c>-</c> or <c>.</c>, unique in the document.</summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-modelcontexttool-name"/>
    [IdlMember("name", "https://webmachinelearning.github.io/webmcp/#dom-modelcontexttool-name")]
    public required string Name { get; init; }

    /// <summary>Gets the human-readable title of the tool, or null.</summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-modelcontexttool-title"/>
    [IdlMember("title", "https://webmachinelearning.github.io/webmcp/#dom-modelcontexttool-title")]
    public string? Title { get; init; }

    /// <summary>Gets the natural language description of the tool (not empty).</summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-modelcontexttool-description"/>
    [IdlMember("description", "https://webmachinelearning.github.io/webmcp/#dom-modelcontexttool-description")]
    public required string Description { get; init; }

    /// <summary>
    /// Gets the JSON Schema of the input arguments, or null: a JavaScript object, or a <see cref="System.Text.Json.Nodes.JsonNode"/>.
    /// It is serialized to JSON when the tool is registered.
    /// </summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-modelcontexttool-inputschema"/>
    [IdlMember("inputSchema", "https://webmachinelearning.github.io/webmcp/#dom-modelcontexttool-inputschema")]
    public object? InputSchema { get; init; }

    /// <summary>
    /// Gets the JSON Schema of the result, or null: a JavaScript object, or a <see cref="System.Text.Json.Nodes.JsonNode"/>.
    /// It is serialized to JSON when the tool is registered.
    /// </summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-modelcontexttool-outputschema"/>
    [IdlMember("outputSchema", "https://webmachinelearning.github.io/webmcp/#dom-modelcontexttool-outputschema")]
    public object? OutputSchema { get; init; }

    /// <summary>Gets the function that executes the tool.</summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-modelcontexttool-execute"/>
    [IdlMember("execute", "https://webmachinelearning.github.io/webmcp/#dom-modelcontexttool-execute")]
    public required ToolExecuteCallback Execute { get; init; }

    /// <summary>Gets the hints that describe the behavior of the tool, or null.</summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-modelcontexttool-annotations"/>
    [IdlMember("annotations", "https://webmachinelearning.github.io/webmcp/#dom-modelcontexttool-annotations")]
    public ToolAnnotations? Annotations { get; init; }
}

/// <summary>The options of <see cref="ModelContext.RegisterTool"/> (https://webmachinelearning.github.io/webmcp/#dictdef-modelcontextregistertooloptions).</summary>
/// <seealso href="https://webmachinelearning.github.io/webmcp/#dictdef-modelcontextregistertooloptions"/>
[IdlInterface("ModelContextRegisterToolOptions", "https://webmachinelearning.github.io/webmcp/#dictdef-modelcontextregistertooloptions")]
public sealed class ModelContextRegisterToolOptions
{
    /// <summary>
    /// Gets the origins of the documents of the frame tree, other than the documents same origin with the document of the tool,
    /// that can discover and execute the tool, or null. Each origin must be a potentially trustworthy URL (e.g. https).
    /// </summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-modelcontextregistertooloptions-exposedto"/>
    [IdlMember("exposedTo", "https://webmachinelearning.github.io/webmcp/#dom-modelcontextregistertooloptions-exposedto")]
    public IReadOnlyList<string>? ExposedTo { get; init; }

    /// <summary>Gets the signal that unregisters the tool when it is aborted, or null.</summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-modelcontextregistertooloptions-signal"/>
    [IdlMember("signal", "https://webmachinelearning.github.io/webmcp/#dom-modelcontextregistertooloptions-signal")]
    public AbortSignal? Signal { get; init; }
}

/// <summary>The options of <see cref="ModelContext.GetTools"/> (https://webmachinelearning.github.io/webmcp/#dictdef-modelcontextgettooloptions).</summary>
/// <seealso href="https://webmachinelearning.github.io/webmcp/#dictdef-modelcontextgettooloptions"/>
[IdlInterface("ModelContextGetToolOptions", "https://webmachinelearning.github.io/webmcp/#dictdef-modelcontextgettooloptions")]
public sealed class ModelContextGetToolOptions
{
    /// <summary>
    /// Gets the origins of the cross-origin documents whose tools are returned (when they expose them to the caller), or null:
    /// the tools of the documents same origin with the caller are always returned.
    /// </summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-modelcontextgettooloptions-fromorigins"/>
    [IdlMember("fromOrigins", "https://webmachinelearning.github.io/webmcp/#dom-modelcontextgettooloptions-fromorigins")]
    public IReadOnlyList<string>? FromOrigins { get; init; }
}

/// <summary>The options of <see cref="ModelContext.ExecuteTool"/> (https://webmachinelearning.github.io/webmcp/#dictdef-modelcontextexecutetooloptions).</summary>
/// <seealso href="https://webmachinelearning.github.io/webmcp/#dictdef-modelcontextexecutetooloptions"/>
[IdlInterface("ModelContextExecuteToolOptions", "https://webmachinelearning.github.io/webmcp/#dictdef-modelcontextexecutetooloptions")]
public sealed class ModelContextExecuteToolOptions
{
    /// <summary>Gets the signal that cancels the execution, or null.</summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-modelcontextexecutetooloptions-signal"/>
    [IdlMember("signal", "https://webmachinelearning.github.io/webmcp/#dom-modelcontextexecutetooloptions-signal")]
    public AbortSignal? Signal { get; init; }
}

/// <summary>The initialization dictionary of a <see cref="ToolActivatedEvent"/> (https://webmachinelearning.github.io/webmcp/#dictdef-toolactivatedeventinit).</summary>
/// <seealso href="https://webmachinelearning.github.io/webmcp/#dictdef-toolactivatedeventinit"/>
[IdlInterface("ToolActivatedEventInit", "https://webmachinelearning.github.io/webmcp/#dictdef-toolactivatedeventinit")]
public sealed class ToolActivatedEventInit : EventInit
{
    /// <summary>Gets the name of the tool.</summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-toolactivatedeventinit-toolname"/>
    [IdlMember("toolName", "https://webmachinelearning.github.io/webmcp/#dom-toolactivatedeventinit-toolname")]
    public string ToolName { get; init; } = "";
}

/// <summary>
/// The <c>toolactivated</c> event, fired at the <see cref="ModelContext"/> of the document of a tool after its execute
/// callback is invoked (https://webmachinelearning.github.io/webmcp/#tool-activated-event).
/// </summary>
/// <seealso href="https://webmachinelearning.github.io/webmcp/#toolactivatedevent"/>
[IdlInterface("ToolActivatedEvent", "https://webmachinelearning.github.io/webmcp/#toolactivatedevent")]
public sealed class ToolActivatedEvent : Event
{
    /// <summary>Creates a <c>toolactivated</c> event.</summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-toolactivatedevent-toolactivatedevent"/>
    [IdlMember("constructor", "https://webmachinelearning.github.io/webmcp/#dom-toolactivatedevent-toolactivatedevent")]
    public ToolActivatedEvent(string type, ToolActivatedEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        ToolName = eventInitDict?.ToolName ?? "";
    }

    /// <summary>Gets the name of the tool whose execution started.</summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-toolactivatedevent-toolname"/>
    [IdlMember("toolName", "https://webmachinelearning.github.io/webmcp/#dom-toolactivatedevent-toolname")]
    public string ToolName { get; }
}

/// <summary>The initialization dictionary of a <see cref="ToolCancelEvent"/> (https://webmachinelearning.github.io/webmcp/#dictdef-toolcanceleventinit).</summary>
/// <seealso href="https://webmachinelearning.github.io/webmcp/#dictdef-toolcanceleventinit"/>
[IdlInterface("ToolCancelEventInit", "https://webmachinelearning.github.io/webmcp/#dictdef-toolcanceleventinit")]
public sealed class ToolCancelEventInit : EventInit
{
    /// <summary>Gets the name of the tool.</summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-toolcanceleventinit-toolname"/>
    [IdlMember("toolName", "https://webmachinelearning.github.io/webmcp/#dom-toolcanceleventinit-toolname")]
    public string ToolName { get; init; } = "";
}

/// <summary>
/// The <c>toolcancel</c> event, fired at the <see cref="ModelContext"/> of the document of a tool when an execution of the
/// tool is canceled (https://webmachinelearning.github.io/webmcp/#tool-cancel-event).
/// </summary>
/// <seealso href="https://webmachinelearning.github.io/webmcp/#toolcancelevent"/>
[IdlInterface("ToolCancelEvent", "https://webmachinelearning.github.io/webmcp/#toolcancelevent")]
public sealed class ToolCancelEvent : Event
{
    /// <summary>Creates a <c>toolcancel</c> event.</summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-toolcancelevent-toolcancelevent"/>
    [IdlMember("constructor", "https://webmachinelearning.github.io/webmcp/#dom-toolcancelevent-toolcancelevent")]
    public ToolCancelEvent(string type, ToolCancelEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        ToolName = eventInitDict?.ToolName ?? "";
    }

    /// <summary>Gets the name of the tool whose execution was canceled.</summary>
    /// <seealso href="https://webmachinelearning.github.io/webmcp/#dom-toolcancelevent-toolname"/>
    [IdlMember("toolName", "https://webmachinelearning.github.io/webmcp/#dom-toolcancelevent-toolname")]
    public string ToolName { get; }
}

/// <summary>
/// A tool registered by a document and exposed to another document, or to the application
/// (https://webmachinelearning.github.io/webmcp/#tool-interface, as defined by webmachinelearning/webmcp#348, which
/// replaces the <c>RegisteredTool</c> dictionary): a snapshot of the tool when <see cref="ModelContext.GetTools"/> or
/// <see cref="BrowsingContext.GetModelContextTools"/> discovered it, which identifies this registration of the tool: once
/// the tool is unregistered, it cannot be executed anymore, even when a tool with the same name is registered again.
/// </summary>
/// <seealso href="https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/348.html#tool"/>
[IdlInterface("Tool", "https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/348.html#tool")]
public sealed class Tool
{
    private readonly Window _window;
    private Scripting.JsonText? _inputSchemaValue;
    private Scripting.JsonText? _outputSchemaValue;
    private Scripting.JsonText? _annotationsValue;

    internal Tool(ModelContextToolDefinition definition, Document document, Window window, OriginRecord origin)
    {
        TargetDocument = document;
        Id = definition.Id;
        Name = definition.Name;
        Title = definition.Title ?? "";
        Description = definition.Description;
        InputSchemaJson = definition.InputSchema.Length == 0 ? null : definition.InputSchema;
        OutputSchemaJson = definition.OutputSchema.Length == 0 ? null : definition.OutputSchema;
        ToolAnnotations = definition.Annotations;
        _window = window;
        OriginRecord = origin;
    }

    /// <summary>The tool id of the registration (https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/348.html#tool-tool-id).</summary>
    internal object Id { get; }

    /// <summary>The window of the document that registered the tool, even when its browsing context was destroyed.</summary>
    internal Window TargetWindow => _window;

    /// <summary>The document that registered the tool (a window can be reused by the document that replaces its initial about:blank document).</summary>
    internal Document TargetDocument { get; }

    internal OriginRecord OriginRecord { get; }

    /// <summary>Gets the name of the tool.</summary>
    /// <seealso href="https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/348.html#dom-tool-name"/>
    [IdlMember("name", "https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/348.html#dom-tool-name")]
    public string Name { get; }

    /// <summary>Gets the human-readable title of the tool, or the empty string when it has none.</summary>
    /// <seealso href="https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/348.html#dom-tool-title"/>
    [IdlMember("title", "https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/348.html#dom-tool-title")]
    public string Title { get; }

    /// <summary>Gets the natural language description of the tool.</summary>
    /// <seealso href="https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/348.html#dom-tool-description"/>
    [IdlMember("description", "https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/348.html#dom-tool-description")]
    public string Description { get; }

    /// <summary>
    /// Gets the JSON Schema of the input arguments as a value for the scripts (a JavaScript object, parsed by the script engine
    /// once), or null. The .NET code reads <see cref="InputSchemaJson"/>.
    /// </summary>
    /// <seealso href="https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/348.html#dom-tool-inputschema"/>
    [IdlMember("inputSchema", "https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/348.html#dom-tool-inputschema")]
    public object? InputSchema => InputSchemaJson is null ? null : _inputSchemaValue ??= new Scripting.JsonText(InputSchemaJson);

    /// <summary>
    /// Gets the JSON Schema of the result as a value for the scripts (a JavaScript object, parsed by the script engine once), or
    /// null. The .NET code reads <see cref="OutputSchemaJson"/>.
    /// </summary>
    /// <seealso href="https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/348.html#dom-tool-outputschema"/>
    [IdlMember("outputSchema", "https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/348.html#dom-tool-outputschema")]
    public object? OutputSchema => OutputSchemaJson is null ? null : _outputSchemaValue ??= new Scripting.JsonText(OutputSchemaJson);

    /// <summary>
    /// Gets the window of the document that registered the tool, or null when its browsing context was destroyed (e.g. its
    /// iframe was removed).
    /// </summary>
    /// <seealso href="https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/348.html#dom-tool-window"/>
    [IdlMember("window", "https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/348.html#dom-tool-window")]
    public Window? Window => _window.BrowsingContext is null ? null : _window;

    /// <summary>Gets the serialization of the origin of the document that registered the tool (<c>null</c> for an opaque origin).</summary>
    /// <seealso href="https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/348.html#dom-tool-origin"/>
    [IdlMember("origin", "https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/348.html#dom-tool-origin")]
    public string Origin => OriginRecord.Serialize();

    /// <summary>
    /// Gets the annotations of the tool as a value for the scripts (a JavaScript object, created by the script engine once), or
    /// null when the tool was registered without annotations. The .NET code reads <see cref="ToolAnnotations"/>.
    /// </summary>
    /// <seealso href="https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/348.html#dom-tool-annotations"/>
    [IdlMember("annotations", "https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/348.html#dom-tool-annotations")]
    public object? Annotations => ToolAnnotations is null ? null : _annotationsValue ??= new Scripting.JsonText(ToolAnnotations.ToJson());

    /// <summary>Gets the JSON Schema of the input arguments, as JSON text, or null when the tool was registered without one.</summary>
    [NotInIdl]
    public string? InputSchemaJson { get; }

    /// <summary>Gets the JSON Schema of the result, as JSON text, or null when the tool was registered without one.</summary>
    [NotInIdl]
    public string? OutputSchemaJson { get; }

    /// <summary>Gets the annotations of the tool, or null when the tool was registered without annotations.</summary>
    [NotInIdl]
    public ToolAnnotations? ToolAnnotations { get; }

    /// <summary>Gets the document that registered the tool.</summary>
    [NotInIdl]
    public Document Document => TargetDocument;

    /// <inheritdoc/>
    public override string ToString() => Name;
}
