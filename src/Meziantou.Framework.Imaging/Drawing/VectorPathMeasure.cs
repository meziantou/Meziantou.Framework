using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>
/// The chords of a path, computed once, to answer many questions about its length, its points and its directions: the
/// position of a marker that moves along the path, or the point a pointer snaps to as it moves.
/// </summary>
/// <remarks>
/// <para>
/// The answers are those of <see cref="VectorPath.GetLength"/>, <see cref="VectorPath.TryGetPointAtDistance"/> and
/// <see cref="VectorPath.TryGetClosestPoint"/>, bit for bit: the path is measured by the chords it is drawn with, within
/// 1/16 of a pixel of its curves, a closed contour includes its closing segment, and the contours follow each other
/// without the gap between them. Those methods walk the whole path at each call; a measure walks it once, when it is
/// created, then finds a point at a distance by a binary search, and the closest point by skipping the groups of chords
/// that are farther than the nearest one found.
/// </para>
/// <para>
/// A path cannot change once it is built: a measure keeps answering for the path it was created from
/// (<see cref="Path"/>), and is thread-safe. It holds 48 bytes per chord.
/// </para>
/// </remarks>
public sealed class VectorPathMeasure
{
    // For each chord: the coordinates of its start, the vector to its end, its length and the distance from the start of
    // the path to its end
    private const int ValuesPerChord = 6;

    // The chords are grouped for the search of the closest point: the box of each group bounds the distance of its chords
    private const int ChordsPerGroup = 32;

    private readonly double[] _chords;
    private readonly double[] _groups;
    private readonly int _count;
    private readonly double _lastX;
    private readonly double _lastY;

