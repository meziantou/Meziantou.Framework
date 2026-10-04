using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
/// <para>Calls can run concurrently. The Node.js process exits when the host is disposed or when the .NET process exits.</para>
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
    private static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(1);

    // Messages are only read by JSON.parse, so characters that are sensitive in HTML or non-ASCII do not need to be escaped, which would make them up to 6 times larger
    internal static readonly JsonWriterOptions MessageWriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // Results can be deeply nested (e.g. syntax trees). JsonDocument does not use recursion, so there is no reason to limit the depth.
    private static readonly JsonDocumentOptions ResultDocumentOptions = new() { MaxDepth = int.MaxValue };

    // Initial size of the buffer that receives messages. It grows to contain the largest message, and shrinks back once the message is processed.
    private const int ReadBufferSize = 16 * 1024;

    // The default options, except that NaN and infinities, sent as "NaN", "Infinity", and "-Infinity", can be read as numbers
    [SuppressMessage("Usage", "MA0224:Set RespectNullableAnnotations on the JsonSerializerOptions instance", Justification = "Same behavior as the default options")]
    [SuppressMessage("Usage", "MA0225:Set RespectRequiredConstructorParameters on the JsonSerializerOptions instance", Justification = "Same behavior as the default options")]
    private static readonly JsonSerializerOptions DefaultResultSerializerOptions = new() { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    private readonly NodeJsHostOptions _options;
    private readonly Process _process;
    private readonly ConcurrentDictionary<long, PendingRequest> _pendingRequests = new();
    [SuppressMessage("Reliability", "CA2213:Disposable fields should be disposed", Justification = "AvailableWaitHandle is never used, and disposing it would leave concurrent callers waiting forever")]
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    [SuppressMessage("Reliability", "CA2213:Disposable fields should be disposed", Justification = "AvailableWaitHandle is never used, and disposing it would leave concurrent callers waiting forever")]
    private readonly SemaphoreSlim? _concurrencyLimit;
    private readonly Queue<string> _standardErrorTail = new();
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Stream? _stream;
    private Task? _readTask;
    private Exception? _terminationException;
    private long _nextRequestId;
    private int _disposed;

    private NodeJsHost(NodeJsHostOptions options)
    {
        _options = options;
        if (options.MaxConcurrentCalls is { } maxConcurrentCalls)
        {
            _concurrencyLimit = new SemaphoreSlim(maxConcurrentCalls, maxConcurrentCalls);
        }

        _process = new Process { EnableRaisingEvents = true };
        _process.Exited += (_, _) => _exited.TrySetResult();
    }

    /// <summary>Gets the identifier of the Node.js process.</summary>
    public int ProcessId => _process.Id;

    /// <summary>Gets the version of Node.js, as reported by <c>process.version</c> (e.g. <c>v24.15.0</c>).</summary>
    public string NodeVersion { get; private set; } = "";

    /// <summary>Gets a value indicating whether the host can no longer run code, because it is disposed or the process exited.</summary>
    internal bool IsTerminated => Volatile.Read(ref _terminationException) is not null;

    /// <summary>Starts a new Node.js process.</summary>
    /// <exception cref="NodeJsException">The <c>node</c> executable cannot be found, or the process fails to start.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="NodeJsHostOptions.MaxConcurrentCalls"/> is zero or negative.</exception>
    public static async Task<NodeJsHost> StartAsync(NodeJsHostOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= NodeJsHostOptions.Default;
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
    /// <param name="module">The module specifier: an npm package name, a path relative to <see cref="NodeJsHostOptions.WorkingDirectory"/>, an absolute path, or a URL.</param>
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
    public async Task<T?> InvokeAsync<T>(string module, string? exportName, IReadOnlyList<JsonNode?>? arguments, JsonTypeInfo<T> resultTypeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resultTypeInfo);

        return await InvokeCoreAsync(module, exportName, arguments, ResultKind.Json, CreateResultReader(resultTypeInfo), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Imports a module, calls one of its exports, and deserializes the result. Arguments are serialized using reflection.</summary>
    /// <inheritdoc cref="InvokeAsync(string, string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    [RequiresUnreferencedCode(ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionDynamicCodeMessage)]
    public async Task<T?> InvokeAsync<T>(string module, string? exportName, object?[]? arguments = null, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await InvokeCoreAsync(module, exportName, ArgumentWriter.SerializeArguments(arguments, options), ResultKind.Json, CreateResultReader<T>(options), cancellationToken).ConfigureAwait(false);
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
    public async Task<JSReference> InvokeReferenceAsync(string module, string? exportName, IReadOnlyList<JsonNode?>? arguments = null, CancellationToken cancellationToken = default)
    {
        var referenceId = await InvokeCoreAsync(module, exportName, arguments, ResultKind.Reference, ReadReferenceId, cancellationToken).ConfigureAwait(false);
        return new JSReference(this, referenceId);
    }

    /// <summary>Imports a module, calls one of its exports, and keeps the result in the Node.js process. Arguments are serialized using reflection.</summary>
    /// <inheritdoc cref="InvokeReferenceAsync(string, string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    [RequiresUnreferencedCode(ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionDynamicCodeMessage)]
    public Task<JSReference> InvokeReferenceAsync(string module, string? exportName, object?[]? arguments, JsonSerializerOptions? options, CancellationToken cancellationToken = default)
    {
        return InvokeReferenceAsync(module, exportName, ArgumentWriter.SerializeArguments(arguments, options), cancellationToken);
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
    public async Task<T?> EvaluateAsync<T>(string code, IReadOnlyList<JsonNode?>? arguments, JsonTypeInfo<T> resultTypeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resultTypeInfo);

        return await EvaluateCoreAsync(code, hasArguments: true, arguments, ResultKind.Json, CreateResultReader(resultTypeInfo), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Evaluates JavaScript code as the body of an async function that receives arguments, and deserializes the result. Arguments are serialized using reflection.</summary>
    /// <inheritdoc cref="EvaluateAsync(string, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    [RequiresUnreferencedCode(ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionDynamicCodeMessage)]
    public async Task<T?> EvaluateAsync<T>(string code, object?[]? arguments, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await EvaluateCoreAsync(code, hasArguments: true, ArgumentWriter.SerializeArguments(arguments, options), ResultKind.Json, CreateResultReader<T>(options), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Evaluates JavaScript code as the body of an async function and deserializes the result.</summary>
    /// <inheritdoc cref="EvaluateAsync(string, CancellationToken)"/>
    public async Task<T?> EvaluateAsync<T>(string code, JsonTypeInfo<T> resultTypeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resultTypeInfo);

        return await EvaluateCoreAsync(code, hasArguments: false, arguments: null, ResultKind.Json, CreateResultReader(resultTypeInfo), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Evaluates JavaScript code as the body of an async function and deserializes the result using reflection.</summary>
    /// <inheritdoc cref="EvaluateAsync(string, CancellationToken)"/>
    [RequiresUnreferencedCode("JSON deserialization might require types that cannot be statically analyzed. Use the overload that takes a JsonTypeInfo instead.")]
    [RequiresDynamicCode("JSON deserialization might require types that cannot be statically analyzed and might need runtime code generation. Use the overload that takes a JsonTypeInfo instead.")]
    public async Task<T?> EvaluateAsync<T>(string code, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await EvaluateCoreAsync(code, hasArguments: false, arguments: null, ResultKind.Json, CreateResultReader<T>(options), cancellationToken).ConfigureAwait(false);
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
    public async Task<JSReference> EvaluateReferenceAsync(string code, CancellationToken cancellationToken = default)
    {
        var referenceId = await EvaluateCoreAsync(code, hasArguments: false, arguments: null, ResultKind.Reference, ReadReferenceId, cancellationToken).ConfigureAwait(false);
        return new JSReference(this, referenceId);
    }

    /// <summary>Evaluates JavaScript code as the body of an async function that receives arguments, and keeps the result in the Node.js process.</summary>
    /// <inheritdoc cref="EvaluateAsync(string, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    /// <returns>A reference to the returned value. Dispose it when the value is no longer needed.</returns>
    public async Task<JSReference> EvaluateReferenceAsync(string code, IReadOnlyList<JsonNode?>? arguments, CancellationToken cancellationToken = default)
    {
        var referenceId = await EvaluateCoreAsync(code, hasArguments: true, arguments, ResultKind.Reference, ReadReferenceId, cancellationToken).ConfigureAwait(false);
        return new JSReference(this, referenceId);
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

        // Closing the socket makes the Node.js process exit
        if (_stream is not null)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }

        if (_readTask is not null)
        {
            await _readTask.ConfigureAwait(false);
        }

        if (IsProcessStarted())
        {
            // When the connection was never established, the process cannot know it must exit
            if (_stream is null || !await WaitForExitAsync(ExitTimeout, drainOutput: false).ConfigureAwait(false))
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // The process has already exited
                }

                await WaitForExitAsync(ExitTimeout, drainOutput: false).ConfigureAwait(false);
            }
        }

        _process.Dispose();
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        var nodePath = _options.NodeExecutablePath ?? ExecutableFinder.GetFullExecutablePath("node") ?? throw new NodeJsException("Cannot find the 'node' executable in the PATH. Install Node.js or set NodeJsHostOptions.NodeExecutablePath.");
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

        using var endpoint = NodeJsEndpoint.Create();

        var startInfo = _process.StartInfo;
        startInfo.FileName = nodePath;
        startInfo.WorkingDirectory = _options.WorkingDirectory ?? Environment.CurrentDirectory;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
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

        startInfo.ArgumentList.Add("--input-type=module");
        startInfo.ArgumentList.Add("--eval");
        startInfo.ArgumentList.Add(GetBootstrapScript());

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
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            throw new NodeJsException($"Cannot start '{nodePath}': {ex.Message}", ex);
        }

        _process.StandardInput.Close();
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

            _stream = await acceptTask.ConfigureAwait(false);

            var hello = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            _readTask = ReadMessagesAsync(_stream, hello);
            ProcessHelloMessage(await hello.Task.WaitAsync(startupCts.Token).ConfigureAwait(false), token);
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

    // The JsonElement owns a copy of the bytes, so it remains valid once the buffer that receives messages is reused
    internal static JsonElement ReadJsonElement(ReadOnlySpan<byte> utf8Json) => JsonElement.Parse(utf8Json, ResultDocumentOptions);

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
        if (root.ValueKind is not JsonValueKind.Object ||
            !root.TryGetProperty("type", out var type) || !type.ValueEquals("hello") ||
            !root.TryGetProperty("token", out var token) || token.ValueKind is not JsonValueKind.String ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token.GetString()!), Encoding.UTF8.GetBytes(expectedToken)))
        {
            throw new NodeJsException("The Node.js process sent an invalid handshake.");
        }

        if (root.TryGetProperty("version", out var version) && version.ValueKind is JsonValueKind.String)
        {
            NodeVersion = version.GetString()!;
        }
    }

    private Task<T> InvokeCoreAsync<T>(string module, string? exportName, IReadOnlyList<JsonNode?>? arguments, ResultKind resultKind, ResultReader<T> readResult, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(module);

        return SendRequestAsync(writer =>
        {
            writer.WriteString("type", "invoke");
            writer.WriteString("module", module);
            writer.WriteString("export", exportName);
            ArgumentWriter.Write(writer, this, arguments);
        }, resultKind, readResult, cancellationToken);
    }

    private Task<T> EvaluateCoreAsync<T>(string code, bool hasArguments, IReadOnlyList<JsonNode?>? arguments, ResultKind resultKind, ResultReader<T> readResult, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);

        return SendRequestAsync(writer =>
        {
            writer.WriteString("type", "eval");
            writer.WriteString("code", code);
            if (hasArguments)
            {
                ArgumentWriter.Write(writer, this, arguments);
            }
        }, resultKind, readResult, cancellationToken);
    }

    internal Task<T> InvokeMemberAsync<T>(JSReference target, string? memberName, IReadOnlyList<JsonNode?>? arguments, ResultKind resultKind, ResultReader<T> readResult, CancellationToken cancellationToken)
    {
        return SendRequestAsync(writer =>
        {
            ObjectDisposedException.ThrowIf(target.IsDisposed, target);
            writer.WriteString("type", "invokeReference");
            writer.WriteNumber("reference", target.Id);
            writer.WriteString("member", memberName);
            ArgumentWriter.Write(writer, this, arguments);
        }, resultKind, readResult, cancellationToken);
    }

    internal Task<T> GetReferenceValueAsync<T>(JSReference target, ResultReader<T> readResult, CancellationToken cancellationToken)
    {
        return SendRequestAsync(writer =>
        {
            ObjectDisposedException.ThrowIf(target.IsDisposed, target);
            writer.WriteString("type", "getReference");
            writer.WriteNumber("reference", target.Id);
        }, ResultKind.Json, readResult, cancellationToken);
    }

    /// <summary>Gets information about the state of the Node.js process, for tests.</summary>
    internal Task<JsonElement> GetDebugInformationAsync(CancellationToken cancellationToken)
    {
        return SendRequestAsync(writer => writer.WriteString("type", "debug"), ResultKind.Json, ReadJsonElement, cancellationToken);
    }

    /// <summary>Sends a request that is not valid JSON, for tests.</summary>
    internal Task<JsonElement> SendInvalidRequestAsync(CancellationToken cancellationToken)
    {
        return SendRequestAsync(writer =>
        {
            writer.WriteString("type", "debug");
            writer.WritePropertyName("invalid");

            // The object is never closed
            writer.WriteRawValue("{", skipInputValidation: true);
        }, ResultKind.Json, ReadJsonElement, cancellationToken);
    }

    /// <summary>Releases a value referenced by a <see cref="JSReference"/>, without waiting for the message to be sent.</summary>
    internal void ReleaseReference(long referenceId)
    {
        _ = ReleaseReferenceAsync(referenceId);
    }

    /// <summary>Releases a value referenced by a <see cref="JSReference"/>. Never throws: when the process is gone, there is nothing to release.</summary>
    internal async Task ReleaseReferenceAsync(long referenceId)
    {
        if (IsTerminated)
            return;

        PooledBufferWriter? message = SerializeMessage(writer =>
        {
            writer.WriteString("type", "release");
            writer.WriteNumber("reference", referenceId);
        });

        try
        {
            await _writeLock.WaitAsync().ConfigureAwait(false);
            var writeTask = WriteMessageAndReleaseLockAsync(message);
            message = null;
            await writeTask.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The connection is lost, so the process and its values are gone
        }
        finally
        {
            message?.Dispose();
        }
    }

    /// <summary>Serializes a message. The caller must dispose the returned buffer, or pass it to <see cref="WriteMessageAndReleaseLockAsync"/>.</summary>
    private static PooledBufferWriter SerializeMessage(Action<Utf8JsonWriter> writeMessage)
    {
        var buffer = new PooledBufferWriter();
        try
        {
            using (var writer = new Utf8JsonWriter(buffer, MessageWriterOptions))
            {
                writer.WriteStartObject();
                writeMessage(writer);
                writer.WriteEndObject();
            }

            buffer.Write("\n"u8);
            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    private async Task<T> SendRequestAsync<T>(Action<Utf8JsonWriter> writeMessage, ResultKind resultKind, ResultReader<T> readResult, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed is 1, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (_concurrencyLimit is null)
            return await SendRequestCoreAsync(writeMessage, resultKind, readResult, cancellationToken).ConfigureAwait(false);

        // The message is serialized once the call can run, so waiting calls do not keep a serialized copy of their arguments
        await _concurrencyLimit.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SendRequestCoreAsync(writeMessage, resultKind, readResult, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _concurrencyLimit.Release();
        }
    }

    private async Task<T> SendRequestCoreAsync<T>(Action<Utf8JsonWriter> writeMessage, ResultKind resultKind, ResultReader<T> readResult, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextRequestId);
        PooledBufferWriter? message = SerializeMessage(writer =>
        {
            writer.WriteNumber("id", id);
            switch (resultKind)
            {
                case ResultKind.Void:
                    writer.WriteString("returns", "void");
                    break;
                case ResultKind.Reference:
                    writer.WriteString("returns", "reference");
                    break;
            }

            writeMessage(writer);
        });

        var request = new PendingRequest<T>(readResult);
        _pendingRequests[id] = request;
        try
        {
            // Terminate sets the exception before failing pending requests, so a request added concurrently is never left pending
            ThrowIfTerminated();

            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            // Once started, the message is always written completely, as a partial message would corrupt the following ones.
            // Canceling only stops waiting for the write to complete.
            var writeTask = WriteMessageAndReleaseLockAsync(message);
            message = null;
            try
            {
                await writeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            // On Windows, disposing the named pipe cancels the pending write, which throws OperationCanceledException
            catch (Exception ex) when ((writeTask.IsFaulted || writeTask.IsCanceled) && ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
                ThrowIfTerminated();
                throw new NodeJsException("Cannot send the message to the Node.js process.", ex);
            }
            catch (OperationCanceledException)
            {
                // The write continues in the background. A failure means the connection is lost, which is reported by the read loop.
                _ = writeTask.ContinueWith(static task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                throw;
            }

            try
            {
                return await request.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (resultKind is ResultKind.Reference)
            {
                // ProcessResponse releases the value when the response arrives after this point.
                // When the response is already being processed, the value must be released here.
                if (!_pendingRequests.TryRemove(id, out _))
                {
                    _ = request.Task.ContinueWith(task =>
                    {
                        if (task.Result is long referenceId)
                        {
                            ReleaseReference(referenceId);
                        }
                    }, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }

                throw;
            }
        }
        finally
        {
            _pendingRequests.TryRemove(id, out _);
            message?.Dispose();
        }
    }

    /// <summary>Writes a message, then releases the write lock and the message buffer.</summary>
    private async Task WriteMessageAndReleaseLockAsync(PooledBufferWriter message)
    {
        try
        {
            await _stream!.WriteAsync(message.WrittenMemory, CancellationToken.None).ConfigureAwait(false);
            await _stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
            message.Dispose();
        }
    }

    private async Task ReadMessagesAsync(Stream stream, TaskCompletionSource<byte[]> hello)
    {
        Exception? error = null;

        // Messages are read as UTF-8 bytes and parsed in place, so a large result is not converted to a string, and only the parsed result is allocated.
        // Messages are separated by a line break, which JSON.stringify never writes in a value.
        var buffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
        try
        {
            var messageStart = 0;
            var dataEnd = 0;
            while (true)
            {
                if (dataEnd == buffer.Length)
                {
                    if (messageStart > 0)
                    {
                        // Move the incomplete message to the start of the buffer
                        buffer.AsSpan(messageStart, dataEnd - messageStart).CopyTo(buffer);
                        dataEnd -= messageStart;
                        messageStart = 0;
                    }
                    else
                    {
                        // The buffer grows exponentially, so a large message is copied a constant number of times on average
                        if (buffer.Length == Array.MaxLength)
                            throw new InvalidOperationException("The message is too large.");

                        var newBuffer = ArrayPool<byte>.Shared.Rent((int)Math.Min(buffer.Length * 2L, Array.MaxLength));
                        buffer.AsSpan(0, dataEnd).CopyTo(newBuffer);
                        ArrayPool<byte>.Shared.Return(buffer);
                        buffer = newBuffer;
                    }
                }

                var bytesRead = await stream.ReadAsync(buffer.AsMemory(dataEnd)).ConfigureAwait(false);
                if (bytesRead is 0)
                    break;

                // Only the new bytes are searched for line breaks, so a message split into many chunks is read in linear time
                var searchStart = dataEnd;
                dataEnd += bytesRead;
                int index;
                while ((index = buffer.AsSpan(searchStart, dataEnd - searchStart).IndexOf((byte)'\n')) >= 0)
                {
                    var messageEnd = searchStart + index;
                    var message = buffer.AsSpan(messageStart, messageEnd - messageStart);

                    // The first message is the handshake, validated by StartCoreAsync
                    if (!hello.Task.IsCompleted)
                    {
                        hello.TrySetResult(message.ToArray());
                    }
                    else
                    {
                        ProcessResponse(message);
                    }

                    messageStart = searchStart = messageEnd + 1;
                }

                if (messageStart == dataEnd)
                {
                    messageStart = dataEnd = 0;

                    // Do not keep the large buffer needed by a large message
                    if (buffer.Length > ReadBufferSize)
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                        buffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            error = ex;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (!hello.Task.IsCompleted)
        {
            if (Volatile.Read(ref _terminationException) is null)
            {
                await WaitForExitAsync(ExitTimeout, drainOutput: true).ConfigureAwait(false);
            }

            hello.TrySetException(CreateProcessExitedException("The Node.js process closed the connection before it was ready"));
        }

        if (Volatile.Read(ref _terminationException) is null)
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

    // Responses always start with the identifier of the request, followed by at most one property: {"id":1,"result":...}
    private void ProcessResponse(ReadOnlySpan<byte> message)
    {
        var reader = new Utf8JsonReader(message);
        if (!reader.Read() || reader.TokenType is not JsonTokenType.StartObject ||
            !reader.Read() || reader.TokenType is not JsonTokenType.PropertyName || !reader.ValueTextEquals("id"u8) ||
            !reader.Read())
        {
            throw new JsonException("The response does not start with the identifier of the request.");
        }

        var id = reader.GetInt64();
        if (!reader.Read())
            throw new JsonException("The response is incomplete.");

        // A void call has no result
        if (reader.TokenType is JsonTokenType.EndObject)
        {
            if (_pendingRequests.TryRemove(id, out var request))
            {
                request.SetNoResult();
            }

            return;
        }

        if (reader.TokenType is not JsonTokenType.PropertyName)
            throw new JsonException("The response is invalid.");

        // The value is the rest of the message, so it is parsed only once, directly by the reader of the request.
        // Parsing fails when the value is not exactly one JSON value, for instance when another property follows.
        var valueEnd = message.LastIndexOf((byte)'}');
        if (valueEnd < reader.BytesConsumed)
            throw new JsonException("The response is incomplete.");

        var value = message[(int)reader.BytesConsumed..valueEnd];

        // A request is only removed once the response is known to be valid, so it is never left pending when the connection is terminated
        if (reader.ValueTextEquals("result"u8) || reader.ValueTextEquals("reference"u8))
        {
            if (_pendingRequests.TryRemove(id, out var request))
            {
                request.SetResult(value);
            }
            else if (reader.ValueTextEquals("reference"u8))
            {
                // The caller stopped waiting, so the value kept for it is no longer needed
                ReleaseReference(ReadReferenceId(value));
            }
        }
        else if (reader.ValueTextEquals("error"u8))
        {
            var error = JsonElement.Parse(value);
            if (_pendingRequests.TryRemove(id, out var request))
            {
                var name = GetStringOrNull(error, "name");
                var errorMessage = GetStringOrNull(error, "message");
                var stack = GetStringOrNull(error, "stack");
                request.SetException(new NodeJsException(name is null ? errorMessage ?? "" : $"{name}: {errorMessage}", name, stack));
            }
        }
        else
        {
            throw new JsonException("The response contains an unexpected property.");
        }

        static string? GetStringOrNull(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;
        }
    }

    private void Terminate(Exception exception)
    {
        if (Interlocked.CompareExchange(ref _terminationException, exception, comparand: null) is not null)
            return;

        foreach (var (_, request) in _pendingRequests)
        {
            request.SetException(exception);
        }
    }

    private void ThrowIfTerminated()
    {
        var exception = Volatile.Read(ref _terminationException);
        ObjectDisposedException.ThrowIf(exception is ObjectDisposedException, this);

        if (exception is NodeJsException nodeJsException)
            throw new NodeJsException(nodeJsException.Message, nodeJsException.ExitCode);

        if (exception is not null)
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
        lock (_standardErrorTail)
        {
            if (_standardErrorTail.Count > 0)
            {
                result.AppendLine().Append("Standard error:");
                foreach (var line in _standardErrorTail)
                {
                    result.AppendLine().Append(line);
                }
            }
        }

        return new NodeJsException(result.ToString(), exitCode);
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

    private static string GetBootstrapScript()
    {
        using var stream = typeof(NodeJsHost).Assembly.GetManifestResourceStream("Meziantou.Framework.NodeJs.bootstrap.mjs") ?? throw new InvalidOperationException("The bootstrap script is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
