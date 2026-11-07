using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>A coded frame stored in a track buffer: its position on the media timeline, and the description of its track when it was appended.</summary>
internal readonly record struct BufferedFrame(double PresentationTimestamp, double DecodeTimestamp, double Duration, bool IsRandomAccessPoint, int Width, int Height, long ByteCount)
{
    public double EndTimestamp => PresentationTimestamp + Duration;
}

/// <summary>
/// A track buffer of a <see cref="SourceBuffer"/> (https://w3c.github.io/media-source/#track-buffers): the coded frames of a
/// track (the library does not keep their data), in decode order, and the state of the coded frame processing algorithm for
/// the track.
/// </summary>
internal sealed class TrackBuffer(long trackId, bool isVideo)
{
    // The frames are ordered by decode timestamp: the frames that depend on a frame follow it, up to the next random access
    // point. Their presentation timestamps are not ordered (the bidirectional prediction), but a frame is presented at most
    // _maxReorderDistance from its decode timestamp, so the frames of a presentation time range are found in a window
    private readonly List<BufferedFrame> _frames = [];
    private double _maxReorderDistance;
    private MediaTimeRange[]? _ranges;
    private double? _highestPresentationTimestamp;

    /// <summary>The largest duration of the frames appended to the track buffer so far: the ranges separated by less than this duration are coalesced.</summary>
    private double _maxFrameDuration;

    /// <summary>The largest gap between two frames that is not a discontinuity of the track buffer ranges.</summary>
    public double GapTolerance => _maxFrameDuration;

    public long TrackId { get; set; } = trackId;

    public bool IsVideo { get; } = isVideo;

    /// <summary>The natural size of the frames of the track description of the last initialization segment.</summary>
    public int Width { get; set; }

    public int Height { get; set; }

    public double? LastDecodeTimestamp { get; set; }

    public double? LastFrameDuration { get; set; }

    public double? HighestEndTimestamp { get; set; }

    public bool NeedRandomAccessPoint { get; set; } = true;

    public IReadOnlyList<BufferedFrame> Frames => _frames;

    /// <summary>The number of bytes of the media segments of the frames of the track buffer.</summary>
    public long ByteCount { get; private set; }

    /// <summary>Unsets the variables of the current coded frame group.</summary>
    public void ResetGroup()
    {
        LastDecodeTimestamp = null;
        LastFrameDuration = null;
        HighestEndTimestamp = null;
        NeedRandomAccessPoint = true;
    }

    /// <summary>The highest presentation timestamp of the frames, or null when the track buffer is empty.</summary>
    public double? HighestPresentationTimestamp
    {
        get
        {
            _ = Ranges;
            return _highestPresentationTimestamp;
        }
    }

