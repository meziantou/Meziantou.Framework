using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Meziantou.Framework.NodeJs.Internal;

namespace Meziantou.Framework.NodeJs;

/// <summary>Runs JavaScript code on a fixed number of Node.js processes, so CPU-bound code can run in parallel.</summary>
/// <remarks>
/// <para>Each call is sent to the host with the fewest calls in progress, including canceled calls whose JavaScript code has not completed. When <see cref="NodeJsHostOptions.MaxConcurrentCalls"/> is set, calls wait in the pool until a host can run them, so a host busy with a long call does not delay the following calls. Hosts do not share state: values stored on <c>globalThis</c> are only visible to calls running on the same host. Use <see cref="RunAsync{T}(Func{NodeJsHost, Task{T}}, CancellationToken)"/> to run several calls on the same host.</para>
/// <para>When a Node.js process exits, its host is replaced by a new one the next time a host is selected. Set <see cref="NodeJsHostOptions.UnresponsiveTimeout"/> to also replace a process whose event loop is blocked by a canceled call. With <see cref="NodeJsHostOptions.WorkerThreads"/>, each process runs the calls on several worker threads, and only the worker thread blocked by a canceled call is restarted.</para>
/// </remarks>
/// <example>
/// <code>
/// await using var pool = await NodeJsHostPool.StartAsync(Environment.ProcessorCount);
/// var results = await Task.WhenAll(documents.Select(document => pool.InvokeAsync("./render.mjs", "render", [document])));
/// </code>
/// </example>
public sealed class NodeJsHostPool : IAsyncDisposable
{
    // The pool whose RunAsync callback is running, so the calls the callback makes on the pool do not wait for the capacity it already holds
    private static readonly AsyncLocal<NodeJsHostPool?> RunningPool = new();

    private readonly NodeJsHostOptions _options;
    private readonly Slot[] _slots;
    private readonly Lock _lock = new();
    private readonly List<Task> _pendingDisposals = [];
    private readonly CancellationTokenSource _disposeCts = new();
    [SuppressMessage("Reliability", "CA2213:Disposable fields should be disposed", Justification = "AvailableWaitHandle is never used, and disposing it would leave concurrent callers waiting forever")]
    private readonly SemaphoreSlim? _capacity;
    private uint _nextSlot;
    private bool _disposed;

    private NodeJsHostPool(NodeJsHostOptions options, NodeJsHost[] hosts)
    {
        _options = options;
        _slots = Array.ConvertAll(hosts, host => new Slot(Task.FromResult(host)));
        if (options.MaxConcurrentCalls is { } maxConcurrentCalls)
        {
            var capacity = (int)Math.Min((long)maxConcurrentCalls * hosts.Length, int.MaxValue);
            _capacity = new SemaphoreSlim(capacity, capacity);
        }
    }

    /// <summary>Gets the number of Node.js processes in the pool.</summary>
    public int Size => _slots.Length;

    /// <summary>Starts a pool of Node.js processes.</summary>
    /// <param name="size">The number of Node.js processes. <see cref="Environment.ProcessorCount"/> is a good default for CPU-bound code.</param>
    /// <param name="options">The options used to start each process. They are copied, so changing them once the pool is started has no effect, including on the processes that replace the ones that exit.</param>
    /// <param name="cancellationToken">A token to cancel the startup.</param>
    /// <exception cref="NodeJsException">A Node.js process fails to start.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/>, <see cref="NodeJsHostOptions.MaxConcurrentCalls"/>, <see cref="NodeJsHostOptions.StartupTimeout"/>, <see cref="NodeJsHostOptions.UnresponsiveTimeout"/>, or <see cref="NodeJsHostOptions.WorkerThreads"/> is zero or negative.</exception>
    public static async Task<NodeJsHostPool> StartAsync(int size, NodeJsHostOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

        options = (options ?? NodeJsHostOptions.Default).Clone();
        NodeJsHost.ValidateOptions(options);
        var tasks = new Task<NodeJsHost>[size];
        for (var i = 0; i < size; i++)
        {
            tasks[i] = NodeJsHost.StartAsync(options, cancellationToken);
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            foreach (var task in tasks)
            {
                if (task.IsCompletedSuccessfully)
                {
                    await task.Result.DisposeAsync().ConfigureAwait(false);
                }
            }

            throw;
        }

        return new NodeJsHostPool(options, Array.ConvertAll(tasks, task => task.Result));
    }

