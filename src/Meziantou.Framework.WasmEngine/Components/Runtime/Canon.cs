using Meziantou.Framework.WasmEngine.Components.Binary;
using static Meziantou.Framework.WasmEngine.Components.Runtime.CanonicalAbi;

namespace Meziantou.Framework.WasmEngine.Components.Runtime;

/// <summary>A function of the component model (canonical ABI "FuncInst"): it starts a call, and returns the action that requests its cancellation.</summary>
internal delegate Action ComponentFuncInst(Func<ComponentValue[]> onStart, Action<ComponentValue[]?> onResolve);

/// <summary>A function of the component model: its type and its implementation.</summary>
internal sealed class ComponentFunctionValue(ComponentFunctionType type, ComponentFuncInst invoke)
{
    public ComponentFunctionType Type { get; } = type;

    public ComponentFuncInst Invoke { get; } = invoke;
}

/// <summary>The canonical definitions: <c>canon lift</c>, <c>canon lower</c>, and the built-ins.</summary>
internal static class Canon
{
    private const uint Blocked = 0xffff_ffff;

    private enum CallbackCode
    {
        Exit = 0,
        Yield = 1,
        Wait = 2,
    }

    // ------------------------------------------------------------------------------------------------------------
    // canon lift
    // ------------------------------------------------------------------------------------------------------------

    public static ComponentFuncInst Lift(WasmFunction callee, ComponentFunctionType type, CanonicalOptions options, ComponentInstanceState instance)
    {
        return (onStart, onResolve) =>
        {
            var store = instance.Store;
            store.ThrowIfLockedDown();
            store.NestingDepth++;
            try
            {
                return CanonLift(callee, type, options, instance, onStart, onResolve);
            }
            finally
            {
                store.NestingDepth--;
            }
        };
    }

    private static Action CanonLift(WasmFunction callee, ComponentFunctionType type, CanonicalOptions options, ComponentInstanceState instance, Func<ComponentValue[]> onStart, Action<ComponentValue[]?> onResolve)
    {
        var task = new ComponentTask(type, options, instance, onStart, onResolve);
        if (!type.IsAsync)
        {
            // A synchronous call cannot return before its result: it runs on the stack of its caller
            var thread = ComponentThread.CreateInline(task);
            var previous = ComponentThread.Current;
            ComponentThread.Current = thread;
            try
            {
                ThreadFunc(task, thread, callee);
            }
            finally
            {
                thread.MarkExited();
                ComponentThread.Current = previous;
            }

            if (task.State != ComponentTask.TaskState.Resolved)
            {
                instance.Store.RunUntil(() => task.State == ComponentTask.TaskState.Resolved, instance);
            }

            return task.RequestCancellation;
        }

        ComponentThread? asyncThread = null;
        asyncThread = ComponentThread.CreateContinuation(task, () => ThreadFunc(task, asyncThread!, callee));
        asyncThread.Resume();
        return task.RequestCancellation;
    }

