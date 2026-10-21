namespace Meziantou.Framework.JavaScriptEngine.Tests.Builtins;

public sealed class PromiseTests
{
    private static JsPromise Resolved(CollectionsTestEngine test, JsValue value) => (JsPromise)CollectionsTestEngine.Invoke(test.Global("Promise"), "resolve", value).AsObject();

    private static JsPromise Rejected(CollectionsTestEngine test, JsValue value) => (JsPromise)CollectionsTestEngine.Invoke(test.Global("Promise"), "reject", value).AsObject();

    private static JsValue Then(JsValue promise, JsValue onFulfilled, JsValue onRejected = default) => CollectionsTestEngine.Invoke(promise, "then", onFulfilled, onRejected);

    [Fact]
    public void Constructor_Properties()
    {
        using var test = new CollectionsTestEngine();
        var promise = test.Global("Promise");
        Assert.Equal(1, CollectionsTestEngine.Get(promise, "length").AsNumber());
        Assert.Equal("Promise", CollectionsTestEngine.Get(promise, "name").AsString());
        Assert.True(promise.GetOwnProperty(new PropertyKey("prototype"), out var prototype));
        Assert.False(prototype.Writable || prototype.Enumerable || prototype.Configurable);
        Assert.Equal("Promise", CollectionsTestEngine.Get(prototype.Value, WellKnownSymbols.ToStringTag).AsString());
        Assert.Same(test.Realm.Intrinsics.PromisePrototypeThen, CollectionsTestEngine.Get(prototype.Value, "then").AsObject());
        Assert.True(promise.GetOwnProperty(new PropertyKey(WellKnownSymbols.Species), out var species));
        Assert.Equal("get [Symbol.species]", CollectionsTestEngine.Get(species.Get, "name").AsString());
    }

    [Fact]
    public void Constructor_RequiresNewAndCallableExecutor()
    {
        using var test = new CollectionsTestEngine();
        var promise = test.Global("Promise");
        test.AssertThrows(ErrorType.TypeError, () => promise.Call(JsValue.Undefined, [test.Function((_, _) => default)]));
        test.AssertThrows(ErrorType.TypeError, () => JsOperations.Construct(promise, [1]));
    }

    [Fact]
    public void Constructor_ResolvingFunctions()
    {
        using var test = new CollectionsTestEngine();
        JsValue resolve = default;
        JsValue reject = default;
        var promise = (JsPromise)test.New("Promise", test.Function((_, arguments) =>
        {
            resolve = arguments[0];
            reject = arguments[1];
            return default;
        })).AsObject();

        Assert.Equal(1, CollectionsTestEngine.Get(resolve, "length").AsNumber());
        Assert.Equal("", CollectionsTestEngine.Get(resolve, "name").AsString());
        Assert.False(resolve.AsObject().IsConstructor);
        Assert.False(resolve.AsObject().HasOwnProperty(new PropertyKey("prototype")));

        resolve.AsObject().Call(JsValue.Undefined, [42]);
        reject.AsObject().Call(JsValue.Undefined, [1]);
        resolve.AsObject().Call(JsValue.Undefined, [2]);
        Assert.Equal(PromiseState.Fulfilled, promise.State);
        Assert.Equal(42, promise.Result.AsNumber());
    }

    [Fact]
    public void Constructor_ExecutorThrows_Rejects()
    {
        using var test = new CollectionsTestEngine();
        var promise = (JsPromise)test.New("Promise", test.Function((_, _) => throw new JsException("boom"))).AsObject();
        Assert.Equal(PromiseState.Rejected, promise.State);
    }

    [Fact]
    public void Constructor_SelfResolution_RejectsWithTypeError()
    {
        using var test = new CollectionsTestEngine();
        JsValue resolve = default;
        var promise = (JsPromise)test.New("Promise", test.Function((_, arguments) =>
        {
            resolve = arguments[0];
            return default;
        })).AsObject();

        resolve.AsObject().Call(JsValue.Undefined, [promise]);
        Assert.Equal(PromiseState.Rejected, promise.State);
        test.AssertError(ErrorType.TypeError, promise.Result);
    }

