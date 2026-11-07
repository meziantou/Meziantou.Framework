namespace Meziantou.Framework.Fonts;

/// <summary>
/// Fits the outlines of a font to the pixel grid along the vertical axis only, without the instructions of the font:
/// the baseline, the x-height, the cap height, the ascenders and the descenders on whole pixels, and the horizontal
/// stems a whole number of pixels thick. Any outline (TrueType, CFF, CFF2) is hinted this way; the horizontal
/// coordinates and the advances do not change.
/// </summary>
/// <remarks>
/// <para>
/// The alignment zones of the font are measured once, from the boxes of reference letters (the flat top of x and H,
/// the round top of o and O, the bottom of o and p, the top of d), or from the x-height and the cap height of the
/// <c>OS/2</c> table: the zones a font has no reference letter for are left out. At the size, the flat height of each
/// zone is rounded to a whole pixel; the overshoot of the round letters is kept when it is half a pixel or more,
/// rounded too, and suppressed otherwise.
/// </para>
/// <para>
/// In a glyph, an edge is a horizontal segment of a contour, or a point where a curve is horizontal; which side its
/// ink is on follows from the direction of the contour. An edge in a zone, on the side of the zone, goes to the
/// height of the zone. Two edges that face each other over less than a fifth of an em, and overlap horizontally, are
/// a stem: its thickness is rounded to a whole pixel (at least one), from the edge that is in a zone, or around its
/// middle. The other points move with the edges: the vertical coordinates are mapped by the piecewise linear
/// function through the edges that moved (in increasing order, never crossing), and shifted like the nearest edge
/// beyond the first and the last one.
/// </para>
/// </remarks>
internal sealed class LightHinter
{
    private const byte MoveToCommand = 0;
    private const byte LineToCommand = 1;
    private const byte QuadraticToCommand = 2;
    private const byte CubicToCommand = 3;
    private const byte CloseCommand = 4;

    private readonly double _scale;
    private readonly double _upem;
    private readonly List<Zone> _zones = [];
    private readonly List<Edge> _edges = [];
    private readonly List<(double Original, double Hinted, int Priority)> _constraints = [];
    private readonly Recorder _recorder = new();

    private LightHinter(double size, int unitsPerEm)
    {
        _scale = size / unitsPerEm;
        _upem = unitsPerEm;
    }

    /// <summary>Creates the hinter of a font at a size: measures the alignment zones of the font.</summary>
    /// <exception cref="InvalidFontException">The data of a reference letter is malformed.</exception>
    /// <exception cref="FontResourceLimitException">A reference letter exceeds the complexity limit or the allocation limit.</exception>
    public static LightHinter Create(FontFace face, double size, GlyphScratch scratch)
    {
        var hinter = new LightHinter(size, face.UnitsPerEm);
        var xHeight = GetTop(face, 'x', scratch) ?? (face.XHeight > 0 ? face.XHeight : null);
        var capHeight = GetTop(face, 'H', scratch) ?? (face.CapHeight > 0 ? face.CapHeight : null);
        var roundBottom = GetBottom(face, 'o', scratch);
        if (roundBottom is not null || xHeight is not null || capHeight is not null)
        {
            hinter.AddZone(0, Math.Min(0, roundBottom ?? 0), isTop: false);
        }

        if (xHeight is { } x)
        {
            hinter.AddZone(x, Math.Max(x, GetTop(face, 'o', scratch) ?? x), isTop: true);
        }

        if (capHeight is { } cap)
        {
            hinter.AddZone(cap, Math.Max(cap, GetTop(face, 'O', scratch) ?? cap), isTop: true);
        }

        if (GetTop(face, 'd', scratch) is { } ascender && (capHeight is null || ascender > capHeight + (face.UnitsPerEm / 64.0)))
        {
            hinter.AddZone(ascender, ascender, isTop: true);
        }

        if (GetBottom(face, 'p', scratch) is { } descender && descender < 0)
        {
            hinter.AddZone(descender, descender, isTop: false);
        }

        return hinter;
    }

    private static double? GetTop(FontFace face, char character, GlyphScratch scratch)
    {
        var glyph = face.MapCodePoint(character);
        return glyph != 0 && face.TryGetGlyphBounds(glyph, scratch, out var box) ? box.YMax : null;
    }

