using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Meziantou.Framework.NodeJs.Internal;

namespace Meziantou.Framework.NodeJs;

/// <summary>Hosts a Node.js process and runs JavaScript code in it.</summary>
/// <remarks>
/// <para>The Node.js process runs out of process and communicates with the host using JSON messages over a private local socket. Arguments and results are serialized as JSON.</para>
/// <para>Calls can run concurrently. They run on the main thread of the process, or on worker threads when <see cref="NodeJsHostOptions.WorkerThreads"/> is set, so CPU-bound calls run in parallel. The Node.js process exits when the host is disposed, or when the connection with the .NET process is closed (e.g. when the .NET process exits) unless its event loop is blocked. Its standard input is closed, and its standard output and error are only passed to <see cref="NodeJsHostOptions.StandardOutputReceived"/> and <see cref="NodeJsHostOptions.StandardErrorReceived"/>.</para>
/// </remarks>
/// <example>
/// <code>
/// await using var node = await NodeJsHost.StartAsync();
/// var sum = await node.EvaluateAsync("return 1 + 2;");
/// var html = await node.InvokeAsync("marked", "parse", ["# Hello"]);
/// </code>
/// </example>
public sealed class NodeJsHost : IAsyncDisposable
{
    internal const string ReflectionUnreferencedCodeMessage = "JSON serialization and deserialization might require types that cannot be statically analyzed. Use the overload that takes a JsonTypeInfo instead.";
    internal const string ReflectionDynamicCodeMessage = "JSON serialization and deserialization might require types that cannot be statically analyzed and might need runtime code generation. Use the overload that takes a JsonTypeInfo instead.";
    private const int MaxStandardErrorLines = 20;
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(10);

    // Once the connection is closed, the process exits immediately unless its event loop is blocked, so there is no reason to wait longer before killing it
    private static readonly TimeSpan DisposeExitTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(1);

    private static readonly string BootstrapScript = GetBootstrapScript();

    // While canceled calls are running, the process is checked at this interval, as they can block the event loop at any time
    private static readonly TimeSpan MaxResponsivenessCheckInterval = TimeSpan.FromSeconds(1);

    private static readonly byte[] WatchdogRequest = "{\"type\":\"running\"}\n"u8.ToArray();

    // Messages are only read by JSON.parse, so characters that are sensitive in HTML or non-ASCII do not need to be escaped, which would make them up to 6 times larger
    internal static readonly JsonWriterOptions MessageWriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // Messages can be deeply nested (e.g. syntax trees). JsonDocument does not use recursion, so there is no reason to limit the depth.
    internal static readonly JsonDocumentOptions UnlimitedDepthDocumentOptions = new() { MaxDepth = int.MaxValue };

    // JavaScript code uses camelCase names, so the web defaults are used (camelCase names, case-insensitive matching) when serializing using reflection without options
    [SuppressMessage("Usage", "MA0224:Set RespectNullableAnnotations on the JsonSerializerOptions instance", Justification = "Same behavior as the default options")]
    [SuppressMessage("Usage", "MA0225:Set RespectRequiredConstructorParameters on the JsonSerializerOptions instance", Justification = "Same behavior as the default options")]
    internal static readonly JsonSerializerOptions DefaultArgumentSerializerOptions = new(JsonSerializerDefaults.Web);

    // The web defaults, except that NaN and infinities, sent as "NaN", "Infinity", and "-Infinity", can be read as numbers
    [SuppressMessage("Usage", "MA0224:Set RespectNullableAnnotations on the JsonSerializerOptions instance", Justification = "Same behavior as the default options")]
    [SuppressMessage("Usage", "MA0225:Set RespectRequiredConstructorParameters on the JsonSerializerOptions instance", Justification = "Same behavior as the default options")]
    private static readonly JsonSerializerOptions DefaultResultSerializerOptions = new(JsonSerializerDefaults.Web) { NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals };

    private readonly NodeJsHostOptions _options;
    private readonly Process _process;

    // The connections with the threads of the process, so they are all terminated with the host
    private readonly ConcurrentDictionary<NodeJsChannel, byte> _channels = new();
    [SuppressMessage("Reliability", "CA2213:Disposable fields should be disposed", Justification = "AvailableWaitHandle is never used, and disposing it would leave concurrent callers waiting forever")]
    private readonly SemaphoreSlim? _concurrencyLimit;
    [SuppressMessage("Reliability", "CA2213:Disposable fields should be disposed", Justification = "AvailableWaitHandle is never used, and disposing it would make the responsiveness check fail")]
    private readonly SemaphoreSlim _responsivenessCheckSignal = new(0);
    private readonly Queue<string> _standardErrorTail = new();
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _terminated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _acceptCts = new();
    private readonly Func<NodeJsChannel, ResultReader<JSReference>> _referenceReader;

    // The worker threads that run the calls when NodeJsHostOptions.WorkerThreads is set
    private readonly WorkerSlot[]? _workers;
    private readonly Lock _workersLock = new();
    private readonly ConcurrentDictionary<(int Worker, int Generation), TaskCompletionSource<int?>> _workerExits = new();
    private NodeJsEndpoint? _endpoint;

    // The connection with the main thread. With worker threads, it only controls them.
    private NodeJsChannel? _mainChannel;
    private Stream? _watchdogStream;
    private StreamReader? _watchdogReader;
    private Task _acceptTask = Task.CompletedTask;
    private Task _responsivenessCheck = Task.CompletedTask;
    private Exception? _terminationException;
    private long _nextRequestId;
    private uint _nextWorker;
    private int _processId;
    private long _canceledCalls;
    private long _checkedCanceledCalls;
    private int _responsivenessCheckRunning;
    private int _disposed;

    private NodeJsHost(NodeJsHostOptions options)
    {
        _options = options;
        if (options.MaxConcurrentCalls is { } maxConcurrentCalls)
        {
            _concurrencyLimit = new SemaphoreSlim(maxConcurrentCalls, maxConcurrentCalls);
        }

        if (options.WorkerThreads is { } workerThreads)
        {
            _workers = new WorkerSlot[workerThreads];
            for (var i = 0; i < workerThreads; i++)
            {
                _workers[i] = new WorkerSlot();
            }
        }

        // A reference is bound to the thread that created it
        _referenceReader = channel => utf8Json => new JSReference(this, channel, ReadReferenceId(utf8Json));

        _process = new Process { EnableRaisingEvents = true };
        _process.Exited += (_, _) => _exited.TrySetResult();
    }

    /// <summary>Gets the identifier of the Node.js process.</summary>
    /// <remarks>The identifier remains available once the process exits or the host is disposed.</remarks>
    public int ProcessId => _processId;

    /// <summary>Gets the version of Node.js, as reported by <c>process.version</c> (e.g. <c>v24.15.0</c>).</summary>
    public string NodeVersion { get; private set; } = "";

    /// <summary>Gets a value indicating whether the host can no longer run code, because it is disposed or the process exited.</summary>
    internal bool IsTerminated => Volatile.Read(ref _terminationException) is not null;

    /// <summary>Gets the number of canceled calls whose response has not been received yet, so their JavaScript code may still be running.</summary>
    /// <remarks>The calls of a worker thread that exited are not counted, as their JavaScript code no longer runs.</remarks>
    internal int AbandonedCalls
    {
        get
        {
            if (_workers is null)
                return _mainChannel?.AbandonedCalls ?? 0;

            var result = 0;
            lock (_workersLock)
            {
                foreach (var slot in _workers)
                {
                    result += slot.Channel?.AbandonedCalls ?? 0;
                }
            }

            return result;
        }
    }

