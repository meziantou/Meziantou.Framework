using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    private readonly NodeJsHostOptions _options;
    private readonly Process _process;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pendingRequests = new();
    [SuppressMessage("Reliability", "CA2213:Disposable fields should be disposed", Justification = "AvailableWaitHandle is never used, and disposing it would leave concurrent callers waiting forever")]
    private readonly SemaphoreSlim _writeLock = new(1, 1);
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
    public static async Task<NodeJsHost> StartAsync(NodeJsHostOptions? options = null, CancellationToken cancellationToken = default)
    {
        var host = new NodeJsHost(options ?? NodeJsHostOptions.Default);
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
        return InvokeCoreAsync(module, exportName, arguments, ResultKind.Json, cancellationToken);
    }

    /// <summary>Imports a module, calls one of its exports, and deserializes the result.</summary>
    /// <inheritdoc cref="InvokeAsync(string, string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    public async Task<T?> InvokeAsync<T>(string module, string? exportName, IReadOnlyList<JsonNode?>? arguments, JsonTypeInfo<T> resultTypeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resultTypeInfo);

        var result = await InvokeAsync(module, exportName, arguments, cancellationToken).ConfigureAwait(false);
        return result.Deserialize(resultTypeInfo);
    }

    /// <summary>Imports a module, calls one of its exports, and deserializes the result. Arguments are serialized using reflection.</summary>
    /// <inheritdoc cref="InvokeAsync(string, string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    [RequiresUnreferencedCode(ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionDynamicCodeMessage)]
    public async Task<T?> InvokeAsync<T>(string module, string? exportName, object?[]? arguments = null, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        var result = await InvokeAsync(module, exportName, ArgumentWriter.SerializeArguments(arguments, options), cancellationToken).ConfigureAwait(false);
        return result.Deserialize<T>(options);
    }

    /// <summary>Imports a module and calls one of its exports, ignoring its result. The result does not need to be serializable.</summary>
    /// <inheritdoc cref="InvokeAsync(string, string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    public Task InvokeVoidAsync(string module, string? exportName, IReadOnlyList<JsonNode?>? arguments = null, CancellationToken cancellationToken = default)
    {
        return InvokeCoreAsync(module, exportName, arguments, ResultKind.Void, cancellationToken);
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
        var result = await InvokeCoreAsync(module, exportName, arguments, ResultKind.Reference, cancellationToken).ConfigureAwait(false);
        return new JSReference(this, result.GetInt64());
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
        return EvaluateCoreAsync(code, ResultKind.Json, cancellationToken);
    }

    /// <summary>Evaluates JavaScript code as the body of an async function and deserializes the result.</summary>
    /// <inheritdoc cref="EvaluateAsync(string, CancellationToken)"/>
    public async Task<T?> EvaluateAsync<T>(string code, JsonTypeInfo<T> resultTypeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resultTypeInfo);

        var result = await EvaluateAsync(code, cancellationToken).ConfigureAwait(false);
        return result.Deserialize(resultTypeInfo);
    }

    /// <summary>Evaluates JavaScript code as the body of an async function and deserializes the result using reflection.</summary>
    /// <inheritdoc cref="EvaluateAsync(string, CancellationToken)"/>
    [RequiresUnreferencedCode("JSON deserialization might require types that cannot be statically analyzed. Use the overload that takes a JsonTypeInfo instead.")]
    [RequiresDynamicCode("JSON deserialization might require types that cannot be statically analyzed and might need runtime code generation. Use the overload that takes a JsonTypeInfo instead.")]
    public async Task<T?> EvaluateAsync<T>(string code, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        var result = await EvaluateAsync(code, cancellationToken).ConfigureAwait(false);
        return result.Deserialize<T>(options);
    }

    /// <summary>Evaluates JavaScript code as the body of an async function, ignoring its result. The result does not need to be serializable.</summary>
    /// <inheritdoc cref="EvaluateAsync(string, CancellationToken)"/>
    public Task EvaluateVoidAsync(string code, CancellationToken cancellationToken = default)
    {
        return EvaluateCoreAsync(code, ResultKind.Void, cancellationToken);
    }

    /// <summary>Evaluates JavaScript code as the body of an async function, and keeps the result in the Node.js process.</summary>
    /// <inheritdoc cref="EvaluateAsync(string, CancellationToken)"/>
    /// <returns>A reference to the returned value. Dispose it when the value is no longer needed.</returns>
    public async Task<JSReference> EvaluateReferenceAsync(string code, CancellationToken cancellationToken = default)
    {
        var result = await EvaluateCoreAsync(code, ResultKind.Reference, cancellationToken).ConfigureAwait(false);
        return new JSReference(this, result.GetInt64());
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
                _options.StandardOutputReceived?.Invoke(e.Data);
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

                _options.StandardErrorReceived?.Invoke(e.Data);
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

            var hello = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
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

    private void ProcessHelloMessage(string line, string expectedToken)
    {
        using var document = JsonDocument.Parse(line);
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

    private Task<JsonElement> InvokeCoreAsync(string module, string? exportName, IReadOnlyList<JsonNode?>? arguments, ResultKind resultKind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(module);

        return SendRequestAsync(writer =>
        {
            writer.WriteString("type", "invoke");
            writer.WriteString("module", module);
            writer.WriteString("export", exportName);
            ArgumentWriter.Write(writer, this, arguments);
        }, resultKind, cancellationToken);
    }

    private Task<JsonElement> EvaluateCoreAsync(string code, ResultKind resultKind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);

        return SendRequestAsync(writer =>
        {
            writer.WriteString("type", "eval");
            writer.WriteString("code", code);
        }, resultKind, cancellationToken);
    }

    internal Task<JsonElement> InvokeMemberAsync(JSReference target, string? memberName, IReadOnlyList<JsonNode?>? arguments, ResultKind resultKind, CancellationToken cancellationToken)
    {
        return SendRequestAsync(writer =>
        {
            ObjectDisposedException.ThrowIf(target.IsDisposed, target);
            writer.WriteString("type", "invokeReference");
            writer.WriteNumber("reference", target.Id);
            writer.WriteString("member", memberName);
            ArgumentWriter.Write(writer, this, arguments);
        }, resultKind, cancellationToken);
    }

    internal Task<JsonElement> GetReferenceValueAsync(JSReference target, CancellationToken cancellationToken)
    {
        return SendRequestAsync(writer =>
        {
            ObjectDisposedException.ThrowIf(target.IsDisposed, target);
            writer.WriteString("type", "getReference");
            writer.WriteNumber("reference", target.Id);
        }, ResultKind.Json, cancellationToken);
    }

    /// <summary>Gets information about the state of the Node.js process, for tests.</summary>
    internal Task<JsonElement> GetDebugInformationAsync(CancellationToken cancellationToken)
    {
        return SendRequestAsync(writer => writer.WriteString("type", "debug"), ResultKind.Json, cancellationToken);
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

        var message = SerializeMessage(writer =>
        {
            writer.WriteString("type", "release");
            writer.WriteNumber("reference", referenceId);
        });

        try
        {
            await _writeLock.WaitAsync().ConfigureAwait(false);
            await WriteMessageAndReleaseLockAsync(message).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The connection is lost, so the process and its values are gone
        }
    }

    private static ReadOnlyMemory<byte> SerializeMessage(Action<Utf8JsonWriter> writeMessage)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writeMessage(writer);
            writer.WriteEndObject();
        }

        buffer.Write("\n"u8);
        return buffer.WrittenMemory;
    }

    private async Task<JsonElement> SendRequestAsync(Action<Utf8JsonWriter> writeMessage, ResultKind resultKind, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed is 1, this);
        cancellationToken.ThrowIfCancellationRequested();

        var id = Interlocked.Increment(ref _nextRequestId);
        var message = SerializeMessage(writer =>
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

        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[id] = tcs;
        try
        {
            // Terminate sets the exception before failing pending requests, so a request added concurrently is never left pending
            ThrowIfTerminated();

            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            // Once started, the message is always written completely, as a partial message would corrupt the following ones.
            // Canceling only stops waiting for the write to complete.
            var writeTask = WriteMessageAndReleaseLockAsync(message);
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
                return await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (resultKind is ResultKind.Reference)
            {
                // ProcessResponse releases the value when the response arrives after this point.
                // When the response is already being processed, the value must be released here.
                if (!_pendingRequests.TryRemove(id, out _))
                {
                    _ = tcs.Task.ContinueWith(task => ReleaseReference(task.Result.GetInt64()), CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }

                throw;
            }
        }
        finally
        {
            _pendingRequests.TryRemove(id, out _);
        }
    }

    private async Task WriteMessageAndReleaseLockAsync(ReadOnlyMemory<byte> message)
    {
        try
        {
            await _stream!.WriteAsync(message, CancellationToken.None).ConfigureAwait(false);
            await _stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadMessagesAsync(Stream stream, TaskCompletionSource<string> hello)
    {
        Exception? error = null;
        try
        {
            using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
            string? line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                // The first message is the handshake, validated by StartCoreAsync
                if (!hello.Task.IsCompleted)
                {
                    hello.TrySetResult(line);
                    continue;
                }

                ProcessResponse(line);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            error = ex;
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

    private void ProcessResponse(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        var id = root.GetProperty("id").GetInt64();
        if (!_pendingRequests.TryRemove(id, out var tcs))
        {
            // The caller stopped waiting, so the value kept for it is no longer needed
            if (root.TryGetProperty("reference", out var unusedReference))
            {
                ReleaseReference(unusedReference.GetInt64());
            }

            return;
        }

        if (root.TryGetProperty("error", out var error))
        {
            var name = GetStringOrNull(error, "name");
            var message = GetStringOrNull(error, "message");
            var stack = GetStringOrNull(error, "stack");
            tcs.TrySetException(new NodeJsException(name is null ? message ?? "" : $"{name}: {message}", name, stack));
        }
        else if (root.TryGetProperty("reference", out var reference))
        {
            tcs.TrySetResult(reference.Clone());
        }
        else if (root.TryGetProperty("result", out var result))
        {
            tcs.TrySetResult(result.Clone());
        }
        else
        {
            // The result of a void call
            tcs.TrySetResult(default);
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

        foreach (var (_, tcs) in _pendingRequests)
        {
            tcs.TrySetException(exception);
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
