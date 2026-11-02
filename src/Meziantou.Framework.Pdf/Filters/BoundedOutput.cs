using System.Buffers;
using System.Globalization;

namespace Meziantou.Framework.Pdf.Filters;

/// <summary>The output of a filter: a growable buffer that refuses to grow beyond the limit of the decoded data.</summary>
/// <remarks>
/// The data is written to chunks that are not copied as they are added: <see cref="ToArray"/> copies them once into the
/// result (or returns the first chunk when it holds exactly the data), so the peak memory stays close to twice the
/// decoded length, and the chunks never hold more than the limit. The chunks after the first one are rented from the
/// shared pool and returned by <see cref="ToArray"/>, after which the output is not used.
/// </remarks>
internal sealed class BoundedOutput
{
    private const int MinChunkLength = 1 << 16;
    private const int MaxChunkLength = 1 << 20;

    private readonly byte[] _first;
    private List<(byte[] Array, int Length)>? _rented;
    private byte[] _chunk;
    private int _capacity;
    private int _position;
    private long _completed;

    public BoundedOutput(long limit, int initialCapacity)
    {
        Limit = limit;
        var maximum = (int)Math.Clamp(limit, 1, MaxChunkLength);
        _first = new byte[Math.Clamp(initialCapacity, Math.Min(16, maximum), maximum)];
        _chunk = _first;
        _capacity = _first.Length;
    }

    public long Limit { get; }

    public int Length => (int)(_completed + _position);

    public void Write(byte value)
    {
        if (_position == _capacity)
        {
            NextChunk(1);
        }

        _chunk[_position++] = value;
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        while (data.Length > _capacity - _position)
        {
            var count = _capacity - _position;
            data[..count].CopyTo(_chunk.AsSpan(_position));
            _position += count;
            data = data[count..];
            NextChunk(data.Length);
        }

        data.CopyTo(_chunk.AsSpan(_position));
        _position += data.Length;
    }

    /// <summary>Writes a byte several times (run-length).</summary>
    public void WriteRepeated(byte value, int count)
    {
        while (count > _capacity - _position)
        {
            var available = _capacity - _position;
            _chunk.AsSpan(_position, available).Fill(value);
            _position += available;
            count -= available;
            NextChunk(count);
        }

        _chunk.AsSpan(_position, count).Fill(value);
        _position += count;
    }

    /// <summary>Gets the free space after the data, to write to before <see cref="Advance"/>: empty when the data has reached the limit.</summary>
    public Span<byte> GetFreeSpan()
    {
        if (_position == _capacity)
        {
            if (_completed + _position >= Math.Min(Limit, Array.MaxLength))
                return [];

            NextChunk(1);
        }

        return _chunk.AsSpan(_position, _capacity - _position);
    }

    /// <summary>Adds the bytes written to the span of <see cref="GetFreeSpan"/> to the data.</summary>
    public void Advance(int count) => _position += count;

    public byte[] ToArray()
    {
        if (_rented is null)
            return _position == _first.Length ? _first : _first.AsSpan(0, _position).ToArray();

        var result = new byte[Length];
        _first.CopyTo(result, 0);
        var offset = _first.Length;
        foreach (var (array, length) in _rented)
        {
            var used = array == _chunk ? _position : length;
            array.AsSpan(0, used).CopyTo(result.AsSpan(offset));
            offset += used;
            ArrayPool<byte>.Shared.Return(array);
        }

        _rented = null;
        return result;
    }

    private void NextChunk(int count)
    {
        var completed = _completed + _position;
        if (completed + count > Limit)
            throw new PdfResourceLimitException($"The decoded data of a stream exceeds {Limit.ToString(CultureInfo.InvariantCulture)} bytes ({nameof(PdfReadOptions)}.{nameof(PdfReadOptions.MaxDecodedStreamLength)})");

        if (completed + count > Array.MaxLength)
            throw new PdfResourceLimitException("The decoded data of a stream exceeds the maximum size of an array");

        // The chunks grow with the data, up to 1 MiB, and never beyond the limit: a rented array can be larger than the
        // length asked, only that length is used
        var length = (int)Math.Min(Math.Clamp(completed, MinChunkLength, MaxChunkLength), Math.Min(Limit, Array.MaxLength) - completed);
        var chunk = ArrayPool<byte>.Shared.Rent(length);
        _rented ??= [];
        _rented.Add((chunk, length));
        _completed = completed;
        _chunk = chunk;
        _capacity = length;
        _position = 0;
    }
}
