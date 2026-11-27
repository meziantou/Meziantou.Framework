namespace Meziantou.Framework.WasmEngine.Components.Runtime;

/// <summary>A buffer of a copy between the ends of a stream or a future (canonical ABI "Buffer").</summary>
internal interface IComponentBuffer
{
    const int MaxLength = (1 << 28) - 1;

    ComponentValueType? ElementType { get; }

    int Remain { get; }

    int Progress { get; }
}

/// <summary>A buffer that a copy reads values from.</summary>
internal interface IReadableBuffer : IComponentBuffer
{
    ComponentValue[] Read(int count);
}

/// <summary>A buffer that a copy writes values to.</summary>
internal interface IWritableBuffer : IComponentBuffer
{
    void Write(ComponentValue[] values);
}

/// <summary>A buffer in the memory of a component (canonical ABI "GuestBuffer").</summary>
internal abstract class GuestBuffer : IComponentBuffer
{
    protected GuestBuffer(ComponentValueType? elementType, LiftLowerContext cx, ulong pointer, ulong length)
    {
        ComponentTrap.ThrowIf(length > IComponentBuffer.MaxLength, "buffer too large");
        if (elementType is not null)
        {
            ComponentTrap.ThrowIf(pointer != CanonicalAbi.AlignTo(pointer, CanonicalAbi.Alignment(cx, elementType)), "unaligned pointer");
            ComponentTrap.ThrowIf(pointer > cx.MemoryLength || pointer + (length * CanonicalAbi.ElemSize(cx, elementType)) > cx.MemoryLength, "pointer out of bounds of memory");
        }

        ElementType = elementType;
        Context = cx;
        Pointer = pointer;
        Length = (int)length;
    }

    public ComponentValueType? ElementType { get; }

    protected LiftLowerContext Context { get; }

    protected ulong Pointer { get; set; }

    protected int Length { get; }

    public int Progress { get; protected set; }

    public int Remain => Length - Progress;
}

internal sealed class ReadableGuestBuffer(ComponentValueType? elementType, LiftLowerContext cx, ulong pointer, ulong length) : GuestBuffer(elementType, cx, pointer, length), IReadableBuffer
{
    public ComponentValue[] Read(int count)
    {
        ComponentValue[] values;
        if (ElementType is { } type)
        {
            var list = CanonicalAbi.LoadListFromValidRange(Context, Pointer, (ulong)count, type);
            values = list.ByteArray is { } bytes ? Array.ConvertAll(bytes, ComponentValue.FromU8) : [.. list.AsList()];
            Pointer += (ulong)count * CanonicalAbi.ElemSize(Context, type);
        }
        else
        {
            values = new ComponentValue[count];
        }

        Progress += count;
        return values;
    }
}

internal sealed class WritableGuestBuffer(ComponentValueType? elementType, LiftLowerContext cx, ulong pointer, ulong length) : GuestBuffer(elementType, cx, pointer, length), IWritableBuffer
{
    public void Write(ComponentValue[] values)
    {
        if (ElementType is { } type)
        {
            CanonicalAbi.StoreListIntoValidRange(Context, ComponentValue.FromListArray(values), Pointer, type);
            Pointer += (ulong)values.Length * CanonicalAbi.ElemSize(Context, type);
        }

        Progress += values.Length;
    }
}

internal enum CopyResult
{
    Completed = 0,
    Dropped = 1,
    Cancelled = 2,
}

/// <summary>An end of a stream or a future (canonical ABI "End").</summary>
internal abstract class StreamOrFutureEnd(ComponentValueType? elementType, ComponentInstanceState? owner, EventCode eventCode) : Waitable
{
    public enum EndState
    {
        Idle,
        Copying,
        CancellingCopy,
        Done,
    }

    public ComponentValueType? ElementType { get; } = elementType;

    public EndState State { get; set; }

    public StreamOrFutureEnd? Other { get; set; }

    public IComponentBuffer? Buffer { get; set; }

    public ComponentInstanceState? Owner { get; set; } = owner;

    public uint Index { get; set; }

    public EventCode EventCode { get; } = eventCode;

    /// <summary>Called when the other end is dropped: the host ends stop their operations.</summary>
    public Action? OtherDropped { get; set; }

