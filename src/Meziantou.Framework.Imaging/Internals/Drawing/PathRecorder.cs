namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Keeps the contours of a flattened path, to send them to other sinks: a path that is both a clip and a stroke.</summary>
/// <remarks>The storage grows to the largest path and is kept: recording another path allocates nothing.</remarks>
internal sealed class PathRecorder : PathSink, IDisposable
{
    private const int ValuesPerEntry = 3;
    private const double MoveToEntry = 2;
    private const double CloseEntry = 3;

    private readonly GrowableBuffer _entries;
    private double _minX;
    private double _minY;
    private double _maxX;
    private double _maxY;

    public PathRecorder(AllocationScope? scope)
    {
        _entries = new GrowableBuffer(scope);
    }

    /// <summary>Gets a value indicating whether the path has a point.</summary>
    public bool HasBounds { get; private set; }

    /// <summary>Gets the rectangle that contains every point of the path.</summary>
    public RectangleD Bounds => HasBounds ? RectangleD.FromLTRB(_minX, _minY, _maxX, _maxY) : default;

    /// <summary>Removes every contour and keeps the storage.</summary>
    public void Reset()
    {
        _entries.Count = 0;
        HasBounds = false;
    }

    public override void MoveTo(double x, double y) => Add(x, y, MoveToEntry);

    public override void LineTo(double x, double y, bool smooth) => Add(x, y, smooth ? 1 : 0);

    public override void Close(bool smooth)
    {
        var entry = _entries.Append(ValuesPerEntry);
        entry[0] = smooth ? 1 : 0;
        entry[1] = 0;
        entry[2] = CloseEntry;
    }

    public override void End()
    {
    }

    /// <summary>Sends the contours to a sink, as they were received, and ends its path.</summary>
    public void Replay(PathSink sink)
    {
        var entries = _entries.Values;
        for (var i = 0; i < entries.Length; i += ValuesPerEntry)
        {
            switch (entries[i + 2])
            {
                case MoveToEntry:
                    sink.MoveTo(entries[i], entries[i + 1]);
                    break;

                case CloseEntry:
                    sink.Close(entries[i] != 0);
                    break;

                default:
                    sink.LineTo(entries[i], entries[i + 1], entries[i + 2] != 0);
                    break;
            }
        }

        sink.End();
    }

    public void Dispose() => _entries.Dispose();

    private void Add(double x, double y, double kind)
    {
        var entry = _entries.Append(ValuesPerEntry);
        entry[0] = x;
        entry[1] = y;
        entry[2] = kind;
        if (!HasBounds)
        {
            _minX = _maxX = x;
            _minY = _maxY = y;
            HasBounds = true;
            return;
        }

        _minX = Math.Min(_minX, x);
        _minY = Math.Min(_minY, y);
        _maxX = Math.Max(_maxX, x);
        _maxY = Math.Max(_maxY, y);
    }
}
