using Meziantou.Framework.WasmEngine.Components.Runtime;

namespace Meziantou.Framework.WasmEngine.Components;

/// <summary>
/// The readable end of a <c>stream</c> held by the host: the host reads the values that a component writes. Use it on the
/// thread of the store (in the functions of the host, whose continuations run there).
/// </summary>
public sealed class ComponentStreamReader : IDisposable
{
    private readonly ReadableStreamEnd _end;
    private bool _disposed;

    internal ComponentStreamReader(ReadableStreamEnd end)
    {
        _end = end;
    }

    /// <summary>Gets the type of the elements, or null.</summary>
    public ComponentValueType? ElementType => _end.ElementType;

    /// <summary>Gets whether the writer dropped its end: no more values will be read.</summary>
    public bool IsCompleted { get; private set; }

    /// <summary>Reads up to <paramref name="maxCount"/> values: returns when the writer writes values, or drops its end (an empty array).</summary>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
    public async ValueTask<ComponentValue[]> ReadAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxCount);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsCompleted)
            return [];

        var buffer = new HostWritableBuffer(_end.ElementType, maxCount);
        var result = await HostEnds.CopyAsync(_end, buffer, cancellationToken).ConfigureAwait(true);
        if (result == CopyResult.Dropped)
        {
            IsCompleted = true;
        }

        return [.. buffer.Received];
    }

    /// <summary>Reads bytes of a <c>stream&lt;u8&gt;</c>: returns the number of bytes read, 0 when the writer dropped its end.</summary>
    public async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
    {
        if (destination.IsEmpty)
            return 0;

        var values = await ReadAsync(destination.Length, cancellationToken).ConfigureAwait(true);
        for (var i = 0; i < values.Length; i++)
        {
            destination.Span[i] = values[i].AsU8();
        }

        return values.Length;
    }

    /// <summary>Drops the readable end: the writer cannot write anymore.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (!_end.CopyingOrCancelling)
        {
            _end.Drop();
        }
    }
}

/// <summary>The writable end of a <c>stream</c> held by the host: the host writes values that a component reads.</summary>
public sealed class ComponentStreamWriter : IDisposable
{
    private readonly WritableStreamEnd _end;
    private readonly CancellationTokenSource _readerDropped = new();
    private bool _disposed;

    private ComponentStreamWriter(WritableStreamEnd end)
    {
        _end = end;
        end.OtherDropped = () =>
        {
            IsReaderDropped = true;
            if (!_disposed)
            {
                _readerDropped.Cancel();
            }
        };
    }

    /// <summary>Creates a stream: the host writes to the writer, and gives the readable end to a component.</summary>
    /// <param name="elementType">The type of the elements, or null.</param>
    /// <param name="readable">The readable end, to give to a component as a value of type <c>stream</c>.</param>
    public static ComponentStreamWriter Create(ComponentValueType? elementType, out ComponentValue readable)
    {
        var (reader, writer) = ComponentStreams.NewStream(elementType, owner: null);
        readable = ComponentValue.FromStreamEnd(reader);
        return new ComponentStreamWriter(writer);
    }

    /// <summary>Gets whether the reader dropped its end: no more values can be written.</summary>
    public bool IsReaderDropped { get; private set; }

    /// <summary>
    /// Gets a token that is cancelled when the reader drops its end, such as to stop an operation of the host that produces
    /// the values (the reception of a socket). The token is cancelled on the thread of the store.
    /// </summary>
    public CancellationToken ReaderDroppedToken => _readerDropped.Token;

    /// <summary>Writes values: returns the number of values the reader read, which is less than the count when the reader dropped its end.</summary>
    public async ValueTask<int> WriteAsync(ComponentValue[] values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var written = 0;
        while (written < values.Length && !IsReaderDropped)
        {
            var buffer = new HostReadableBuffer(_end.ElementType, values[written..]);
            var result = await HostEnds.CopyAsync(_end, buffer, cancellationToken).ConfigureAwait(true);
            written += buffer.Progress;
            if (result == CopyResult.Dropped)
            {
                IsReaderDropped = true;
            }
            else if (result == CopyResult.Cancelled)
            {
                break;
            }
        }

        return written;
    }

    /// <summary>Writes bytes to a <c>stream&lt;u8&gt;</c>: returns the number of bytes the reader read.</summary>
    public ValueTask<int> WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        var values = new ComponentValue[bytes.Length];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = ComponentValue.FromU8(bytes.Span[i]);
        }

        return WriteAsync(values, cancellationToken);
    }

    /// <summary>Drops the writable end: the reader reads the end of the stream.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (!_end.CopyingOrCancelling)
        {
            _end.Drop();
        }

        _readerDropped.Dispose();
    }
}

