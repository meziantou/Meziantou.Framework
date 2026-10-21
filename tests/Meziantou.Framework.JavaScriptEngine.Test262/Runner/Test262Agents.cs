using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Meziantou.Framework.JavaScriptEngine.Test262.Runner;

/// <summary>
/// The agents of a test (https://github.com/tc39/test262/blob/main/INTERPRETING.md, <c>$262.agent</c>): the main agent
/// starts agents, each one runs its source in its own engine on its own thread, receives the SharedArrayBuffer
/// broadcasts and reports strings to the main agent.
/// </summary>
internal sealed class Test262Agents : IDisposable
{
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _cancellation;
    private readonly List<Agent> _agents = [];
    private readonly ConcurrentQueue<string> _reports = new();
    private readonly ConcurrentQueue<string> _errors = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public Test262Agents(CancellationToken cancellationToken)
    {
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    private int _pendingTimeouts;

    /// <summary>Gets the errors thrown by the agents (an uncaught exception fails the test).</summary>
    public string? GetError() => _errors.TryPeek(out var error) ? error : null;

    /// <summary>
    /// Defines the global <c>setTimeout(callback, delay)</c> of the main agent (a timeout job of the engine): without it,
    /// atomicsHelper.js emulates it with a chain of promise jobs that starves the jobs of Atomics.waitAsync.
    /// </summary>
    public void InstallSetTimeout(JsRealm realm)
    {
        realm.DefineMethod(realm.GlobalObject, "setTimeout", 2, (_, _, arguments, _) =>
        {
            if (arguments.Length == 0 || !arguments[0].IsCallable)
                throw ThrowHelper.CreateException(ErrorType.TypeError, "setTimeout expects a function");

            var delay = arguments.Length > 1 ? JsOperations.ToNumber(arguments[1]) : 0;
            var milliseconds = double.IsNaN(delay) || delay < 0 ? 0 : Math.Min(delay, int.MaxValue);
            Interlocked.Increment(ref _pendingTimeouts);
            realm.Engine.EnqueueTimeoutJob(RunTimeout, arguments[0].AsObject(), default, realm, TimeSpan.FromMilliseconds(milliseconds));
            return default;
        });
    }

    private void RunTimeout(object? state, JsValue argument)
    {
        Interlocked.Decrement(ref _pendingTimeouts);
        ((JsObject)state!).Call(JsValue.Undefined, []);
    }

    /// <summary>Runs the jobs of the main agent until no Atomics.waitAsync waiter nor setTimeout callback is pending.</summary>
    public void RunJobs(JsEngine engine)
    {
        engine.RunJobsAndWaitForAtomicsWaiters();
        while (Volatile.Read(ref _pendingTimeouts) > 0)
        {
            if (_cancellation.IsCancellationRequested)
                throw new JsTerminationException(JsTerminationReason.Canceled, "The execution was canceled");

            var delay = engine.GetDelayUntilNextTimeoutJob() ?? TimeSpan.Zero;
            if (delay > TimeSpan.Zero)
            {
                Thread.Sleep(delay < TimeSpan.FromMilliseconds(10) ? delay : TimeSpan.FromMilliseconds(10));
            }

            engine.RunJobsAndWaitForAtomicsWaiters();
        }
    }

    /// <summary>Defines the <c>agent</c> object of the <c>$262</c> object of the main agent.</summary>
    public void InstallMainAgent(JsRealm realm, JsObject host)
    {
        var agent = new JsObject(realm.Intrinsics.ObjectPrototype);
        realm.DefineMethod(agent, "start", 1, (_, _, arguments, _) =>
        {
            Start(arguments.Length > 0 ? JsOperations.ToString(arguments[0]) : "");
            return default;
        });
        realm.DefineMethod(agent, "broadcast", 2, (_, _, arguments, _) =>
        {
            if (arguments.Length == 0 || arguments[0].Cell is not JsArrayBuffer { SharedBlock: { } block })
                throw ThrowHelper.CreateException(ErrorType.TypeError, "broadcast expects a SharedArrayBuffer");

            var id = arguments.Length > 1 ? arguments[1] : default;
            Broadcast(block, id);
            return default;
        });
        realm.DefineMethod(agent, "getReport", 0, (_, _, _, _) => _reports.TryDequeue(out var report) ? report : JsValue.Null);
        DefineCommonMethods(realm, agent);
        host.DefineBuiltIn("agent", agent);
    }

    private void DefineCommonMethods(JsRealm realm, JsObject agent)
    {
        realm.DefineMethod(agent, "sleep", 1, (_, _, arguments, _) =>
        {
            var milliseconds = arguments.Length > 0 ? JsOperations.ToNumber(arguments[0]) : 0;
            if (milliseconds > 0)
            {
                _cancellation.Token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(Math.Min(milliseconds, int.MaxValue)));
            }

            return default;
        });
        realm.DefineMethod(agent, "monotonicNow", 0, (_, _, _, _) => _clock.Elapsed.TotalMilliseconds);
    }

