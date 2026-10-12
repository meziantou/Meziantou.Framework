using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The presentation of the samples of a track through its edit list (ISO/IEC 14496-12, section 8.6.6): each edit plays a
/// part of the media, from its media time and for its duration, and the presentation is the edits one after the other.
/// The edits of an image sequence play the media forward: they are in the order of the media and do not overlap, so
/// the presentation is the images of the track in their order, some of them left out and some shown for a part of
/// their duration only.
/// </summary>
/// <remarks>
/// <para>
/// The duration of an edit is in units of the time scale of the movie, and the times of the media in units of the time
/// scale of the track. Both are converted to units of <c>1 / (movie time scale x media time scale)</c> second, where
/// they are integers: an image is shown for the exact time that the edits cover of it, which is the sum of the
/// intersections of its interval with the intervals of the edits, and an image that no edit covers is not shown. An
/// image without duration is shown when its time is inside an edit or at its end.
/// </para>
/// <para>
/// The images that are not shown are still decoded when a later image is: the images that follow may refer to them.
/// </para>
/// </remarks>
internal sealed class AvifEditTimeline
{
    // The intervals of the edits, in units of 1 / (movie time scale x media time scale) second, in increasing order
    private readonly UInt128[] _starts;
    private readonly UInt128[] _ends;
    private readonly uint _movieTimescale;
    private readonly uint _mediaTimescale;

    // The position of the sequential reading of the durations
    private long _nextSample;
    private UInt128 _nextStart;
    private int _run;
    private long _runFirst;
    private int _edit;

    private AvifEditTimeline(UInt128[] starts, UInt128[] ends, uint movieTimescale, uint mediaTimescale)
    {
        _starts = starts;
        _ends = ends;
        _movieTimescale = movieTimescale;
        _mediaTimescale = mediaTimescale;
    }

    /// <summary>Gets the number of images that the edits show.</summary>
    public long PresentedCount { get; private set; }

    /// <summary>Creates the timeline of a track.</summary>
    /// <param name="track">The track, whose edit list has at least one edit.</param>
    /// <param name="movieTimescale">The time scale of the movie, which the durations of the edits are in.</param>
    /// <exception cref="InvalidImageContentException">The time scale of the movie is zero.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The edits are not in the order of the media, or overlap.</exception>
    public static AvifEditTimeline Create(AvifTrack track, uint movieTimescale)
    {
        if (movieTimescale is 0)
            throw AvifFormat.Invalid("the time scale of the movie is zero.");

        var edits = track.Edits;
        var starts = new UInt128[edits.Count];
        var ends = new UInt128[edits.Count];
        var mediaEnd = (UInt128)track.MediaDuration * movieTimescale;
        for (var i = 0; i < edits.Count; i++)
        {
            var (duration, mediaTime) = edits[i];
            starts[i] = (UInt128)(ulong)mediaTime * movieTimescale;

            // An edit without duration plays the media to its end
            ends[i] = duration is 0 or ulong.MaxValue ? UInt128.Max(mediaEnd, starts[i]) : starts[i] + ((UInt128)duration * track.Timescale);
            if (i > 0 && starts[i] < ends[i - 1])
                throw AvifFormat.Unsupported("The AVIF image sequence has an edit list that plays its images out of order or several times.", "edit list");
        }

        var timeline = new AvifEditTimeline(starts, ends, movieTimescale, track.Timescale);
        timeline.PresentedCount = timeline.CountPresented(track);
        return timeline;
    }

    /// <summary>
    /// Gets how the next image of the track is shown. The images are read in their order: the first call is about the
    /// first image.
    /// </summary>
    /// <param name="track">The track.</param>
    /// <param name="duration">Receives the time the image is shown for.</param>
    /// <returns><see langword="false"/> when the image is not shown.</returns>
    /// <exception cref="UnsupportedImageFeatureException">The duration is not a fraction of two 63-bit numbers.</exception>
    public bool TryGetNext(AvifTrack track, out FrameDuration duration)
    {
        // The duration of the image, from the run it belongs to
        var runs = track.Durations;
        while (_run < runs.Count && _nextSample >= _runFirst + runs[_run].Count)
        {
            _runFirst += runs[_run].Count;
            _run++;
        }

        uint delta = _run < runs.Count ? runs[_run].Delta : 0;
        var start = _nextStart;
        var end = start + ((UInt128)delta * _movieTimescale);
        _nextSample++;
        _nextStart = end;

        // The edits that end before the image starts are behind every image that follows
        while (_edit < _starts.Length && _ends[_edit] < start)
        {
            _edit++;
        }

        var covered = UInt128.Zero;
        var isShown = false;
        for (var i = _edit; i < _starts.Length && _starts[i] <= end; i++)
        {
            if (delta is 0)
            {
                isShown |= _starts[i] <= start && start <= _ends[i];
                continue;
            }

            var from = UInt128.Max(_starts[i], start);
            var to = UInt128.Min(_ends[i], end);
            if (from < to)
            {
                covered += to - from;
                isShown = true;
            }
        }

        duration = isShown ? ToDuration(covered) : FrameDuration.Zero;
        return isShown;
    }

    // A time in units of 1 / (movie time scale x media time scale) second, as a fraction of a second: a whole number of
    // units of the media time scale when it is one, as the duration of an image without edit list is
    private FrameDuration ToDuration(UInt128 time)
    {
        if (time % _movieTimescale == UInt128.Zero)
            return new FrameDuration((long)(time / _movieTimescale), _mediaTimescale);

        var denominator = (UInt128)_movieTimescale * _mediaTimescale;
        var divisor = (UInt128)BigInteger.GreatestCommonDivisor((BigInteger)time, (BigInteger)denominator);
        var (n, d) = (time / divisor, denominator / divisor);
        if (n > long.MaxValue || d > long.MaxValue)
            throw AvifFormat.Unsupported("An image of the AVIF sequence is shown for a duration that is not a fraction of 63-bit numbers.", "edit list");

        return new FrameDuration((long)n, (long)d);
    }

    // The number of images that TryGetNext shows, computed run by run: the images of a run that an edit covers are
    // consecutive, and the edits are in the order of the media
    private long CountPresented(AvifTrack track)
    {
        long total = 0;
        var runStart = UInt128.Zero;
        var edit = 0;
        foreach (var (count, delta) in track.Durations)
        {
            if (count is 0)
                continue;

            var step = (UInt128)delta * _movieTimescale;
            var runEnd = runStart + (step * count);
            if (delta is 0)
            {
                // The images of the run are at the same time: an edit that contains it shows them all
                for (var i = edit; i < _starts.Length && _starts[i] <= runStart; i++)
                {
                    if (runStart <= _ends[i])
                    {
                        total += count;
                        break;
                    }
                }

                continue;
            }

            // The first image of the run that is not counted yet
            long next = 0;
            for (var i = edit; i < _starts.Length && _starts[i] < runEnd; i++)
            {
                if (_ends[i] <= runStart || _starts[i] >= _ends[i])
                {
                    if (_ends[i] <= runStart)
                        edit = i + 1;

                    continue;
                }

                // The image k covers [runStart + k step, runStart + (k + 1) step): the first one that ends after the start
                // of the edit, and the last one that starts before its end
                var first = _starts[i] <= runStart ? 0 : (long)((_starts[i] - runStart) / step);
                var last = _ends[i] >= runEnd ? count - 1 : (long)((_ends[i] - runStart - 1) / step);
                first = Math.Max(first, next);
                if (last >= first)
                {
                    total += last - first + 1;
                    next = last + 1;
                }
            }

            runStart = runEnd;
        }

        return total;
    }
}