/// <summary>The readable end of a <c>future</c> held by the host.</summary>
public sealed class ComponentFutureReader : IDisposable
{
    private readonly ReadableFutureEnd _end;
    private bool _disposed;

    internal ComponentFutureReader(ReadableFutureEnd end)
    {
        _end = end;
    }

    /// <summary>Gets the type of the value, or null.</summary>
    public ComponentValueType? ElementType => _end.ElementType;

    /// <summary>Reads the value: returns it when the writer writes it, or null when the writer drops its end without writing.</summary>
    public async ValueTask<ComponentValue?> ReadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var buffer = new HostWritableBuffer(_end.ElementType, 1);
        var result = await HostEnds.CopyAsync(_end, buffer, cancellationToken).ConfigureAwait(true);
        if (result != CopyResult.Completed)
            return null;

        return buffer.Received.Count > 0 ? buffer.Received[0] : ComponentValue.FromRecord();
    }

    /// <summary>Drops the readable end.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (!_end.CopyingOrCancelling)
        {
            _end.Drop();
        }
    }
}

/// <summary>The writable end of a <c>future</c> held by the host.</summary>
public sealed class ComponentFutureWriter
{
    private readonly WritableFutureEnd _end;
    private bool _written;

    private ComponentFutureWriter(WritableFutureEnd end)
    {
        _end = end;
    }

    /// <summary>Creates a future: the host writes its value, and gives the readable end to a component.</summary>
    /// <param name="elementType">The type of the value, or null.</param>
    /// <param name="readable">The readable end, to give to a component as a value of type <c>future</c>.</param>
    public static ComponentFutureWriter Create(ComponentValueType? elementType, out ComponentValue readable)
    {
        var (reader, writer) = ComponentStreams.NewFuture(elementType, owner: null);
        readable = ComponentValue.FromFutureEnd(reader);
        return new ComponentFutureWriter(writer);
    }

    /// <summary>Writes the value of the future: the reader receives it when it reads the future.</summary>
    /// <param name="value">The value, or null for a future without value type.</param>
    /// <exception cref="InvalidOperationException">The value is already written.</exception>
    public void Write(ComponentValue? value)
    {
        if (_written)
            throw new InvalidOperationException("The value of the future is already written");

        _written = true;
        var buffer = new HostReadableBuffer(_end.ElementType, value is { } some ? [some] : [default]);
        _end.Copy(buffer);
    }
}

/// <summary>The copies of the ends of the host.</summary>
internal static class HostEnds
{
    public static async Task<CopyResult> CopyAsync(StreamOrFutureEnd end, IComponentBuffer buffer, CancellationToken cancellationToken)
    {
        if (end.State == StreamOrFutureEnd.EndState.Done)
            return CopyResult.Dropped;

        end.Copy(buffer);
        if (!end.HasPendingEvent())
        {
            // On the thread of a store, the copy continues on it: the event posts the continuation to the store at once, so
            // that a call that returns after the copy runs it before returning (a continuation that runs asynchronously
            // would reach the store through the thread pool, possibly after the call returned). A second event can replace
            // the first one before the continuation runs.
            TaskCompletionSource completion;
            if (SynchronizationContext.Current is Runtime.ComponentSynchronizationContext store)
            {
                completion = new TaskCompletionSource();
                end.OnPendingEvent = () => store.Post(_ => completion.TrySetResult(), null);
            }
            else
            {
                completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                end.OnPendingEvent = () => completion.TrySetResult();
            }

            await using (cancellationToken.Register(() => end.Cancel()).ConfigureAwait(true))
            {
                if (!end.HasPendingEvent())
                {
                    await completion.Task.ConfigureAwait(true);
                }
            }

            end.OnPendingEvent = null;
        }

        var (_, _, payload) = end.GetPendingEvent();
        return (CopyResult)(payload & 0xF);
    }
}

/// <summary>A buffer of the host that receives the values a component writes.</summary>
internal sealed class HostWritableBuffer(ComponentValueType? elementType, int capacity) : IWritableBuffer
{
    private readonly List<ComponentValue> _received = [];

    public ComponentValueType? ElementType { get; } = elementType;

    public int Progress => _received.Count;

    public int Remain => capacity - _received.Count;

    public IReadOnlyList<ComponentValue> Received => _received;

    public void Write(ComponentValue[] values) => _received.AddRange(values);
}

/// <summary>A buffer of the host that holds the values a component reads.</summary>
internal sealed class HostReadableBuffer(ComponentValueType? elementType, ComponentValue[] values) : IReadableBuffer
{
    public ComponentValueType? ElementType { get; } = elementType;

    public int Progress { get; private set; }

    public int Remain => values.Length - Progress;

    public ComponentValue[] Read(int count)
    {
        var result = values.AsSpan(Progress, count).ToArray();
        Progress += count;
        return result;
    }
}