    [Fact]
    public void Then_ReactionsRunInOrder()
    {
        using var test = new CollectionsTestEngine();
        var p1 = Resolved(test, 1);
        Then(p1, test.Logger("a"));
        var p2 = test.New("Promise", test.Function((_, arguments) => arguments[0].AsObject().Call(JsValue.Undefined, [2])));
        Then(p2, test.Logger("b"));
        var p3 = Then(Resolved(test, 3), test.Logger("c"));
        Then(p3, test.Logger("d"));
        Assert.Empty(test.Log);

        test.Engine.RunJobs();
        Assert.Equal(["a:1", "b:2", "c:3", "d:undefined"], test.Log);
    }

    [Fact]
    public void Then_ResolvingWithAPromiseTakesTwoMoreJobs()
    {
        using var test = new CollectionsTestEngine();
        var p = Resolved(test, JsValue.Undefined);
        var outer = test.New("Promise", test.Function((_, arguments) => arguments[0].AsObject().Call(JsValue.Undefined, [p])));
        Then(outer, test.Logger("x"));
        Then(Then(Then(p, test.Logger("1")), test.Logger("2")), test.Logger("3"));

        test.Engine.RunJobs();
        Assert.Equal(["1:undefined", "2:undefined", "x:undefined", "3:undefined"], test.Log);
    }

    [Fact]
    public void Then_HandlerResultResolvesDerivedPromise()
    {
        using var test = new CollectionsTestEngine();
        var derived = (JsPromise)Then(Resolved(test, 2), test.Function((_, arguments) => arguments[0].AsNumber() * 10)).AsObject();
        var rejected = (JsPromise)Then(Resolved(test, 2), test.Function((_, _) => throw new JsException(new JsValue(5)))).AsObject();
        var passThrough = (JsPromise)Then(Rejected(test, 7), JsValue.Undefined).AsObject();
        test.Engine.RunJobs();

        Assert.Equal(PromiseState.Fulfilled, derived.State);
        Assert.Equal(20, derived.Result.AsNumber());
        Assert.Equal(PromiseState.Rejected, rejected.State);
        Assert.Equal(5, rejected.Result.AsNumber());
        Assert.Equal(PromiseState.Rejected, passThrough.State);
        Assert.Equal(7, passThrough.Result.AsNumber());
    }

    [Fact]
    public void Then_NonPromiseReceiver_Throws()
    {
        using var test = new CollectionsTestEngine();
        var then = test.Realm.Intrinsics.PromisePrototypeThen;
        test.AssertThrows(ErrorType.TypeError, () => then.Call(test.Object(), []));
    }

    [Fact]
    public void Resolve_Thenable_ThenIsReadOnceAndCalledInAJob()
    {
        using var test = new CollectionsTestEngine();
        var getCount = 0;
        var thenable = test.Object();
        var thenFunction = test.Function((thisValue, arguments) =>
        {
            test.Log.Add("then");
            Assert.Same(thenable, thisValue.AsObject());
            arguments[0].AsObject().Call(JsValue.Undefined, [10]);
            arguments[0].AsObject().Call(JsValue.Undefined, [20]);
            return default;
        });
        thenable.DefineBuiltInAccessor(new PropertyKey("then"), test.Function((_, _) =>
        {
            getCount++;
            return thenFunction;
        }), setter: null);

        var promise = Resolved(test, thenable);
        Assert.Equal(1, getCount);
        Assert.Empty(test.Log);
        Assert.Equal(PromiseState.Pending, promise.State);

        test.Engine.RunJobs();
        Assert.Equal(["then"], test.Log);
        Assert.Equal(1, getCount);
        Assert.Equal(PromiseState.Fulfilled, promise.State);
        Assert.Equal(10, promise.Result.AsNumber());
    }