    private static void ThreadFunc(ComponentTask task, ComponentThread thread, WasmFunction callee)
    {
        if (!task.EnterImplicitThread(thread))
            return;

        var options = task.Options;
        var instance = task.Instance;
        var type = task.Type;
        var cx = new LiftLowerContext(LiftLowerDirection.Lift, options, instance, task);
        var arguments = task.Start();
        var flatArguments = LowerFlatValues(cx.ReverseDirection(), MaxFlatParams, arguments, type.ParameterTypes);
        if (!options.Async)
        {
            var flatResults = CallAndTrapOnThrow(callee, flatArguments.ToArray());
            var result = LiftFlatValues(cx, MaxFlatResults, new CoreValueIterator(flatResults), type.ResultTypes);
            task.Return(result);
            if (options.PostReturn is { } postReturn)
            {
                instance.MayLeave = false;
                try
                {
                    CallAndTrapOnThrow(postReturn, flatResults);
                }
                finally
                {
                    instance.MayLeave = true;
                }
            }

            task.ExitImplicitThread();
            return;
        }

        if (options.Callback is null)
        {
            CallAndTrapOnThrow(callee, flatArguments.ToArray());
            task.ExitImplicitThread();
            return;
        }

        var packed = (uint)CallAndTrapOnThrow(callee, flatArguments.ToArray())[0].I32;
        var (code, setIndex) = UnpackCallbackResult(packed);
        while (code != CallbackCode.Exit)
        {
            (EventCode Code, uint Index, uint Payload) @event;
            if (task.DeliverPendingCancel())
            {
                @event = (EventCode.TaskCancelled, 0u, 0u);
            }
            else
            {
                instance.ExclusiveThread = null;
                thread.Cancellable = true;
                switch (code)
                {
                    case CallbackCode.Yield:
                        thread.WaitUntil(() => instance.ExclusiveThread is null);
                        @event = task.DeliverPendingCancel() ? (EventCode.TaskCancelled, 0u, 0u) : (EventCode.None, 0u, 0u);
                        break;

                    case CallbackCode.Wait:
                        var set = instance.Handles.Get(setIndex) as WaitableSet ?? throw ComponentTrap.Create("handle is not a waitable set");
                        @event = set.WaitFromCallback();
                        break;

                    default:
                        throw ComponentTrap.Create("unsupported callback code");
                }

                thread.Cancellable = false;
                instance.ExclusiveThread = task.ImplicitThread;
            }

            var results = CallAndTrapOnThrow(options.Callback, [WasmValue.FromI32((int)@event.Code), WasmValue.FromI32((int)@event.Index), WasmValue.FromI32((int)@event.Payload)]);
            (code, setIndex) = UnpackCallbackResult((uint)results[0].I32);
        }

        task.ExitImplicitThread();
    }