    /// <summary>$262.agent.start(source)</summary>
    private void Start(string source)
    {
        var agent = new Agent(this, source);
        lock (_agents)
        {
            _agents.Add(agent);
        }

        agent.Thread.Start();
    }

    /// <summary>$262.agent.broadcast(sab, id): blocks until every agent received the message.</summary>
    private void Broadcast(JsSharedDataBlock block, JsValue id)
    {
        Agent[] agents;
        lock (_agents)
        {
            agents = [.. _agents];
        }

        // The id is a number or a BigInt: an immutable value that can be shared with the other engines
        foreach (var agent in agents)
        {
            agent.Post(block, id);
        }

        foreach (var agent in agents)
        {
            WaitHandle.WaitAny([agent.Received.WaitHandle, _cancellation.Token.WaitHandle]);
            agent.Received.Reset();
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        Agent[] agents;
        lock (_agents)
        {
            agents = [.. _agents];
        }

        foreach (var agent in agents)
        {
            agent.Thread.Join(JoinTimeout);
            agent.Dispose();
        }

        _cancellation.Dispose();
    }

    private sealed class Agent : IDisposable
    {
        private readonly Test262Agents _owner;
        private readonly string _source;
        private readonly ManualResetEventSlim _messageAvailable = new();
        private readonly Lock _lock = new();
        private JsSharedDataBlock? _block;
        private JsValue _id;
        private JsObject? _callback;

        public Agent(Test262Agents owner, string source)
        {
            _owner = owner;
            _source = source;
            Thread = new Thread(Run) { IsBackground = true, Name = "test262 agent" };
        }

        public Thread Thread { get; }

        public ManualResetEventSlim Received { get; } = new();

        public void Post(JsSharedDataBlock block, JsValue id)
        {
            lock (_lock)
            {
                _block = block;
                _id = id;
            }

            _messageAvailable.Set();
        }

        private void Run()
        {
            var token = _owner._cancellation.Token;
            try
            {
                using var engine = new JsEngine(new JsEngineOptions
                {
                    CancellationToken = token,
                    RecursionLimit = 1_000,
                    TimeZone = "UTC",
                });

                var realm = engine.CreateRealm();
                var host = Test262Host.Install(realm, new StringBuilder());
                var agent = new JsObject(realm.Intrinsics.ObjectPrototype);
                realm.DefineMethod(agent, "receiveBroadcast", 1, (_, _, arguments, _) =>
                {
                    _callback = arguments.Length > 0 ? arguments[0].TryGetObject() : null;
                    return default;
                });
                realm.DefineMethod(agent, "report", 1, (_, _, arguments, _) =>
                {
                    _owner._reports.Enqueue(arguments.Length > 0 ? JsOperations.ToString(arguments[0]) : "undefined");
                    return default;
                });
                realm.DefineMethod(agent, "leaving", 0, (_, _, _, _) => default);
                _owner.DefineCommonMethods(realm, agent);
                host.DefineBuiltIn("agent", agent);

                realm.Execute(_source, "agent");
                engine.RunJobsAndWaitForAtomicsWaiters();
                while (_callback is { } callback)
                {
                    _callback = null;
                    WaitHandle.WaitAny([_messageAvailable.WaitHandle, token.WaitHandle]);
                    if (token.IsCancellationRequested)
                        return;

                    JsSharedDataBlock block;
                    JsValue id;
                    lock (_lock)
                    {
                        block = _block!;
                        id = _id;
                        _messageAvailable.Reset();
                    }

                    var buffer = JsArrayBuffer.CreateShared(realm, block);
                    Received.Set();
                    using (engine.Enter())
                    {
                        engine.CurrentRealm = realm;
                        callback.Call(JsValue.Undefined, [buffer, id]);
                    }

                    engine.RunJobsAndWaitForAtomicsWaiters();
                }
            }
            catch (JsException ex)
            {
                _owner._errors.Enqueue("Agent error: " + ex.Message);
            }
            catch (JsTerminationException)
            {
                // The test is over (or timed out)
            }
            catch (InvalidOperationException ex)
            {
                _owner._errors.Enqueue("Agent failure: " + ex);
            }
        }

        public void Dispose()
        {
            _messageAvailable.Dispose();
            Received.Dispose();
        }
    }
}