    [Fact]
    public void Resolve_PoisonedThen_Rejects()
    {
        using var test = new CollectionsTestEngine();
        var thenable = test.Object();
        thenable.DefineBuiltInAccessor(new PropertyKey("then"), test.Function((_, _) => throw new JsException(new JsValue(3))), setter: null);
        var promise = Resolved(test, thenable);
        Assert.Equal(PromiseState.Rejected, promise.State);
        Assert.Equal(3, promise.Result.AsNumber());
    }

    [Fact]
    public void Resolve_ReturnsSamePromise()
    {
        using var test = new CollectionsTestEngine();
        var promise = Resolved(test, 1);
        Assert.Same(promise, Resolved(test, promise));
        test.AssertThrows(ErrorType.TypeError, () => CollectionsTestEngine.Get(test.Global("Promise"), "resolve").AsObject().Call(1, []));
    }

    [Fact]
    public void NewPromiseCapability_CustomConstructor()
    {
        using var test = new CollectionsTestEngine();
        JsValue executor = default;
        var log = new List<string>();
        var notPromise = test.Realm.CreateBuiltinFunction("NotPromise", 1, (_, _, arguments, _) =>
        {
            executor = arguments[0];
            var obj = test.Object();
            executor.AsObject().Call(JsValue.Undefined, [test.Function((_, a) => { log.Add("resolve:" + a[0]); return default; }), test.Function((_, a) => { log.Add("reject:" + a[0]); return default; })]);
            return obj;
        }, isConstructor: true);

        CollectionsTestEngine.Get(test.Global("Promise"), "resolve").AsObject().Call(notPromise, [1]);
        CollectionsTestEngine.Get(test.Global("Promise"), "reject").AsObject().Call(notPromise, [2]);
        Assert.Equal(["resolve:1", "reject:2"], log);
        Assert.Equal(2, CollectionsTestEngine.Get(executor, "length").AsNumber());
        Assert.Equal("", CollectionsTestEngine.Get(executor, "name").AsString());

        // The executor throws when it is called again with a function
        test.AssertThrows(ErrorType.TypeError, () => executor.AsObject().Call(JsValue.Undefined, [test.Function((_, _) => default), JsValue.Undefined]));

        // The executor must receive callable functions
        var badPromise = test.Realm.CreateBuiltinFunction("BadPromise", 1, (_, _, arguments, _) =>
        {
            arguments[0].AsObject().Call(JsValue.Undefined, [1, 2]);
            return test.Object();
        }, isConstructor: true);
        test.AssertThrows(ErrorType.TypeError, () => CollectionsTestEngine.Get(test.Global("Promise"), "resolve").AsObject().Call(badPromise, [1]));
    }

    [Fact]
    public void Catch_InvokesThen()
    {
        using var test = new CollectionsTestEngine();
        var receiver = test.Object(("then", test.Function((_, arguments) =>
        {
            test.Log.Add(arguments[0] + "," + arguments[1].IsCallable);
            return 5;
        })));
        var result = CollectionsTestEngine.Get(test.Realm.Intrinsics.PromisePrototype, "catch").AsObject().Call(receiver, [test.Function((_, _) => default)]);
        Assert.Equal(5, result.AsNumber());
        Assert.Equal(["undefined,True"], test.Log);
    }

    [Fact]
    public void Finally_PassesValuesThrough()
    {
        using var test = new CollectionsTestEngine();
        var fulfilled = (JsPromise)CollectionsTestEngine.Invoke(Resolved(test, 1), "finally", test.Logger("finally1")).AsObject();
        var rejected = (JsPromise)CollectionsTestEngine.Invoke(Rejected(test, 2), "finally", test.Logger("finally2")).AsObject();
        var overridden = (JsPromise)CollectionsTestEngine.Invoke(Resolved(test, 1), "finally", test.Function((_, _) => throw new JsException(new JsValue(9)))).AsObject();
        var notCallable = (JsPromise)CollectionsTestEngine.Invoke(Resolved(test, 4), "finally", 1).AsObject();
        test.Engine.RunJobs();

        Assert.Equal(["finally1", "finally2"], test.Log);
        Assert.Equal(PromiseState.Fulfilled, fulfilled.State);
        Assert.Equal(1, fulfilled.Result.AsNumber());
        Assert.Equal(PromiseState.Rejected, rejected.State);
        Assert.Equal(2, rejected.Result.AsNumber());
        Assert.Equal(PromiseState.Rejected, overridden.State);
        Assert.Equal(9, overridden.Result.AsNumber());
        Assert.Equal(4, notCallable.Result.AsNumber());
    }