    private static (CallbackCode Code, uint SetIndex) UnpackCallbackResult(uint packed)
    {
        var code = packed & 0xf;
        ComponentTrap.ThrowIf(code > 2, "unsupported callback code: " + code.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return ((CallbackCode)code, packed >> 4);
    }



    private static WasmValue[] CallAndTrapOnThrow(WasmFunction callee, WasmValue[] arguments)
    {
        try
        {
            return callee.Call(arguments);
        }
        catch (WasmThrowException ex)
        {
            throw ComponentTrap.Create("an exception escaped a component function: " + ex.Message);
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // canon lower
    // ------------------------------------------------------------------------------------------------------------

    public static WasmValue[] Lower(ComponentFunctionValue callee, ComponentFunctionType type, CanonicalOptions options, ComponentInstanceState instance, ReadOnlySpan<WasmValue> arguments)
    {
        var thread = ComponentThread.Current;
        ComponentTrap.ThrowIf(thread is null || thread.Task.Instance != instance, "cannot call an import outside of a call of the component");
        ComponentTrap.ThrowIf(!instance.MayLeave, "cannot leave component instance");
        instance.Store.ThrowIfLockedDown();

        var subtask = new Subtask();
        var cx = new LiftLowerContext(LiftLowerDirection.Lower, options, instance, subtask);
        var flatArguments = new CoreValueIterator(arguments.ToArray());
        var maxFlatParams = options.Async ? MaxFlatAsyncParams : MaxFlatParams;
        var maxFlatResults = options.Async ? 0 : MaxFlatResults;
        Action onProgress = () => { };

        ComponentValue[] OnStart()
        {
            onProgress();
            subtask.State = Subtask.SubtaskState.Started;
            return LiftFlatValues(cx.ReverseDirection(), maxFlatParams, flatArguments, type.ParameterTypes);
        }

        void OnResolve(ComponentValue[]? result)
        {
            onProgress();
            if (result is null)
            {
                subtask.Resolve(subtask.State == Subtask.SubtaskState.Starting ? Subtask.SubtaskState.CancelledBeforeStarted : Subtask.SubtaskState.CancelledBeforeReturned, []);
            }
            else
            {
                var flatResults = LowerFlatValues(cx, maxFlatResults, result, type.ResultTypes, flatArguments);
                subtask.Resolve(Subtask.SubtaskState.Returned, flatResults);
            }
        }

        subtask.OnCancel = callee.Invoke(OnStart, OnResolve);
        if (!options.Async)
        {
            if (!subtask.Resolved())
            {
                thread!.WaitUntil(subtask.Resolved);
            }

            subtask.DeliverResolve();
            return [.. subtask.FlatResults];
        }

        if (subtask.Resolved())
        {
            subtask.DeliverResolve();
            return [WasmValue.FromI32((int)Subtask.SubtaskState.Returned)];
        }

        var subtaskIndex = (uint)instance.Handles.Add(subtask);
        onProgress = () => subtask.SetPendingEvent(() =>
        {
            if (subtask.Resolved())
            {
                subtask.DeliverResolve();
            }

            return (EventCode.Subtask, subtaskIndex, (uint)subtask.State);
        });

        return [WasmValue.FromI32((int)((uint)subtask.State | (subtaskIndex << 4)))];
    }

    // ------------------------------------------------------------------------------------------------------------
    // Host functions
    // ------------------------------------------------------------------------------------------------------------

    public static ComponentFuncInst HostFunction(ComponentStoreState store, ComponentFunctionType type, ComponentHostCallback callback)
    {
        return (onStart, onResolve) =>
        {
            var arguments = onStart();
            var context = new ComponentCallContext(type, store.Store);

            // The asynchronous operations that the callback starts continue on the thread of the store
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(store.SynchronizationContext);
            ComponentValue? result;
            try
            {
                result = callback(context, arguments);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }

            onResolve(ToResults(type, result));
            return () => { };
        };
    }

    public static ComponentFuncInst HostAsyncFunction(ComponentStoreState store, ComponentFunctionType type, ComponentHostAsyncCallback callback)
    {
        return (onStart, onResolve) =>
        {
            var arguments = onStart();
            var cancellation = new CancellationTokenSource();
            var context = new ComponentCallContext(type, store.Store) { CancellationToken = cancellation.Token };
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(store.SynchronizationContext);
            ValueTask<ComponentValue?> pending;
            try
            {
                pending = callback(context, arguments);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }

            if (pending.IsCompleted)
            {
                onResolve(ToResults(type, pending.Result));
                return () => { };
            }

            store.BeginHostOperation();
            var resolved = false;
            _ = pending.AsTask().ContinueWith(
                completed => store.Post(_ =>
                {
                    store.EndHostOperation();
                    if (resolved)
                        return;

                    resolved = true;
                    if (completed.IsCanceled && cancellation.IsCancellationRequested)
                    {
                        onResolve(null);
                        return;
                    }

                    onResolve(ToResults(type, completed.GetAwaiter().GetResult()));
                }, null),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return cancellation.Cancel;
        };
    }

    private static ComponentValue[] ToResults(ComponentFunctionType type, ComponentValue? result)
    {
        if (type.Result is null)
            return [];

        return result is { } value ? [value] : throw new InvalidOperationException("The host function did not return a value of the type " + type.Result);
    }

    /// <summary>Calls a lifted function from the host, and runs the store until the call returns.</summary>
    public static ComponentValue? Call(ComponentStoreState store, ComponentFunctionValue function, ComponentValue[] arguments)
    {
        if (arguments.Length != function.Type.Parameters.Length)
            throw new ArgumentException("The function expects " + function.Type.Parameters.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " arguments", nameof(arguments));

        store.ThrowIfLockedDown();
        ComponentValue[]? result = null;
        var resolved = false;
        var previous = ComponentThread.Current;
        ComponentThread.Current = null;
        try
        {
            function.Invoke(() => arguments, value =>
            {
                result = value;
                resolved = true;
            });

            if (!resolved)
            {
                store.RunUntil(() => resolved, instance: null);
            }

            // The host operations that completed during the call (such as the copies of the outputs) and the calls parked
            // under it run before it returns
            store.RunParked();
            store.RunPosted();
        }
        catch
        {
            // A call that fails leaves the instances in an unknown state: they cannot be called anymore
            store.LockDown();
            throw;
        }
        finally
        {
            ComponentThread.Current = previous;
        }

        if (result is null)
            throw new OperationCanceledException("The call was cancelled");

        return result.Length == 0 ? null : result[0];
    }

    // ------------------------------------------------------------------------------------------------------------
    // Resources
    // ------------------------------------------------------------------------------------------------------------

    private static ComponentThread CurrentThread() => ComponentThread.Current ?? throw ComponentTrap.Create("cannot call a built-in outside of a call of the component");

    private static ComponentInstanceState CurrentInstance(ComponentInstanceState expected)
    {
        var instance = CurrentThread().Task.Instance;
        ComponentTrap.ThrowIf(instance != expected, "cannot call a built-in of another instance");
        return instance;
    }

    private static void CheckMayLeave(ComponentInstanceState instance) => ComponentTrap.ThrowIf(!instance.MayLeave, "cannot leave component instance");

    public static WasmValue ResourceNew(ComponentInstanceState instance, ComponentResourceType type, WasmValue representation)
    {
        CheckMayLeave(CurrentInstance(instance));
        var resource = new ComponentResource(type, type.Is64 ? representation.I64 : (uint)representation.I32);
        return WasmValue.FromI32(instance.Handles.Add(new ResourceHandle(resource, own: true, borrowScope: null)));
    }

    public static void ResourceDrop(ComponentInstanceState instance, ComponentResourceType type, uint index)
    {
        CheckMayLeave(CurrentInstance(instance));
        var item = instance.Handles.Remove(index);
        if (item is not ResourceHandle handle)
            throw ComponentTrap.Create("handle index is not a resource");

        ComponentTrap.ThrowIf(!ReferenceEquals(handle.Type, type), "handle index used with the wrong type");
        ComponentTrap.ThrowIf(handle.LendCount != 0, "cannot drop a resource while it is borrowed");
        if (handle.Own)
        {
            RunDestructor(instance, handle.Resource);
        }
        else if (handle.BorrowScope is { } scope)
        {
            scope.BorrowCount--;
        }
    }

    /// <summary>Runs the destructor of a resource whose last owned handle is dropped.</summary>
    public static void RunDestructor(ComponentInstanceState caller, ComponentResource resource)
    {
        var type = resource.Type;
        resource.Dropped = true;
        if (type.HostDestructor is { } hostDestructor)
        {
            hostDestructor(resource);
            return;
        }

        if (type.Destructor is not { } destructor || type.Implementation is not { } implementation)
            return;

        var destructorType = new ComponentFunctionType([], null);
        WasmValue[] arguments = [type.Is64 ? WasmValue.FromI64(resource.GuestRepresentation) : WasmValue.FromI32((int)resource.GuestRepresentation)];
        var callee = Lift(new DestructorFunction(destructor, arguments), destructorType, CanonicalOptions.Default, implementation);
        var function = new ComponentFunctionValue(destructorType, callee);
        if (ComponentThread.Current is null)
        {
            Call(caller.Store, function, []);
        }
        else
        {
            _ = Lower(function, destructorType, CanonicalOptions.Default, ComponentThread.Current.Task.Instance, []);
        }
    }

    public static WasmValue ResourceRep(ComponentInstanceState instance, ComponentResourceType type, uint index)
    {
        _ = CurrentInstance(instance);
        var item = instance.Handles.Get(index);
        if (item is not ResourceHandle handle)
            throw ComponentTrap.Create("handle index is not a resource");

        ComponentTrap.ThrowIf(!ReferenceEquals(handle.Type, type), "handle index used with the wrong type");
        var representation = handle.Resource.GuestRepresentation;
        return type.Is64 ? WasmValue.FromI64(representation) : WasmValue.FromI32((int)representation);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Tasks, contexts, backpressure
    // ------------------------------------------------------------------------------------------------------------

    public static WasmValue ContextGet(ComponentInstanceState instance, CoreValKind type, int index)
    {
        var thread = CurrentThread();
        _ = CurrentInstance(instance);
        var value = thread.Storage[index];
        return type == CoreValKind.I64 ? WasmValue.FromI64(value) : WasmValue.FromI32((int)value);
    }

    public static void ContextSet(ComponentInstanceState instance, CoreValKind type, int index, WasmValue value)
    {
        var thread = CurrentThread();
        _ = CurrentInstance(instance);
        thread.Storage[index] = type == CoreValKind.I64 ? value.I64 : (uint)value.I32;
    }

    public static void BackpressureIncrement(ComponentInstanceState instance)
    {
        _ = CurrentInstance(instance);
        instance.Backpressure++;
        ComponentTrap.ThrowIf(instance.Backpressure >= 1 << 16, "backpressure counter overflow");
    }

    public static void BackpressureDecrement(ComponentInstanceState instance)
    {
        _ = CurrentInstance(instance);
        instance.Backpressure--;
        ComponentTrap.ThrowIf(instance.Backpressure < 0, "backpressure counter underflow");
    }

    public static void TaskReturn(ComponentInstanceState instance, ComponentValueType? resultType, CanonicalOptions options, ReadOnlySpan<WasmValue> arguments)
    {
        var task = CurrentThread().Task;
        _ = CurrentInstance(instance);
        CheckMayLeave(instance);
        ComponentTrap.ThrowIf(!task.Options.Async, "invalid task.return from a synchronous task");
        ComponentTrap.ThrowIf(!ComponentValueType.AreEqual(resultType, task.Type.Result), "invalid task.return signature");
        // definitions.py compares the memories with LiftOptions.equal, but the test suite (cross-abi-calls.wast) calls a
        // task.return without memory from a task lifted with a memory: only a different memory is rejected, as wasmtime does
        ComponentTrap.ThrowIf(options.StringEncoding != task.Options.StringEncoding || (options.Memory is not null && !ReferenceEquals(options.Memory, task.Options.Memory)), "invalid task.return options");
        var cx = new LiftLowerContext(LiftLowerDirection.Lift, options, instance, task);
        var result = LiftFlatValues(cx, MaxFlatParams, new CoreValueIterator(arguments.ToArray()), task.Type.ResultTypes);
        task.Return(result);
    }

    public static void TaskCancel(ComponentInstanceState instance)
    {
        var task = CurrentThread().Task;
        _ = CurrentInstance(instance);
        CheckMayLeave(instance);
        ComponentTrap.ThrowIf(!task.Options.Async, "invalid task.cancel from a synchronous task");
        task.Cancel();
    }

    // ------------------------------------------------------------------------------------------------------------
    // Waitables
    // ------------------------------------------------------------------------------------------------------------

    public static WasmValue WaitableSetNew(ComponentInstanceState instance)
    {
        CheckMayLeave(CurrentInstance(instance));
        return WasmValue.FromI32(instance.Handles.Add(new WaitableSet()));
    }

    public static WasmValue WaitableSetWait(ComponentInstanceState instance, WasmMemory memory, uint setIndex, uint pointer)
    {
        CheckMayLeave(CurrentInstance(instance));
        var set = instance.Handles.Get(setIndex) as WaitableSet ?? throw ComponentTrap.Create("handle is not a waitable set");
        return UnpackEvent(memory, pointer, set.Wait());
    }

    public static WasmValue WaitableSetPoll(ComponentInstanceState instance, WasmMemory memory, uint setIndex, uint pointer)
    {
        CheckMayLeave(CurrentInstance(instance));
        var set = instance.Handles.Get(setIndex) as WaitableSet ?? throw ComponentTrap.Create("handle is not a waitable set");
        return UnpackEvent(memory, pointer, set.Poll());
    }

    private static WasmValue UnpackEvent(WasmMemory memory, uint pointer, (EventCode Code, uint Index, uint Payload) @event)
    {
        var bytes = memory.GetSpan();
        ComponentTrap.ThrowIf(pointer % 4 != 0, "unaligned pointer");
        ComponentTrap.ThrowIf((ulong)pointer + 8 > (ulong)bytes.Length, "pointer out of bounds of memory");
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes[(int)pointer..], @event.Index);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes[((int)pointer + 4)..], @event.Payload);
        return WasmValue.FromI32((int)@event.Code);
    }

    public static void WaitableSetDrop(ComponentInstanceState instance, uint index)
    {
        CheckMayLeave(CurrentInstance(instance));
        var set = instance.Handles.Remove(index) as WaitableSet ?? throw ComponentTrap.Create("handle is not a waitable set");
        set.Drop();
    }

    public static void WaitableJoin(ComponentInstanceState instance, uint waitableIndex, uint setIndex)
    {
        CheckMayLeave(CurrentInstance(instance));
        var waitable = instance.Handles.Get(waitableIndex) as Waitable ?? throw ComponentTrap.Create("handle is not a waitable");
        ComponentTrap.ThrowIf(waitable.HasSyncWaiter, "cannot join a waitable with a synchronous waiter");
        if (setIndex == 0)
        {
            waitable.Join(null);
        }
        else
        {
            waitable.Join(instance.Handles.Get(setIndex) as WaitableSet ?? throw ComponentTrap.Create("handle is not a waitable set"));
        }
    }

    public static WasmValue SubtaskCancel(ComponentInstanceState instance, bool isAsync, uint index)
    {
        var thread = CurrentThread();
        CheckMayLeave(CurrentInstance(instance));
        var subtask = instance.Handles.Get(index) as Subtask ?? throw ComponentTrap.Create("handle is not a subtask");
        ComponentTrap.ThrowIf(subtask.ResolveDelivered(), "cannot cancel a subtask that already resolved");
        ComponentTrap.ThrowIf(subtask.CancellationRequested, "cannot cancel a subtask twice");
        ComponentTrap.ThrowIf(subtask.InWaitableSet, "cannot cancel a subtask in a waitable set");
        if (!subtask.Resolved())
        {
            subtask.CancellationRequested = true;
            subtask.HasSyncWaiter = true;
            subtask.OnCancel!();
            if (!isAsync && !subtask.Resolved())
            {
                thread.WaitUntil(subtask.Resolved);
            }

            subtask.HasSyncWaiter = false;
            if (!subtask.Resolved())
                return WasmValue.FromI32(unchecked((int)Blocked));
        }

        _ = subtask.GetPendingEvent();
        return WasmValue.FromI32((int)subtask.State);
    }

    public static void SubtaskDrop(ComponentInstanceState instance, uint index)
    {
        CheckMayLeave(CurrentInstance(instance));
        var subtask = instance.Handles.Remove(index) as Subtask ?? throw ComponentTrap.Create("handle is not a subtask");
        subtask.Drop();
    }

    // ------------------------------------------------------------------------------------------------------------
    // Streams and futures
    // ------------------------------------------------------------------------------------------------------------

    public static WasmValue StreamOrFutureNew(ComponentInstanceState instance, bool stream, ComponentValueType? elementType)
    {
        CheckMayLeave(CurrentInstance(instance));
        StreamOrFutureEnd reader;
        StreamOrFutureEnd writer;
        if (stream)
        {
            (reader, writer) = ComponentStreams.NewStream(elementType, instance);
        }
        else
        {
            (reader, writer) = ComponentStreams.NewFuture(elementType, instance);
        }

        reader.Index = (uint)instance.Handles.Add(reader);
        writer.Index = (uint)instance.Handles.Add(writer);
        return WasmValue.FromI64(reader.Index | ((long)writer.Index << 32));
    }

    public static WasmValue Copy(ComponentInstanceState instance, bool stream, bool read, ComponentValueType? elementType, CanonicalOptions options, uint index, ulong pointer, ulong length)
    {
        var thread = CurrentThread();
        CheckMayLeave(CurrentInstance(instance));
        var item = instance.Handles.Get(index);
        var end = item as StreamOrFutureEnd;
        ComponentTrap.ThrowIf(end is null || end.IsReadable != read || (end is StreamEnd) != stream, "handle is not the expected end of a " + (stream ? "stream" : "future"));
        ComponentTrap.ThrowIf(!ComponentValueType.AreEqual(end!.ElementType, elementType), "the element type does not match");
        ComponentTrap.ThrowIf(end.State != StreamOrFutureEnd.EndState.Idle, end.State == StreamOrFutureEnd.EndState.Done ? "cannot copy to an end that is done" : "cannot copy to an end that is busy");
        ComponentTrap.ThrowIf(end.InWaitableSet && !options.Async, "cannot synchronously copy to an end in a waitable set");
        var cx = new LiftLowerContext(read ? LiftLowerDirection.Lower : LiftLowerDirection.Lift, options, instance);
        IComponentBuffer buffer = read ? new WritableGuestBuffer(elementType, cx, pointer, length) : new ReadableGuestBuffer(elementType, cx, pointer, length);
        end.Copy(buffer);
        if (!end.HasPendingEvent())
        {
            if (!options.Async)
            {
                end.WaitForPendingEvent();
            }
            else
            {
                return WasmValue.FromI32(unchecked((int)Blocked));
            }
        }

        var (_, _, payload) = end.GetPendingEvent();
        _ = thread;
        return WasmValue.FromI32((int)payload);
    }

    public static WasmValue CancelCopy(ComponentInstanceState instance, bool stream, bool read, ComponentValueType? elementType, bool isAsync, uint index)
    {
        CheckMayLeave(CurrentInstance(instance));
        var end = instance.Handles.Get(index) as StreamOrFutureEnd;
        ComponentTrap.ThrowIf(end is null || end.IsReadable != read || (end is StreamEnd) != stream, "handle is not the expected end of a " + (stream ? "stream" : "future"));
        ComponentTrap.ThrowIf(!ComponentValueType.AreEqual(end!.ElementType, elementType), "the element type does not match");
        ComponentTrap.ThrowIf(end.State != StreamOrFutureEnd.EndState.Copying, "cannot cancel a copy that is not pending");
        ComponentTrap.ThrowIf(end.HasSyncWaiter, "cannot cancel a copy with a synchronous waiter");
        ComponentTrap.ThrowIf(end.InWaitableSet && !isAsync, "cannot synchronously cancel a copy of an end in a waitable set");
        end.Cancel();
        if (!end.HasPendingEvent())
        {
            if (!isAsync)
            {
                end.WaitForPendingEvent();
            }
            else
            {
                return WasmValue.FromI32(unchecked((int)Blocked));
            }
        }

        var (_, _, payload) = end.GetPendingEvent();
        return WasmValue.FromI32((int)payload);
    }

    public static void DropEnd(ComponentInstanceState instance, bool stream, bool readable, ComponentValueType? elementType, uint index)
    {
        CheckMayLeave(CurrentInstance(instance));
        var end = instance.Handles.Remove(index) as StreamOrFutureEnd;
        ComponentTrap.ThrowIf(end is null || end.IsReadable != readable || (end is StreamEnd) != stream, "handle is not the expected end of a " + (stream ? "stream" : "future"));
        ComponentTrap.ThrowIf(!ComponentValueType.AreEqual(end!.ElementType, elementType), "the element type does not match");
        ComponentTrap.ThrowIf(end.CopyingOrCancelling, "cannot drop an end with a pending copy");
        ComponentTrap.ThrowIf(end is WritableFutureEnd && end.State != StreamOrFutureEnd.EndState.Done, "cannot drop the writable end of a future before writing it");
        end.Drop();
    }

    public static void Forward(ComponentInstanceState instance, bool stream, ComponentValueType? elementType, uint readableIndex, uint writableIndex)
    {
        CheckMayLeave(CurrentInstance(instance));
        var readable = instance.Handles.Remove(readableIndex) as StreamOrFutureEnd;
        ComponentTrap.ThrowIf(readable is null || !readable.IsReadable || (readable is StreamEnd) != stream || readable.State != StreamOrFutureEnd.EndState.Idle || readable.InWaitableSet, "invalid readable end");
        var writable = instance.Handles.Remove(writableIndex) as StreamOrFutureEnd;
        ComponentTrap.ThrowIf(writable is null || writable.IsReadable || (writable is StreamEnd) != stream || writable.State != StreamOrFutureEnd.EndState.Idle || writable.InWaitableSet, "invalid writable end");
        ComponentTrap.ThrowIf(!ComponentValueType.AreEqual(readable!.ElementType, elementType) || !ComponentValueType.AreEqual(writable!.ElementType, elementType), "the element type does not match");
        StreamOrFutureEnd.Forward(readable, writable!);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Threads
    // ------------------------------------------------------------------------------------------------------------

    public static WasmValue ThreadIndex(ComponentInstanceState instance)
    {
        var thread = CurrentThread();
        CheckMayLeave(CurrentInstance(instance));
        return WasmValue.FromI32(thread.Index ?? 0);
    }

    public static WasmValue ThreadNewIndirect(ComponentInstanceState instance, WasmTable table, CoreFunctionTypeExpr functionType, uint functionIndex, WasmValue context)
    {
        var task = CurrentThread().Task;
        CheckMayLeave(CurrentInstance(instance));
        var element = table.Get(functionIndex);
        if (element.Reference is not WasmFunction function)
            throw ComponentTrap.Create("uninitialized element");

        var parameters = function.Type.Parameters;
        ComponentTrap.ThrowIf(parameters.Length != 1 || function.Type.Results.Length != 0 || CanonicalAbi.GetKind(parameters[0]) != functionType.Parameters[0], "indirect call type mismatch");
        ComponentThread? newThread = null;
        newThread = ComponentThread.CreateContinuation(task, () =>
        {
            CallAndTrapOnThrow(function, [context]);
            instance.Threads.Remove(newThread!.Index!.Value);
        });

        newThread.Index = instance.Threads.Add(newThread);
        return WasmValue.FromI32(newThread.Index.Value);
    }

    private static ComponentThread GetOtherThread(ComponentInstanceState instance, uint index, bool mustBeSuspended)
    {
        var other = instance.Threads.Get(index);
        ComponentTrap.ThrowIf(mustBeSuspended && !other.Suspended, "thread is not suspended");
        ComponentTrap.ThrowIf(other == ComponentThread.Current, "cannot switch to the current thread");
        return other;
    }

    public static void ThreadResumeLater(ComponentInstanceState instance, uint index)
    {
        CheckMayLeave(CurrentInstance(instance));
        GetOtherThread(instance, index, mustBeSuspended: true).ResumeLater();
    }

    public static WasmValue ThreadSwitch(ComponentInstanceState instance, CanonKind kind, uint index)
    {
        var thread = CurrentThread();
        CheckMayLeave(CurrentInstance(instance));
        switch (kind)
        {
            case CanonKind.ThreadSuspend:
                thread.Suspend();
                break;
            case CanonKind.ThreadYield:
                thread.Yield();
                break;
            case CanonKind.ThreadSuspendThenResume:
                thread.SuspendThenResume(GetOtherThread(instance, index, mustBeSuspended: true));
                break;
            case CanonKind.ThreadYieldThenResume:
                thread.YieldThenResume(GetOtherThread(instance, index, mustBeSuspended: true));
                break;
            case CanonKind.ThreadSuspendThenPromote:
                ComponentTrap.ThrowIf(index == thread.Index, "cannot switch to the current thread");
                thread.SuspendThenPromote(instance.Threads.Get(index));
                break;
            default:
                ComponentTrap.ThrowIf(index == thread.Index, "cannot switch to the current thread");
                thread.YieldThenPromote(instance.Threads.Get(index));
                break;
        }

        return WasmValue.FromI32(thread.Task.HasPendingCancel && thread.Cancellable ? 1 : 0);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Error contexts
    // ------------------------------------------------------------------------------------------------------------

    public static WasmValue ErrorContextNew(ComponentInstanceState instance, CanonicalOptions options, ulong pointer, ulong taggedCodeUnits)
    {
        CheckMayLeave(CurrentInstance(instance));
        var cx = new LiftLowerContext(LiftLowerDirection.Lift, options, instance);
        var message = LoadStringFromRange(cx, pointer, taggedCodeUnits).AsString();
        return WasmValue.FromI32(instance.Handles.Add(new ErrorContextHandle(new ComponentErrorContext(message))));
    }

    public static void ErrorContextDebugMessage(ComponentInstanceState instance, CanonicalOptions options, uint index, ulong pointer)
    {
        CheckMayLeave(CurrentInstance(instance));
        var handle = instance.Handles.Get(index) as ErrorContextHandle ?? throw ComponentTrap.Create("handle is not an error-context");
        var cx = new LiftLowerContext(LiftLowerDirection.Lower, options, instance);
        ComponentTrap.ThrowIf(pointer != AlignTo(pointer, Alignment(cx, ComponentValueType.String)), "unaligned pointer");
        ComponentTrap.ThrowIf(pointer > cx.MemoryLength || pointer + ElemSize(cx, ComponentValueType.String) > cx.MemoryLength, "pointer out of bounds of memory");
        Store(cx, ComponentValue.FromString(handle.ErrorContext.DebugMessage), ComponentValueType.String, pointer);
    }

    public static void ErrorContextDrop(ComponentInstanceState instance, uint index)
    {
        CheckMayLeave(CurrentInstance(instance));
        _ = instance.Handles.Remove(index) as ErrorContextHandle ?? throw ComponentTrap.Create("handle is not an error-context");
    }

    /// <summary>A destructor bound to the representation of a resource: a function without parameters that calls the destructor.</summary>
    private sealed class DestructorFunction(WasmFunction destructor, WasmValue[] arguments) : WasmFunction
    {
        public override WasmFunctionType Type { get; } = new([], []);

        public override void Invoke(ReadOnlySpan<WasmValue> args, Span<WasmValue> results) => destructor.Invoke(arguments, []);
    }
}
