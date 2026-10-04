namespace Meziantou.Framework.NodeJs;

/// <summary>Options used to start a <see cref="NodeJsHost"/>.</summary>
/// <remarks>The options are copied when a <see cref="NodeJsHost"/> or a <see cref="NodeJsHostPool"/> is started, so changing them afterwards has no effect on it.</remarks>
public sealed class NodeJsHostOptions
{
    /// <summary>Gets the options used when none are provided. It must not be modified.</summary>
    internal static NodeJsHostOptions Default { get; } = new();

    /// <summary>Gets or sets the path of the <c>node</c> executable. When <see langword="null"/>, <c>node</c> is searched in the <c>PATH</c>.</summary>
    public string? NodeExecutablePath { get; set; }

    /// <summary>Gets or sets the working directory of the Node.js process. Module specifiers (relative paths and npm packages) are resolved from this directory. When <see langword="null"/>, the current directory is used.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Gets the additional arguments passed to <c>node</c>, such as <c>--max-old-space-size=4096</c>.</summary>
    public IList<string> NodeArguments { get; } = [];

    /// <summary>Gets the environment variables set for the Node.js process. A <see langword="null"/> value removes the variable.</summary>
    public IDictionary<string, string?> EnvironmentVariables { get; } = new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>Gets or sets the maximum time to wait for the Node.js process to start, or <see cref="Timeout.InfiniteTimeSpan"/> to wait indefinitely.</summary>
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the maximum number of calls that run at the same time in a Node.js process. Other calls wait until a call completes. When <see langword="null"/>, the number of calls is not limited.</summary>
    /// <remarks>
    /// <para>Limiting the number of calls bounds the memory and the work queued in the Node.js process. With <see cref="NodeJsHostPool"/>, calls wait in the pool and run on the first process that becomes available, which balances calls of different durations; <c>1</c> is a good value for CPU-bound code.</para>
    /// <para>With <see cref="WorkerThreads"/>, the limit applies to the calls of all the worker threads of the process, and each call that can run is sent to the worker thread with the fewest calls in progress. Use the number of worker threads for CPU-bound code, so a call runs on a worker thread that becomes available instead of waiting for a long call.</para>
    /// <para>A canceled call no longer counts, even if its JavaScript code is still running. A call made using <see cref="NodeJsHostPool.RunAsync{T}(Func{NodeJsHost, Task{T}}, CancellationToken)"/> counts as one call of the pool, and each call it makes on the host counts as one call of the host.</para>
    /// </remarks>
    public int? MaxConcurrentCalls { get; set; }

    /// <summary>Gets or sets the maximum time a canceled call can block the event loop of the Node.js process (e.g. with an infinite loop, or with promises that never let the event loop run other callbacks) before the process is killed. When <see langword="null"/>, the process is never killed.</summary>
    /// <remarks>
    /// <para>Canceling a call does not stop its JavaScript code, and synchronous code blocks all the other calls of the process. Killing the process is the only way to stop it. The calls in progress then fail with a <see cref="NodeJsException"/> whose <see cref="NodeJsException.ExitCode"/> is <see langword="null"/>, and <see cref="NodeJsHostPool"/> replaces the process the next time a host is selected.</para>
    /// <para>The process is killed only when the code blocking its event loop belongs to a canceled call, including code run by the promises, timeouts, and I/O operations it started. Code run by long-lived objects (intervals, sockets, servers, event listeners on objects the call did not create...) and the code that loads a module imported by <see cref="NodeJsHost.InvokeAsync(string, string?, IReadOnlyList{System.Text.Json.Nodes.JsonNode?}?, CancellationToken)"/> or <c>require</c> belong to no call, as they can run code for several calls. Calls that are not canceled are never stopped, even when they keep the event loop busy for longer. While canceled calls are running, the process is checked at least every second, so a canceled call that blocks the event loop after awaiting is also stopped. Choose a value larger than the longest synchronous step of a canceled call that must not be stopped.</para>
    /// <para>To know which call runs, the process tracks the asynchronous context of each call using <c>node:async_hooks</c>, which slows down code that awaits many promises, and a watchdog thread answers the host while the event loop is blocked.</para>
    /// <para>With <see cref="WorkerThreads"/>, only the worker thread blocked by a canceled call is terminated and restarted: the calls in progress on this worker thread fail, the other worker threads are not affected, and the process keeps running. The main thread of the process, which does not run calls, answers the host.</para>
    /// </remarks>
    public TimeSpan? UnresponsiveTimeout { get; set; }

    /// <summary>Gets or sets the number of worker threads (<c>node:worker_threads</c>) that run the calls, so CPU-bound calls run in parallel in a single Node.js process. When <see langword="null"/>, the calls run on the main thread of the process.</summary>
    /// <remarks>
    /// <para>Each call runs on one worker thread: the one with the fewest calls in progress, or, when the call uses a <see cref="JSReference"/>, the one that keeps the referenced value. Worker threads do not share state: each one has its own <c>globalThis</c>, loads its own modules, and keeps its own referenced values, so consecutive calls may run on different worker threads and not see the same values. A call cannot use references kept by different worker threads.</para>
    /// <para>A worker thread that exits (e.g. calling <c>process.exit()</c>) is restarted: the calls in progress on it fail with a <see cref="NodeJsException"/> whose <see cref="NodeJsException.ExitCode"/> is <see langword="null"/>, and its referenced values are lost. Some APIs behave differently in a worker thread: <c>process.exit()</c> only stops the worker thread, <c>process.chdir()</c> is not supported, <c>process.env</c> is a copy, and native addons must support worker threads.</para>
    /// </remarks>
    public int? WorkerThreads { get; set; }

    /// <summary>Gets or sets a callback invoked for each line written by the Node.js process to its standard output (e.g. <c>console.log</c>). When <see langword="null"/>, the output is discarded. Exceptions thrown by the callback are ignored.</summary>
    public Action<string>? StandardOutputReceived { get; set; }

    /// <summary>Gets or sets a callback invoked for each line written by the Node.js process to its standard error (e.g. <c>console.error</c>). When <see langword="null"/>, the output is discarded, except the last lines, which are included in the message of the exception thrown when the process exits. Exceptions thrown by the callback are ignored.</summary>
    public Action<string>? StandardErrorReceived { get; set; }

    /// <summary>Creates a copy of the options, so changes made once a host is started do not affect it.</summary>
    internal NodeJsHostOptions Clone()
    {
        var clone = new NodeJsHostOptions
        {
            NodeExecutablePath = NodeExecutablePath,
            WorkingDirectory = WorkingDirectory,
            StartupTimeout = StartupTimeout,
            MaxConcurrentCalls = MaxConcurrentCalls,
            UnresponsiveTimeout = UnresponsiveTimeout,
            WorkerThreads = WorkerThreads,
            StandardOutputReceived = StandardOutputReceived,
            StandardErrorReceived = StandardErrorReceived,
        };

        foreach (var argument in NodeArguments)
        {
            clone.NodeArguments.Add(argument);
        }

        foreach (var (name, value) in EnvironmentVariables)
        {
            clone.EnvironmentVariables[name] = value;
        }

        return clone;
    }
}
