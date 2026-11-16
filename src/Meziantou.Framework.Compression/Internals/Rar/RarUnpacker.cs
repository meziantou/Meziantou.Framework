using System.Numerics;

namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>
/// The sliding window, the output and the filters shared by the RAR decoders. A decoder writes the decoded bytes in the
/// window (a power of two, indexed with a mask), at absolute positions that run over the files of a solid stream; the
/// bytes are written to the output once they are decoded and filtered. The window is allocated lazily: it starts at the
/// size the file needs and doubles up to the dictionary size, bounded by <see cref="ArchiveReaderOptions.MaxMemoryUsage"/>.
/// </summary>
internal abstract class RarUnpacker
{
    /// <summary>The longest match of every RAR version, plus a margin: the decoders stop this far from the bytes not written yet.</summary>
    private protected const int MatchMargin = 0x1100;

    private const int MinWindowSize = 0x10000;

    // RAR allocates at least 256 KiB, whatever the dictionary size: the filters of a block may need it
    private const long MinMaxWindowSize = 0x40000;
    private const long MaxSupportedWindowSize = 1L << 30;

    // A file of a solid stream is decoded to its end, so that the next one starts where the encoder started it, but not
    // further than this after its declared size
    private const long MaxExtraOutput = 4 * 1024 * 1024;

    private const int MaxPendingFilters = 8192;

    private readonly long _maxMemoryUsage;
    private readonly Queue<RarFilter> _filters = new();
    private long _maxWindowSize;
    private long _writtenPosition;
    private long _fileSize;
    private long _fileOutput;
    private long _lastFilterEnd;
    private byte[]? _filterBuffer;
    private int _filterOutputOffset;
    private int _filterOutputEnd;
    private bool _started;

    protected RarUnpacker(long maxMemoryUsage)
    {
        _maxMemoryUsage = maxMemoryUsage;
    }

    private protected byte[] Window { get; private set; } = [];

    private protected int WindowMask { get; private set; }

    /// <summary>Gets or sets the number of bytes decoded since the start of the solid stream.</summary>
    private protected long DecodedPosition { get; set; }

    /// <summary>Gets the position of the start of the current file in the solid stream.</summary>
    private protected long FileStart { get; private set; }

    /// <summary>Gets the declared size of the current file, or -1 when it is unknown.</summary>
    private protected long FileSize => _fileSize;

    /// <summary>Gets or sets a value indicating whether the compressed data of the current file ended.</summary>
    private protected bool FileEnded { get; set; }

    private protected RarBitReader Input { get; } = new();

    /// <summary>Gets a value indicating whether an error left the decoder in a state that cannot continue a solid stream.</summary>
    public bool IsBroken => _error is not null;

    private Exception? _error;

    /// <summary>Gets a value indicating whether the current file was decoded to its end (<see cref="FinishFile"/> succeeded).</summary>
    public bool IsFileFinished { get; private set; }

    /// <summary>Starts to decode a file.</summary>
    /// <param name="input">The compressed (and decrypted) data of the file.</param>
    /// <param name="size">The size of the file, or -1 when it is unknown.</param>
    /// <param name="dictionarySize">The dictionary size of the file.</param>
    /// <param name="solid">Whether the file continues the solid stream of the previous file.</param>
    public void StartFile(Stream input, long size, long dictionarySize, bool solid)
    {
        ThrowIfBroken();

        if (!solid || !_started)
        {
            Window = [];
            WindowMask = 0;
            DecodedPosition = 0;
            _maxWindowSize = 0;
            ResetState();
        }

        _started = true;
        var maxWindowSize = Math.Max(MinMaxWindowSize, dictionarySize);
        if (maxWindowSize > MaxSupportedWindowSize)
        {
            maxWindowSize = MaxSupportedWindowSize * 2;
        }
        else
        {
            maxWindowSize = (long)BitOperations.RoundUpToPowerOf2((ulong)maxWindowSize);
        }

        _maxWindowSize = Math.Max(_maxWindowSize, maxWindowSize);
        Input.Reset(input);
        FileStart = DecodedPosition;
        _writtenPosition = DecodedPosition;
        _lastFilterEnd = DecodedPosition;
        _filters.Clear();
        _filterOutputOffset = 0;
        _filterOutputEnd = 0;
        _fileSize = size;
        _fileOutput = 0;
        FileEnded = false;
        IsFileFinished = false;
        StartFileCore(solid);
        EnsureWindow();
    }

    /// <summary>Reads the decoded bytes of the current file.</summary>
    public int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
            return 0;

        ThrowIfBroken();
        if (_fileSize >= 0)
        {
            var remaining = _fileSize - _fileOutput;
            if (remaining <= 0)
                return 0;

            if (buffer.Length > remaining)
            {
                buffer = buffer[..(int)remaining];
            }
        }