    [Fact]
    public void All_ResolvesWithValuesInOrder()
    {
        using var test = new CollectionsTestEngine();
        JsValue resolveFirst = default;
        var pending = test.New("Promise", test.Function((_, arguments) =>
        {
            resolveFirst = arguments[0];
            return default;
        }));
        var result = (JsPromise)CollectionsTestEngine.Invoke(test.Global("Promise"), "all", test.Iterable(pending, 2, Resolved(test, 3))).AsObject();
        test.Engine.RunJobs();
        Assert.Equal(PromiseState.Pending, result.State);

        resolveFirst.AsObject().Call(JsValue.Undefined, [1]);
        test.Engine.RunJobs();
        Assert.Equal(PromiseState.Fulfilled, result.State);
        Assert.Equal([1d, 2d, 3d], CollectionsTestEngine.ToList(result.Result).Select(v => v.AsNumber()));
    }

    [Fact]
    public void All_Empty_ResolvesSynchronously()
    {
        using var test = new CollectionsTestEngine();
        var result = (JsPromise)CollectionsTestEngine.Invoke(test.Global("Promise"), "all", test.Iterable()).AsObject();
        Assert.Equal(PromiseState.Fulfilled, result.State);
        Assert.Empty(CollectionsTestEngine.ToList(result.Result));
    }

    [Fact]
    public void All_RejectsWithFirstRejection()
    {
        using var test = new CollectionsTestEngine();
        var result = (JsPromise)CollectionsTestEngine.Invoke(test.Global("Promise"), "all", test.Iterable(Resolved(test, 1), Rejected(test, 2), Rejected(test, 3))).AsObject();
        test.Engine.RunJobs();
        Assert.Equal(PromiseState.Rejected, result.State);
        Assert.Equal(2, result.Result.AsNumber());
    }

    [Fact]
    public void All_NotIterable_RejectsPromise()
    {
        using var test = new CollectionsTestEngine();
        var result = (JsPromise)CollectionsTestEngine.Invoke(test.Global("Promise"), "all", 1).AsObject();
        Assert.Equal(PromiseState.Rejected, result.State);
        test.AssertError(ErrorType.TypeError, result.Result);
    }

    [Fact]
    public void All_ResolveNotCallable_ClosesNothingAndRejects()
    {
        using var test = new CollectionsTestEngine();
        var promise = test.Global("Promise");
        var original = promise.Get(new PropertyKey("resolve"));
        promise.Set(new PropertyKey("resolve"), 1, throwOnFailure: true);
        var result = (JsPromise)CollectionsTestEngine.Invoke(promise, "all", test.Iterable(1)).AsObject();
        promise.Set(new PropertyKey("resolve"), original, throwOnFailure: true);
        Assert.Equal(PromiseState.Rejected, result.State);
        test.AssertError(ErrorType.TypeError, result.Result);
    }

