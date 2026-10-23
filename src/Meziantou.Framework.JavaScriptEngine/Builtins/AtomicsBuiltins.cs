using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The Atomics object (https://tc39.es/ecma262/#sec-atomics-object). On a SharedArrayBuffer, the operations are atomic
/// (Interlocked, Volatile); on an ArrayBuffer, they are plain reads and writes.
/// </summary>
internal static class AtomicsBuiltins
{
    private static readonly PropertyKey AsyncKey = new("async");
    private static readonly PropertyKey ValueKey = new("value");

    private enum Operation
    {
        Add,
        And,
        Exchange,
        Or,
        Sub,
        Xor,
    }

    private static readonly BuiltinMethodTable AtomicsMethods =
    [
        new("add", 3, (_, _, arguments, _) => ReadModifyWrite(arguments, Operation.Add)),
        new("and", 3, (_, _, arguments, _) => ReadModifyWrite(arguments, Operation.And)),
        new("compareExchange", 4, CompareExchange),
        new("exchange", 3, (_, _, arguments, _) => ReadModifyWrite(arguments, Operation.Exchange)),
        new("isLockFree", 1, IsLockFree),
        new("load", 2, Load),
        new("notify", 3, Notify),
        new("or", 3, (_, _, arguments, _) => ReadModifyWrite(arguments, Operation.Or)),
        new("pause", 0, Pause),
        new("store", 3, Store),
        new("sub", 3, (_, _, arguments, _) => ReadModifyWrite(arguments, Operation.Sub)),
        new("wait", 4, (function, _, arguments, _) => DoWait(function.Realm, arguments, isAsync: false)),
        new("waitAsync", 4, (function, _, arguments, _) => DoWait(function.Realm, arguments, isAsync: true)),
        new("xor", 3, (_, _, arguments, _) => ReadModifyWrite(arguments, Operation.Xor)),
    ];

    public static JsObject Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var atomics = new JsObject(intrinsics.ObjectPrototype);
        realm.DefineMethods(atomics, AtomicsMethods);
        atomics.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Atomics", PropertyFlags.Configurable);
        intrinsics.AddGlobal("Atomics", atomics);
        return atomics;
    }

    private static JsValue Arg(ReadOnlySpan<JsValue> arguments, int index) => arguments.Length > index ? arguments[index] : default;

    // ------------------------------------------------------------------------------------------------------------------
    // Validation
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>ValidateIntegerTypedArray(typedArray, waitable, accessMode): the typed array and its length; <paramref name="write"/> is the write access mode.</summary>
    private static JsTypedArray ValidateIntegerTypedArray(JsValue value, bool waitable, out int length, bool write = false)
    {
        var typedArray = TypedArrayBuiltins.ValidateTypedArray(value, out length, write);
        var type = typedArray.ElementType;
        if (waitable)
        {
            if (type is not (TypedArrayElementType.Int32 or TypedArrayElementType.BigInt64))
                return ThrowHelper.ThrowTypeError<JsTypedArray>("Atomics.wait and Atomics.notify require an Int32Array or a BigInt64Array");
        }
        else if (!BinaryDataOperations.IsUnclampedIntegerElementType(type) && !BinaryDataOperations.IsBigIntElementType(type))
        {
            return ThrowHelper.ThrowTypeError<JsTypedArray>("Atomics operations require an integer TypedArray");
        }

        return typedArray;
    }

    /// <summary>ValidateAtomicAccess(taRecord, requestIndex): the byte index in the buffer.</summary>
    private static int ValidateAtomicAccess(JsTypedArray typedArray, int length, JsValue requestIndex)
    {
        var accessIndex = JsOperations.ToIndex(requestIndex);
        if (accessIndex >= length)
            return ThrowHelper.ThrowRangeError<int>("Invalid atomic access index");

        return ((int)accessIndex * typedArray.ElementSize) + typedArray.ByteOffset;
    }

    /// <summary>ValidateAtomicAccessOnIntegerTypedArray(typedArray, requestIndex, false, accessMode): <paramref name="write"/> is the write access mode.</summary>
    private static int ValidateAtomicAccessOnIntegerTypedArray(JsValue value, JsValue requestIndex, out JsTypedArray typedArray, bool write = false)
    {
        typedArray = ValidateIntegerTypedArray(value, waitable: false, out var length, write);
        return ValidateAtomicAccess(typedArray, length, requestIndex);
    }

    /// <summary>RevalidateAtomicAccess(typedArray, byteIndexInBuffer)</summary>
    private static void RevalidateAtomicAccess(JsTypedArray typedArray, int byteIndexInBuffer)
    {
        if (typedArray.IsOutOfBounds)
        {
            ThrowHelper.ThrowTypeError("Cannot perform the Atomics operation on a detached or out-of-bounds TypedArray");
        }

        if (byteIndexInBuffer >= typedArray.ViewedArrayBuffer.ByteLength)
        {
            ThrowHelper.ThrowRangeError("Invalid atomic access index");
        }
    }

    /// <summary>The value of an operand: ToBigInt for a BigInt typed array, the number ToIntegerOrInfinity otherwise.</summary>
    private static JsValue ToOperand(JsTypedArray typedArray, JsValue value)
    {
        if (typedArray.IsBigInt)
            return BinaryDataOperations.ToBigIntValue(value);

        return JsOperations.ToIntegerOrInfinity(value);
    }

    /// <summary>The bits of a converted value (NumericToRawBytes, as an integer in the byte order of the agent).</summary>
    private static ulong ToBits(JsTypedArray typedArray, JsValue value)
    {
        if (typedArray.IsBigInt)
            return BinaryDataOperations.BigIntToUInt64(value.AsBigInt());

        return unchecked((uint)JsOperations.DoubleToInt32(value.NumberValue));
    }

    /// <summary>RawBytesToNumeric for the bits of an element.</summary>
    private static JsValue FromBits(TypedArrayElementType type, ulong bits)
    {
        return type switch
        {
            TypedArrayElementType.Int8 => unchecked((sbyte)bits),
            TypedArrayElementType.Uint8 => unchecked((byte)bits),
            TypedArrayElementType.Int16 => unchecked((short)bits),
            TypedArrayElementType.Uint16 => unchecked((ushort)bits),
            TypedArrayElementType.Int32 => unchecked((int)bits),
            TypedArrayElementType.Uint32 => unchecked((uint)bits),
            TypedArrayElementType.BigInt64 => JsValue.FromBigInt(unchecked((long)bits)),
            _ => JsValue.FromBigInt(bits),
        };
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Memory
    // ------------------------------------------------------------------------------------------------------------------

    private static ulong Apply(Operation operation, ulong current, ulong operand)
    {
        return operation switch
        {
            Operation.Add => unchecked(current + operand),
            Operation.And => current & operand,
            Operation.Exchange => operand,
            Operation.Or => current | operand,
            Operation.Sub => unchecked(current - operand),
            _ => current ^ operand,
        };
    }

    /// <summary>Reads an element in the byte order of the agent (a plain read).</summary>
    private static ulong ReadBits(Span<byte> span, int size)
    {
        return size switch
        {
            1 => span[0],
            2 => BitConverter.IsLittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(span) : BinaryPrimitives.ReadUInt16BigEndian(span),
            4 => BitConverter.IsLittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(span) : BinaryPrimitives.ReadUInt32BigEndian(span),
            _ => BitConverter.IsLittleEndian ? BinaryPrimitives.ReadUInt64LittleEndian(span) : BinaryPrimitives.ReadUInt64BigEndian(span),
        };
    }

    /// <summary>Writes an element in the byte order of the agent (a plain write).</summary>
    private static void WriteBits(Span<byte> span, int size, ulong bits)
    {
        switch (size)
        {
            case 1:
                span[0] = unchecked((byte)bits);
                break;
            case 2:
                if (BitConverter.IsLittleEndian)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(span, unchecked((ushort)bits));
                }
                else
                {
                    BinaryPrimitives.WriteUInt16BigEndian(span, unchecked((ushort)bits));
                }

                break;
            case 4:
                if (BitConverter.IsLittleEndian)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(span, unchecked((uint)bits));
                }
                else
                {
                    BinaryPrimitives.WriteUInt32BigEndian(span, unchecked((uint)bits));
                }

                break;
            default:
                if (BitConverter.IsLittleEndian)
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(span, bits);
                }
                else
                {
                    BinaryPrimitives.WriteUInt64BigEndian(span, bits);
                }

                break;
        }
    }

    /// <summary>GetModifySetValueInBuffer(arrayBuffer, byteIndex, type, value, op): returns the bits of the previous value.</summary>
    private static ulong GetModifySetValueInBuffer(JsArrayBuffer buffer, int byteIndex, int size, ulong operand, Operation operation)
    {
        if (buffer.SharedBlock is not { } block)
        {
            var span = buffer.GetSpan()[byteIndex..];
            var current = ReadBits(span, size);
            WriteBits(span, size, Apply(operation, current, operand));
            return current;
        }

        ref var location = ref block.GetReference(byteIndex);
        switch (size)
        {
            case 1:
                while (true)
                {
                    var current = Volatile.Read(ref location);
                    var value = unchecked((byte)Apply(operation, current, operand));
                    if (Interlocked.CompareExchange(ref location, value, current) == current)
                        return current;
                }

            case 2:
                {
                    ref var location16 = ref unsafe(Unsafe.As<byte, ushort>(ref location));
                    while (true)
                    {
                        var current = Volatile.Read(ref location16);
                        var value = unchecked((ushort)Apply(operation, current, operand));
                        if (Interlocked.CompareExchange(ref location16, value, current) == current)
                            return current;
                    }
                }

            case 4:
                {
                    ref var location32 = ref unsafe(Unsafe.As<byte, uint>(ref location));
                    switch (operation)
                    {
                        case Operation.Add:
                            return unchecked(Interlocked.Add(ref location32, (uint)operand) - (uint)operand);
                        case Operation.Sub:
                            return unchecked(Interlocked.Add(ref location32, 0u - (uint)operand) + (uint)operand);
                        case Operation.And:
                            return Interlocked.And(ref location32, (uint)operand);
                        case Operation.Or:
                            return Interlocked.Or(ref location32, (uint)operand);
                        case Operation.Exchange:
                            return Interlocked.Exchange(ref location32, (uint)operand);
                    }

                    while (true)
                    {
                        var current = Volatile.Read(ref location32);
                        var value = unchecked((uint)Apply(operation, current, operand));
                        if (Interlocked.CompareExchange(ref location32, value, current) == current)
                            return current;
                    }
                }

            default:
                {
                    ref var location64 = ref unsafe(Unsafe.As<byte, ulong>(ref location));
                    switch (operation)
                    {
                        case Operation.Add:
                            return unchecked(Interlocked.Add(ref location64, operand) - operand);
                        case Operation.Sub:
                            return unchecked(Interlocked.Add(ref location64, 0UL - operand) + operand);
                        case Operation.And:
                            return Interlocked.And(ref location64, operand);
                        case Operation.Or:
                            return Interlocked.Or(ref location64, operand);
                        case Operation.Exchange:
                            return Interlocked.Exchange(ref location64, operand);
                    }

                    while (true)
                    {
                        var current = Volatile.Read(ref location64);
                        var value = Apply(operation, current, operand);
                        if (Interlocked.CompareExchange(ref location64, value, current) == current)
                            return current;
                    }
                }
        }
    }

    /// <summary>The compare-exchange of an element: returns the bits of the previous value.</summary>
    private static ulong CompareExchangeInBuffer(JsArrayBuffer buffer, int byteIndex, int size, ulong expected, ulong replacement)
    {
        var mask = size == 8 ? ulong.MaxValue : (1UL << (size * 8)) - 1;
        expected &= mask;
        replacement &= mask;
        if (buffer.SharedBlock is not { } block)
        {
            var span = buffer.GetSpan()[byteIndex..];
            var current = ReadBits(span, size);
            if (current == expected)
            {
                WriteBits(span, size, replacement);
            }

            return current;
        }

        ref var location = ref block.GetReference(byteIndex);
        return size switch
        {
            1 => Interlocked.CompareExchange(ref location, (byte)replacement, (byte)expected),
            2 => Interlocked.CompareExchange(ref unsafe(Unsafe.As<byte, ushort>(ref location)), (ushort)replacement, (ushort)expected),
            4 => Interlocked.CompareExchange(ref unsafe(Unsafe.As<byte, uint>(ref location)), (uint)replacement, (uint)expected),
            _ => Interlocked.CompareExchange(ref unsafe(Unsafe.As<byte, ulong>(ref location)), replacement, expected),
        };
    }

    /// <summary>A seq-cst load of an element.</summary>
    private static ulong LoadFromBuffer(JsArrayBuffer buffer, int byteIndex, int size)
    {
        if (buffer.SharedBlock is not { } block)
            return ReadBits(buffer.GetSpan()[byteIndex..], size);

        ref var location = ref block.GetReference(byteIndex);
        return size switch
        {
            1 => Volatile.Read(ref location),
            2 => Volatile.Read(ref unsafe(Unsafe.As<byte, ushort>(ref location))),
            4 => Volatile.Read(ref unsafe(Unsafe.As<byte, uint>(ref location))),
            _ => Volatile.Read(ref unsafe(Unsafe.As<byte, ulong>(ref location))),
        };
    }

    /// <summary>A seq-cst store of an element.</summary>
    private static void StoreInBuffer(JsArrayBuffer buffer, int byteIndex, int size, ulong bits)
    {
        if (buffer.SharedBlock is not { } block)
        {
            WriteBits(buffer.GetSpan()[byteIndex..], size, bits);
            return;
        }

        ref var location = ref block.GetReference(byteIndex);
        switch (size)
        {
            case 1:
                Interlocked.Exchange(ref location, unchecked((byte)bits));
                break;
            case 2:
                Interlocked.Exchange(ref unsafe(Unsafe.As<byte, ushort>(ref location)), unchecked((ushort)bits));
                break;
            case 4:
                Interlocked.Exchange(ref unsafe(Unsafe.As<byte, uint>(ref location)), unchecked((uint)bits));
                break;
            default:
                Interlocked.Exchange(ref unsafe(Unsafe.As<byte, ulong>(ref location)), bits);
                break;
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Functions
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>AtomicReadModifyWrite(typedArray, index, value, op): Atomics.add, and, exchange, or, sub and xor.</summary>
    private static JsValue ReadModifyWrite(ReadOnlySpan<JsValue> arguments, Operation operation)
    {
        var byteIndexInBuffer = ValidateAtomicAccessOnIntegerTypedArray(Arg(arguments, 0), Arg(arguments, 1), out var typedArray, write: true);
        var value = ToOperand(typedArray, Arg(arguments, 2));
        RevalidateAtomicAccess(typedArray, byteIndexInBuffer);
        var previous = GetModifySetValueInBuffer(typedArray.ViewedArrayBuffer, byteIndexInBuffer, typedArray.ElementSize, ToBits(typedArray, value), operation);
        return FromBits(typedArray.ElementType, previous);
    }

    /// <summary>Atomics.compareExchange(typedArray, index, expectedValue, replacementValue)</summary>
    private static JsValue CompareExchange(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var byteIndexInBuffer = ValidateAtomicAccessOnIntegerTypedArray(Arg(arguments, 0), Arg(arguments, 1), out var typedArray, write: true);
        var expected = ToOperand(typedArray, Arg(arguments, 2));
        var replacement = ToOperand(typedArray, Arg(arguments, 3));
        RevalidateAtomicAccess(typedArray, byteIndexInBuffer);
        var previous = CompareExchangeInBuffer(typedArray.ViewedArrayBuffer, byteIndexInBuffer, typedArray.ElementSize, ToBits(typedArray, expected), ToBits(typedArray, replacement));
        return FromBits(typedArray.ElementType, previous);
    }

    /// <summary>Atomics.isLockFree(size)</summary>
    private static JsValue IsLockFree(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var n = JsOperations.ToIntegerOrInfinity(Arg(arguments, 0));
        return n is 1 or 2 or 4 or 8;
    }

    /// <summary>Atomics.load(typedArray, index)</summary>
    private static JsValue Load(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var byteIndexInBuffer = ValidateAtomicAccessOnIntegerTypedArray(Arg(arguments, 0), Arg(arguments, 1), out var typedArray);
        RevalidateAtomicAccess(typedArray, byteIndexInBuffer);
        return FromBits(typedArray.ElementType, LoadFromBuffer(typedArray.ViewedArrayBuffer, byteIndexInBuffer, typedArray.ElementSize));
    }

    /// <summary>Atomics.store(typedArray, index, value)</summary>
    private static JsValue Store(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var byteIndexInBuffer = ValidateAtomicAccessOnIntegerTypedArray(Arg(arguments, 0), Arg(arguments, 1), out var typedArray, write: true);
        var value = ToOperand(typedArray, Arg(arguments, 2));
        RevalidateAtomicAccess(typedArray, byteIndexInBuffer);
        StoreInBuffer(typedArray.ViewedArrayBuffer, byteIndexInBuffer, typedArray.ElementSize, ToBits(typedArray, value));
        return value;
    }

    /// <summary>Atomics.pause([N])</summary>
    private static JsValue Pause(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var n = Arg(arguments, 0);
        var iterations = 1;
        if (!n.IsUndefined)
        {
            if (!n.IsNumber || double.IsInfinity(n.NumberValue) || n.NumberValue != Math.Floor(n.NumberValue))
                return ThrowHelper.ThrowTypeError<JsValue>("The argument of Atomics.pause must be undefined or an integral number");

            iterations = (int)Math.Clamp(n.NumberValue, 1, 64);
        }

        Thread.SpinWait(iterations);
        return JsValue.Undefined;
    }

    /// <summary>Atomics.notify(typedArray, index, count)</summary>
    private static JsValue Notify(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateIntegerTypedArray(Arg(arguments, 0), waitable: true, out var length);
        var byteIndexInBuffer = ValidateAtomicAccess(typedArray, length, Arg(arguments, 1));
        var count = Arg(arguments, 2);
        var c = count.IsUndefined ? double.PositiveInfinity : Math.Max(JsOperations.ToIntegerOrInfinity(count), 0);
        if (typedArray.ViewedArrayBuffer.SharedBlock is not { } block)
            return 0;

        using (AtomicsWaiterList.Enter())
        {
            return AtomicsWaiterList.NotifyWaiters(block, byteIndexInBuffer, c);
        }
    }

    /// <summary>DoWait(mode, typedArray, index, value, timeout): Atomics.wait and Atomics.waitAsync.</summary>
    private static JsValue DoWait(JsRealm realm, ReadOnlySpan<JsValue> arguments, bool isAsync)
    {
        var typedArray = ValidateIntegerTypedArray(Arg(arguments, 0), waitable: true, out var length);
        if (typedArray.ViewedArrayBuffer.SharedBlock is not { } block)
            return ThrowHelper.ThrowTypeError<JsValue>("Atomics.wait requires a shared Int32Array or BigInt64Array");

        var byteIndexInBuffer = ValidateAtomicAccess(typedArray, length, Arg(arguments, 1));
        var isBigInt = typedArray.ElementType == TypedArrayElementType.BigInt64;
        var value = isBigInt ? BigIntOperations.ToBigInt64(Arg(arguments, 2)) : JsOperations.ToInt32(Arg(arguments, 2));
        var q = JsOperations.ToNumber(Arg(arguments, 3));
        var timeout = double.IsNaN(q) || double.IsPositiveInfinity(q) ? double.PositiveInfinity : double.IsNegativeInfinity(q) ? 0 : Math.Max(q, 0);
        var engine = realm.Engine;
        if (!isAsync && !engine.CanBlock)
            return ThrowHelper.ThrowTypeError<JsValue>("Atomics.wait cannot be called in this context");

        var size = typedArray.ElementSize;
        var capability = isAsync ? PromiseCapability.CreateIntrinsic(realm) : null;
        AtomicsWaiter waiter;
        using (AtomicsWaiterList.Enter())
        {
            var current = LoadFromBuffer(typedArray.ViewedArrayBuffer, byteIndexInBuffer, size);
            var currentValue = isBigInt ? unchecked((long)current) : unchecked((int)current);
            if (currentValue != value)
                return isAsync ? CreateWaitAsyncResult(realm, isAsync: false, "not-equal") : (JsValue)"not-equal";

            if (isAsync && timeout == 0)
                return CreateWaitAsyncResult(realm, isAsync: false, "timed-out");

            if (capability is not null)
            {
                engine.IncrementPendingAtomicsWaitAsync();

                // NotifyWaiter of an asynchronous waiter: EnqueueResolveInAgentJob (from any thread)
                waiter = new AtomicsWaiter
                {
                    OnResolved = notified => engine.EnqueueAtomicsWaitAsyncResolution(ResolveWaitAsync, capability, notified ? "ok" : "timed-out", realm),
                };

                AtomicsWaiterList.AddWaiter(block, byteIndexInBuffer, waiter);
                if (!double.IsPositiveInfinity(timeout))
                {
                    // EnqueueAtomicsWaitAsyncTimeoutJob(WL, waiterRecord)
                    engine.EnqueueTimeoutJob(TimeOutWaitAsync, new WaitAsyncTimeout(block, byteIndexInBuffer, waiter), default, realm, ToTimeSpan(timeout));
                }
            }
            else
            {
                waiter = new AtomicsWaiter { Event = new ManualResetEventSlim(initialState: false) };
                AtomicsWaiterList.AddWaiter(block, byteIndexInBuffer, waiter);
            }
        }

        if (capability is not null)
            return CreateWaitAsyncResult(realm, isAsync: true, capability.Promise);

        return SuspendThisAgent(engine, block, byteIndexInBuffer, waiter, timeout);
    }

    /// <summary>The job resolving the promise of Atomics.waitAsync, on the thread of the waiting agent.</summary>
    private static void ResolveWaitAsync(object? state, JsValue argument)
    {
        var capability = (PromiseCapability)state!;
        JsEngine.Current!.DecrementPendingAtomicsWaitAsync();
        capability.Resolve(argument);
    }

    /// <summary>The job of EnqueueAtomicsWaitAsyncTimeoutJob: the waiter times out when it is still in its list.</summary>
    private static void TimeOutWaitAsync(object? state, JsValue argument)
    {
        var (block, byteIndex, waiter) = (WaitAsyncTimeout)state!;
        using (AtomicsWaiterList.Enter())
        {
            if (!waiter.IsInList)
                return;

            AtomicsWaiterList.RemoveWaiter(block, byteIndex, waiter);
            waiter.OnResolved?.Invoke(false);
        }
    }

    private sealed record WaitAsyncTimeout(JsSharedDataBlock Block, int ByteIndex, AtomicsWaiter Waiter);

    /// <summary>SuspendThisAgent(WL, waiterRecord): blocks the thread until the waiter is notified or the timeout elapses.</summary>
    private static string SuspendThisAgent(JsEngine engine, JsSharedDataBlock block, int byteIndex, AtomicsWaiter waiter, double timeout)
    {
        var waitEvent = waiter.Event!;
        var cancellationToken = engine.Options.CancellationToken;
        var canceled = false;
        try
        {
            if (double.IsPositiveInfinity(timeout))
            {
                waitEvent.Wait(cancellationToken);
            }
            else
            {
                // The deadline is measured with the high-resolution clock: the waiter never wakes before the timeout
                var start = System.Diagnostics.Stopwatch.GetTimestamp();
                while (true)
                {
                    var remaining = timeout - System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    if (remaining <= 0 || waitEvent.Wait((int)Math.Min(Math.Ceiling(remaining), int.MaxValue), cancellationToken))
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }

        bool notified;
        using (AtomicsWaiterList.Enter())
        {
            notified = waiter.Notified;
            if (!notified)
            {
                AtomicsWaiterList.RemoveWaiter(block, byteIndex, waiter);
            }
        }

        waitEvent.Dispose();
        if (canceled && !notified)
            throw new JsTerminationException(JsTerminationReason.Canceled, "The execution was canceled");

        return notified ? "ok" : "timed-out";
    }

    private static TimeSpan ToTimeSpan(double milliseconds) => TimeSpan.FromMilliseconds(Math.Min(milliseconds, int.MaxValue - 1));

    private static JsObject CreateWaitAsyncResult(JsRealm realm, bool isAsync, JsValue value)
    {
        var result = new JsObject(realm.Intrinsics.ObjectPrototype);
        result.CreateDataPropertyOrThrow(AsyncKey, isAsync);
        result.CreateDataPropertyOrThrow(ValueKey, value);
        return result;
    }
}