    /// <summary>The index of the first frame whose decode timestamp is not less than the given time.</summary>
    private int LowerBound(double decodeTimestamp)
    {
        var low = 0;
        var high = _frames.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (_frames[middle].DecodeTimestamp < decodeTimestamp)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>The indexes of the frames that can be presented in a time range: [first, last).</summary>
    private (int First, int Last) GetWindow(double start, double end)
    {
        var first = LowerBound(start - _maxReorderDistance);
        var last = first;
        var limit = end + _maxReorderDistance;
        while (last < _frames.Count && _frames[last].DecodeTimestamp <= limit)
        {
            last++;
        }

        return (first, last);
    }

    /// <summary>Returns the index of the frame whose presentation interval contains the given time, or -1.</summary>
    public int FindFrameAt(double time)
    {
        var (first, last) = GetWindow(time - _maxFrameDuration, time);
        var result = -1;
        for (var i = first; i < last; i++)
        {
            var frame = _frames[i];
            if (frame.PresentationTimestamp <= time && time < frame.EndTimestamp && (result < 0 || frame.PresentationTimestamp > _frames[result].PresentationTimestamp))
            {
                result = i;
            }
        }

        return result;
    }

    public void Add(BufferedFrame frame)
    {
        // The frames of a coded frame group are appended in decode order: the new frame usually is the last one
        var index = _frames.Count;
        if (index > 0 && _frames[index - 1].DecodeTimestamp > frame.DecodeTimestamp)
        {
            index = LowerBound(frame.DecodeTimestamp);
            while (index < _frames.Count && _frames[index].DecodeTimestamp == frame.DecodeTimestamp)
            {
                index++;
            }
        }

        _frames.Insert(index, frame);
        ByteCount += frame.ByteCount;
        _maxFrameDuration = Math.Max(_maxFrameDuration, frame.Duration);
        _maxReorderDistance = Math.Max(_maxReorderDistance, Math.Abs(frame.PresentationTimestamp - frame.DecodeTimestamp));
        _ranges = null;
    }

    public void RemoveAt(int index)
    {
        ByteCount -= _frames[index].ByteCount;
        _frames.RemoveAt(index);
        _ranges = null;
    }

    public void Replace(int index, BufferedFrame frame)
    {
        ByteCount += frame.ByteCount - _frames[index].ByteCount;
        _frames[index] = frame;
        _ranges = null;
    }

    /// <summary>
    /// Removes the frames whose presentation timestamp is in [<paramref name="start"/>, <paramref name="end"/>), then the
    /// frames that follow each of them in decode order up to the next random access point (their possible decoding
    /// dependencies). Returns whether frames were removed; <paramref name="lastDecodedFrameTimestamp"/> is the presentation
    /// timestamp of the removed frame whose decode timestamp is the last decode timestamp of the track buffer, if any.
    /// </summary>
    public bool RemoveRange(double start, double end, out double? lastDecodedFrameTimestamp)
    {
        lastDecodedFrameTimestamp = null;
        if (_frames.Count == 0 || !(start < end))
            return false;

        var (first, last) = GetWindow(start, end);
        List<int>? removed = null;
        var removingDependencies = false;
        for (var i = first; i < _frames.Count && (i < last || removingDependencies); i++)
        {
            var frame = _frames[i];
            if (frame.PresentationTimestamp >= start && frame.PresentationTimestamp < end)
            {
                removingDependencies = true;
            }
            else if (frame.IsRandomAccessPoint)
            {
                removingDependencies = false;
                continue;
            }
            else if (!removingDependencies)
            {
                continue;
            }

            (removed ??= []).Add(i);
            ByteCount -= frame.ByteCount;
            if (LastDecodeTimestamp is { } decodeTimestamp && frame.DecodeTimestamp == decodeTimestamp)
            {
                lastDecodedFrameTimestamp = frame.PresentationTimestamp;
            }
        }

        if (removed is null)
            return false;

        if (removed[^1] - removed[0] == removed.Count - 1)
        {
            _frames.RemoveRange(removed[0], removed.Count);
        }
        else
        {
            for (var i = removed.Count - 1; i >= 0; i--)
            {
                _frames.RemoveAt(removed[i]);
            }
        }

        _ranges = null;
        return true;
    }

    /// <summary>The presentation timestamp of the first random access point at or after the given time, or null.</summary>
    public double? FindRandomAccessPointAtOrAfter(double time)
    {
        double? result = null;
        for (var i = LowerBound(time - _maxReorderDistance); i < _frames.Count; i++)
        {
            var frame = _frames[i];
            if (frame.IsRandomAccessPoint && frame.PresentationTimestamp >= time && !(frame.PresentationTimestamp >= result))
            {
                result = frame.PresentationTimestamp;
            }

            // The next frames are presented later
            if (result is not null && frame.DecodeTimestamp - _maxReorderDistance > result)
                break;
        }

        return result;
    }

    /// <summary>The presentation timestamp of the last random access point at or before the given time, or null.</summary>
    public double? FindRandomAccessPointAtOrBefore(double time)
    {
        var (_, last) = GetWindow(time, time);
        for (var i = last - 1; i >= 0; i--)
        {
            var frame = _frames[i];
            if (frame.IsRandomAccessPoint && frame.PresentationTimestamp <= time)
                return frame.PresentationTimestamp;
        }

        return null;
    }

    /// <summary>
    /// The track buffer ranges (https://w3c.github.io/media-source/#track-buffer-ranges): the presentation time ranges of the
    /// frames. As the specification allows (it suggests a threshold of at most twice this duration), the ranges separated
    /// by a gap smaller than the largest frame duration buffered so far are coalesced: the timestamps of the byte streams
    /// are rounded, so the end of a frame rarely is the exact start of the next one.
    /// </summary>
    public MediaTimeRange[] Ranges
    {
        get
        {
            if (_ranges is { } ranges)
                return ranges;

            // The frames are almost ordered by presentation timestamp: a frame usually extends the last range
            var result = new List<MediaTimeRange>();
            var fudge = GapTolerance;
            double? highest = null;
            foreach (var frame in _frames)
            {
                if (!(frame.PresentationTimestamp <= highest))
                {
                    highest = frame.PresentationTimestamp;
                }

                if (result.Count > 0 && frame.PresentationTimestamp - result[^1].End < fudge && result[^1].Start - frame.EndTimestamp < fudge)
                {
                    result[^1] = new MediaTimeRange(Math.Min(result[^1].Start, frame.PresentationTimestamp), Math.Max(result[^1].End, frame.EndTimestamp));
                }
                else
                {
                    result.Add(new MediaTimeRange(frame.PresentationTimestamp, frame.EndTimestamp));
                }
            }

            // The ranges that overlap or that are closer than the threshold are merged
            result.Sort(static (a, b) => a.Start.CompareTo(b.Start));
            var merged = new List<MediaTimeRange>(result.Count);
            foreach (var range in result)
            {
                if (merged.Count > 0 && range.Start - merged[^1].End < fudge)
                {
                    merged[^1] = new MediaTimeRange(merged[^1].Start, Math.Max(merged[^1].End, range.End));
                }
                else
                {
                    merged.Add(range);
                }
            }

            _highestPresentationTimestamp = highest;
            return _ranges = [.. merged];
        }
    }

    /// <summary>The end time of the last range, or 0 when the track buffer is empty.</summary>
    public double HighestEndTime => Ranges is { Length: > 0 } ranges ? ranges[^1].End : 0;
}

/// <summary>Operations on normalized lists of time ranges.</summary>
internal static class MediaTimeRanges
{
    public static MediaTimeRange[] Intersect(ReadOnlySpan<MediaTimeRange> first, ReadOnlySpan<MediaTimeRange> second)
    {
        var result = new List<MediaTimeRange>();
        var i = 0;
        var j = 0;
        while (i < first.Length && j < second.Length)
        {
            var start = Math.Max(first[i].Start, second[j].Start);
            var end = Math.Min(first[i].End, second[j].End);
            if (start < end)
            {
                result.Add(new MediaTimeRange(start, end));
            }

            if (first[i].End < second[j].End)
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return [.. result];
    }

    /// <summary>Returns the parts of the ranges of <paramref name="first"/> that are not in <paramref name="second"/>.</summary>
    public static MediaTimeRange[] Subtract(ReadOnlySpan<MediaTimeRange> first, ReadOnlySpan<MediaTimeRange> second)
    {
        var result = new List<MediaTimeRange>();
        foreach (var range in first)
        {
            var start = range.Start;
            foreach (var other in second)
            {
                if (other.End <= start || other.Start >= range.End)
                    continue;

                if (other.Start > start)
                {
                    result.Add(new MediaTimeRange(start, other.Start));
                }

                start = Math.Max(start, other.End);
            }

            if (start < range.End)
            {
                result.Add(new MediaTimeRange(start, range.End));
            }
        }

        return [.. result];
    }

    public static bool AreEqual(ReadOnlySpan<MediaTimeRange> first, ReadOnlySpan<MediaTimeRange> second) => first.SequenceEqual(second);

    /// <summary>Returns the range that contains the given time (its end included), or null.</summary>
    public static MediaTimeRange? Find(ReadOnlySpan<MediaTimeRange> ranges, double time)
    {
        foreach (var range in ranges)
        {
            if (time >= range.Start && time <= range.End)
                return range;
        }

        return null;
    }
}