    /// <summary>Starts a new Node.js process.</summary>
    /// <exception cref="NodeJsException">The <c>node</c> executable cannot be found, or the process fails to start.</exception>
    /// <param name="options">The options. They are copied, so changing them once the process is started has no effect.</param>
    /// <param name="cancellationToken">A token to cancel the startup.</param>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="NodeJsHostOptions.MaxConcurrentCalls"/>, <see cref="NodeJsHostOptions.StartupTimeout"/>, <see cref="NodeJsHostOptions.UnresponsiveTimeout"/>, or <see cref="NodeJsHostOptions.WorkerThreads"/> is zero or negative.</exception>
    public static async Task<NodeJsHost> StartAsync(NodeJsHostOptions? options = null, CancellationToken cancellationToken = default)
    {
        options = (options ?? NodeJsHostOptions.Default).Clone();
        ValidateOptions(options);

        var host = new NodeJsHost(options);
        try
        {
            await host.StartCoreAsync(cancellationToken).ConfigureAwait(false);
            return host;
        }
        catch
        {
            await host.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Imports a module and calls one of its exports.</summary>
    /// <param name="module">The module specifier, as used by <c>import()</c>: an npm package name, a relative path starting with <c>./</c> or <c>../</c> and including the file extension, resolved from <see cref="NodeJsHostOptions.WorkingDirectory"/>, an absolute path, or a <c>file:</c> or <c>node:</c> URL. A module is loaded once, and kept for the lifetime of the process.</param>
    /// <param name="exportName">The name of the export. When <see langword="null"/>, the default export is used. CommonJS exports are supported.</param>
    /// <param name="arguments">The arguments passed to the function. Use <see cref="JSValue"/> for values that JSON cannot represent, and <see cref="JSReference"/> to pass a value kept in the Node.js process.</param>
    /// <param name="cancellationToken">A token to stop waiting for the result. The JavaScript code keeps running.</param>
    /// <returns>The JSON representation of the value returned by the function (awaited if it is a promise), or the value of the export when it is not a function. <c>BigInt</c> values are exact JSON numbers (Node.js 21 or later).</returns>
    /// <exception cref="NodeJsException">The JavaScript code throws, or the Node.js process exits.</exception>
    public Task<JsonElement> InvokeAsync(string module, string? exportName, IReadOnlyList<JsonNode?>? arguments = null, CancellationToken cancellationToken = default)
    {
        return InvokeCoreAsync(module, exportName, arguments, ResultKind.Json, ReadJsonElement, cancellationToken);
    }

    /// <summary>Imports a module, calls one of its exports, and deserializes the result.</summary>
    /// <inheritdoc cref="InvokeAsync(string, string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    /// <exception cref="JsonException">The result cannot be deserialized to <typeparamref name="T"/>.</exception>
    public async Task<T?> InvokeAsync<T>(string module, string? exportName, IReadOnlyList<JsonNode?>? arguments, JsonTypeInfo<T> resultTypeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resultTypeInfo);

        return await InvokeCoreAsync(module, exportName, arguments, ResultKind.Json, CreateResultReader(resultTypeInfo), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Imports a module, calls one of its exports, and deserializes the result. Arguments are serialized using reflection.</summary>
    /// <inheritdoc cref="InvokeAsync(string, string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    /// <exception cref="JsonException">The result cannot be deserialized to <typeparamref name="T"/>.</exception>
    [RequiresUnreferencedCode(ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionDynamicCodeMessage)]
    public async Task<T?> InvokeAsync<T>(string module, string? exportName, object?[]? arguments = null, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await InvokeCoreAsync(module, exportName, ArgumentWriter.SerializeArguments(arguments, options), ResultKind.Json, CreateResultReader<T>(options), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Imports a module, calls one of its exports without arguments, and deserializes the result using reflection.</summary>
    /// <inheritdoc cref="InvokeAsync(string, string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    /// <exception cref="JsonException">The result cannot be deserialized to <typeparamref name="T"/>.</exception>
    [RequiresUnreferencedCode(ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionDynamicCodeMessage)]
    public Task<T?> InvokeAsync<T>(string module, string? exportName, CancellationToken cancellationToken)
    {
        return InvokeAsync<T>(module, exportName, arguments: null, options: null, cancellationToken);
    }

    /// <summary>Imports a module and calls one of its exports, ignoring its result. The result does not need to be serializable.</summary>
    /// <inheritdoc cref="InvokeAsync(string, string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    public Task InvokeVoidAsync(string module, string? exportName, IReadOnlyList<JsonNode?>? arguments = null, CancellationToken cancellationToken = default)
    {
        return InvokeCoreAsync(module, exportName, arguments, ResultKind.Void, ReadJsonElement, cancellationToken);
    }

    /// <summary>Imports a module and calls one of its exports, ignoring its result. Arguments are serialized using reflection.</summary>
    /// <inheritdoc cref="InvokeVoidAsync(string, string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    [RequiresUnreferencedCode(ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionDynamicCodeMessage)]
    public Task InvokeVoidAsync(string module, string? exportName, object?[]? arguments, JsonSerializerOptions? options, CancellationToken cancellationToken = default)
    {
        return InvokeVoidAsync(module, exportName, ArgumentWriter.SerializeArguments(arguments, options), cancellationToken);
    }

    /// <summary>Imports a module, calls one of its exports, and keeps the result in the Node.js process.</summary>
    /// <inheritdoc cref="InvokeAsync(string, string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    /// <returns>A reference to the value returned by the function (awaited if it is a promise), or to the value of the export when it is not a function. Dispose it when the value is no longer needed.</returns>
    public Task<JSReference> InvokeReferenceAsync(string module, string? exportName, IReadOnlyList<JsonNode?>? arguments = null, CancellationToken cancellationToken = default)
    {
        return InvokeCoreAsync(module, exportName, arguments, ResultKind.Reference, _referenceReader, cancellationToken);
    }

    /// <summary>Imports a module, calls one of its exports, and keeps the result in the Node.js process. Arguments are serialized using reflection.</summary>
    /// <inheritdoc cref="InvokeReferenceAsync(string, string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    [RequiresUnreferencedCode(ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionDynamicCodeMessage)]
    public Task<JSReference> InvokeReferenceAsync(string module, string? exportName, object?[]? arguments, JsonSerializerOptions? options, CancellationToken cancellationToken = default)
    {
        return InvokeReferenceAsync(module, exportName, ArgumentWriter.SerializeArguments(arguments, options), cancellationToken);
    }

    /// <summary>Imports a module, creates an instance of one of its exported classes (<c>new</c>), and keeps the instance in the Node.js process.</summary>
    /// <param name="module">The module specifier, as used by <c>import()</c>: an npm package name, a relative path starting with <c>./</c> or <c>../</c> and including the file extension, resolved from <see cref="NodeJsHostOptions.WorkingDirectory"/>, an absolute path, or a <c>file:</c> or <c>node:</c> URL. A module is loaded once, and kept for the lifetime of the process.</param>
    /// <param name="exportName">The name of the exported class or constructor function. When <see langword="null"/>, the default export is used.</param>
    /// <param name="arguments">The arguments passed to the constructor. Use <see cref="JSValue"/> for values that JSON cannot represent, and <see cref="JSReference"/> to pass a value kept in the Node.js process.</param>
    /// <param name="cancellationToken">A token to stop waiting for the result. The JavaScript code keeps running.</param>
    /// <returns>A reference to the new instance. Dispose it when the instance is no longer needed.</returns>
    /// <exception cref="NodeJsException">The export is not a constructor, the constructor throws, or the Node.js process exits.</exception>
    public Task<JSReference> CreateInstanceAsync(string module, string? exportName, IReadOnlyList<JsonNode?>? arguments = null, CancellationToken cancellationToken = default)
    {
        return InvokeCoreAsync(module, exportName, arguments, ResultKind.Reference, _referenceReader, cancellationToken, construct: true);
    }

    /// <summary>Imports a module, creates an instance of one of its exported classes (<c>new</c>), and keeps the instance in the Node.js process. Arguments are serialized using reflection.</summary>
    /// <inheritdoc cref="CreateInstanceAsync(string, string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    [RequiresUnreferencedCode(ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionDynamicCodeMessage)]
    public Task<JSReference> CreateInstanceAsync(string module, string? exportName, object?[]? arguments, JsonSerializerOptions? options, CancellationToken cancellationToken = default)
    {
        return CreateInstanceAsync(module, exportName, ArgumentWriter.SerializeArguments(arguments, options), cancellationToken);
    }

    /// <summary>Evaluates JavaScript code as the body of an async function.</summary>
    /// <param name="code">The body of the function. Use <c>return</c> to return a value and <c>await</c> to wait for promises. <c>require</c> is available, and values stored on <c>globalThis</c> persist across calls.</param>
    /// <param name="cancellationToken">A token to stop waiting for the result. The JavaScript code keeps running.</param>
    /// <returns>The JSON representation of the returned value. <c>BigInt</c> values are exact JSON numbers (Node.js 21 or later).</returns>
    /// <exception cref="NodeJsException">The JavaScript code throws, or the Node.js process exits.</exception>
    public Task<JsonElement> EvaluateAsync(string code, CancellationToken cancellationToken = default)
    {
        return EvaluateCoreAsync(code, hasArguments: false, arguments: null, ResultKind.Json, ReadJsonElement, cancellationToken);
    }

    /// <summary>Evaluates JavaScript code as the body of an async function that receives arguments.</summary>
    /// <param name="code">The body of the function. The arguments are available in the <c>args</c> array. Use <c>return</c> to return a value and <c>await</c> to wait for promises. <c>require</c> is available, and values stored on <c>globalThis</c> persist across calls.</param>
    /// <param name="arguments">The arguments, available in the <c>args</c> array. Pass values as arguments instead of inserting them in the code, so they cannot change the code. Use <see cref="JSValue"/> for values that JSON cannot represent, and <see cref="JSReference"/> to pass a value kept in the Node.js process.</param>
    /// <param name="cancellationToken">A token to stop waiting for the result. The JavaScript code keeps running.</param>
    /// <returns>The JSON representation of the returned value. <c>BigInt</c> values are exact JSON numbers (Node.js 21 or later).</returns>
    /// <exception cref="NodeJsException">The JavaScript code throws, or the Node.js process exits.</exception>
    public Task<JsonElement> EvaluateAsync(string code, IReadOnlyList<JsonNode?>? arguments, CancellationToken cancellationToken = default)
    {
        return EvaluateCoreAsync(code, hasArguments: true, arguments, ResultKind.Json, ReadJsonElement, cancellationToken);
    }

    /// <summary>Evaluates JavaScript code as the body of an async function that receives arguments, and deserializes the result.</summary>
    /// <inheritdoc cref="EvaluateAsync(string, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    /// <exception cref="JsonException">The result cannot be deserialized to <typeparamref name="T"/>.</exception>
    public async Task<T?> EvaluateAsync<T>(string code, IReadOnlyList<JsonNode?>? arguments, JsonTypeInfo<T> resultTypeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resultTypeInfo);

        return await EvaluateCoreAsync(code, hasArguments: true, arguments, ResultKind.Json, CreateResultReader(resultTypeInfo), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Evaluates JavaScript code as the body of an async function that receives arguments, and deserializes the result. Arguments are serialized using reflection.</summary>
    /// <inheritdoc cref="EvaluateAsync(string, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    /// <exception cref="JsonException">The result cannot be deserialized to <typeparamref name="T"/>.</exception>
    [RequiresUnreferencedCode(ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionDynamicCodeMessage)]
    public async Task<T?> EvaluateAsync<T>(string code, object?[]? arguments, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await EvaluateCoreAsync(code, hasArguments: true, ArgumentWriter.SerializeArguments(arguments, options), ResultKind.Json, CreateResultReader<T>(options), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Evaluates JavaScript code as the body of an async function and deserializes the result.</summary>
    /// <inheritdoc cref="EvaluateAsync(string, CancellationToken)"/>
    /// <exception cref="JsonException">The result cannot be deserialized to <typeparamref name="T"/>.</exception>
    public async Task<T?> EvaluateAsync<T>(string code, JsonTypeInfo<T> resultTypeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resultTypeInfo);

        return await EvaluateCoreAsync(code, hasArguments: false, arguments: null, ResultKind.Json, CreateResultReader(resultTypeInfo), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Evaluates JavaScript code as the body of an async function and deserializes the result using reflection.</summary>
    /// <inheritdoc cref="EvaluateAsync(string, CancellationToken)"/>
    /// <exception cref="JsonException">The result cannot be deserialized to <typeparamref name="T"/>.</exception>
    [RequiresUnreferencedCode("JSON deserialization might require types that cannot be statically analyzed. Use the overload that takes a JsonTypeInfo instead.")]
    [RequiresDynamicCode("JSON deserialization might require types that cannot be statically analyzed and might need runtime code generation. Use the overload that takes a JsonTypeInfo instead.")]
    public async Task<T?> EvaluateAsync<T>(string code, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await EvaluateCoreAsync(code, hasArguments: false, arguments: null, ResultKind.Json, CreateResultReader<T>(options), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Evaluates JavaScript code as the body of an async function and deserializes the result using reflection.</summary>
    /// <inheritdoc cref="EvaluateAsync(string, CancellationToken)"/>
    /// <exception cref="JsonException">The result cannot be deserialized to <typeparamref name="T"/>.</exception>
    [RequiresUnreferencedCode("JSON deserialization might require types that cannot be statically analyzed. Use the overload that takes a JsonTypeInfo instead.")]
    [RequiresDynamicCode("JSON deserialization might require types that cannot be statically analyzed and might need runtime code generation. Use the overload that takes a JsonTypeInfo instead.")]
    public Task<T?> EvaluateAsync<T>(string code, CancellationToken cancellationToken)
    {
        return EvaluateAsync<T>(code, options: null, cancellationToken);
    }

    /// <summary>Evaluates JavaScript code as the body of an async function, ignoring its result. The result does not need to be serializable.</summary>
    /// <inheritdoc cref="EvaluateAsync(string, CancellationToken)"/>
    public Task EvaluateVoidAsync(string code, CancellationToken cancellationToken = default)
    {
        return EvaluateCoreAsync(code, hasArguments: false, arguments: null, ResultKind.Void, ReadJsonElement, cancellationToken);
    }

    /// <summary>Evaluates JavaScript code as the body of an async function that receives arguments, ignoring its result. The result does not need to be serializable.</summary>
    /// <inheritdoc cref="EvaluateAsync(string, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    public Task EvaluateVoidAsync(string code, IReadOnlyList<JsonNode?>? arguments, CancellationToken cancellationToken = default)
    {
        return EvaluateCoreAsync(code, hasArguments: true, arguments, ResultKind.Void, ReadJsonElement, cancellationToken);
    }

    /// <summary>Evaluates JavaScript code as the body of an async function that receives arguments, ignoring its result. Arguments are serialized using reflection.</summary>
    /// <inheritdoc cref="EvaluateVoidAsync(string, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    [RequiresUnreferencedCode(ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionDynamicCodeMessage)]
    public Task EvaluateVoidAsync(string code, object?[]? arguments, JsonSerializerOptions? options, CancellationToken cancellationToken = default)
    {
        return EvaluateVoidAsync(code, ArgumentWriter.SerializeArguments(arguments, options), cancellationToken);
    }

    /// <summary>Evaluates JavaScript code as the body of an async function, and keeps the result in the Node.js process.</summary>
    /// <inheritdoc cref="EvaluateAsync(string, CancellationToken)"/>
    /// <returns>A reference to the returned value. Dispose it when the value is no longer needed.</returns>
    public Task<JSReference> EvaluateReferenceAsync(string code, CancellationToken cancellationToken = default)
    {
        return EvaluateCoreAsync(code, hasArguments: false, arguments: null, ResultKind.Reference, _referenceReader, cancellationToken);
    }

    /// <summary>Evaluates JavaScript code as the body of an async function that receives arguments, and keeps the result in the Node.js process.</summary>
    /// <inheritdoc cref="EvaluateAsync(string, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    /// <returns>A reference to the returned value. Dispose it when the value is no longer needed.</returns>
    public Task<JSReference> EvaluateReferenceAsync(string code, IReadOnlyList<JsonNode?>? arguments, CancellationToken cancellationToken = default)
    {
        return EvaluateCoreAsync(code, hasArguments: true, arguments, ResultKind.Reference, _referenceReader, cancellationToken);
    }

    /// <summary>Evaluates JavaScript code as the body of an async function that receives arguments, and keeps the result in the Node.js process. Arguments are serialized using reflection.</summary>
    /// <inheritdoc cref="EvaluateReferenceAsync(string, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    [RequiresUnreferencedCode(ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionDynamicCodeMessage)]
    public Task<JSReference> EvaluateReferenceAsync(string code, object?[]? arguments, JsonSerializerOptions? options, CancellationToken cancellationToken = default)
    {
        return EvaluateReferenceAsync(code, ArgumentWriter.SerializeArguments(arguments, options), cancellationToken);
    }

    /// <summary>Stops the Node.js process. Pending calls fail with <see cref="ObjectDisposedException"/>.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is 1)
            return;

        Terminate(new ObjectDisposedException(nameof(NodeJsHost)));

        // Closing the connection with the main thread makes the Node.js process exit, without restarting the worker threads
        if (_mainChannel is not null)
        {
            await _mainChannel.CloseAsync().ConfigureAwait(false);
        }

        _watchdogReader?.Dispose();
        if (_watchdogStream is not null)
        {
            await _watchdogStream.DisposeAsync().ConfigureAwait(false);
        }

        // Stop accepting the connections of the worker threads, then close the connections of the worker threads
        await _acceptTask.ConfigureAwait(false);
        _endpoint?.Dispose();
        var channels = _channels.Keys.ToArray();
        foreach (var channel in channels)
        {
            await channel.CloseAsync().ConfigureAwait(false);
        }

        if (_mainChannel is not null)
        {
            await _mainChannel.ReadTask.ConfigureAwait(false);
        }

        foreach (var channel in channels)
        {
            await channel.ReadTask.ConfigureAwait(false);
        }

        // The host is terminated, so the check completes without waiting for its timeout
        await _responsivenessCheck.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        if (IsProcessStarted())
        {
            // When the connection was never established, the process cannot know it must exit
            if (_mainChannel is null || !await WaitForExitAsync(DisposeExitTimeout, drainOutput: false).ConfigureAwait(false))
            {
                KillProcess();
                await WaitForExitAsync(ExitTimeout, drainOutput: false).ConfigureAwait(false);
            }
        }

        _process.Dispose();
        _acceptCts.Dispose();
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        var nodePath = _options.NodeExecutablePath ?? ExecutableFinder.GetFullExecutablePath("node") ?? throw new NodeJsException("Cannot find the 'node' executable in the PATH. Install Node.js or set NodeJsHostOptions.NodeExecutablePath.");
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

        // The watchdog uses a second connection. Worker threads connect when they start, including when they are restarted, so the endpoint lives as long as the host.
        var endpoint = NodeJsEndpoint.Create(maxConnections: _workers is not null ? null : _options.UnresponsiveTimeout is null ? 1 : 2);
        _endpoint = endpoint;
        try
        {
            await StartProcessAsync(nodePath, endpoint, token, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (_workers is null)
            {
                _endpoint = null;
                endpoint.Dispose();
            }
        }
    }

    private async Task StartProcessAsync(string nodePath, NodeJsEndpoint endpoint, string token, CancellationToken cancellationToken)
    {
        var startInfo = _process.StartInfo;
        startInfo.FileName = nodePath;
        startInfo.WorkingDirectory = _options.WorkingDirectory ?? Environment.CurrentDirectory;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;
#if NET11_0_OR_GREATER
        // Only inherit the standard streams, so Node.js does not keep other handles of this process open (e.g. pipes of other processes)
        startInfo.InheritedHandles = [];

        // On macOS, the process exits when the connection is closed, which also happens when this process exits
        if (OperatingSystem.IsLinux() || OperatingSystem.IsWindows() || OperatingSystem.IsAndroid())
        {
            startInfo.KillOnParentExit = true;
        }
#endif
        foreach (var argument in _options.NodeArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // The bootstrap script is read from the standard input (see WriteBootstrapScriptAsync)
        startInfo.ArgumentList.Add("--input-type=module");

        foreach (var (name, value) in _options.EnvironmentVariables)
        {
            if (value is null)
            {
                startInfo.Environment.Remove(name);
            }
            else
            {
                startInfo.Environment[name] = value;
            }
        }

        startInfo.Environment["MEZIANTOU_NODEJS_ENDPOINT"] = endpoint.Address;
        startInfo.Environment["MEZIANTOU_NODEJS_TOKEN"] = token;

        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                InvokeOutputCallback(_options.StandardOutputReceived, e.Data);
            }
        };

        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (_standardErrorTail)
                {
                    if (_standardErrorTail.Count == MaxStandardErrorLines)
                    {
                        _standardErrorTail.Dequeue();
                    }

                    _standardErrorTail.Enqueue(e.Data);
                }

                InvokeOutputCallback(_options.StandardErrorReceived, e.Data);
            }
        };

        try
        {
            _process.Start();
            _processId = _process.Id;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            throw new NodeJsException($"Cannot start '{nodePath}': {ex.Message}", ex);
        }

        _ = WriteBootstrapScriptAsync(_process.StandardInput);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        using var startupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startupCts.CancelAfter(_options.StartupTimeout);

        var acceptTask = endpoint.AcceptAsync(startupCts.Token);
        var exitTask = _exited.Task.WaitAsync(startupCts.Token);
        try
        {
            await Task.WhenAny(acceptTask, exitTask).ConfigureAwait(false);
            if (!acceptTask.IsCompleted && exitTask.IsCompletedSuccessfully)
            {
                await WaitForExitAsync(ExitTimeout, drainOutput: true).ConfigureAwait(false);
                throw CreateProcessExitedException("The Node.js process exited before it was ready");
            }

            var mainChannel = CreateChannel(await acceptTask.ConfigureAwait(false), isMainChannel: true);
            ProcessHelloMessage(await mainChannel.Hello.WaitAsync(startupCts.Token).ConfigureAwait(false), token);

            if (_workers is not null)
            {
                await StartWorkersAsync(endpoint, token, startupCts.Token).ConfigureAwait(false);
            }
            else if (_options.UnresponsiveTimeout is not null)
            {
                await StartWatchdogAsync(endpoint, token, startupCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && startupCts.IsCancellationRequested)
        {
            throw new NodeJsException($"The Node.js process did not start within {_options.StartupTimeout}.", ex);
        }
        finally
        {
            // Stop waiting for the process to exit
            await startupCts.CancelAsync().ConfigureAwait(false);
            await exitTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    // An exception thrown by the callback would be rethrown on a thread pool thread by Process, which would crash the application
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The callback is user code, and its exceptions must not crash the application")]
    private static void InvokeOutputCallback(Action<string>? callback, string line)
    {
        if (callback is null)
            return;

        try
        {
            callback(line);
        }
        catch (Exception)
        {
        }
    }

    internal static void ValidateOptions(NodeJsHostOptions options)
    {
        if (options.MaxConcurrentCalls is <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxConcurrentCalls, "NodeJsHostOptions.MaxConcurrentCalls must be greater than zero.");

        if (options.StartupTimeout != Timeout.InfiniteTimeSpan && (options.StartupTimeout <= TimeSpan.Zero || options.StartupTimeout.TotalMilliseconds > int.MaxValue))
            throw new ArgumentOutOfRangeException(nameof(options), options.StartupTimeout, "NodeJsHostOptions.StartupTimeout must be greater than zero and less than Int32.MaxValue milliseconds, or Timeout.InfiniteTimeSpan.");

        if (options.UnresponsiveTimeout is { } unresponsiveTimeout && (unresponsiveTimeout <= TimeSpan.Zero || unresponsiveTimeout.TotalMilliseconds > int.MaxValue))
            throw new ArgumentOutOfRangeException(nameof(options), unresponsiveTimeout, "NodeJsHostOptions.UnresponsiveTimeout must be greater than zero and less than Int32.MaxValue milliseconds.");

        if (options.WorkerThreads is <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.WorkerThreads, "NodeJsHostOptions.WorkerThreads must be greater than zero.");
    }

    /// <summary>Creates a reader that deserializes the result directly from the UTF-8 bytes of the response.</summary>
    internal static ResultReader<T?> CreateResultReader<T>(JsonTypeInfo<T> resultTypeInfo)
    {
        return utf8Json => JsonSerializer.Deserialize(utf8Json, resultTypeInfo);
    }

    /// <summary>Creates a reader that deserializes the result directly from the UTF-8 bytes of the response, using reflection.</summary>
    [RequiresUnreferencedCode(ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionDynamicCodeMessage)]
    internal static ResultReader<T?> CreateResultReader<T>(JsonSerializerOptions? options)
    {
        var resultOptions = options ?? DefaultResultSerializerOptions;
        return utf8Json => JsonSerializer.Deserialize<T>(utf8Json, resultOptions);
    }

    /// <summary>Uses the same reader whatever the connection the call is sent on.</summary>
    private static Func<NodeJsChannel, ResultReader<T>> ForAnyChannel<T>(ResultReader<T> readResult) => _ => readResult;

    // The JsonElement owns a copy of the bytes, so it remains valid once the buffer that receives messages is reused
    internal static JsonElement ReadJsonElement(ReadOnlySpan<byte> utf8Json) => JsonElement.Parse(utf8Json, UnlimitedDepthDocumentOptions);

    internal static long ReadReferenceId(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new Utf8JsonReader(utf8Json);
        if (!reader.Read())
            throw new JsonException("The reference identifier is missing.");

        return reader.GetInt64();
    }

    private void ProcessHelloMessage(byte[] message, string expectedToken)
    {
        using var document = JsonDocument.Parse(message);
        var root = document.RootElement;
        ValidateHelloMessage(root, expectedToken);

        if (root.TryGetProperty("version", out var version) && version.ValueKind is JsonValueKind.String)
        {
            NodeVersion = version.GetString()!;
        }
    }

    private static void ValidateHelloMessage(JsonElement root, string expectedToken)
    {
        if (root.ValueKind is not JsonValueKind.Object ||
            !root.TryGetProperty("type", out var type) || !type.ValueEquals("hello") ||
            !root.TryGetProperty("token", out var token) || token.ValueKind is not JsonValueKind.String ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token.GetString()!), Encoding.UTF8.GetBytes(expectedToken)))
        {
            throw new NodeJsException("The Node.js process sent an invalid handshake.");
        }
    }

    /// <summary>Starts the watchdog, a thread of the Node.js process that tells which call runs on the main thread, even when its event loop is blocked (see <see cref="CheckResponsivenessAsync"/>).</summary>
    private async Task StartWatchdogAsync(NodeJsEndpoint endpoint, string expectedToken, CancellationToken cancellationToken)
    {
        using var acceptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var acceptTask = endpoint.AcceptAsync(acceptCts.Token);
        try
        {
            await _mainChannel!.WriteMessageAsync(writer => writer.WriteString("type", "watchdog")).ConfigureAwait(false);

            // The read loop completes when the process exits, e.g. because the watchdog cannot start
            if (await Task.WhenAny(acceptTask, _mainChannel.ReadTask).ConfigureAwait(false) != acceptTask)
            {
                ThrowIfTerminated();
            }

            _watchdogStream = await acceptTask.ConfigureAwait(false);
        }
        finally
        {
            await acceptCts.CancelAsync().ConfigureAwait(false);
            await ((Task)acceptTask).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        _watchdogReader = new StreamReader(_watchdogStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 256, leaveOpen: true);
        var hello = await _watchdogReader.ReadLineAsync(cancellationToken).ConfigureAwait(false) ?? throw CreateProcessExitedException("The Node.js process closed the watchdog connection before it was ready");
        using var document = JsonDocument.Parse(hello);
        ValidateHelloMessage(document.RootElement, expectedToken);
    }

    /// <summary>Asks the main thread to start the worker threads, and waits for all of them to connect.</summary>
    private async Task StartWorkersAsync(NodeJsEndpoint endpoint, string token, CancellationToken cancellationToken)
    {
        _acceptTask = AcceptWorkersAsync(endpoint, token);
        await _mainChannel!.WriteMessageAsync(writer =>
        {
            writer.WriteString("type", "workers");
            writer.WriteNumber("count", _workers!.Length);
            writer.WriteBoolean("tracking", _options.UnresponsiveTimeout is not null);
            if (_options.StartupTimeout != Timeout.InfiniteTimeSpan)
            {
                writer.WriteNumber("connectTimeout", (long)_options.StartupTimeout.TotalMilliseconds);
            }
        }).ConfigureAwait(false);

        // The read loop completes when the process exits, e.g. because a worker thread cannot start
        Task[] ready;
        lock (_workersLock)
        {
            ready = Array.ConvertAll(_workers!, slot => (Task)slot.Ready.Task);
        }

        await Task.WhenAny(Task.WhenAll(ready), _mainChannel.ReadTask).WaitAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfTerminated();
    }

    /// <summary>Accepts the connections of the worker threads, when they start and each time they are restarted, until the host is terminated.</summary>
    private async Task AcceptWorkersAsync(NodeJsEndpoint endpoint, string token)
    {
        while (true)
        {
            Stream stream;
            try
            {
                stream = await endpoint.AcceptAsync(_acceptCts.Token).ConfigureAwait(false);
            }
            catch (IOException) when (!_acceptCts.IsCancellationRequested)
            {
                // The client disconnected before the connection was accepted
                continue;
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException or SocketException)
            {
                return;
            }

            // A client that never sends its handshake must not prevent other worker threads from connecting
            _ = AttachWorkerAsync(stream, token);
        }
    }

    /// <summary>Validates the handshake of a worker thread, then sends calls to it.</summary>
    private async Task AttachWorkerAsync(Stream stream, string token)
    {
        var channel = CreateChannel(stream, isMainChannel: false);
        var attached = false;
        try
        {
            var hello = await channel.Hello.WaitAsync(_options.StartupTimeout).ConfigureAwait(false);
            using var document = JsonDocument.Parse(hello);
            var root = document.RootElement;
            ValidateHelloMessage(root, token);
            var worker = root.GetProperty("worker").GetInt32();
            var generation = root.GetProperty("generation").GetInt32();
            if (worker >= 0 && worker < _workers!.Length)
            {
                lock (_workersLock)
                {
                    // A worker thread that restarts always has a new generation, so a connection cannot replace a newer one.
                    // A connection closed before being attached is terminated, as nothing would fail its calls.
                    var slot = _workers[worker];
                    if (!IsTerminated && !channel.IsTerminated && generation > slot.Generation)
                    {
                        channel.Worker = worker;
                        channel.Generation = generation;
                        slot.Channel = channel;
                        slot.Generation = generation;
                        slot.Ready.TrySetResult();
                        attached = true;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or NodeJsException or TimeoutException or IOException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
        }

        if (!attached)
        {
            await channel.CloseAsync().ConfigureAwait(false);
        }
    }

    private NodeJsChannel CreateChannel(Stream stream, bool isMainChannel)
    {
        var channel = new NodeJsChannel(this, stream);
        if (isMainChannel)
        {
            _mainChannel = channel;
        }

        _channels[channel] = 0;
        channel.StartReading();
        return channel;
    }

    /// <summary>Called by a channel once its connection is closed.</summary>
    internal async Task OnChannelClosedAsync(NodeJsChannel channel, Exception? error)
    {
        try
        {
            if (channel == _mainChannel)
            {
                await OnMainChannelClosedAsync(channel, error).ConfigureAwait(false);
                return;
            }

            int? worker;
            lock (_workersLock)
            {
                worker = channel.Worker;
                if (worker is null)
                {
                    // A connection whose handshake is not accepted, so it can no longer be attached to a worker thread
                    channel.Terminate(new NodeJsException("The connection was closed."));
                }
                else
                {
                    // No new call is sent to the worker thread
                    ResetSlot(_workers![worker.Value], channel);
                }
            }

            if (worker is not null)
            {
                await OnWorkerChannelClosedAsync(channel, worker.Value, error).ConfigureAwait(false);
            }
        }
        finally
        {
            _channels.TryRemove(channel, out _);
        }
    }

    private async Task OnMainChannelClosedAsync(NodeJsChannel channel, Exception? error)
    {
        if (!channel.Hello.IsCompleted)
        {
            if (!IsTerminated)
            {
                await WaitForExitAsync(ExitTimeout, drainOutput: true).ConfigureAwait(false);
            }

            channel.FailHello(CreateProcessExitedException("The Node.js process closed the connection before it was ready"));
        }

        if (!IsTerminated)
        {
            Exception exception;
            if (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            {
                exception = new NodeJsException("The Node.js process sent an invalid message.", error);
            }
            else
            {
                // The connection is lost, most likely because the process exited (e.g. process.exit() or a crash)
                await WaitForExitAsync(ExitTimeout, drainOutput: true).ConfigureAwait(false);
                exception = CreateProcessExitedException("The Node.js process exited unexpectedly");
            }

            Terminate(exception);
        }
    }

    /// <summary>Fails the calls of a worker thread whose connection is closed. The main thread restarts the worker thread, which connects again.</summary>
    private async Task OnWorkerChannelClosedAsync(NodeJsChannel channel, int worker, Exception? error)
    {
        // The worker thread stops once its connection is closed, including when it sent an invalid message
        await channel.CloseAsync().ConfigureAwait(false);

        // The connection is closed before the main thread reports the exit of the worker thread
        var exitCode = await WaitForWorkerExitAsync(worker, channel.Generation).ConfigureAwait(false);
        Exception exception;
        if (Volatile.Read(ref _terminationException) is { } terminationException)
        {
            exception = terminationException;
        }
        else if (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            exception = new NodeJsException("The Node.js worker thread sent an invalid message.", error);
        }
        else
        {
            var message = new StringBuilder("The Node.js worker thread exited unexpectedly");
            if (exitCode is not null)
            {
                message.Append(" with exit code ").Append(exitCode.Value);
            }

            message.Append('.');
            AppendStandardErrorTail(message);

            // The process did not exit
            exception = new NodeJsException(message.ToString(), exitCode: null);
        }

        channel.Terminate(exception);
    }

    private async Task<int?> WaitForWorkerExitAsync(int worker, int generation)
    {
        var key = (worker, generation);
        var exit = _workerExits.GetOrAdd(key, _ => new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously));
        try
        {
            await Task.WhenAny(exit.Task, _terminated.Task).WaitAsync(ExitTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }

        _workerExits.TryRemove(key, out _);
        return exit.Task.IsCompletedSuccessfully ? exit.Task.Result : null;
    }

    /// <summary>Processes a message of the main thread that is not a response.</summary>
    internal void OnNotice(NodeJsChannel channel, ReadOnlySpan<byte> message)
    {
        if (channel != _mainChannel || _workers is null)
            throw new JsonException("The message is not a response.");

        var root = JsonElement.Parse(message);
        if (!root.GetProperty("type").ValueEquals("workerExit"))
            throw new JsonException("The message is not a known notice.");

        // A worker thread exited, and the main thread restarts it
        var worker = root.GetProperty("worker").GetInt32();
        var generation = root.GetProperty("generation").GetInt32();
        int? exitCode = root.TryGetProperty("exitCode", out var exitCodeElement) && exitCodeElement.ValueKind is JsonValueKind.Number ? exitCodeElement.GetInt32() : null;
        _workerExits.GetOrAdd((worker, generation), _ => new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult(exitCode);
    }

    /// <summary>Stops sending calls to a worker thread, until it is restarted. Must be called while holding <see cref="_workersLock"/>.</summary>
    private static void ResetSlot(WorkerSlot slot, NodeJsChannel channel)
    {
        if (slot.Channel != channel)
            return;

        slot.Channel = null;
        slot.Ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private Task<T> InvokeCoreAsync<T>(string module, string? exportName, IReadOnlyList<JsonNode?>? arguments, ResultKind resultKind, ResultReader<T> readResult, CancellationToken cancellationToken, bool construct = false)
    {
        return InvokeCoreAsync(module, exportName, arguments, resultKind, ForAnyChannel(readResult), cancellationToken, construct);
    }

    private Task<T> InvokeCoreAsync<T>(string module, string? exportName, IReadOnlyList<JsonNode?>? arguments, ResultKind resultKind, Func<NodeJsChannel, ResultReader<T>> createReader, CancellationToken cancellationToken, bool construct = false)
    {
        ArgumentNullException.ThrowIfNull(module);

        return SendRequestAsync(target: null, arguments, (writer, channel) =>
        {
            writer.WriteString("type", "invoke");
            writer.WriteString("module", module);
            writer.WriteString("export", exportName);
            if (construct)
            {
                writer.WriteBoolean("construct", true);
            }

            ArgumentWriter.Write(writer, channel, arguments);
        }, resultKind, createReader, cancellationToken);
    }

    private Task<T> EvaluateCoreAsync<T>(string code, bool hasArguments, IReadOnlyList<JsonNode?>? arguments, ResultKind resultKind, ResultReader<T> readResult, CancellationToken cancellationToken)
    {
        return EvaluateCoreAsync(code, hasArguments, arguments, resultKind, ForAnyChannel(readResult), cancellationToken);
    }

    private Task<T> EvaluateCoreAsync<T>(string code, bool hasArguments, IReadOnlyList<JsonNode?>? arguments, ResultKind resultKind, Func<NodeJsChannel, ResultReader<T>> createReader, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);

        return SendRequestAsync(target: null, arguments, (writer, channel) =>
        {
            writer.WriteString("type", "eval");
            writer.WriteString("code", code);
            if (hasArguments)
            {
                ArgumentWriter.Write(writer, channel, arguments);
            }
        }, resultKind, createReader, cancellationToken);
    }

    internal Task<T> InvokeMemberAsync<T>(JSReference target, string? memberName, IReadOnlyList<JsonNode?>? arguments, ResultKind resultKind, ResultReader<T> readResult, CancellationToken cancellationToken)
    {
        return InvokeMemberCoreAsync(target, memberName, arguments, resultKind, ForAnyChannel(readResult), construct: false, cancellationToken);
    }

    internal Task<JSReference> InvokeMemberReferenceAsync(JSReference target, string? memberName, IReadOnlyList<JsonNode?>? arguments, CancellationToken cancellationToken, bool construct = false)
    {
        return InvokeMemberCoreAsync(target, memberName, arguments, ResultKind.Reference, _referenceReader, construct, cancellationToken);
    }

    private Task<T> InvokeMemberCoreAsync<T>(JSReference target, string? memberName, IReadOnlyList<JsonNode?>? arguments, ResultKind resultKind, Func<NodeJsChannel, ResultReader<T>> createReader, bool construct, CancellationToken cancellationToken)
    {
        return SendRequestAsync(target, arguments, (writer, channel) =>
        {
            ObjectDisposedException.ThrowIf(target.IsDisposed, target);
            writer.WriteString("type", "invokeReference");
            writer.WriteNumber("reference", target.Id);
            writer.WriteString("member", memberName);
            if (construct)
            {
                writer.WriteBoolean("construct", true);
            }

            ArgumentWriter.Write(writer, channel, arguments);
        }, resultKind, createReader, cancellationToken);
    }

    internal Task<T> GetReferenceValueAsync<T>(JSReference target, ResultReader<T> readResult, CancellationToken cancellationToken)
    {
        return SendRequestAsync(target, arguments: null, (writer, _) =>
        {
            ObjectDisposedException.ThrowIf(target.IsDisposed, target);
            writer.WriteString("type", "getReference");
            writer.WriteNumber("reference", target.Id);
        }, ResultKind.Json, ForAnyChannel(readResult), cancellationToken);
    }

    /// <summary>Gets information about the state of the thread that keeps the value of <paramref name="target"/>, or of any thread that runs calls, for tests.</summary>
    internal Task<JsonElement> GetDebugInformationAsync(JSReference? target, CancellationToken cancellationToken)
    {
        return SendRequestAsync(target, arguments: null, (writer, _) => writer.WriteString("type", "debug"), ResultKind.Json, ForAnyChannel<JsonElement>(ReadJsonElement), cancellationToken);
    }

    /// <summary>Gets information about the state of the Node.js process, for tests.</summary>
    internal Task<JsonElement> GetDebugInformationAsync(CancellationToken cancellationToken) => GetDebugInformationAsync(target: null, cancellationToken);

    /// <summary>Sends a request that is not valid JSON, for tests.</summary>
    internal Task<JsonElement> SendInvalidRequestAsync(CancellationToken cancellationToken)
    {
        return SendRequestAsync(target: null, arguments: null, (writer, _) =>
        {
            writer.WriteString("type", "debug");
            writer.WritePropertyName("invalid");

            // The object is never closed
            writer.WriteRawValue("{", skipInputValidation: true);
        }, ResultKind.Json, ForAnyChannel<JsonElement>(ReadJsonElement), cancellationToken);
    }

    /// <param name="target">The reference whose value the call uses, if any.</param>
    /// <param name="arguments">The arguments of the call. With worker threads, the call runs on the worker thread that keeps the values of the references they contain.</param>
    /// <param name="writeMessage">Writes the message, for the connection it is sent on.</param>
    /// <param name="resultKind">How the result is returned.</param>
    /// <param name="createReader">Creates the reader of the result, for the connection the message is sent on.</param>
    /// <param name="cancellationToken">A token to stop waiting for the result.</param>
    private async Task<T> SendRequestAsync<T>(JSReference? target, IReadOnlyList<JsonNode?>? arguments, Action<Utf8JsonWriter, NodeJsChannel> writeMessage, ResultKind resultKind, Func<NodeJsChannel, ResultReader<T>> createReader, CancellationToken cancellationToken)
    {
        // A host whose process exited reports the exit even once disposed (e.g. by NodeJsHostPool when it replaces the process), as it is the cause of the failure
        ThrowIfTerminated();
        ObjectDisposedException.ThrowIf(_disposed is 1, this);
        cancellationToken.ThrowIfCancellationRequested();

        // A call that uses a reference runs on the worker thread that keeps the referenced value
        var owner = _workers is null ? null : ArgumentWriter.FindReferenceChannel(this, target, arguments);

        if (_concurrencyLimit is null)
            return await SendRequestCoreAsync(owner, writeMessage, resultKind, createReader, cancellationToken).ConfigureAwait(false);

        // The message is serialized once the call can run, so waiting calls do not keep a serialized copy of their arguments
        await _concurrencyLimit.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SendRequestCoreAsync(owner, writeMessage, resultKind, createReader, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _concurrencyLimit.Release();
        }
    }

    private async Task<T> SendRequestCoreAsync<T>(NodeJsChannel? owner, Action<Utf8JsonWriter, NodeJsChannel> writeMessage, ResultKind resultKind, Func<NodeJsChannel, ResultReader<T>> createReader, CancellationToken cancellationToken)
    {
        var (channel, slot) = await AcquireChannelAsync(owner, cancellationToken).ConfigureAwait(false);
        try
        {
            var id = Interlocked.Increment(ref _nextRequestId);
            return await channel.SendRequestAsync(id, writer => writeMessage(writer, channel), resultKind, createReader(channel), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (slot is not null)
            {
                lock (_workersLock)
                {
                    slot.PendingCalls--;
                }
            }
        }
    }

    /// <summary>Selects the connection a call is sent on: the main thread, the worker thread that keeps the referenced values, or the worker thread with the fewest calls in progress.</summary>
    /// <returns>The connection, and the worker thread whose pending calls must be decremented once the call completes.</returns>
    private async ValueTask<(NodeJsChannel Channel, WorkerSlot? Slot)> AcquireChannelAsync(NodeJsChannel? owner, CancellationToken cancellationToken)
    {
        if (_workers is null)
            return (_mainChannel!, null);

        if (owner is not null)
        {
            // The values of a worker thread that exited are lost
            owner.ThrowIfTerminated();
            lock (_workersLock)
            {
                var slot = _workers[owner.Worker!.Value];
                if (slot.Channel == owner)
                {
                    slot.PendingCalls++;
                    return (owner, slot);
                }
            }

            // The worker thread is exiting, so the call fails once its connection is terminated
            return (owner, null);
        }

        while (true)
        {
            Task ready;
            lock (_workersLock)
            {
                ThrowIfTerminated();

                // Start from a different worker thread each time, so worker threads with the same number of pending calls are used in turn
                var offset = (int)(_nextWorker++ % (uint)_workers.Length);
                WorkerSlot? bestSlot = null;
                var bestLoad = 0;
                for (var i = 0; i < _workers.Length; i++)
                {
                    var slot = _workers[(offset + i) % _workers.Length];
                    if (slot.Channel is null)
                        continue;

                    // A canceled call may still be running, e.g. blocking the event loop, so it counts until the worker thread responds
                    var load = slot.PendingCalls + slot.Channel.AbandonedCalls;
                    if (bestSlot is null || load < bestLoad)
                    {
                        bestSlot = slot;
                        bestLoad = load;
                    }
                }

                if (bestSlot is not null)
                {
                    bestSlot.PendingCalls++;
                    return (bestSlot.Channel!, bestSlot);
                }

                // Every worker thread is restarting
                var tasks = new Task[_workers.Length + 1];
                for (var i = 0; i < _workers.Length; i++)
                {
                    tasks[i] = _workers[i].Ready.Task;
                }

                tasks[^1] = _terminated.Task;
                ready = Task.WhenAny(tasks);
            }

            await ready.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Called by a channel when the caller stops waiting for the response of a call.</summary>
    internal void OnRequestAbandoned()
    {
        Interlocked.Increment(ref _canceledCalls);
        if (_options.UnresponsiveTimeout is { } timeout)
        {
            if (Interlocked.CompareExchange(ref _responsivenessCheckRunning, 1, 0) is 0)
            {
                _responsivenessCheck = CheckResponsivenessAsync(timeout);
            }
            else
            {
                // Check the process again, as the call may block the event loop
                _responsivenessCheckSignal.Release();
            }
        }
    }

    /// <summary>Kills the process, or terminates the worker thread, when a canceled call blocks its event loop, as it is the only way to stop its JavaScript code. Runs until no canceled call is running.</summary>
    /// <remarks>
    /// The decision only depends on a thread of the Node.js process that measures for how long the event loop has been running the same turn, and tells which call runs:
    /// the watchdog thread for the main thread, or the main thread for the worker threads.
    /// It does not depend on the time it takes to exchange messages with the process, which also depends on this process (e.g. a large message being sent, or a busy thread pool).
    /// </remarks>
    private async Task CheckResponsivenessAsync(TimeSpan timeout)
    {
        var interval = timeout < MaxResponsivenessCheckInterval ? timeout : MaxResponsivenessCheckInterval;
        while (true)
        {
            var canceledCalls = Interlocked.Read(ref _canceledCalls);
            var delay = interval;
            try
            {
                var blocked = false;
                foreach (var (channel, runningCall, elapsed) in await GetRunningCallsAsync().ConfigureAwait(false))
                {
                    if (!channel.IsAbandoned(runningCall))
                        continue;

                    if (elapsed >= timeout)
                    {
                        if (_workers is null)
                        {
                            Terminate(new NodeJsException($"The Node.js process was killed, as a canceled call blocked its event loop for more than {timeout}, e.g. with an infinite loop."));
                            KillProcess();
                            return;
                        }

                        await TerminateWorkerAsync(channel, timeout).ConfigureAwait(false);
                        continue;
                    }

                    // Check again as soon as the call may have blocked the event loop for longer than the timeout
                    blocked = true;
                    if (timeout - elapsed < delay)
                    {
                        delay = timeout - elapsed;
                    }
                }

                if (!blocked)
                {
                    // No canceled call blocks an event loop now
                    Interlocked.Exchange(ref _checkedCanceledCalls, canceledCalls);
                }
            }
            catch (Exception ex) when (ex is ObjectDisposedException or IOException or OperationCanceledException or NodeJsException)
            {
                // The process exited, or the host is disposed
                return;
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                // The process sent an invalid response, so it cannot be checked
                Terminate(new NodeJsException("The Node.js process sent an invalid message.", ex));
                KillProcess();
                return;
            }

            // A canceled call can block the event loop later (e.g. after awaiting I/O), so the process is checked until no canceled call is running.
            // The check runs sooner when another call is canceled, or when the host is terminated.
            if (AbandonedCalls > 0 && !IsTerminated)
            {
                await _responsivenessCheckSignal.WaitAsync(delay).ConfigureAwait(false);
                continue;
            }

            // A call canceled concurrently either sees that the check is running and signals it, or starts a new check
            Volatile.Write(ref _responsivenessCheckRunning, 0);
            if (AbandonedCalls is 0 || IsTerminated || Interlocked.CompareExchange(ref _responsivenessCheckRunning, 1, 0) is not 0)
                return;
        }
    }

    /// <summary>Gets the identifier of the call whose code runs on each thread that runs calls (0 when its event loop is idle, -1 when the code does not belong to a call), and for how long its event loop has been running the same turn.</summary>
    /// <remarks>The main thread, or the watchdog thread when the calls run on the main thread, answers even when an event loop is blocked. When the whole process is unresponsive (e.g. suspended), this waits until it responds or exits.</remarks>
    private async Task<List<(NodeJsChannel Channel, long RunningCall, TimeSpan Elapsed)>> GetRunningCallsAsync()
    {
        var result = new List<(NodeJsChannel Channel, long RunningCall, TimeSpan Elapsed)>();
        if (_workers is null)
        {
            await _watchdogStream!.WriteAsync(WatchdogRequest).ConfigureAwait(false);
            await _watchdogStream.FlushAsync().ConfigureAwait(false);
            var response = await _watchdogReader!.ReadLineAsync().ConfigureAwait(false) ?? throw new IOException("The watchdog connection is closed.");
            using var document = JsonDocument.Parse(response);
            var root = document.RootElement;
            result.Add((_mainChannel!, root.GetProperty("running").GetInt64(), TimeSpan.FromMilliseconds(root.GetProperty("elapsed").GetInt64())));
            return result;
        }

        // The main thread does not run calls, so it answers even when the event loop of a worker thread is blocked
        var id = Interlocked.Increment(ref _nextRequestId);
        var calls = await _mainChannel!.SendRequestAsync(id, writer => writer.WriteString("type", "running"), ResultKind.Json, ReadJsonElement, CancellationToken.None).ConfigureAwait(false);
        foreach (var call in calls.EnumerateArray())
        {
            var worker = call.GetProperty("worker").GetInt32();
            var generation = call.GetProperty("generation").GetInt32();
            if (worker < 0 || worker >= _workers.Length)
                throw new JsonException("The worker thread does not exist.");

            NodeJsChannel? channel;
            lock (_workersLock)
            {
                channel = _workers[worker].Channel;
            }

            // A worker thread that is restarting runs no call
            if (channel is not null && channel.Generation == generation)
            {
                result.Add((channel, call.GetProperty("running").GetInt64(), TimeSpan.FromMilliseconds(call.GetProperty("elapsed").GetInt64())));
            }
        }

        return result;
    }

    /// <summary>Terminates a worker thread blocked by a canceled call. Its calls fail, and the main thread restarts it.</summary>
    private async Task TerminateWorkerAsync(NodeJsChannel channel, TimeSpan timeout)
    {
        channel.Terminate(new NodeJsException($"The Node.js worker thread was terminated, as a canceled call blocked its event loop for more than {timeout}, e.g. with an infinite loop.", exitCode: null));
        lock (_workersLock)
        {
            ResetSlot(_workers![channel.Worker!.Value], channel);
        }

        // The worker thread cannot process its connection being closed while its event loop is blocked, so the main thread terminates it
        await _mainChannel!.WriteMessageAsync(writer =>
        {
            writer.WriteString("type", "terminateWorker");
            writer.WriteNumber("worker", channel.Worker!.Value);
            writer.WriteNumber("generation", channel.Generation);
        }).ConfigureAwait(false);
        await channel.CloseAsync().ConfigureAwait(false);
    }

    /// <summary>Waits until the process is found not blocked by a canceled call after the last canceled call, or the check stops (e.g. when the process is killed), for tests.</summary>
    internal async Task WaitForResponsivenessCheckAsync()
    {
        var canceledCalls = Interlocked.Read(ref _canceledCalls);
        while (Interlocked.Read(ref _checkedCanceledCalls) < canceledCalls && !_responsivenessCheck.IsCompleted)
        {
            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    private void Terminate(Exception exception)
    {
        if (Interlocked.CompareExchange(ref _terminationException, exception, comparand: null) is not null)
            return;

        foreach (var (channel, _) in _channels)
        {
            channel.Terminate(exception);
        }

        _terminated.TrySetResult();

        // Stop accepting the connections of the worker threads, and the responsiveness check
        _acceptCts.Cancel();
        _responsivenessCheckSignal.Release();
    }

    private void ThrowIfTerminated()
    {
        var exception = Volatile.Read(ref _terminationException);
        if (exception is not null)
        {
            ThrowTerminationException(exception);
        }
    }

    /// <summary>Throws the exception of a call made once the host or one of its connections is terminated by <paramref name="exception"/>.</summary>
    [DoesNotReturn]
    internal void ThrowTerminationException(Exception exception)
    {
        ObjectDisposedException.ThrowIf(exception is ObjectDisposedException, this);

        if (exception is NodeJsException nodeJsException)
            throw new NodeJsException(nodeJsException.Message, nodeJsException.ExitCode);

        throw new NodeJsException(exception.Message, exception);
    }

    private NodeJsException CreateProcessExitedException(string message)
    {
        int? exitCode = _process.HasExited ? _process.ExitCode : null;
        var result = new StringBuilder(message);
        if (exitCode is not null)
        {
            result.Append(" with exit code ").Append(exitCode.Value);
        }

        result.Append('.');
        AppendStandardErrorTail(result);
        return new NodeJsException(result.ToString(), exitCode);
    }

    private void AppendStandardErrorTail(StringBuilder message)
    {
        lock (_standardErrorTail)
        {
            if (_standardErrorTail.Count > 0)
            {
                message.AppendLine().Append("Standard error:");
                foreach (var line in _standardErrorTail)
                {
                    message.AppendLine().Append(line);
                }
            }
        }
    }

    // Process.WaitForExitAsync also waits for the redirected output to be closed. A child process of Node.js that
    // inherited the output keeps it open after Node.js exits, so it is only used for a short time to read the remaining output.
    private async Task<bool> WaitForExitAsync(TimeSpan timeout, bool drainOutput)
    {
        try
        {
            await _exited.Task.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return false;
        }

        if (drainOutput)
        {
            // Read the remaining output, so the standard error tail is complete
            using var cts = new CancellationTokenSource(OutputDrainTimeout);
            await _process.WaitForExitAsync(cts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        return true;
    }

    private void KillProcess()
    {
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process has already exited, or the host is disposed
        }
    }

    private bool IsProcessStarted()
    {
        try
        {
            _ = _process.Id;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>A worker thread that runs calls. It is restarted with a new connection when it exits.</summary>
    private sealed class WorkerSlot
    {
        /// <summary>Gets or sets the connection with the worker thread, or <see langword="null"/> while it is restarting.</summary>
        public NodeJsChannel? Channel;

        /// <summary>Gets or sets the generation of the last connection of the worker thread.</summary>
        public int Generation;

        /// <summary>Gets or sets the number of calls sent to the worker thread that are waiting for their response.</summary>
        public int PendingCalls;

        /// <summary>Gets or sets a task that completes once the worker thread is connected.</summary>
        public TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // The script is sent on the standard input instead of the command line, so its size is not limited by the maximum length of a command line
    // (32,767 characters on Windows), and it does not appear in the list of processes. It is written in the background, as Node.js only reads it once
    // the modules passed with --import are loaded, and the pipe buffer can be smaller than the script.
    // Never throws: when the process exits before reading the script, the startup reports it.
    private static async Task WriteBootstrapScriptAsync(StreamWriter standardInput)
    {
        try
        {
            await standardInput.WriteAsync(BootstrapScript).ConfigureAwait(false);

            // Node.js runs the script once the standard input is closed
            standardInput.Close();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }
    }

    private static string GetBootstrapScript()
    {
        using var stream = typeof(NodeJsHost).Assembly.GetManifestResourceStream("Meziantou.Framework.NodeJs.bootstrap.mjs") ?? throw new InvalidOperationException("The bootstrap script is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
