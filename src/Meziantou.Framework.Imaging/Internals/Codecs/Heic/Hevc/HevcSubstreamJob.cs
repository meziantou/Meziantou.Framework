namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The substreams of a slice segment that the workers decode, and what each one leaves: whether it ends the slice segment
/// or the next substream follows it, where it ends, its context variables. A substream depends on the one before it when
/// it is a row of coding tree blocks of the wavefront that is not the first row of its tile: it starts once the coding
/// tree block above and to the right of its first one is decoded (its context variables are those that the row above
/// stored after that block, clause 9.3.2.4), and decodes the block in column x once the block in column x + 1 of the row
/// above is decoded (the last one of the row at the right of its tile): no process of a coding tree block reads further
/// to the right in the row above, nor below. The substreams of other tiles never wait.
/// </summary>
internal sealed class HevcSubstreamJob : IDisposable
{
    private const int SlotLength = HevcContextLayout.Count + 4;

    private readonly object _gate = new();
    private readonly int[] _progress;
    private readonly Av1PooledArray<byte> _slots;
    private int _next;
    private int _stopAfter = int.MaxValue;
    private int _waiters;

    public HevcSubstreamJob(AllocationScope scope, int count)
    {
        Count = count;
        First = new int[count];
        End = new int[count];
        Start = new int[count];
        DependsOnPrevious = new bool[count];
        Outcomes = new HevcSubstreamOutcome[count];
        Errors = new Exception?[count];
        EndPositions = new int[count];
        EndQpY = new int[count];
        EndCtbAddrInTs = new int[count];
        Tools = new HevcTools[count];
        HasStoredWppContexts = new bool[count];
        _progress = new int[count];

        // For each substream, the context variables stored for the next row (clause 9.3.2.4), then those at its end
        _slots = Av1PooledArray<byte>.Rent(scope, 2L * count * SlotLength);
    }

    /// <summary>Gets the number of substreams.</summary>
    public int Count { get; }

    /// <summary>Gets the address in tile scan of the first coding tree block of each substream.</summary>
    public int[] First { get; }

    /// <summary>Gets the address in tile scan of the coding tree block that follows each substream: the first one of the next tile or of the next row.</summary>
    public int[] End { get; }

    /// <summary>Gets the position in the payload of the first byte of each substream.</summary>
    public int[] Start { get; }

    /// <summary>Gets whether each substream is a row of the wavefront that reads the row of its tile decoded by the substream before it.</summary>
    public bool[] DependsOnPrevious { get; }

    public HevcSubstreamOutcome[] Outcomes { get; }

    public Exception?[] Errors { get; }

    /// <summary>Gets where each substream that does not end the slice segment ends: the position of the byte that follows its alignment bits.</summary>
    public int[] EndPositions { get; }

    /// <summary>Gets the address in tile scan of the coding tree block that follows each substream that ends the slice segment.</summary>
    public int[] EndCtbAddrInTs { get; }

    /// <summary>Gets <c>QpY</c> of the last coding unit of each substream that ends the slice segment.</summary>
    public int[] EndQpY { get; }

    /// <summary>Gets the coding tools that each substream used: a diagnostic.</summary>
    public HevcTools[] Tools { get; }

    /// <summary>Gets whether each substream stored the context variables of the wavefront.</summary>
    public bool[] HasStoredWppContexts { get; }

    /// <summary>Gets the context variables that a substream stored for the next row (the Rice parameter initialization states follow them).</summary>
    public Span<byte> GetWppSlot(int substream) => _slots.Span.Slice(2 * substream * SlotLength, SlotLength);

    /// <summary>Gets the context variables of a substream at the end of the slice segment (the Rice parameter initialization states follow them).</summary>
    public Span<byte> GetEndSlot(int substream) => _slots.Span.Slice(((2 * substream) + 1) * SlotLength, SlotLength);

    /// <summary>Gets the index of the next substream to decode, in order, or -1 when there is none left to decode.</summary>
    public int TakeNext()
    {
        var substream = Interlocked.Increment(ref _next) - 1;
        return substream < Count && !IsAbandoned(substream) ? substream : -1;
    }

    /// <summary>Gets whether a substream that precedes <paramref name="substream"/> failed or ended the slice segment: its decoding is not used.</summary>
    public bool IsAbandoned(int substream) => substream > Volatile.Read(ref _stopAfter);

    /// <summary>Records that a substream failed or ended the slice segment: the substreams after it are not decoded.</summary>
    public void StopAfter(int substream)
    {
        var current = Volatile.Read(ref _stopAfter);
        while (substream < current)
        {
            var previous = Interlocked.CompareExchange(ref _stopAfter, substream, current);
            if (previous == current)
                return;

            current = previous;
        }
    }

    /// <summary>Records that a substream decoded the coding tree blocks before <paramref name="nextCtbAddrInTs"/>, or <see cref="int.MaxValue"/> when it stops.</summary>
    public void Publish(int substream, int nextCtbAddrInTs)
    {
        Interlocked.Exchange(ref _progress[substream], nextCtbAddrInTs);
        if (Volatile.Read(ref _waiters) > 0)
        {
            lock (_gate)
            {
                Monitor.PulseAll(_gate);
            }
        }
    }

    /// <summary>Waits until a substream decoded the coding tree block <paramref name="ctbAddrInTs"/>, or stopped.</summary>
    public void WaitFor(int substream, int ctbAddrInTs)
    {
        if (Volatile.Read(ref _progress[substream]) > ctbAddrInTs)
            return;

        var spinner = default(SpinWait);
        for (var i = 0; i < 16; i++)
        {
            spinner.SpinOnce(sleep1Threshold: -1);
            if (Volatile.Read(ref _progress[substream]) > ctbAddrInTs)
                return;
        }

        lock (_gate)
        {
            // The count is incremented before the progress is read again, and the progress is written before the count is
            // read: one of the two threads sees what the other one wrote
            Interlocked.Increment(ref _waiters);
            try
            {
                while (Volatile.Read(ref _progress[substream]) <= ctbAddrInTs)
                {
                    Monitor.Wait(_gate);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _waiters);
            }
        }
    }

    /// <summary>Sets the progress of every substream to the first coding tree block of the substream.</summary>
    public void ResetProgress()
    {
        for (var i = 0; i < Count; i++)
        {
            _progress[i] = First[i];
        }
    }

    public void Dispose() => _slots.Dispose();
}
