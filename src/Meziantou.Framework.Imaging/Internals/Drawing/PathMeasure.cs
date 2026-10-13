using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Measures a flattened path: records its chords and finds the point at a distance along them, with the direction of
/// the path there.
/// </summary>
/// <remarks>
/// <para>
/// The path is the chords the flattener gives for it, within the tolerance of every curve of the drawing API. Its
/// contours are measured one after the other: the distance continues from the end of a contour to the start of the next
/// one, without the gap between them. A closed contour includes its closing segment.
/// </para>
/// <para>
/// Only additions, multiplications, divisions and square roots are used, and the direction of a chord is its
/// normalized vector: nothing depends on a trigonometric function of the platform.
/// </para>
/// <para>The storage is rented from an allocation scope and kept: measuring another path allocates nothing once it has grown.</para>
/// </remarks>
internal sealed class PathMeasure : PathSink, IDisposable
{
    private const int InitialCapacity = 64;

    private readonly AllocationScope _scope;
    private PooledBuffer? _buffer;
    private int _capacity;
    private int _count;
    private bool _hasCurrent;
    private double _x;
    private double _y;
    private double _startX;
    private double _startY;
    private int _contours;
    private bool _isAfterClose;

    public PathMeasure(AllocationScope scope)
    {
        _scope = scope;
    }

    /// <summary>Gets the length of the path.</summary>
    public double Length { get; private set; }

    /// <summary>Gets a value indicating whether the path is one closed contour: a distance beyond its end continues from its start.</summary>
    public bool IsClosed { get; private set; }

    private ReadOnlySpan<Chord> Chords => _buffer is null ? default : unsafe(MemoryMarshal.Cast<byte, Chord>(_buffer.Span))[.._count];

    /// <summary>Removes every chord and keeps the storage.</summary>
    public void Reset()
    {
        _count = 0;
        _hasCurrent = false;
        _contours = 0;
        _isAfterClose = false;
        Length = 0;
        IsClosed = false;
    }

    public override void MoveTo(double x, double y)
    {
        _x = _startX = x;
        _y = _startY = y;
        _hasCurrent = true;
        _isAfterClose = false;
        _contours++;
        IsClosed = false;
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

        // A chord without length has no direction: it is left out.
        if (length > 0)
        {
            if (_isAfterClose)
            {
                // A segment after a closed contour starts another contour at the same point.
                _isAfterClose = false;
                _contours++;
                IsClosed = false;
            }

            if (_count == _capacity)
            {
                Grow();
            }

            ref var chord = ref unsafe(MemoryMarshal.Cast<byte, Chord>(_buffer!.Span))[_count++];
            chord.X = _x;
            chord.Y = _y;
            chord.DirectionX = dx / length;
            chord.DirectionY = dy / length;
            chord.Start = Length;
            chord.Length = length;
            Length += length;
        }

        _x = x;
        _y = y;
    }

    public override void Close(bool smooth)
    {
        if (!_hasCurrent)
            return;

        LineTo(_startX, _startY, smooth);
        IsClosed = _contours == 1;
        _isAfterClose = true;
    }

    public override void End()
    {
    }

    /// <summary>Gets the point at a distance from the start of the path, and the unit vector of the direction of the path there.</summary>
    /// <param name="distance">The distance. Before the start or beyond the end, the point is on the line that continues the first or the last chord.</param>
    /// <param name="x">The horizontal coordinate of the point.</param>
    /// <param name="y">The vertical coordinate of the point.</param>
    /// <param name="directionX">The horizontal component of the direction.</param>
    /// <param name="directionY">The vertical component of the direction.</param>
    /// <returns><see langword="false"/> when the path has no chord.</returns>
    public bool TryGetPoint(double distance, out double x, out double y, out double directionX, out double directionY)
    {
        var chords = Chords;
        if (chords.IsEmpty)
        {
            x = y = directionX = directionY = 0;
            return false;
        }

        // The chord that contains the distance: the last one that starts at or before it.
        var low = 0;
        var high = chords.Length - 1;
        while (low < high)
        {
            var middle = (low + high + 1) >>> 1;
            if (chords[middle].Start <= distance)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        ref readonly var chord = ref chords[low];
        var along = distance - chord.Start;
        directionX = chord.DirectionX;
        directionY = chord.DirectionY;
        x = chord.X + (along * directionX);
        y = chord.Y + (along * directionY);
        return true;
    }

    /// <summary>
    /// Gets the end of the chord that contains a distance: where the direction of the path can change. Beyond the last
    /// chord, the path continues straight without end.
    /// </summary>
    public double GetChordEnd(double distance)
    {
        var chords = Chords;
        if (chords.IsEmpty || distance >= chords[^1].Start)
            return chords.IsEmpty || distance >= Length ? double.PositiveInfinity : Length;

        if (distance < 0)
            return 0;

        var low = 0;
        var high = chords.Length - 1;
        while (low < high)
        {
            var middle = (low + high + 1) >>> 1;
            if (chords[middle].Start <= distance)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return chords[low].Start + chords[low].Length;
    }

    /// <summary>Gets the start of the chord that contains a distance; a distance at the start of a chord belongs to the chord before.</summary>
    public double GetChordStartBefore(double distance)
    {
        var chords = Chords;
        if (chords.IsEmpty || distance <= 0)
            return double.NegativeInfinity;

        if (distance > Length)
            return Length;

        // The last chord that starts before the distance.
        var low = 0;
        var high = chords.Length - 1;
        while (low < high)
        {
            var middle = (low + high + 1) >>> 1;
            if (chords[middle].Start < distance)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return chords[low].Start;
    }

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
        _capacity = 0;
        _count = 0;
    }

    private void Grow()
    {
        var capacity = _capacity == 0 ? InitialCapacity : (long)_capacity * 2;
        var buffer = ResampleWeights.Rent<Chord>(_scope, capacity);
        if (_buffer is not null)
        {
            _buffer.Span.CopyTo(buffer.Span);
            _buffer.Dispose();
        }

        _buffer = buffer;
        _capacity = (int)capacity;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Chord
    {
        public double X;
        public double Y;
        public double DirectionX;
        public double DirectionY;

        /// <summary>The distance from the start of the path to the start of the chord.</summary>
        public double Start;
        public double Length;
    }
}