    [Fact]
    public void AllSettled_ReportsEachOutcome()
    {
        using var test = new CollectionsTestEngine();
        var result = (JsPromise)CollectionsTestEngine.Invoke(test.Global("Promise"), "allSettled", test.Iterable(Resolved(test, 1), Rejected(test, 2))).AsObject();
        test.Engine.RunJobs();
        Assert.Equal(PromiseState.Fulfilled, result.State);
        var values = CollectionsTestEngine.ToList(result.Result);
        Assert.Equal("fulfilled", CollectionsTestEngine.Get(values[0], "status").AsString());
        Assert.Equal(1, CollectionsTestEngine.Get(values[0], "value").AsNumber());
        Assert.Equal("rejected", CollectionsTestEngine.Get(values[1], "status").AsString());
        Assert.Equal(2, CollectionsTestEngine.Get(values[1], "reason").AsNumber());
        Assert.False(values[1].AsObject().HasOwnProperty(new PropertyKey("value")));
    }

    [Fact]
    public void Any_FulfillsWithFirstFulfillment()
    {
        using var test = new CollectionsTestEngine();
        var result = (JsPromise)CollectionsTestEngine.Invoke(test.Global("Promise"), "any", test.Iterable(Rejected(test, 1), Resolved(test, 2), Resolved(test, 3))).AsObject();
        test.Engine.RunJobs();
        Assert.Equal(PromiseState.Fulfilled, result.State);
        Assert.Equal(2, result.Result.AsNumber());
    }

    [Fact]
    public void Any_AllRejected_RejectsWithAggregateError()
    {
        using var test = new CollectionsTestEngine();
        var result = (JsPromise)CollectionsTestEngine.Invoke(test.Global("Promise"), "any", test.Iterable(Rejected(test, 1), Rejected(test, 2))).AsObject();
        var empty = (JsPromise)CollectionsTestEngine.Invoke(test.Global("Promise"), "any", test.Iterable()).AsObject();
        test.Engine.RunJobs();

        Assert.Equal(PromiseState.Rejected, result.State);
        test.AssertError(ErrorType.AggregateError, result.Result);
        Assert.True(result.Result.AsObject().GetOwnProperty(new PropertyKey("errors"), out var errors));
        Assert.True(errors.Writable && errors.Configurable && !errors.Enumerable);
        Assert.Equal([1d, 2d], CollectionsTestEngine.ToList(errors.Value).Select(v => v.AsNumber()));
        Assert.False(result.Result.AsObject().HasOwnProperty(new PropertyKey("message")));

        Assert.Equal(PromiseState.Rejected, empty.State);
        test.AssertError(ErrorType.AggregateError, empty.Result);
    }

    [Fact]
    public void Race_SettlesWithFirst()
    {
        using var test = new CollectionsTestEngine();
        var pending = test.New("Promise", test.Function((_, _) => default));
        var result = (JsPromise)CollectionsTestEngine.Invoke(test.Global("Promise"), "race", test.Iterable(pending, Rejected(test, 2), Resolved(test, 3))).AsObject();
        test.Engine.RunJobs();
        Assert.Equal(PromiseState.Rejected, result.State);
        Assert.Equal(2, result.Result.AsNumber());
    }

    [Fact]
    public void Combinator_ThenThrows_ClosesIteratorAndRejects()
    {
        using var test = new CollectionsTestEngine();
        var thenable = Resolved(test, 1);
        thenable.DefineBuiltIn(new PropertyKey("then"), test.Function((_, _) => throw new JsException(new JsValue(8))));
        var returnCalls = 0;
        var nextCalls = 0;
        var iterator = test.Object(
            ("next", test.Function((_, _) => IteratorOperations.CreateIteratorResultObject(test.Realm, thenable, nextCalls++ > 0))),
            ("return", test.Function((_, _) =>
            {
                returnCalls++;
                return test.Object();
            })));
        var iterable = test.Object();
        iterable.DefineBuiltIn(new PropertyKey(WellKnownSymbols.Iterator), test.Function((_, _) => iterator));

        var result = (JsPromise)CollectionsTestEngine.Invoke(test.Global("Promise"), "all", iterable).AsObject();
        Assert.Equal(1, returnCalls);
        Assert.Equal(PromiseState.Rejected, result.State);
        Assert.Equal(8, result.Result.AsNumber());
    }