    private static double? GetBottom(FontFace face, char character, GlyphScratch scratch)
    {
        var glyph = face.MapCodePoint(character);
        return glyph != 0 && face.TryGetGlyphBounds(glyph, scratch, out var box) ? box.YMin : null;
    }

    private void AddZone(double flat, double overshoot, bool isTop)
    {
        var flatPixels = flat * _scale;
        var snappedFlat = Math.Round(flatPixels, MidpointRounding.AwayFromZero);
        var overshootPixels = (overshoot - flat) * _scale;
        var snappedOvershoot = Math.Abs(overshootPixels) < 0.5 ? snappedFlat : snappedFlat + Math.Round(overshootPixels, MidpointRounding.AwayFromZero);
        _zones.Add(new Zone(Math.Min(flat, overshoot), Math.Max(flat, overshoot), flat, overshoot, snappedFlat, snappedOvershoot, isTop));
    }

    /// <summary>Decodes the outline of a glyph, hinted, into a sink, in font units.</summary>
    /// <exception cref="InvalidFontException">The data of the glyph is malformed.</exception>
    /// <exception cref="FontResourceLimitException">The glyph exceeds the complexity limit or the allocation limit.</exception>
    public void Decode(FontFace face, int glyphId, OutlineSink sink, GlyphScratch scratch, ref GlyphBudget budget)
    {
        scratch.Reset();
        _recorder.Begin(scratch);
        face.DecodeOutline(glyphId, _recorder, scratch, ref budget);
        var commands = _recorder.Commands;
        if (_recorder.Start < 0)
            return;

        var work = scratch.Work[_recorder.Start..];
        budget.Charge(commands.Count);
        FindEdges(commands, work);
        BuildConstraints();
        Emit(commands, work, sink);
    }

    // ---- Edges -------------------------------------------------------------------------------------------------

    private void FindEdges(List<byte> commands, Span<double> coordinates)
    {
        _edges.Clear();

        // The direction of the contours: the sign of the area of the outline (the curves count as their control polygon).
        var area = 0.0;
        double startX = 0, startY = 0, lastX = 0, lastY = 0;
        var index = 0;
        foreach (var command in commands)
        {
            var count = GetCoordinateCount(command);
            if (command == MoveToCommand)
            {
                startX = lastX = coordinates[index];
                startY = lastY = coordinates[index + 1];
            }
            else if (command == CloseCommand)
            {
                area += (lastX * startY) - (startX * lastY);
                lastX = startX;
                lastY = startY;
            }
            else
            {
                for (var k = 0; k < count; k += 2)
                {
                    var x = coordinates[index + k];
                    var y = coordinates[index + k + 1];
                    area += (lastX * y) - (x * lastY);
                    lastX = x;
                    lastY = y;
                }
            }

            index += count;
        }

        var isClockwise = area < 0;
        var tolerance = 0.5 + (_upem / 500);
        var minimumLength = _upem / 200;
        index = 0;
        foreach (var command in commands)
        {
            var count = GetCoordinateCount(command);
            switch (command)
            {
                case MoveToCommand:
                    lastX = coordinates[index];
                    lastY = coordinates[index + 1];
                    break;

                case LineToCommand:
                {
                    var x = coordinates[index];
                    var y = coordinates[index + 1];
                    if (Math.Abs(y - lastY) <= tolerance && Math.Abs(x - lastX) >= minimumLength)
                    {
                        AddEdge((y + lastY) / 2, lastX, x, isClockwise);
                    }

                    lastX = x;
                    lastY = y;
                    break;
                }

                case QuadraticToCommand:
                case CubicToCommand:
                {
                    var firstControlX = coordinates[index];
                    var firstControlY = coordinates[index + 1];
                    var lastControlX = coordinates[index + count - 4];
                    var lastControlY = coordinates[index + count - 3];
                    var x = coordinates[index + count - 2];
                    var y = coordinates[index + count - 1];

                    // A curve that leaves or reaches a point horizontally has an extremum there.
                    if (Math.Abs(firstControlY - lastY) <= tolerance && firstControlX != lastX)
                    {
                        AddEdge(lastY, lastX, firstControlX, isClockwise);
                    }

                    if (Math.Abs(lastControlY - y) <= tolerance && lastControlX != x)
                    {
                        AddEdge(y, lastControlX, x, isClockwise);
                    }

                    lastX = x;
                    lastY = y;
                    break;
                }

                case CloseCommand:
                    break;
            }

            index += count;
        }
    }