    public abstract bool IsReadable { get; }

    public abstract void Notify(int progress);

    public void Copy(IComponentBuffer buffer)
    {
        State = EndState.Copying;
        if (Other is null)
            return;

        if (Other.Buffer is null)
        {
            Buffer = buffer;
            return;
        }

        ComponentTrap.ThrowIf(Owner is not null && ReferenceEquals(Owner, Other.Owner) && !CanonicalAbi.IsNoneOrNumber(ElementType), "cannot read from and write to the same stream in the same component");
        var otherBuffer = Other.Buffer;
        if (buffer.Remain > 0 && otherBuffer.Remain > 0)
        {
            var count = Math.Min(buffer.Remain, otherBuffer.Remain);
            if (IsReadable)
            {
                ((IWritableBuffer)buffer).Write(((IReadableBuffer)otherBuffer).Read(count));
            }
            else
            {
                ((IWritableBuffer)otherBuffer).Write(((IReadableBuffer)buffer).Read(count));
            }

            Notify(buffer.Progress);
            Other.Notify(otherBuffer.Progress);
            if (otherBuffer.Remain == 0)
            {
                Other.Buffer = null;
            }
        }
        else if (buffer.Remain > 0 || (IsReadable && otherBuffer.Remain == 0))
        {
            Other.Notify(0);
            Other.Buffer = null;
            Buffer = buffer;
        }
        else
        {
            Notify(0);
        }
    }

    public void Cancel()
    {
        State = EndState.CancellingCopy;
        if (!HasPendingEvent())
        {
            Notify(0);
        }
    }

    public static void Forward(StreamOrFutureEnd source, StreamOrFutureEnd destination)
    {
        if (source.Other == destination || source.Other is null || destination.Other is null)
        {
            source.Drop();
            destination.Drop();
            return;
        }

        var writableEnd = source.Other;
        var readableEnd = destination.Other;
        writableEnd.Other = readableEnd;
        readableEnd.Other = writableEnd;
        if (writableEnd.Buffer is not null && readableEnd.Buffer is not null)
        {
            var smallerEnd = readableEnd.Buffer.Remain < writableEnd.Buffer.Remain ? readableEnd : writableEnd;
            var smallerBuffer = smallerEnd.Buffer!;
            smallerEnd.Buffer = null;
            smallerEnd.Copy(smallerBuffer);
        }
    }

    public override void Drop()
    {
        if (Other is { } other)
        {
            other.Other = null;
            if (other.State != EndState.Done && !other.HasPendingEvent())
            {
                other.Notify(0);
            }

            Other = null;
            other.OtherDropped?.Invoke();
        }

        base.Drop();
    }

    public bool CopyingOrCancelling => State is EndState.Copying or EndState.CancellingCopy;
}

internal abstract class StreamEnd(ComponentValueType? elementType, ComponentInstanceState? owner, EventCode eventCode) : StreamOrFutureEnd(elementType, owner, eventCode)
{
    public override void Notify(int progress)
    {
        SetPendingEvent(() =>
        {
            Buffer = null;
            CopyResult result;
            if (Other is null)
            {
                result = CopyResult.Dropped;
                State = EndState.Done;
            }
            else if (State == EndState.CancellingCopy)
            {
                result = CopyResult.Cancelled;
                State = EndState.Idle;
            }
            else
            {
                result = CopyResult.Completed;
                State = EndState.Idle;
            }

            return (EventCode, Index, (uint)result | ((uint)progress << 4));
        });
    }
}

internal abstract class FutureEnd(ComponentValueType? elementType, ComponentInstanceState? owner, EventCode eventCode) : StreamOrFutureEnd(elementType, owner, eventCode)
{
    public override void Notify(int progress)
    {
        SetPendingEvent(() =>
        {
            CopyResult result;
            if (progress == 1)
            {
                State = EndState.Done;
                result = CopyResult.Completed;
            }
            else if (Other is null)
            {
                Buffer = null;
                State = EndState.Done;
                result = CopyResult.Dropped;
            }
            else
            {
                Buffer = null;
                State = EndState.Idle;
                result = CopyResult.Cancelled;
            }

            return (EventCode, Index, (uint)result);
        });
    }
}