    [Fact]
    public void Try_CallsCallbackSynchronously()
    {
        using var test = new CollectionsTestEngine();
        var promise = test.Global("Promise");
        var fulfilled = (JsPromise)CollectionsTestEngine.Invoke(promise, "try", test.Function((_, arguments) => arguments[0].AsNumber() + arguments[1].AsNumber()), 1, 2).AsObject();
        Assert.Equal(PromiseState.Fulfilled, fulfilled.State);
        Assert.Equal(3, fulfilled.Result.AsNumber());

        var rejected = (JsPromise)CollectionsTestEngine.Invoke(promise, "try", test.Function((_, _) => throw new JsException(new JsValue(4)))).AsObject();
        Assert.Equal(PromiseState.Rejected, rejected.State);
        Assert.Equal(4, rejected.Result.AsNumber());

        // Returning a promise does not wrap it
        var inner = Resolved(test, 1);
        Assert.Same(inner, CollectionsTestEngine.Invoke(promise, "try", test.Function((_, _) => inner)).AsObject());
    }

    [Fact]
    public void WithResolvers_ReturnsTheCapability()
    {
        using var test = new CollectionsTestEngine();
        var result = CollectionsTestEngine.Invoke(test.Global("Promise"), "withResolvers");
        var promise = (JsPromise)CollectionsTestEngine.Get(result, "promise").AsObject();
        CollectionsTestEngine.Get(result, "resolve").AsObject().Call(JsValue.Undefined, [5]);
        CollectionsTestEngine.Get(result, "reject").AsObject().Call(JsValue.Undefined, [6]);
        Assert.Equal(PromiseState.Fulfilled, promise.State);
        Assert.Equal(5, promise.Result.AsNumber());
        Assert.Equal(["promise", "resolve", "reject"], result.AsObject().OwnPropertyKeys().Select(k => k.ToString()));
    }

    [Fact]
    public void RejectionTracker_ReportsRejectAndHandle()
    {
        using var test = new CollectionsTestEngine();
        var hooks = new RecordingHostHooks();
        test.Engine.HostHooks = hooks;
        var rejected = Rejected(test, 1);
        Assert.Equal([(rejected, JsPromiseRejectionOperation.Reject)], hooks.Rejections);

        Then(rejected, JsValue.Undefined, test.Function((_, _) => default));
        Assert.Equal([(rejected, JsPromiseRejectionOperation.Reject), (rejected, JsPromiseRejectionOperation.Handle)], hooks.Rejections);

        // A second handler is not reported; a handled promise rejected later is not reported
        Then(rejected, JsValue.Undefined, test.Function((_, _) => default));
        var capability = PromiseCapability.CreateIntrinsic(test.Realm);
        JsPromise.PerformPromiseThen((JsPromise)capability.Promise, JsValue.Undefined, JsValue.Undefined, resultCapability: null);
        capability.Reject(2);
        Assert.HasCount(2, hooks.Rejections);
        test.Engine.RunJobs();
    }

    [Fact]
    public void JobCallback_HostDefinedStateIsCapturedByThen()
    {
        using var test = new CollectionsTestEngine();
        var hooks = new RecordingHostHooks { CurrentState = "registration" };
        test.Engine.HostHooks = hooks;
        var states = new List<string?>();
        Then(Resolved(test, 1), test.Function((_, _) =>
        {
            states.Add(hooks.CurrentState);
            return default;
        }));
        hooks.CurrentState = "other";
        test.Engine.RunJobs();
        Assert.Equal(["registration"], states);
        Assert.Equal("other", hooks.CurrentState);
    }