    /// <summary>Runs a function with one host of the pool. Use it to run several calls on the same Node.js process.</summary>
    /// <param name="action">The function to run. The host must not be used after the returned task completes.</param>
    /// <param name="cancellationToken">A token to stop waiting for a host to be available.</param>
    /// <remarks>Calls made on the pool by the function, instead of on the host, do not wait for <see cref="NodeJsHostOptions.MaxConcurrentCalls"/>: the function already counts as a call, and waiting would never end when all the calls of the pool are such functions.</remarks>
    public async Task<T> RunAsync<T>(Func<NodeJsHost, Task<T>> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        // Calls wait here instead of being queued on a host, so they run on the first host that becomes available
        var capacity = RunningPool.Value == this ? null : _capacity;
        if (capacity is not null)
        {
            await capacity.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var (slot, host) = await AcquireAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // The value flows to the function, and is not visible to the caller of this async method
                RunningPool.Value = this;
                return await action(host).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref slot.PendingCalls);
            }
        }
        finally
        {
            capacity?.Release();
        }
    }

    /// <summary>Runs a function with one host of the pool. Use it to run several calls on the same Node.js process.</summary>
    /// <param name="action">The function to run. The host must not be used after the returned task completes.</param>
    /// <param name="cancellationToken">A token to stop waiting for a host to be available.</param>
    public Task RunAsync(Func<NodeJsHost, Task> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        return RunAsync<object?>(async host =>
        {
            await action(host).ConfigureAwait(false);
            return null;
        }, cancellationToken);
    }

    /// <inheritdoc cref="NodeJsHost.InvokeAsync(string, string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    /// <remarks>When the arguments contain a <see cref="JSReference"/>, the call runs on the host that keeps the referenced value.</remarks>
    public Task<JsonElement> InvokeAsync(string module, string? exportName, IReadOnlyList<JsonNode?>? arguments = null, CancellationToken cancellationToken = default)
    {
        return RunAsync(ArgumentWriter.FindReferenceHost(arguments), host => host.InvokeAsync(module, exportName, arguments, cancellationToken), cancellationToken);
    }

    /// <inheritdoc cref="NodeJsHost.InvokeAsync{T}(string, string?, IReadOnlyList{JsonNode?}?, JsonTypeInfo{T}, CancellationToken)"/>
    /// <remarks>When the arguments contain a <see cref="JSReference"/>, the call runs on the host that keeps the referenced value.</remarks>
    public Task<T?> InvokeAsync<T>(string module, string? exportName, IReadOnlyList<JsonNode?>? arguments, JsonTypeInfo<T> resultTypeInfo, CancellationToken cancellationToken = default)
    {
        return RunAsync(ArgumentWriter.FindReferenceHost(arguments), host => host.InvokeAsync(module, exportName, arguments, resultTypeInfo, cancellationToken), cancellationToken);
    }

    /// <inheritdoc cref="NodeJsHost.InvokeAsync{T}(string, string?, object?[], JsonSerializerOptions?, CancellationToken)"/>
    /// <remarks>When the arguments contain a <see cref="JSReference"/>, the call runs on the host that keeps the referenced value.</remarks>
    [RequiresUnreferencedCode(NodeJsHost.ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(NodeJsHost.ReflectionDynamicCodeMessage)]
    public Task<T?> InvokeAsync<T>(string module, string? exportName, object?[]? arguments = null, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        return RunAsync(ArgumentWriter.FindReferenceHost(arguments), host => host.InvokeAsync<T>(module, exportName, arguments, options, cancellationToken), cancellationToken);
    }

    /// <inheritdoc cref="NodeJsHost.InvokeAsync{T}(string, string?, CancellationToken)"/>
    [RequiresUnreferencedCode(NodeJsHost.ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(NodeJsHost.ReflectionDynamicCodeMessage)]
    public Task<T?> InvokeAsync<T>(string module, string? exportName, CancellationToken cancellationToken)
    {
        return RunAsync(host => host.InvokeAsync<T>(module, exportName, cancellationToken), cancellationToken);
    }

    /// <inheritdoc cref="NodeJsHost.InvokeVoidAsync(string, string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    /// <remarks>When the arguments contain a <see cref="JSReference"/>, the call runs on the host that keeps the referenced value.</remarks>
    public Task InvokeVoidAsync(string module, string? exportName, IReadOnlyList<JsonNode?>? arguments = null, CancellationToken cancellationToken = default)
    {
        return RunAsync(ArgumentWriter.FindReferenceHost(arguments), host => host.InvokeVoidAsync(module, exportName, arguments, cancellationToken), cancellationToken);
    }

    /// <inheritdoc cref="NodeJsHost.InvokeVoidAsync(string, string?, object?[], JsonSerializerOptions?, CancellationToken)"/>
    /// <remarks>When the arguments contain a <see cref="JSReference"/>, the call runs on the host that keeps the referenced value.</remarks>
    [RequiresUnreferencedCode(NodeJsHost.ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(NodeJsHost.ReflectionDynamicCodeMessage)]
    public Task InvokeVoidAsync(string module, string? exportName, object?[]? arguments, JsonSerializerOptions? options, CancellationToken cancellationToken = default)
    {
        return RunAsync(ArgumentWriter.FindReferenceHost(arguments), host => host.InvokeVoidAsync(module, exportName, arguments, options, cancellationToken), cancellationToken);
    }

    /// <inheritdoc cref="NodeJsHost.InvokeReferenceAsync(string, string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    /// <remarks>The reference is bound to the host that ran the call. Calls of the pool whose arguments contain it run on that host.</remarks>
    public Task<JSReference> InvokeReferenceAsync(string module, string? exportName, IReadOnlyList<JsonNode?>? arguments = null, CancellationToken cancellationToken = default)
    {
        return RunAsync(ArgumentWriter.FindReferenceHost(arguments), host => host.InvokeReferenceAsync(module, exportName, arguments, cancellationToken), cancellationToken);
    }

    /// <inheritdoc cref="NodeJsHost.InvokeReferenceAsync(string, string?, object?[], JsonSerializerOptions?, CancellationToken)"/>
    /// <remarks>The reference is bound to the host that ran the call. Calls of the pool whose arguments contain it run on that host.</remarks>
    [RequiresUnreferencedCode(NodeJsHost.ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(NodeJsHost.ReflectionDynamicCodeMessage)]
    public Task<JSReference> InvokeReferenceAsync(string module, string? exportName, object?[]? arguments, JsonSerializerOptions? options, CancellationToken cancellationToken = default)
    {
        return RunAsync(ArgumentWriter.FindReferenceHost(arguments), host => host.InvokeReferenceAsync(module, exportName, arguments, options, cancellationToken), cancellationToken);
    }

    /// <inheritdoc cref="NodeJsHost.CreateInstanceAsync(string, string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    /// <remarks>The reference is bound to the host that ran the call. When the arguments contain a <see cref="JSReference"/>, the call runs on the host that keeps the referenced value.</remarks>
    public Task<JSReference> CreateInstanceAsync(string module, string? exportName, IReadOnlyList<JsonNode?>? arguments = null, CancellationToken cancellationToken = default)
    {
        return RunAsync(ArgumentWriter.FindReferenceHost(arguments), host => host.CreateInstanceAsync(module, exportName, arguments, cancellationToken), cancellationToken);
    }

    /// <inheritdoc cref="NodeJsHost.CreateInstanceAsync(string, string?, object?[], JsonSerializerOptions?, CancellationToken)"/>
    /// <remarks>The reference is bound to the host that ran the call. When the arguments contain a <see cref="JSReference"/>, the call runs on the host that keeps the referenced value.</remarks>
    [RequiresUnreferencedCode(NodeJsHost.ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(NodeJsHost.ReflectionDynamicCodeMessage)]
    public Task<JSReference> CreateInstanceAsync(string module, string? exportName, object?[]? arguments, JsonSerializerOptions? options, CancellationToken cancellationToken = default)
    {
        return RunAsync(ArgumentWriter.FindReferenceHost(arguments), host => host.CreateInstanceAsync(module, exportName, arguments, options, cancellationToken), cancellationToken);
    }

    /// <inheritdoc cref="NodeJsHost.EvaluateAsync(string, CancellationToken)"/>
    public Task<JsonElement> EvaluateAsync(string code, CancellationToken cancellationToken = default)
    {
        return RunAsync(host => host.EvaluateAsync(code, cancellationToken), cancellationToken);
    }

    /// <inheritdoc cref="NodeJsHost.EvaluateAsync(string, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    /// <remarks>When the arguments contain a <see cref="JSReference"/>, the call runs on the host that keeps the referenced value.</remarks>
    public Task<JsonElement> EvaluateAsync(string code, IReadOnlyList<JsonNode?>? arguments, CancellationToken cancellationToken = default)
    {
        return RunAsync(ArgumentWriter.FindReferenceHost(arguments), host => host.EvaluateAsync(code, arguments, cancellationToken), cancellationToken);
    }

    /// <inheritdoc cref="NodeJsHost.EvaluateAsync{T}(string, IReadOnlyList{JsonNode?}?, JsonTypeInfo{T}, CancellationToken)"/>
    /// <remarks>When the arguments contain a <see cref="JSReference"/>, the call runs on the host that keeps the referenced value.</remarks>
    public Task<T?> EvaluateAsync<T>(string code, IReadOnlyList<JsonNode?>? arguments, JsonTypeInfo<T> resultTypeInfo, CancellationToken cancellationToken = default)
    {
        return RunAsync(ArgumentWriter.FindReferenceHost(arguments), host => host.EvaluateAsync(code, arguments, resultTypeInfo, cancellationToken), cancellationToken);
    }

    /// <inheritdoc cref="NodeJsHost.EvaluateAsync{T}(string, object?[], JsonSerializerOptions?, CancellationToken)"/>
    /// <remarks>When the arguments contain a <see cref="JSReference"/>, the call runs on the host that keeps the referenced value.</remarks>
    [RequiresUnreferencedCode(NodeJsHost.ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(NodeJsHost.ReflectionDynamicCodeMessage)]
    public Task<T?> EvaluateAsync<T>(string code, object?[]? arguments, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        return RunAsync(ArgumentWriter.FindReferenceHost(arguments), host => host.EvaluateAsync<T>(code, arguments, options, cancellationToken), cancellationToken);
    }

    /// <inheritdoc cref="NodeJsHost.EvaluateVoidAsync(string, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    /// <remarks>When the arguments contain a <see cref="JSReference"/>, the call runs on the host that keeps the referenced value.</remarks>
    public Task EvaluateVoidAsync(string code, IReadOnlyList<JsonNode?>? arguments, CancellationToken cancellationToken = default)
    {
        return RunAsync(ArgumentWriter.FindReferenceHost(arguments), host => host.EvaluateVoidAsync(code, arguments, cancellationToken), cancellationToken);
    }

    /// <inheritdoc cref="NodeJsHost.EvaluateVoidAsync(string, object?[], JsonSerializerOptions?, CancellationToken)"/>
    /// <remarks>When the arguments contain a <see cref="JSReference"/>, the call runs on the host that keeps the referenced value.</remarks>
    [RequiresUnreferencedCode(NodeJsHost.ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(NodeJsHost.ReflectionDynamicCodeMessage)]
    public Task EvaluateVoidAsync(string code, object?[]? arguments, JsonSerializerOptions? options, CancellationToken cancellationToken = default)
    {
        return RunAsync(ArgumentWriter.FindReferenceHost(arguments), host => host.EvaluateVoidAsync(code, arguments, options, cancellationToken), cancellationToken);
    }

    /// <inheritdoc cref="NodeJsHost.EvaluateReferenceAsync(string, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    /// <remarks>The reference is bound to the host that ran the call. When the arguments contain a <see cref="JSReference"/>, the call runs on the host that keeps the referenced value.</remarks>
    public Task<JSReference> EvaluateReferenceAsync(string code, IReadOnlyList<JsonNode?>? arguments, CancellationToken cancellationToken = default)
    {
        return RunAsync(ArgumentWriter.FindReferenceHost(arguments), host => host.EvaluateReferenceAsync(code, arguments, cancellationToken), cancellationToken);
    }

    /// <inheritdoc cref="NodeJsHost.EvaluateReferenceAsync(string, object?[], JsonSerializerOptions?, CancellationToken)"/>
    /// <remarks>The reference is bound to the host that ran the call. When the arguments contain a <see cref="JSReference"/>, the call runs on the host that keeps the referenced value.</remarks>
    [RequiresUnreferencedCode(NodeJsHost.ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(NodeJsHost.ReflectionDynamicCodeMessage)]
    public Task<JSReference> EvaluateReferenceAsync(string code, object?[]? arguments, JsonSerializerOptions? options, CancellationToken cancellationToken = default)
    {
        return RunAsync(ArgumentWriter.FindReferenceHost(arguments), host => host.EvaluateReferenceAsync(code, arguments, options, cancellationToken), cancellationToken);
    }

    /// <inheritdoc cref="NodeJsHost.EvaluateAsync{T}(string, JsonTypeInfo{T}, CancellationToken)"/>
    public Task<T?> EvaluateAsync<T>(string code, JsonTypeInfo<T> resultTypeInfo, CancellationToken cancellationToken = default)
    {
        return RunAsync(host => host.EvaluateAsync(code, resultTypeInfo, cancellationToken), cancellationToken);
    }

    /// <inheritdoc cref="NodeJsHost.EvaluateAsync{T}(string, JsonSerializerOptions?, CancellationToken)"/>
    [RequiresUnreferencedCode("JSON deserialization might require types that cannot be statically analyzed. Use the overload that takes a JsonTypeInfo instead.")]
    [RequiresDynamicCode("JSON deserialization might require types that cannot be statically analyzed and might need runtime code generation. Use the overload that takes a JsonTypeInfo instead.")]
    public Task<T?> EvaluateAsync<T>(string code, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        return RunAsync(host => host.EvaluateAsync<T>(code, options, cancellationToken), cancellationToken);
    }

    /// <inheritdoc cref="NodeJsHost.EvaluateAsync{T}(string, CancellationToken)"/>
    [RequiresUnreferencedCode("JSON deserialization might require types that cannot be statically analyzed. Use the overload that takes a JsonTypeInfo instead.")]
    [RequiresDynamicCode("JSON deserialization might require types that cannot be statically analyzed and might need runtime code generation. Use the overload that takes a JsonTypeInfo instead.")]
    public Task<T?> EvaluateAsync<T>(string code, CancellationToken cancellationToken)
    {
        return RunAsync(host => host.EvaluateAsync<T>(code, cancellationToken), cancellationToken);
    }

    /// <inheritdoc cref="NodeJsHost.EvaluateVoidAsync(string, CancellationToken)"/>
    public Task EvaluateVoidAsync(string code, CancellationToken cancellationToken = default)
    {
        return RunAsync(host => host.EvaluateVoidAsync(code, cancellationToken), cancellationToken);
    }

    /// <inheritdoc cref="NodeJsHost.EvaluateReferenceAsync(string, CancellationToken)"/>
    /// <remarks>The reference is bound to the host that ran the call. Calls of the pool whose arguments contain it run on that host.</remarks>
    public Task<JSReference> EvaluateReferenceAsync(string code, CancellationToken cancellationToken = default)
    {
        return RunAsync(host => host.EvaluateReferenceAsync(code, cancellationToken), cancellationToken);
    }

    /// <summary>Stops all the Node.js processes. Pending calls fail with <see cref="ObjectDisposedException"/>.</summary>
    public async ValueTask DisposeAsync()
    {
        Task[] tasks;
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            tasks = [.. _pendingDisposals, .. _slots.Select(slot => DisposeHostAsync(slot.Host))];
        }

        await _disposeCts.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(tasks).ConfigureAwait(false);
        _disposeCts.Dispose();
    }

    private Task<T> RunAsync<T>(NodeJsHost? host, Func<NodeJsHost, Task<T>> action, CancellationToken cancellationToken)
    {
        return host is null ? RunAsync(action, cancellationToken) : RunOnHostAsync(host, action);
    }

    private async Task RunAsync(NodeJsHost? host, Func<NodeJsHost, Task> action, CancellationToken cancellationToken)
    {
        await RunAsync<object?>(host, async host =>
        {
            await action(host).ConfigureAwait(false);
            return null;
        }, cancellationToken).ConfigureAwait(false);
    }

    // Runs a call that uses a reference on the host that keeps the referenced value
    private async Task<T> RunOnHostAsync<T>(NodeJsHost host, Func<NodeJsHost, Task<T>> action)
    {
        Slot? hostSlot = null;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (var slot in _slots)
            {
                if (slot.Host.IsCompletedSuccessfully && slot.Host.Result == host)
                {
                    Interlocked.Increment(ref slot.PendingCalls);
                    hostSlot = slot;
                    break;
                }
            }
        }

        // When the host is no longer in the pool (e.g. its process exited and it was replaced), the call fails with the error of the host
        try
        {
            return await action(host).ConfigureAwait(false);
        }
        finally
        {
            if (hostSlot is not null)
            {
                Interlocked.Decrement(ref hostSlot.PendingCalls);
            }
        }
    }

    private async Task<(Slot Slot, NodeJsHost Host)> AcquireAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task<NodeJsHost>? startingHost = null;
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                // Start from a different slot each time, so hosts with the same number of pending calls are used in turn
                var offset = (int)(_nextSlot++ % (uint)_slots.Length);
                Slot? bestSlot = null;
                NodeJsHost? bestHost = null;
                var bestLoad = 0;
                for (var i = 0; i < _slots.Length; i++)
                {
                    var slot = _slots[(offset + i) % _slots.Length];
                    var hostTask = slot.Host;
                    if (!hostTask.IsCompleted)
                    {
                        startingHost ??= hostTask;
                        continue;
                    }

                    if (!hostTask.IsCompletedSuccessfully || hostTask.Result.IsTerminated)
                    {
                        // The process exited or failed to start, so replace it
                        _pendingDisposals.RemoveAll(task => task.IsCompleted);
                        _pendingDisposals.Add(DisposeHostAsync(hostTask));
                        slot.Host = NodeJsHost.StartAsync(_options, _disposeCts.Token);
                        startingHost ??= slot.Host;
                        continue;
                    }

                    // A canceled call may still be running, e.g. blocking the event loop, so it counts until the process responds
                    var load = Volatile.Read(ref slot.PendingCalls) + hostTask.Result.AbandonedCalls;
                    if (bestSlot is null || load < bestLoad)
                    {
                        bestSlot = slot;
                        bestHost = hostTask.Result;
                        bestLoad = load;
                    }
                }

                if (bestSlot is not null)
                {
                    Interlocked.Increment(ref bestSlot.PendingCalls);
                    return (bestSlot, bestHost!);
                }
            }

            // Every host is starting. Throws if the host fails to start.
            await startingHost!.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task DisposeHostAsync(Task<NodeJsHost> hostTask)
    {
        await ((Task)hostTask).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (hostTask.IsCompletedSuccessfully)
        {
            await hostTask.Result.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class Slot(Task<NodeJsHost> host)
    {
        public Task<NodeJsHost> Host = host;
        public int PendingCalls;
    }
}