    /// <summary>Initializes a new instance of the <see cref="VectorPathMeasure"/> class: measures a path.</summary>
    /// <param name="path">The path.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public VectorPathMeasure(VectorPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Path = path;
        var sink = new ChordSink();
        PathFlattener.Flatten(path, sink);
        _count = sink.Count;
        _chords = sink.ToArray();
        _lastX = sink.LastX;
        _lastY = sink.LastY;
        Length = sink.Length;
        _groups = CreateGroups(_chords, _count);
    }

    /// <summary>Gets the path that is measured.</summary>
    public VectorPath Path { get; }

    /// <summary>Gets the length of the path: what <see cref="VectorPath.GetLength"/> gives.</summary>
    public double Length { get; }

    /// <summary>Finds the point at a distance from the start of the path, and the direction of the path there.</summary>
    /// <remarks>
    /// The answer is the one of <see cref="VectorPath.TryGetPointAtDistance"/>: a distance before the start gives the
    /// first point, a distance beyond the end the last one, and at a corner the direction is the one of the segment that
    /// starts there. The time grows with the logarithm of the number of chords.
    /// </remarks>
    /// <param name="distance">The distance from the start of the path, in pixels.</param>
    /// <param name="point">The point of the path at that distance.</param>
    /// <param name="tangent">The direction of the path at that point, as a vector of length 1.</param>
    /// <returns><see langword="false"/> when the path has no length: there is no point to find, and no direction.</returns>
    /// <exception cref="ArgumentException"><paramref name="distance"/> is not a number.</exception>
    public bool TryGetPointAtDistance(double distance, out PointD point, out PointD tangent)
    {
        if (double.IsNaN(distance))
            throw new ArgumentException("The distance must be a number.", nameof(distance));

        if (_count == 0)
        {
            point = default;
            tangent = default;
            return false;
        }

        // The chord that contains a distance is the first one that ends after it: the distance that is the length of the
        // path is beyond every chord, and gives the last point itself
        var chords = _chords.AsSpan();
        var low = 0;
        var high = _count;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            if (distance < chords[(middle * ValuesPerChord) + 5])
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        var chord = chords.Slice(Math.Min(low, _count - 1) * ValuesPerChord, ValuesPerChord);
        var directionX = chord[2] / chord[4];
        var directionY = chord[3] / chord[4];
        tangent = new PointD(directionX, directionY);
        if (low == _count)
        {
            point = new PointD(_lastX, _lastY);
            return true;
        }

        var along = distance - GetStart(low);
        along = along > 0 ? along : 0;
        point = new PointD(chord[0] + (along * directionX), chord[1] + (along * directionY));
        return true;
    }

    /// <summary>Finds the point of the path that is the nearest to a position, and its distance along the path.</summary>
    /// <remarks>
    /// The answer is the one of <see cref="VectorPath.TryGetClosestPoint"/>: when several points are as near, the first
    /// one along the path is given. The groups of chords whose box is farther than the nearest point found are skipped:
    /// the time grows with the number of chords near the position, and with the number of groups.
    /// </remarks>
    /// <param name="point">The position.</param>
    /// <param name="closest">The nearest point of the path.</param>
    /// <param name="distanceAlongPath">The distance from the start of the path to that point, along the path, in pixels.</param>
    /// <returns><see langword="false"/> when the path has no length: there is no point to find.</returns>
    /// <exception cref="ArgumentException">A coordinate of <paramref name="point"/> is not a finite number.</exception>
    public bool TryGetClosestPoint(PointD point, out PointD closest, out double distanceAlongPath)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw new ArgumentException("The coordinates must be finite numbers.", nameof(point));

        if (_count == 0)
        {
            closest = default;
            distanceAlongPath = 0;
            return false;
        }

        // The nearest group, by the distance of its box, gives a bound: a group whose box is farther cannot hold the
        // nearest point. The boxes are enlarged by more than the rounding of the points of their chords, and the bound
        // by more than the rounding of the squared distances, so that no chord that the walk along the whole path
        // would keep is skipped.
        var groupCount = _groups.Length / 4;
        var nearestGroup = 0;
        var nearestBound = double.PositiveInfinity;
        for (var group = 0; group < groupCount; group++)
        {
            var bound = GetGroupDistance(group, point.X, point.Y);
            if (bound < nearestBound)
            {
                nearestBound = bound;
                nearestGroup = group;
            }
        }

        var best = double.PositiveInfinity;
        var closestX = 0d;
        var closestY = 0d;
        var distance = 0d;
        FindClosest(nearestGroup, point.X, point.Y, ref best, ref closestX, ref closestY, ref distance);
        var limit = best * (1 + (1.0 / (1L << 40)));

        // The chords are then walked in the order of the path, so that the first of the nearest points is kept
        best = double.PositiveInfinity;
        for (var group = 0; group < groupCount; group++)
        {
            if (GetGroupDistance(group, point.X, point.Y) > limit)
                continue;

            FindClosest(group, point.X, point.Y, ref best, ref closestX, ref closestY, ref distance);
        }

        // A position so far that every squared distance overflows has no nearer point, as for the path itself
        closest = new PointD(closestX, closestY);
        distanceAlongPath = distance;
        return best < double.PositiveInfinity;
    }

    private double GetStart(int chord) => chord == 0 ? 0 : _chords[((chord - 1) * ValuesPerChord) + 5];

    /// <summary>Gets the squared distance from a position to the box of a group of chords.</summary>
    private double GetGroupDistance(int group, double x, double y)
    {
        var box = _groups.AsSpan(group * 4, 4);
        var dx = Math.Max(Math.Max(box[0] - x, x - box[2]), 0);
        var dy = Math.Max(Math.Max(box[1] - y, y - box[3]), 0);
        return (dx * dx) + (dy * dy);
    }

    /// <summary>Keeps the nearest point of the chords of a group when it is nearer than the best one so far: the computation of <see cref="ClosestPointSink"/>.</summary>
    private void FindClosest(int group, double x, double y, ref double best, ref double closestX, ref double closestY, ref double distance)
    {
        var end = Math.Min((group + 1) * ChordsPerGroup, _count);
        for (var i = group * ChordsPerGroup; i < end; i++)
        {
            var chord = _chords.AsSpan(i * ValuesPerChord, ValuesPerChord);
            var dx = chord[2];
            var dy = chord[3];
            var t = (((x - chord[0]) * dx) + ((y - chord[1]) * dy)) / ((dx * dx) + (dy * dy));
            t = t > 0 ? (t < 1 ? t : 1) : 0;
            var px = chord[0] + (t * dx);
            var py = chord[1] + (t * dy);
            var squaredDistance = ((x - px) * (x - px)) + ((y - py) * (y - py));
            if (squaredDistance < best)
            {
                best = squaredDistance;
                closestX = px;
                closestY = py;
                distance = GetStart(i) + (t * chord[4]);
            }
        }
    }

    /// <summary>Computes the boxes of the groups of chords, enlarged by more than the rounding of the points of their chords.</summary>
    private static double[] CreateGroups(double[] chords, int count)
    {
        var groupCount = (count + ChordsPerGroup - 1) / ChordsPerGroup;
        var groups = new double[groupCount * 4];
        var largest = 0d;
        for (var i = 0; i < count; i++)
        {
            largest = Math.Max(largest, Math.Max(Math.Abs(chords[i * ValuesPerChord]), Math.Abs(chords[(i * ValuesPerChord) + 1])));
            largest = Math.Max(largest, Math.Max(Math.Abs(chords[(i * ValuesPerChord) + 2]), Math.Abs(chords[(i * ValuesPerChord) + 3])));
        }

        var margin = (1 + largest) / (1L << 32);
        for (var group = 0; group < groupCount; group++)
        {
            var minX = double.PositiveInfinity;
            var minY = double.PositiveInfinity;
            var maxX = double.NegativeInfinity;
            var maxY = double.NegativeInfinity;
            var end = Math.Min((group + 1) * ChordsPerGroup, count);
            for (var i = group * ChordsPerGroup; i < end; i++)
            {
                var x = chords[i * ValuesPerChord];
                var y = chords[(i * ValuesPerChord) + 1];
                var endX = x + chords[(i * ValuesPerChord) + 2];
                var endY = y + chords[(i * ValuesPerChord) + 3];
                minX = Math.Min(minX, Math.Min(x, endX));
                minY = Math.Min(minY, Math.Min(y, endY));
                maxX = Math.Max(maxX, Math.Max(x, endX));
                maxY = Math.Max(maxY, Math.Max(y, endY));
            }

            groups[group * 4] = minX - margin;
            groups[(group * 4) + 1] = minY - margin;
            groups[(group * 4) + 2] = maxX + margin;
            groups[(group * 4) + 3] = maxY + margin;
        }

        return groups;
    }

    /// <summary>
    /// Records the chords of a flattened path as <see cref="DistanceSink"/> and <see cref="ClosestPointSink"/> walk them:
    /// a chord without length is left out, and a closed contour includes its closing segment.
    /// </summary>
    private sealed class ChordSink : PathSink
    {
        private double[] _chords = [];
        private bool _hasCurrent;
        private double _x;
        private double _y;
        private double _startX;
        private double _startY;

        public int Count { get; private set; }

        public double Length { get; private set; }

        /// <summary>Gets the end of the last chord: the point beyond the end of the path.</summary>
        public double LastX { get; private set; }

        public double LastY { get; private set; }

        public double[] ToArray() => _chords.Length == Count * ValuesPerChord ? _chords : _chords.AsSpan(0, Count * ValuesPerChord).ToArray();

        public override void MoveTo(double x, double y)
        {
            _x = _startX = x;
            _y = _startY = y;
            _hasCurrent = true;
        }

        public override void LineTo(double x, double y, bool smooth)
        {
            if (!_hasCurrent)
            {
                MoveTo(x, y);
                return;
            }

            var dx = x - _x;
            var dy = y - _y;
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length > 0)
            {
                if ((Count + 1) * ValuesPerChord > _chords.Length)
                {
                    Array.Resize(ref _chords, Math.Max(64 * ValuesPerChord, _chords.Length * 2));
                }

                var chord = _chords.AsSpan(Count * ValuesPerChord, ValuesPerChord);
                chord[0] = _x;
                chord[1] = _y;
                chord[2] = dx;
                chord[3] = dy;
                chord[4] = length;
                Length += length;
                chord[5] = Length;
                LastX = x;
                LastY = y;
                Count++;
            }

            _x = x;
            _y = y;
        }

        public override void Close(bool smooth)
        {
            if (_hasCurrent)
            {
                LineTo(_startX, _startY, smooth);
            }
        }

        public override void End()
        {
        }
    }
}
