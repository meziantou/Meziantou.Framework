namespace Meziantou.Framework.Fonts;

/// <summary>
/// Fits the CFF and CFF2 outlines of a font to the pixel grid along the vertical axis with the hints of the font: the
/// horizontal stem hints of each glyph (<c>hstem</c>, <c>hstemhm</c>, with the hint replacement of <c>hintmask</c>) and
/// the alignment zones of its private DICT (<see cref="CffHintValues"/>). The horizontal coordinates do not change.
/// </summary>
/// <remarks>
/// <para>
/// Adobe Technical Note #5177, section 4.3, and the Type 1 font format (Adobe Type 1 Font Format, chapter 5): the zones
/// of <c>BlueValues</c> (the first one is the zone of the baseline) and of <c>OtherBlues</c>, extended by
/// <c>BlueFuzz</c>. At the size, the flat edge of each zone is rounded to a whole pixel. Below <c>BlueScale</c> pixels
/// per unit, the overshoots are suppressed: an edge in a zone goes to its flat edge; above, it keeps its overshoot,
/// rounded, and an overshoot of <c>BlueShift</c> units or more is at least one pixel.
/// </para>
/// <para>
/// A stem whose edge is in a zone on its side (the bottom edge in a zone of a bottom, the top edge in a zone of a top)
/// is aligned with the zone, and its other edge is a whole number of pixels away (at least one); a stem in no zone
/// keeps its middle and is a whole number of pixels thick, its bottom edge on a pixel. A ghost hint (width -20 for a
/// top edge, -21 for a bottom edge) has one edge: aligned with its zone, or rounded. The points between two edges are
/// placed in proportion, those beyond the first and the last edge move like them, as with a hint map.
/// </para>
/// <para>
/// A <c>hintmask</c> replaces the stems that apply: the points the charstring draws after it are placed with the
/// stems it selects (all of them before the first one). The point that closes a contour is placed like its first
/// point. A glyph without horizontal stem hints is not changed, nor are the zones of a private DICT whose hinting values
/// are malformed used. The vertical stems are not used.
/// </para>
/// </remarks>
internal sealed class CffHinter
{
    private const byte MoveToCommand = 0;
    private const byte LineToCommand = 1;
    private const byte QuadraticToCommand = 2;
    private const byte CubicToCommand = 3;
    private const byte CloseCommand = 4;

    /// <summary>The number of pixels per font unit.</summary>
    private readonly double _scale;
    private readonly Recorder _recorder = new();
    private Zone[] _zones = [];
    private readonly List<(double Original, double Hinted, int Priority)> _constraints = [];

    // The hint map of each mask of the glyph being hinted: ranges of the constraints.
    private readonly List<(int Start, int Count)> _maps = [];
    private readonly List<(double Original, double Hinted)> _mapConstraints = [];

    private CffHinter(double scale)
    {
        _scale = scale;
    }

    /// <summary>Gets a value indicating whether the outlines of a font have hints this hinter uses: CFF or CFF2 outlines.</summary>
    public static bool CanHint(FontFace face) => face.Outlines is FontOutlineKind.Cff or FontOutlineKind.Cff2;

    /// <summary>Creates the hinter of a font at a size.</summary>
    /// <param name="face">A face for which <see cref="CanHint"/> is <see langword="true"/>.</param>
    /// <param name="size">The size, in pixels per em.</param>
    public static CffHinter Create(FontFace face, double size) => new(size / face.UnitsPerEm);

    /// <summary>Decodes the outline of a glyph, hinted, into a sink, in font units.</summary>
    /// <exception cref="InvalidFontException">The data of the glyph is malformed.</exception>
    /// <exception cref="UnsupportedFontFeatureException">The charstring uses a feature that is not supported.</exception>
    /// <exception cref="FontResourceLimitException">The glyph exceeds the complexity limit or the allocation limit.</exception>
    public void Decode(FontFace face, int glyphId, OutlineSink sink, GlyphScratch scratch, ref GlyphBudget budget)
    {
        // A hinting value the private DICT cannot have leaves the glyph without alignment zones: the outline does not
        // depend on it.
        CffHintValues values;
        try
        {
            values = face.GetCffHintValues(glyphId) ?? CffHintValues.Empty;
        }
        catch (InvalidFontException)
        {
            values = CffHintValues.Empty;
        }

        _zones = CreateZones(values);

        scratch.Reset();
        _recorder.Begin(scratch);
        face.DecodeOutline(glyphId, _recorder, scratch, ref budget);
        if (_recorder.Start < 0)
            return;

        var coordinates = scratch.Work[_recorder.Start..];
        budget.Charge(_recorder.Commands.Count + _recorder.Stems.Count);
        BuildMaps();
        Emit(coordinates, sink);
    }

    // ---- Zones ---------------------------------------------------------------------------------------------------

