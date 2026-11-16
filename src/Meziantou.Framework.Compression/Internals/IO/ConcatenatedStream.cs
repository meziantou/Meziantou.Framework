namespace Meziantou.Framework.Compression.Internals.IO;

/// <summary>
/// The volumes of a multi-volume archive read as one stream. It is seekable when every volume is seekable. The volumes are
/// opened on demand from a resolver, and disposed with the stream.
/// </summary>
internal sealed class ConcatenatedStream : Stream
{
    private readonly List<Stream> _volumes = [];
    private readonly List<long> _starts = [];
    private readonly Func<int, Stream?>? _resolver;
    private readonly bool _leaveFirstOpen;
    private bool _resolvedAll;
    private int _current;
    private long _position;

    public ConcatenatedStream(Stream first, Func<int, Stream?>? resolver, bool leaveFirstOpen)
    {
        _volumes.Add(first);
        _starts.Add(0);
        _resolver = resolver;
        _leaveFirstOpen = leaveFirstOpen;
        _resolvedAll = resolver is null;
    }

    public int VolumeCount => _volumes.Count;

    public override bool CanRead => true;

    public override bool CanSeek => _volumes.TrueForAll(volume => volume.CanSeek);

    public override bool CanWrite => false;

    public override long Length
    {
        get
        {
            ResolveAll();
            return _starts[^1] + _volumes[^1].Length;
        }
    }

    public override long Position
    {
        get => _position;
        set
        {
            if (!CanSeek)
                throw new NotSupportedException();

            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = value;
            ResolveAll();
            _current = _starts.Count - 1;
            while (_current > 0 && _starts[_current] > value)
            {
                _current--;
            }

            _volumes[_current].Position = Math.Min(value - _starts[_current], _volumes[_current].Length);
        }
    }

    /// <summary>
    /// Gets the offset, in the concatenated stream, at which a volume starts (used to resolve the disk numbers of a split zip
    /// archive). Every volume is resolved first, so the streams must be seekable.
    /// </summary>
    /// <param name="index">The 0-based index of the volume, less than <see cref="VolumeCount"/>.</param>
    /// <returns>The offset of the first byte of the volume.</returns>
    public long GetVolumeStart(int index)
    {
        ResolveAll();
        return _starts[index];
    }

    /// <summary>Gets the volume that contains the current position (used to locate the headers of RAR volumes).</summary>
    public int CurrentVolume => _current;

    public override int Read(Span<byte> buffer)
    {
        while (true)
        {
            var read = _volumes[_current].Read(buffer);
            if (read > 0 || buffer.IsEmpty)
            {
                _position += read;
                return read;
            }

            if (!MoveToNextVolume())
                return 0;
        }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var read = await _volumes[_current].ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read > 0 || buffer.IsEmpty)
            {
                _position += read;
                return read;
            }

            if (!MoveToNextVolume())
                return 0;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return Read(buffer.AsSpan(offset, count));
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <summary>Opens the next volume and moves to its start.</summary>
    /// <returns><see langword="false"/> when there is no next volume.</returns>
    public bool MoveToNextVolume()
    {
        if (_current + 1 >= _volumes.Count && !TryResolveNext())
            return false;

        _current++;
        if (_volumes[_current].CanSeek)
        {
            _volumes[_current].Position = 0;
        }

        _position = _starts[_current];
        return true;
    }

    private bool TryResolveNext()
    {
        if (_resolvedAll)
            return false;

        var next = _resolver!(_volumes.Count);
        if (next is null)
        {
            _resolvedAll = true;
            return false;
        }

        // The start of a volume is known once the previous volume is read to its end (or has a length)
        var previous = _volumes[^1];
        _starts.Add(previous.CanSeek ? _starts[^1] + previous.Length : _position);
        _volumes.Add(next);
        return true;
    }

    private void ResolveAll()
    {
        while (TryResolveNext())
        {
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        return _position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            for (var i = _leaveFirstOpen ? 1 : 0; i < _volumes.Count; i++)
            {
                _volumes[i].Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