internal sealed class ReadableStreamEnd(ComponentValueType? elementType, ComponentInstanceState? owner) : StreamEnd(elementType, owner, EventCode.StreamRead)
{
    public override bool IsReadable => true;
}

internal sealed class WritableStreamEnd(ComponentValueType? elementType, ComponentInstanceState? owner) : StreamEnd(elementType, owner, EventCode.StreamWrite)
{
    public override bool IsReadable => false;
}

internal sealed class ReadableFutureEnd(ComponentValueType? elementType, ComponentInstanceState? owner) : FutureEnd(elementType, owner, EventCode.FutureRead)
{
    public override bool IsReadable => true;
}

internal sealed class WritableFutureEnd(ComponentValueType? elementType, ComponentInstanceState? owner) : FutureEnd(elementType, owner, EventCode.FutureWrite)
{
    public override bool IsReadable => false;
}

/// <summary>The conversions of the ends of the streams and the futures.</summary>
internal static class ComponentStreams
{
    public static (ReadableStreamEnd Reader, WritableStreamEnd Writer) NewStream(ComponentValueType? elementType, ComponentInstanceState? owner)
    {
        var reader = new ReadableStreamEnd(elementType, owner);
        var writer = new WritableStreamEnd(elementType, owner);
        reader.Other = writer;
        writer.Other = reader;
        return (reader, writer);
    }

    public static (ReadableFutureEnd Reader, WritableFutureEnd Writer) NewFuture(ComponentValueType? elementType, ComponentInstanceState? owner)
    {
        var reader = new ReadableFutureEnd(elementType, owner);
        var writer = new WritableFutureEnd(elementType, owner);
        reader.Other = writer;
        writer.Other = reader;
        return (reader, writer);
    }

    public static ComponentValue LiftStream(LiftLowerContext cx, ulong index, ComponentStreamType type) => ComponentValue.FromStreamEnd(LiftAsyncValue<ReadableStreamEnd>(cx, index, type.ElementType));

    public static ComponentValue LiftFuture(LiftLowerContext cx, ulong index, ComponentFutureType type) => ComponentValue.FromFutureEnd(LiftAsyncValue<ReadableFutureEnd>(cx, index, type.ElementType));

    private static T LiftAsyncValue<T>(LiftLowerContext cx, ulong index, ComponentValueType? elementType)
        where T : StreamOrFutureEnd
    {
        var item = cx.Instance.Handles.Remove((long)index);
        if (item is not T end)
            throw ComponentTrap.Create("handle index is not the readable end of a " + (typeof(T) == typeof(ReadableStreamEnd) ? "stream" : "future"));

        ComponentTrap.ThrowIf(!ComponentValueType.AreEqual(end.ElementType, elementType), "the element type does not match");
        ComponentTrap.ThrowIf(end.State != StreamOrFutureEnd.EndState.Idle, "cannot transfer a busy end");
        ComponentTrap.ThrowIf(end.InWaitableSet, "cannot transfer an end that is in a waitable set");
        end.Owner = null;
        end.Index = 0;
        return end;
    }

    public static ulong LowerStream(LiftLowerContext cx, ComponentValue value, ComponentStreamType type) => LowerAsyncValue<ReadableStreamEnd>(cx, value, ComponentValueKind.Stream, type.ElementType);

    public static ulong LowerFuture(LiftLowerContext cx, ComponentValue value, ComponentFutureType type) => LowerAsyncValue<ReadableFutureEnd>(cx, value, ComponentValueKind.Future, type.ElementType);

    private static ulong LowerAsyncValue<T>(LiftLowerContext cx, ComponentValue value, ComponentValueKind kind, ComponentValueType? elementType)
        where T : StreamOrFutureEnd
    {
        if (value.Kind != kind || value.Object is not T end)
            throw new WasmTypeMismatchException("The value " + value + " is not the readable end of a " + kind.ToString().ToLowerInvariant());

        if (!ComponentValueType.AreEqual(end.ElementType, elementType))
            throw new WasmTypeMismatchException("The element type of the " + kind.ToString().ToLowerInvariant() + " does not match");

        end.Owner = cx.Instance;
        end.Index = (uint)cx.Instance.Handles.Add(end);
        return end.Index;
    }
}