    private Zone[] CreateZones(CffHintValues values)
    {
        var suppressesOvershoots = _scale < values.BlueScale;
        var zones = new List<Zone>();
        var blues = values.BlueValues;
        for (var i = 0; i + 1 < blues.Length; i += 2)
        {
            var (low, high) = (Math.Min(blues[i], blues[i + 1]), Math.Max(blues[i], blues[i + 1]));

            // The first zone is the one of the baseline: its flat edge is its top.
            zones.Add(i == 0 ? CreateZone(low, high, flat: high, isTop: false) : CreateZone(low, high, flat: low, isTop: true));
        }

        var others = values.OtherBlues;
        for (var i = 0; i + 1 < others.Length; i += 2)
        {
            var (low, high) = (Math.Min(others[i], others[i + 1]), Math.Max(others[i], others[i + 1]));
            zones.Add(CreateZone(low, high, flat: high, isTop: false));
        }

        return [.. zones];

        Zone CreateZone(double low, double high, double flat, bool isTop) => new(low - values.BlueFuzz, high + values.BlueFuzz, flat, Math.Round(flat * _scale, MidpointRounding.AwayFromZero), isTop, suppressesOvershoots, values.BlueShift);
    }

    /// <summary>Gets where an edge in a zone goes, in pixels, or <see cref="double.NaN"/> when it is in no zone of its side.</summary>
    private double Align(double edge, bool isTop)
    {
        foreach (var zone in _zones)
        {
            if (zone.IsTop != isTop || edge < zone.Low || edge > zone.High)
                continue;

            if (zone.SuppressesOvershoots)
                return zone.SnappedFlat;

            var overshoot = Math.Max(0, isTop ? edge - zone.Flat : zone.Flat - edge);
            var pixels = Math.Round(overshoot * _scale, MidpointRounding.AwayFromZero);
            if (overshoot >= zone.BlueShift && pixels < 1)
            {
                pixels = 1;
            }

            return isTop ? zone.SnappedFlat + pixels : zone.SnappedFlat - pixels;
        }

        return double.NaN;
    }

    // ---- Hint maps -----------------------------------------------------------------------------------------------

    /// <summary>Builds the hint map of each mask of the glyph: the edges of its horizontal stems, where they were and where they go, in pixels.</summary>
    private void BuildMaps()
    {
        _maps.Clear();
        _mapConstraints.Clear();
        var stems = _recorder.Stems;
        var masks = _recorder.Masks;
        for (var map = 0; map < masks.Count; map++)
        {
            var mask = masks[map];
            _constraints.Clear();
            for (var i = 0; i < stems.Count; i++)
            {
                var stem = stems[i];
                if (!stem.IsHorizontal || (mask is not null && (i >> 3 >= mask.Length || (mask[i >> 3] & (0x80 >> (i & 7))) == 0)))
                    continue;

                AddStem(stem.Edge, stem.Width);
            }

            // In increasing order; of two edges at one height, the one aligned with a zone. An edge that would make
            // the map decrease is dropped.
            _constraints.Sort(static (a, b) => a.Original != b.Original ? a.Original.CompareTo(b.Original) : b.Priority.CompareTo(a.Priority));
            var start = _mapConstraints.Count;
            foreach (var constraint in _constraints)
            {
                if (_mapConstraints.Count > start)
                {
                    var previous = _mapConstraints[^1];
                    if (constraint.Original - previous.Original < 1e-9 || constraint.Hinted < previous.Hinted)
                        continue;
                }

                _mapConstraints.Add((constraint.Original, constraint.Hinted));
            }

            _maps.Add((start, _mapConstraints.Count - start));
        }

    }

    private void AddStem(double edge, double width)
    {
        // Ghost hints: one edge.
        if (width == -20)
        {
            AddEdge(edge, isTop: true);
            return;
        }

        if (width == -21)
        {
            AddEdge(edge + width, isTop: false);
            return;
        }

        var bottom = Math.Min(edge, edge + width);
        var top = Math.Max(edge, edge + width);
        var pixels = Math.Max(1, Math.Round((top - bottom) * _scale, MidpointRounding.AwayFromZero));
        var alignedBottom = Align(bottom, isTop: false);
        var alignedTop = Align(top, isTop: true);
        double newBottom;
        double newTop;
        if (!double.IsNaN(alignedBottom))
        {
            newBottom = alignedBottom;
            newTop = double.IsNaN(alignedTop) ? newBottom + pixels : alignedTop;
        }
        else if (!double.IsNaN(alignedTop))
        {
            newTop = alignedTop;
            newBottom = newTop - pixels;
        }
        else
        {
            var middle = (bottom + top) / 2 * _scale;
            newBottom = Math.Round(middle - (pixels / 2), MidpointRounding.AwayFromZero);
            newTop = newBottom + pixels;
        }

        _constraints.Add((bottom * _scale, newBottom, double.IsNaN(alignedBottom) ? 1 : 2));
        _constraints.Add((top * _scale, newTop, double.IsNaN(alignedTop) ? 1 : 2));
    }

