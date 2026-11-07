using System.Buffers;
using System.IO.Pipelines;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Bencode.Fuzz;

/// <summary>
/// A <see cref="PipeReader"/> that receives the input a chunk at a time (sizes from a <see cref="ChunkSizer"/>) and returns
/// the unconsumed data as a sequence of one segment per chunk. It completes every read synchronously and checks that the
/// caller follows the <see cref="PipeReader"/> contract, as <see cref="Pipe"/> does.
/// </summary>
internal sealed class ChunkedPipeReader(byte[] data, ChunkSizer sizer) : PipeReader
{
    private readonly List<int> _chunkEnds = [];
    private int _consumed;
    private int _examined;
    private int _available;
    private bool _isReading;
    private bool _isCompleted;

    /// <summary>Gets the number of bytes the caller consumed.</summary>
    public int Consumed => _consumed;

    /// <summary>Gets a value indicating whether the caller read a buffer it has not advanced past yet.</summary>
    public bool IsReading => _isReading;

    public override bool TryRead(out ReadResult result)
    {
        BeginRead();
        if (_examined < _available || _available == data.Length)
        {
            result = CreateResult();
            return true;
        }

        _isReading = false;
        result = default;
        return false;
    }

    public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        BeginRead();

        // Pipe returns the same data while the caller has not examined all of it
        if (_examined >= _available && _available < data.Length)
        {
            _available += (int)Math.Min(data.Length - _available, (long)sizer.Next());
            _chunkEnds.Add(_available);
        }

        return ValueTask.FromResult(CreateResult());
    }

    public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
    {
        FuzzAssert.True(_isReading, "AdvanceTo was called without a pending read");
        var consumedOffset = GetOffset(consumed);
        var examinedOffset = GetOffset(examined);
        FuzzAssert.True(consumedOffset >= _consumed, "AdvanceTo moved the consumed position backward");
        FuzzAssert.True(consumedOffset <= examinedOffset, "AdvanceTo consumed more than it examined");
        FuzzAssert.True(examinedOffset <= _available, "AdvanceTo examined past the end of the buffer");

        _consumed = consumedOffset;
        _examined = Math.Max(_examined, examinedOffset);
        _isReading = false;
    }

    public override void CancelPendingRead()
    {
    }

    public override void Complete(Exception? exception = null)
    {
        _isCompleted = true;
    }

    private void BeginRead()
    {
        FuzzAssert.False(_isCompleted, "The reader was read after it was completed");
        FuzzAssert.False(_isReading, "The reader was read again before AdvanceTo");
        _isReading = true;
    }

    private ReadResult CreateResult()
    {
        Segment? first = null;
        Segment? last = null;
        var start = 0;
        foreach (var end in _chunkEnds)
        {
            var segmentStart = Math.Max(start, _consumed);
            if (segmentStart < end)
            {
                var segment = new Segment(data.AsMemory(segmentStart, end - segmentStart), segmentStart);
                last?.SetNext(segment);
                first ??= segment;
                last = segment;
            }

            start = end;
        }

        if (first is null || last is null)
        {
            first = last = new Segment(ReadOnlyMemory<byte>.Empty, _consumed);
        }

        var buffer = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        return new ReadResult(buffer, isCanceled: false, isCompleted: _available == data.Length);
    }

    private static int GetOffset(SequencePosition position)
    {
        var segment = position.GetObject() as Segment;
        FuzzAssert.True(segment is not null, "AdvanceTo received a position that does not come from the buffer of the reader");
        return (int)segment.RunningIndex + position.GetInteger();
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory, long runningIndex)
        {
            Memory = memory;
            RunningIndex = runningIndex;
        }

        public void SetNext(Segment next) => Next = next;
    }
}
