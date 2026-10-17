using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Numbers that are appended one after the other, in storage that grows and is kept: rented from an allocation scope
/// when what uses it draws on an image, allocated otherwise.
/// </summary>
internal sealed class GrowableBuffer : IDisposable
{
    private const int InitialCapacity = 192;

    private readonly AllocationScope? _scope;
    private PooledBuffer? _pooled;
    private double[]? _managed;
    private int _capacity;

    /// <param name="scope">The scope the storage is rented from, or <see langword="null"/> to allocate it.</param>
    public GrowableBuffer(AllocationScope? scope)
    {
        _scope = scope;
    }

    /// <summary>Gets or sets the number of values that are used.</summary>
    public int Count { get; set; }

    /// <summary>Gets the values that are used.</summary>
    public Span<double> Values => Storage[..Count];

    private Span<double> Storage => _pooled is not null ? unsafe(MemoryMarshal.Cast<byte, double>(_pooled.Span)) : _managed;

    /// <summary>Appends values, and returns where they are.</summary>
    /// <exception cref="ImageResourceLimitException">The storage exceeds the allocation limit.</exception>
    public Span<double> Append(int count)
    {
        var start = Count;
        if (start + (long)count > _capacity)
        {
            Grow(start + (long)count);
        }

        Count = start + count;
        return Storage.Slice(start, count);
    }

    public void Dispose()
    {
        _pooled?.Dispose();
        _pooled = null;
        _managed = null;
        _capacity = 0;
        Count = 0;
    }

    private void Grow(long needed)
    {
        var capacity = Math.Max(_capacity == 0 ? InitialCapacity : (long)_capacity * 2, needed);
        if (_scope is null)
        {
            var managed = new double[capacity];
            _managed?.AsSpan(0, Count).CopyTo(managed);
            _managed = managed;
        }
        else
        {
            var buffer = ResampleWeights.Rent<double>(_scope, capacity);
            if (_pooled is not null)
            {
                Values.CopyTo(unsafe(MemoryMarshal.Cast<byte, double>(buffer.Span)));
                _pooled.Dispose();
            }

            _pooled = buffer;
        }

        _capacity = (int)capacity;
    }
}