    private static int GetCoordinateCount(byte command) => command switch
    {
        MoveToCommand or LineToCommand => 2,
        QuadraticToCommand => 4,
        CubicToCommand => 6,
        _ => 0,
    };

    private void AddEdge(double y, double fromX, double toX, bool isClockwise)
    {
        // A clockwise contour has its ink below the parts it draws to the right.
        var isTop = (toX > fromX) == isClockwise;
        var minX = Math.Min(fromX, toX);
        var maxX = Math.Max(fromX, toX);
        var tolerance = 0.5 + (_upem / 500);
        for (var i = 0; i < _edges.Count; i++)
        {
            var edge = _edges[i];
            if (edge.IsTop == isTop && Math.Abs(edge.Y - y) <= tolerance)
            {
                _edges[i] = edge with { MinX = Math.Min(edge.MinX, minX), MaxX = Math.Max(edge.MaxX, maxX) };
                return;
            }
        }

        _edges.Add(new Edge(y, minX, maxX, isTop));
    }

    // ---- Constraints -------------------------------------------------------------------------------------------

    private void BuildConstraints()
    {
        _constraints.Clear();
        Span<double> hinted = _edges.Count <= 256 ? stackalloc double[_edges.Count] : new double[_edges.Count];
        hinted.Fill(double.NaN);

        // The edges in an alignment zone go to its height.
        var fuzz = _upem / 64;
        for (var i = 0; i < _edges.Count; i++)
        {
            var edge = _edges[i];
            foreach (var zone in _zones)
            {
                if (zone.IsTop != edge.IsTop || edge.Y < zone.Low - fuzz || edge.Y > zone.High + fuzz)
                    continue;

                hinted[i] = Math.Abs(edge.Y - zone.Flat) <= Math.Abs(edge.Y - zone.Overshoot) ? zone.SnappedFlat : zone.SnappedOvershoot;
                _constraints.Add((edge.Y * _scale, hinted[i], 2));
                break;
            }
        }

        // The stems: a bottom edge and the nearest top edge above it that overlaps it.
        var maximumStem = _upem / 5;
        var overlap = _upem / 50;
        for (var i = 0; i < _edges.Count; i++)
        {
            var bottom = _edges[i];
            if (bottom.IsTop)
                continue;

            var best = -1;
            for (var j = 0; j < _edges.Count; j++)
            {
                var top = _edges[j];
                if (!top.IsTop || top.Y <= bottom.Y || top.Y - bottom.Y > maximumStem)
                    continue;

                if (top.MaxX + overlap < bottom.MinX || bottom.MaxX + overlap < top.MinX)
                    continue;

                if (best < 0 || top.Y < _edges[best].Y)
                {
                    best = j;
                }
            }

            if (best < 0)
                continue;

            var width = Math.Max(1, Math.Round((_edges[best].Y - bottom.Y) * _scale, MidpointRounding.AwayFromZero));
            double newBottom;
            double newTop;
            if (!double.IsNaN(hinted[i]))
            {
                newBottom = hinted[i];
                newTop = double.IsNaN(hinted[best]) ? newBottom + width : hinted[best];
            }
            else if (!double.IsNaN(hinted[best]))
            {
                newTop = hinted[best];
                newBottom = newTop - width;
            }
            else
            {
                var middle = (bottom.Y + _edges[best].Y) / 2 * _scale;
                newBottom = Math.Round(middle - (width / 2), MidpointRounding.AwayFromZero);
                newTop = newBottom + width;
            }

            if (double.IsNaN(hinted[i]))
            {
                hinted[i] = newBottom;
                _constraints.Add((bottom.Y * _scale, newBottom, 1));
            }

            if (double.IsNaN(hinted[best]))
            {
                hinted[best] = newTop;
                _constraints.Add((_edges[best].Y * _scale, newTop, 1));
            }
        }

        // In increasing order; of two constraints at one height, the one of a zone. A constraint that would make the
        // mapping decrease is dropped.
        _constraints.Sort(static (a, b) => a.Original != b.Original ? a.Original.CompareTo(b.Original) : b.Priority.CompareTo(a.Priority));

        // A gap of three quarters of a pixel or more between two edges keeps a pixel: the dot of an i does not touch
        // its stem. The edges of stems above the gap move up with it, up to the next edge of an alignment zone.
        for (var i = 1; i < _constraints.Count; i++)
        {
            var previous = _constraints[i - 1];
            var current = _constraints[i];
            if (current.Priority != 1 || current.Original - previous.Original < 0.75 || current.Hinted - previous.Hinted >= 1)
                continue;

            var shift = 1 - (current.Hinted - previous.Hinted);
            for (var j = i; j < _constraints.Count && _constraints[j].Priority == 1; j++)
            {
                _constraints[j] = _constraints[j] with { Hinted = _constraints[j].Hinted + shift };
            }
        }

        var kept = 0;
        for (var i = 0; i < _constraints.Count; i++)
        {
            var constraint = _constraints[i];
            if (kept > 0)
            {
                var previous = _constraints[kept - 1];
                if (constraint.Original - previous.Original < 1e-9 || constraint.Hinted < previous.Hinted)
                    continue;
            }

            _constraints[kept++] = constraint;
        }

        _constraints.RemoveRange(kept, _constraints.Count - kept);
    }