    private void AddEdge(double edge, bool isTop)
    {
        var aligned = Align(edge, isTop);
        _constraints.Add((edge * _scale, double.IsNaN(aligned) ? Math.Round(edge * _scale, MidpointRounding.AwayFromZero) : aligned, double.IsNaN(aligned) ? 1 : 2));
    }

    /// <summary>Maps a vertical coordinate, in font units, with a hint map.</summary>
    private double MapY(int map, double y)
    {
        var (start, count) = _maps[map];
        if (count == 0)
            return y;

        var pixels = y * _scale;
        var first = _mapConstraints[start];
        if (pixels <= first.Original)
            return (pixels + first.Hinted - first.Original) / _scale;

        for (var i = start + 1; i < start + count; i++)
        {
            var next = _mapConstraints[i];
            if (pixels <= next.Original)
            {
                var previous = _mapConstraints[i - 1];
                var t = (pixels - previous.Original) / (next.Original - previous.Original);
                return (previous.Hinted + ((next.Hinted - previous.Hinted) * t)) / _scale;
            }
        }

        var last = _mapConstraints[start + count - 1];
        return (pixels + last.Hinted - last.Original) / _scale;
    }

    private void Emit(Span<double> coordinates, OutlineSink sink)
    {
        var commands = _recorder.Commands;
        var commandMaps = _recorder.CommandMasks;
        var index = 0;
        double startX = 0, startY = 0, hintedStartY = 0;
        for (var c = 0; c < commands.Count; c++)
        {
            var map = commandMaps[c];
            switch (commands[c])
            {
                case MoveToCommand:
                    startX = coordinates[index];
                    startY = coordinates[index + 1];
                    hintedStartY = MapY(map, startY);
                    sink.MoveTo(startX, hintedStartY);
                    break;

                case LineToCommand:
                {
                    // The line that closes a contour ends where the contour started.
                    var x = coordinates[index];
                    var y = coordinates[index + 1];
                    var closes = c + 1 < commands.Count && commands[c + 1] == CloseCommand && x == startX && y == startY;
                    sink.LineTo(x, closes ? hintedStartY : MapY(map, y));
                    break;
                }

                case QuadraticToCommand:
                    sink.QuadraticTo(coordinates[index], MapY(map, coordinates[index + 1]), coordinates[index + 2], MapY(map, coordinates[index + 3]));
                    break;

                case CubicToCommand:
                    sink.CubicTo(coordinates[index], MapY(map, coordinates[index + 1]), coordinates[index + 2], MapY(map, coordinates[index + 3]), coordinates[index + 4], MapY(map, coordinates[index + 5]));
                    break;

                default:
                    sink.Close();
                    break;
            }

            index += GetCoordinateCount(commands[c]);
        }
    }

    private static int GetCoordinateCount(byte command) => command switch
    {
        MoveToCommand or LineToCommand => 2,
        QuadraticToCommand => 4,
        CubicToCommand => 6,
        _ => 0,
    };

    /// <summary>An alignment zone, extended by the fuzz, and where its flat edge goes at the size, in pixels.</summary>
    private readonly record struct Zone(double Low, double High, double Flat, double SnappedFlat, bool IsTop, bool SuppressesOvershoots, double BlueShift);

    /// <summary>
    /// Records an outline and its hints: the commands in a list, their coordinates in the working numbers of a scratch,
    /// the mask each command is drawn with, the stems, and the masks (<see langword="null"/>: every stem, before the
    /// first <c>hintmask</c>).
    /// </summary>
    private sealed class Recorder : OutlineSink, ICffHintSink
    {
        private GlyphScratch? _scratch;

        public List<byte> Commands { get; } = [];

        public List<int> CommandMasks { get; } = [];

        public List<(bool IsHorizontal, double Edge, double Width)> Stems { get; } = [];

        public List<byte[]?> Masks { get; } = [];

        /// <summary>Gets the index of the first coordinate in the working numbers of the scratch.</summary>
        public int Start { get; private set; } = -1;

        public void Begin(GlyphScratch scratch)
        {
            _scratch = scratch;
            Commands.Clear();
            CommandMasks.Clear();
            Stems.Clear();
            Masks.Clear();
            Masks.Add(null);
            Start = -1;
        }

        public void AddStem(bool isHorizontal, double edge, double width) => Stems.Add((isHorizontal, edge, width));

        public void SetMask(ReadOnlySpan<byte> mask, bool isCounterMask)
        {
            if (!isCounterMask)
            {
                Masks.Add(mask.ToArray());
            }
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

        public override void Close()
        {
            Commands.Add(CloseCommand);
            CommandMasks.Add(Masks.Count - 1);
        }

        private void Add(byte command, double x, double y)
        {
            Commands.Add(command);
            CommandMasks.Add(Masks.Count - 1);
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