    [Fact]
    public void NativeReactions_AwaitFastPath()
    {
        using var test = new CollectionsTestEngine();
        var awaited = JsPromise.PromiseResolve(test.Realm, 5);
        Assert.Same(awaited, JsPromise.PromiseResolve(test.Realm, awaited));
        var thenable = JsPromise.PromiseResolve(test.Realm, test.Object());
        Assert.NotSame(awaited, thenable);

        var log = new List<string>();
        JsPromise.PerformPromiseThen(awaited, static (state, value) => ((List<string>)state!).Add("fulfilled:" + value), static (state, value) => ((List<string>)state!).Add("rejected:" + value), log);
        JsPromise.PerformPromiseThen(Rejected(test, 6), static (state, value) => ((List<string>)state!).Add("fulfilled:" + value), static (state, value) => ((List<string>)state!).Add("rejected:" + value), log);
        Then(Resolved(test, 7), test.Function((_, arguments) =>
        {
            log.Add("then:" + arguments[0]);
            return default;
        }));
        Assert.Empty(log);
        test.Engine.RunJobs();
        Assert.Equal(["fulfilled:5", "rejected:6", "then:7"], log);
    }

    [Fact]
    public void RunJobs_JobException_RethrownByDefault()
    {
        using var test = new CollectionsTestEngine();
        test.Engine.EnqueuePromiseJob(static (_, _) => throw new JsException(new JsValue(1)), null, default, test.Realm);
        test.Engine.EnqueuePromiseJob(static (state, _) => ((List<string>)state!).Add("second"), test.Log, default, test.Realm);
        var exception = Assert.Throws<JsException>(test.Engine.RunJobs);
        Assert.Equal(1, exception.Value.AsNumber());
        Assert.Empty(test.Log);

        test.Engine.RunJobs();
        Assert.Equal(["second"], test.Log);
    }

    [Fact]
    public void RunJobs_JobException_ReportedToHost()
    {
        using var test = new CollectionsTestEngine();
        var hooks = new RecordingHostHooks();
        test.Engine.HostHooks = hooks;
        test.Engine.EnqueuePromiseJob(static (_, _) => throw new JsException(new JsValue(1)), null, default, test.Realm);
        test.Engine.EnqueuePromiseJob(static (state, _) => ((List<string>)state!).Add("second"), test.Log, default, test.Realm);
        test.Engine.RunJobs();
        Assert.Equal(1, Assert.Single(hooks.Exceptions).Value.AsNumber());
        Assert.Equal(["second"], test.Log);
    }

    [Fact]
    public void RunJobs_PromiseJobsRunBeforeGenericJobs()
    {
        using var test = new CollectionsTestEngine();
        test.Engine.EnqueueGenericJob(static (state, _) =>
        {
            var test = (CollectionsTestEngine)state!;
            test.Log.Add("generic1");
            test.Engine.EnqueuePromiseJob(static (s, _) => ((CollectionsTestEngine)s!).Log.Add("promise2"), test, default, test.Realm);
        }, test, default, test.Realm);
        test.Engine.EnqueueGenericJob(static (state, _) => ((CollectionsTestEngine)state!).Log.Add("generic2"), test, default, test.Realm);
        test.Engine.EnqueuePromiseJob(static (state, _) => ((CollectionsTestEngine)state!).Log.Add("promise1"), test, default, test.Realm);
        Assert.True(test.Engine.HasPendingJobs);
        test.Engine.RunJobs();
        Assert.Equal(["promise1", "generic1", "promise2", "generic2"], test.Log);
        Assert.False(test.Engine.HasPendingJobs);
    }

    [Fact]
    public void RunJobs_GenericJobFromAnotherThread()
    {
        using var test = new CollectionsTestEngine();
        var engine = test.Engine;
        var realm = test.Realm;
        var log = test.Log;
        var thread = new Thread(() => engine.EnqueueGenericJob(static (state, _) => ((List<string>)state!).Add("from thread"), log, default, realm));
        thread.Start();
        thread.Join();
        engine.RunJobs();
        Assert.Equal(["from thread"], log);
    }