    /// <summary>Maps a vertical coordinate, in font units.</summary>
    private double MapY(double y)
    {
        if (_constraints.Count == 0)
            return y;

        var pixels = y * _scale;
        var first = _constraints[0];
        if (pixels <= first.Original)
            return (pixels + first.Hinted - first.Original) / _scale;

        for (var i = 1; i < _constraints.Count; i++)
        {
            var next = _constraints[i];
            if (pixels <= next.Original)
            {
                var previous = _constraints[i - 1];
                var t = (pixels - previous.Original) / (next.Original - previous.Original);
                return (previous.Hinted + ((next.Hinted - previous.Hinted) * t)) / _scale;
            }
        }

        var last = _constraints[^1];
        return (pixels + last.Hinted - last.Original) / _scale;
    }

    private void Emit(List<byte> commands, Span<double> coordinates, OutlineSink sink)
    {
        var index = 0;
        foreach (var command in commands)
        {
            switch (command)
            {
                case MoveToCommand:
                    sink.MoveTo(coordinates[index], MapY(coordinates[index + 1]));
                    break;
                case LineToCommand:
                    sink.LineTo(coordinates[index], MapY(coordinates[index + 1]));
                    break;
                case QuadraticToCommand:
                    sink.QuadraticTo(coordinates[index], MapY(coordinates[index + 1]), coordinates[index + 2], MapY(coordinates[index + 3]));
                    break;
                case CubicToCommand:
                    sink.CubicTo(coordinates[index], MapY(coordinates[index + 1]), coordinates[index + 2], MapY(coordinates[index + 3]), coordinates[index + 4], MapY(coordinates[index + 5]));
                    break;
                default:
                    sink.Close();
                    break;
            }

            index += GetCoordinateCount(command);
        }
    }

    private readonly record struct Zone(double Low, double High, double Flat, double Overshoot, double SnappedFlat, double SnappedOvershoot, bool IsTop);

    private readonly record struct Edge(double Y, double MinX, double MaxX, bool IsTop);

    /// <summary>Records an outline: its commands in a list, its coordinates in the working numbers of a scratch.</summary>
    private sealed class Recorder : OutlineSink
    {
        private GlyphScratch? _scratch;

        public List<byte> Commands { get; } = [];

        /// <summary>Gets the index of the first coordinate in the working numbers of the scratch.</summary>
        public int Start { get; private set; } = -1;

        public void Begin(GlyphScratch scratch)
        {
            _scratch = scratch;
            Commands.Clear();
            Start = -1;
        }

        public override void MoveTo(double x, double y) => Add(MoveToCommand, x, y);

        public override void LineTo(double x, double y) => Add(LineToCommand, x, y);

        public override void QuadraticTo(double controlX, double controlY, double x, double y)
        {
            Add(QuadraticToCommand, controlX, controlY);
            Append(x, y);
        }

        public override void CubicTo(double control1X, double control1Y, double control2X, double control2Y, double x, double y)
        {
            Add(CubicToCommand, control1X, control1Y);
            Append(control2X, control2Y);
            Append(x, y);
        }

        public override void Close() => Commands.Add(CloseCommand);

        private void Add(byte command, double x, double y)
        {
            Commands.Add(command);
            Append(x, y);
        }

        private void Append(double x, double y)
        {
            var index = _scratch!.RentWork(2);
            if (Start < 0)
            {
                Start = index;
            }

            var work = _scratch.Work;
            work[index] = x;
            work[index + 1] = y;
        }
    }
}