        try
        {
            while (true)
            {
                if (_filterOutputOffset < _filterOutputEnd)
                {
                    var count = Math.Min(buffer.Length, _filterOutputEnd - _filterOutputOffset);
                    _filterBuffer.AsSpan(_filterOutputOffset, count).CopyTo(buffer);
                    _filterOutputOffset += count;
                    _fileOutput += count;
                    return count;
                }

                var readyEnd = DecodedPosition;
                if (_filters.TryPeek(out var filter))
                {
                    if (filter.Start == _writtenPosition)
                    {
                        if (DecodedPosition >= filter.Start + filter.Length)
                        {
                            RunFilters();
                            continue;
                        }

                        readyEnd = _writtenPosition;
                    }
                    else if (filter.Start < readyEnd)
                    {
                        readyEnd = filter.Start;
                    }
                }

                if (readyEnd > _writtenPosition)
                {
                    var index = (int)(_writtenPosition & WindowMask);
                    var count = (int)Math.Min(Math.Min(readyEnd - _writtenPosition, buffer.Length), Window.Length - index);
                    Window.AsSpan(index, count).CopyTo(buffer);
                    _writtenPosition += count;
                    _fileOutput += count;
                    return count;
                }

                if (FileEnded)
                {
                    if (_filters.Count > 0)
                        throw new InvalidDataException("A filter of the compressed data extends after the end of the file.");

                    return 0;
                }

                DecodeMore();
            }
        }
        catch (Exception ex)
        {
            _error = ex;
            throw;
        }
    }

    private void ThrowIfBroken()
    {
        if (_error is null)
            return;

        var message = "The data cannot be decoded after a previous error: " + _error.Message;
        throw _error switch
        {
            NotSupportedException => new NotSupportedException(message, _error),
            _ => new InvalidDataException(message, _error),
        };
    }

    /// <summary>
    /// Decodes the rest of the compressed data of the current file and discards it, so that the next file of the solid
    /// stream starts at the right position. An error marks the decoder as broken instead of throwing.
    /// </summary>
    public void FinishFile()
    {
        if (IsBroken || IsFileFinished)
            return;

        try
        {
            var budget = (_fileSize < 0 ? 0 : Math.Max(0, _fileSize - _fileOutput)) + MaxExtraOutput;
            while (!FileEnded)
            {
                var before = DecodedPosition;
                DiscardPending();
                DecodeMore();
                budget -= DecodedPosition - before;
                if (budget < 0)
                {
                    _error = new InvalidDataException("The compressed data of the file are longer than its size.");
                    return;
                }
            }

            DiscardPending();
            IsFileFinished = true;
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            _error = ex;
        }
    }

    private void DiscardPending()
    {
        _writtenPosition = DecodedPosition;
        _lastFilterEnd = Math.Max(_lastFilterEnd, DecodedPosition);
        _filters.Clear();
        _filterOutputOffset = 0;
        _filterOutputEnd = 0;
    }

    /// <summary>Resets the state of the decoder at the start of a solid stream.</summary>
    private protected abstract void ResetState();

    /// <summary>Prepares the decoding of a file (the window and the input are reset already).</summary>
    private protected abstract void StartFileCore(bool solid);

    /// <summary>
    /// Decodes symbols until <see cref="DecodedPosition"/> reaches <paramref name="limit"/>, or the compressed data of the
    /// file end (<see cref="FileEnded"/>). A symbol may write up to <see cref="MatchMargin"/> bytes past the limit.
    /// </summary>
    private protected abstract void Decode(long limit);

    /// <summary>Queues a filter of the output.</summary>
    private protected void AddFilter(RarFilter filter)
    {
        if (filter.Length <= 0)
            return;

        if (filter.Start < _lastFilterEnd)
        {
            // RAR 2.9 applies the filters of a block one after the other (the next one filters the output of the previous one)
            if (!_filters.TryPeek(out _) || !IsChained(filter))
                throw new InvalidDataException("The filters of the compressed data overlap.");
        }

        if (_filters.Count >= MaxPendingFilters)
            throw new InvalidDataException("The compressed data contain too many filters.");

        if ((long)filter.Length * 2 > _maxMemoryUsage)
            throw new InvalidDataException("The filter of the compressed data needs more memory than the maximum memory usage.");

        _filters.Enqueue(filter);
        _lastFilterEnd = filter.Start + filter.Length;
    }

    private bool IsChained(RarFilter filter)
    {
        RarFilter? last = null;
        foreach (var item in _filters)
        {
            last = item;
        }

        return last is not null && last.Start == filter.Start && last.Length == filter.Length;
    }

    private void RunFilters()
    {
        var filter = _filters.Dequeue();
        var length = filter.Length;
        var required = length * 2;
        if (_filterBuffer is null || _filterBuffer.Length < required)
        {
            _filterBuffer = new byte[Math.Max(required, 0x10000)];
        }

        // Copy the block out of the window: the window keeps the unfiltered data the next matches refer to
        var index = (int)(filter.Start & WindowMask);
        var first = Math.Min(length, Window.Length - index);
        Window.AsSpan(index, first).CopyTo(_filterBuffer);
        Window.AsSpan(0, length - first).CopyTo(_filterBuffer.AsSpan(first));

        var offset = filter.Apply(_filterBuffer, filter.Start - FileStart, IsRar5);
        while (_filters.TryPeek(out var next) && next.Start == filter.Start && next.Length == length)
        {
            _filters.Dequeue();
            if (offset is not 0)
            {
                _filterBuffer.AsSpan(offset, length).CopyTo(_filterBuffer);
            }

            offset = next.Apply(_filterBuffer, filter.Start - FileStart, IsRar5);
        }

        _writtenPosition += length;
        _filterOutputOffset = offset;
        _filterOutputEnd = offset + length;
    }

    private protected virtual bool IsRar5 => false;

    private void DecodeMore()
    {
        var windowSize = (long)Window.Length;
        var limit = _writtenPosition + windowSize - MatchMargin;
        if (windowSize < _maxWindowSize)
        {
            limit = Math.Min(limit, windowSize - MatchMargin);
        }

        if (_fileSize >= 0 && _fileSize < long.MaxValue - FileStart)
        {
            // Do not decode much more than the file needs: the output is read in chunks
            limit = Math.Min(limit, Math.Max(FileStart + _fileSize, DecodedPosition + 1));
        }

        if (DecodedPosition >= limit)
        {
            if (windowSize < _maxWindowSize && DecodedPosition + MatchMargin >= windowSize)
            {
                GrowWindow(windowSize * 2);
                return;
            }

            if (_writtenPosition + windowSize - MatchMargin <= DecodedPosition)
                throw new InvalidDataException("A filter block of the compressed data is larger than the dictionary.");

            limit = DecodedPosition + 1;
        }

        Decode(limit);
    }

    private void EnsureWindow()
    {
        var needed = _fileSize < 0 || _fileSize > MaxSupportedWindowSize ? 0x100000 : DecodedPosition + _fileSize + MatchMargin;
        var size = (long)BitOperations.RoundUpToPowerOf2((ulong)Math.Clamp(needed, MinWindowSize, Math.Min(_maxWindowSize, MaxSupportedWindowSize)));
        if (size > Window.Length)
        {
            GrowWindow(size);
        }
    }

    private void GrowWindow(long size)
    {
        if (size > _maxWindowSize)
        {
            size = _maxWindowSize;
        }

        if (size <= Window.Length)
            return;

        if (size > MaxSupportedWindowSize)
            throw new NotSupportedException("The dictionary of the compressed data is larger than 1 GiB, which is not supported.");

        if (size > _maxMemoryUsage)
            throw new InvalidDataException($"The dictionary of the compressed data ({size} bytes) is larger than the maximum memory usage ({_maxMemoryUsage} bytes).");

        var window = new byte[size];
        var mask = (int)(size - 1);
        var old = Window;
        if (old.Length > 0)
        {
            // Copy the last bytes of the old window to their positions in the new one
            var start = Math.Max(0, DecodedPosition - old.Length);
            var position = start;
            while (position < DecodedPosition)
            {
                var oldIndex = (int)(position & (old.Length - 1));
                var newIndex = (int)(position & mask);
                var count = (int)Math.Min(Math.Min(DecodedPosition - position, old.Length - oldIndex), window.Length - newIndex);
                old.AsSpan(oldIndex, count).CopyTo(window.AsSpan(newIndex));
                position += count;
            }
        }

        Window = window;
        WindowMask = mask;
    }

    /// <summary>Copies a match in the window. The caller checks that <paramref name="distance"/> is at most the decoded size and the window size.</summary>
    private protected static void CopyMatch(byte[] window, int mask, long position, int length, int distance)
    {
        var destination = (int)(position & mask);
        var source = (destination - distance) & mask;
        if (destination + length <= window.Length && source + length <= window.Length)
        {
            if (distance >= length)
            {
                window.AsSpan(source, length).CopyTo(window.AsSpan(destination));
            }
            else if (distance == 1)
            {
                window.AsSpan(destination, length).Fill(window[source]);
            }
            else
            {
                // The bytes between the source and the destination repeat: copy them in chunks that double
                var copied = 0;
                var step = distance;
                while (copied < length)
                {
                    var count = Math.Min(step, length - copied);
                    window.AsSpan(source, count).CopyTo(window.AsSpan(destination + copied));
                    copied += count;
                    step += count;
                }
            }

            return;
        }

        for (var i = 0; i < length; i++)
        {
            window[destination] = window[source];
            destination = (destination + 1) & mask;
            source = (source + 1) & mask;
        }
    }

    /// <summary>Throws when a match refers to bytes before the start of the stream or out of the window.</summary>
    private protected void CheckDistance(ulong distance, long position)
    {
        if (distance > (ulong)position || distance > (ulong)Window.Length)
            throw new InvalidDataException("The compressed data refer to data before the start of the dictionary.");
    }
}