    [Fact]
    public void RunJobs_TimeoutJobsRunWhenDue()
    {
        var timeProvider = new ManualTimeProvider();
        using var test = new CollectionsTestEngine(new JsEngineOptions { TimeProvider = timeProvider });
        test.Engine.EnqueueTimeoutJob(static (state, _) => ((List<string>)state!).Add("200ms"), test.Log, default, test.Realm, TimeSpan.FromMilliseconds(200));
        test.Engine.EnqueueTimeoutJob(static (state, _) => ((List<string>)state!).Add("100ms"), test.Log, default, test.Realm, TimeSpan.FromMilliseconds(100));
        test.Engine.EnqueueTimeoutJob(static (state, _) => ((List<string>)state!).Add("100ms-2"), test.Log, default, test.Realm, TimeSpan.FromMilliseconds(100));
        test.Engine.RunJobs();
        Assert.Empty(test.Log);
        Assert.Equal(TimeSpan.FromMilliseconds(100), test.Engine.GetDelayUntilNextTimeoutJob());

        timeProvider.Advance(TimeSpan.FromMilliseconds(150));
        test.Engine.RunJobs();
        Assert.Equal(["100ms", "100ms-2"], test.Log);

        timeProvider.Advance(TimeSpan.FromMilliseconds(50));
        Assert.Equal(TimeSpan.Zero, test.Engine.GetDelayUntilNextTimeoutJob());
        test.Engine.RunJobs();
        Assert.Equal(["100ms", "100ms-2", "200ms"], test.Log);
        Assert.Null(test.Engine.GetDelayUntilNextTimeoutJob());
    }

    [Fact]
    public void NestedJobQueue_RunsOnlyNestedJobs()
    {
        using var test = new CollectionsTestEngine();
        Then(Resolved(test, 1), test.Logger("outer"));
        using (test.Engine.EnterNestedJobQueue())
        {
            Then(Resolved(test, 2), test.Logger("nested"));
            test.Engine.RunJobs();
            Assert.Equal(["nested:2"], test.Log);

            Then(Resolved(test, 3), test.Logger("left"));
        }

        test.Engine.RunJobs();
        Assert.Equal(["nested:2", "outer:1", "left:3"], test.Log);
    }

    [Fact]
    public void NestedJobQueue_MustBeExitedInOrder()
    {
        using var test = new CollectionsTestEngine();
        var outer = test.Engine.EnterNestedJobQueue();
        var inner = test.Engine.EnterNestedJobQueue();
        Assert.Throws<InvalidOperationException>(outer.Dispose);
        inner.Dispose();
        outer.Dispose();
    }

    [Fact]
    public void Job_RunsInItsRealm()
    {
        using var test = new CollectionsTestEngine();
        var other = test.Engine.CreateRealm();
        JsRealm? observed = null;
        test.Engine.EnqueuePromiseJob((_, _) => observed = test.Engine.CurrentRealm, null, default, other);
        test.Engine.RunJobs();
        Assert.Same(other, observed);
        Assert.Same(test.Realm, test.Engine.CurrentRealm);
    }

    private sealed class RecordingHostHooks : JsHost
    {
        public List<(JsObject Promise, JsPromiseRejectionOperation Operation)> Rejections { get; } = [];

        public List<JsException> Exceptions { get; } = [];

        public string? CurrentState { get; set; }

        public override void PromiseRejectionTracker(JsEngine engine, JsObject promise, JsPromiseRejectionOperation operation) => Rejections.Add((promise, operation));

        public override JsJobCallback MakeJobCallback(JsEngine engine, JsObject callback) => new(callback, CurrentState);

        public override JsValue CallJobCallback(JsEngine engine, in JsJobCallback jobCallback, JsValue thisValue, ReadOnlySpan<JsValue> arguments)
        {
            var previous = CurrentState;
            CurrentState = (string?)jobCallback.HostDefined;
            try
            {
                return base.CallJobCallback(engine, jobCallback, thisValue, arguments);
            }
            finally
            {
                CurrentState = previous;
            }
        }

        public override void ReportJobException(JsEngine engine, JsException exception) => Exceptions.Add(exception);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan delay) => _timestamp += delay.Ticks;
    }
}
