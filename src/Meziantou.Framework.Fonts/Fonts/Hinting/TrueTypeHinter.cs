using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// Hints the TrueType outlines of a font at a size with the instructions of the font: runs the font program and the
/// control value program once, then the instructions of each glyph.
/// </summary>
/// <remarks>
/// <para>
/// OpenType specification, "Instructing TrueType Glyphs": the points of a glyph are scaled to 26.6 pixels, the phantom
/// points (the origin, the advance, the top and the bottom of the glyph) are added after them and rounded to the
/// grid, then the instructions of the glyph move the points. A composite glyph hints each of its components with its
/// own instructions, places them (an offset can be rounded to the grid), then runs its own instructions on all the
/// points. The hinted advance is the distance between the two first phantom points.
/// </para>
/// <para>
/// Every glyph starts from the state the control value program left: the control values, the storage area and the
/// twilight zone are restored before its instructions run, so the hinted outline of a glyph does not depend on the
/// glyphs hinted before it.
/// </para>
/// <para>
/// The instance is not thread-safe: a hinter is used by one operation at a time.
/// </para>
/// </remarks>
internal sealed class TrueTypeHinter : IDisposable
{
    /// <summary>The number of phantom points after the points of a glyph.</summary>
    public const int PhantomPointCount = 4;

    private readonly FontFace _face;
    private readonly FontAllocator _allocator;
    private readonly TrueTypeInterpreter _interpreter;
    private readonly GraphicsState _glyphState;
    private readonly int _scale;

    // The control values, the storage area and the twilight zone as the control value program left them.
    private FontBuffer? _savedControlValues;
    private FontBuffer? _savedStorage;
    private FontBuffer? _savedTwilight;

    // The points of the glyph being assembled, with their contours: components are hinted one after the other into it.
    private FontBuffer? _points;
    private int _pointCapacity;
    private int _pointCount;
    private FontBuffer? _contours;
    private int _contourCapacity;
    private int _contourCount;

    private TrueTypeHinter(FontFace face, FontAllocator allocator, TrueTypeInterpreter interpreter, int pixelsPerEm, int scale, GraphicsState glyphState)
    {
        _face = face;
        _allocator = allocator;
        _interpreter = interpreter;
        PixelsPerEm = pixelsPerEm;
        _scale = scale;
        _glyphState = glyphState;
    }

    /// <summary>Gets the size the outlines are hinted for, in pixels per em.</summary>
    public int PixelsPerEm { get; }

    /// <summary>Gets a value indicating whether the instructions of the glyphs are run: the control value program can turn them off.</summary>
    public bool RunsGlyphPrograms => (_glyphState.InstructControl & 1) == 0;

    /// <summary>Gets a value indicating whether a font has TrueType instructions this hinter can run: a font program or a control value program, and no variations.</summary>
    public static bool CanHint(FontFace face)
    {
        if (face.Outlines != FontOutlineKind.TrueType || face.Instance is not null)
            return false;

        if (!face.TryFindTable(FontTags.Maxp, out _, out var maxpLength) || maxpLength < 32)
            return false;

        return face.TryFindTable(FontTags.Fpgm, out _, out var fpgm) && fpgm > 0
            || face.TryFindTable(FontTags.Prep, out _, out var prep) && prep > 0;
    }

    /// <summary>
    /// Creates a hinter for a size, or returns <see langword="null"/> when the font program or the control value
    /// program breaks a rule of the specification: the font is then drawn without its instructions.
    /// </summary>
    /// <param name="face">A face for which <see cref="CanHint"/> is <see langword="true"/>.</param>
    /// <param name="size">The size, in pixels per em.</param>
    /// <param name="allocator">The storage of the interpreter.</param>
    /// <exception cref="FontResourceLimitException">A program exceeds the complexity limit, or the storage exceeds the allocation limit.</exception>
    public static TrueTypeHinter? Create(FontFace face, double size, FontAllocator allocator)
    {
        var bytes = face.Data.RawBytes;
        face.TryFindTable(FontTags.Maxp, out var maxpOffset, out _);
        var maxp = bytes.AsSpan(maxpOffset, 32);
        var cvtCount = face.TryFindTable(FontTags.Cvt, out var cvtOffset, out var cvtLength) ? cvtLength / 2 : 0;

        // A font can ask for the size to be a whole number of pixels (bit 3 of the flags of 'head').
        var pixelsPerEm = (int)Math.Round(size, MidpointRounding.AwayFromZero);
        var size26Dot6 = (face.HeadFlags & 8) != 0 ? pixelsPerEm * 64L : (long)Math.Round(size * 64, MidpointRounding.AwayFromZero);
        var scale = (int)(((size26Dot6 << 16) + (face.UnitsPerEm / 2)) / face.UnitsPerEm);

        var interpreter = new TrueTypeInterpreter(bytes, allocator, maxp, cvtCount, face.Axes.Length);
        TrueTypeHinter? hinter = null;
        try
        {
            interpreter.PixelsPerEm = pixelsPerEm;
            interpreter.Scale = scale;
            var controlValues = interpreter.ControlValues;
            for (var i = 0; i < cvtCount; i++)
            {
                controlValues[i] = interpreter.ScaleFontUnits(FontBinary.Int16(bytes.AsSpan(cvtOffset, cvtLength), i * 2));
            }

            var limits = face.Data.Limits;
            try
            {
                if (face.TryFindTable(FontTags.Fpgm, out var fpgmOffset, out var fpgmLength) && fpgmLength > 0)
                {
                    var budget = new GlyphBudget(limits);
                    interpreter.RunFontProgram(fpgmOffset, fpgmLength, ref budget);
                }

                if (face.TryFindTable(FontTags.Prep, out var prepOffset, out var prepLength) && prepLength > 0)
                {
                    var budget = new GlyphBudget(limits);
                    interpreter.RunControlValueProgram(prepOffset, prepLength, ref budget);
                }
            }
            catch (HintingException)
            {
                interpreter.Dispose();
                return null;
            }

            // The control value program can ask for the glyphs to start from the default state.
            var state = interpreter.State;
            if ((state.InstructControl & 2) != 0)
            {
                var instructControl = state.InstructControl;
                state = GraphicsState.Default;
                state.InstructControl = instructControl;
            }

            hinter = new TrueTypeHinter(face, allocator, interpreter, pixelsPerEm, scale, state);
            hinter.SaveState();
            return hinter;
        }
        catch
        {
            if (hinter is not null)
            {
                hinter.Dispose();
            }
            else
            {
                interpreter.Dispose();
            }

            throw;
        }
    }

    /// <summary>Gets the hinted points of the glyph that was hinted last.</summary>
    public ReadOnlySpan<HintPoint> Points => _points is null ? default : unsafe(MemoryMarshal.Cast<byte, HintPoint>(_points.Span))[.._pointCount];

    /// <summary>Gets the index of the last point of each contour of the glyph that was hinted last.</summary>
    public ReadOnlySpan<int> Contours => _contours is null ? default : unsafe(MemoryMarshal.Cast<byte, int>(_contours.Span))[.._contourCount];

    /// <summary>Gets the horizontal position of the origin of the glyph that was hinted last, in 26.6 pixels.</summary>
    public int OriginX { get; private set; }

    /// <summary>Gets the hinted advance of the glyph that was hinted last, in 26.6 pixels: a whole number of pixels.</summary>
    public int Advance { get; private set; }

    /// <summary>Hints a glyph: <see cref="Points"/>, <see cref="Contours"/>, <see cref="OriginX"/> and <see cref="Advance"/> then describe it.</summary>
    /// <returns><see langword="false"/> when the instructions of the glyph break a rule of the specification: the glyph is then to be drawn without hinting.</returns>
    /// <exception cref="InvalidFontException">The data of the glyph is malformed.</exception>
    /// <exception cref="FontResourceLimitException">The glyph exceeds the complexity limit or the allocation limit.</exception>
    public bool TryHint(int glyphId, GlyphScratch scratch, ref GlyphBudget budget)
    {
        _pointCount = 0;
        _contourCount = 0;
        try
        {
            Load(_face.CreateGlyfContext(), glyphId, scratch, ref budget, depth: 0, out var phantoms);
            OriginX = phantoms.Origin.X;
            Advance = RoundToGrid(phantoms.Advance.X - phantoms.Origin.X);
            return true;
        }
        catch (HintingException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _interpreter.Dispose();
        _savedControlValues?.Dispose();
        _savedControlValues = null;
        _savedStorage?.Dispose();
        _savedStorage = null;
        _savedTwilight?.Dispose();
        _savedTwilight = null;
        _points?.Dispose();
        _points = null;
        _contours?.Dispose();
        _contours = null;
    }

    private static int RoundToGrid(int value) => value >= 0 ? (value + 32) & -64 : -((-value + 32) & -64);

    private void SaveState()
    {
        _savedControlValues = Save<int>(_interpreter.ControlValues);
        _savedStorage = Save<int>(_interpreter.Storage);
        _savedTwilight = Save<HintPoint>(_interpreter.TwilightPoints);

        FontBuffer Save<T>(ReadOnlySpan<T> values)
            where T : unmanaged
        {
            var buffer = _allocator.Rent<T>(values.Length);
            values.CopyTo(AsSpan<T>(buffer));
            return buffer;
        }
    }

    private void RestoreState()
    {
        var controlValues = _interpreter.ControlValues;
        AsSpan<int>(_savedControlValues!)[..controlValues.Length].CopyTo(controlValues);
        var storage = _interpreter.Storage;
        AsSpan<int>(_savedStorage!)[..storage.Length].CopyTo(storage);
        var twilight = _interpreter.TwilightPoints;
        AsSpan<HintPoint>(_savedTwilight!)[..twilight.Length].CopyTo(twilight);
    }

    private static Span<T> AsSpan<T>(FontBuffer buffer)
        where T : unmanaged
        => unsafe(MemoryMarshal.Cast<byte, T>(buffer.Span));

    private Span<HintPoint> PointStorage => _points is null ? default : unsafe(MemoryMarshal.Cast<byte, HintPoint>(_points.Span));

    private Span<int> ContourStorage => _contours is null ? default : unsafe(MemoryMarshal.Cast<byte, int>(_contours.Span));

    private int AddPoints(int count)
    {
        var start = _pointCount;
        var required = start + count;
        if (required > _pointCapacity)
        {
            var capacity = Math.Max(required, Math.Max(64, _pointCapacity * 2));
            var buffer = _allocator.Rent<HintPoint>(capacity);
            if (_points is not null)
            {
                _points.Span.CopyTo(buffer.Span);
                _points.Dispose();
            }

            _points = buffer;
            _pointCapacity = capacity;
        }

        _pointCount = required;
        return start;
    }

    private void AddContour(int end)
    {
        if (_contourCount == _contourCapacity)
        {
            var capacity = Math.Max(16, _contourCapacity * 2);
            var buffer = _allocator.Rent<int>(capacity);
            if (_contours is not null)
            {
                _contours.Span.CopyTo(buffer.Span);
                _contours.Dispose();
            }

            _contours = buffer;
            _contourCapacity = capacity;
        }

        ContourStorage[_contourCount++] = end;
    }

    /// <summary>Loads and hints a glyph, appending its points and its contours after those already assembled.</summary>
    private void Load(in GlyfContext context, int glyphId, GlyphScratch scratch, ref GlyphBudget budget, int depth, out PhantomPoints phantoms)
    {
        var glyphOffset = GlyfOutlineReader.GetGlyphOffset(context, glyphId, out var glyphLength);
        var data = context.Glyf.Slice(glyphOffset, glyphLength);
        GlyfOutlineReader.GetMetrics(context, glyphId, out var advance, out var leftSideBearing);
        var contourCount = data.Length >= 10 ? FontBinary.Int16(data, 0) : 0;
        var xMin = data.Length >= 10 ? FontBinary.Int16(data, 2) : 0;
        var yMax = data.Length >= 10 ? FontBinary.Int16(data, 8) : 0;
        phantoms = CreatePhantomPoints(glyphId, xMin - leftSideBearing, advance, yMax);
        if (data.IsEmpty)
            return;

        if (contourCount >= 0)
        {
            LoadSimple(data, contourCount, _face.GlyfOffset + glyphOffset, scratch, ref budget, ref phantoms);
            return;
        }

        if (depth >= GlyfOutlineReader.MaxCompositeDepth)
            throw FontErrors.InvalidTable(FontTags.Glyf, "has composite glyphs nested too deeply, or that refer to each other.");

        LoadComposite(context, data, _face.GlyfOffset + glyphOffset, scratch, ref budget, depth, ref phantoms);
    }

    private PhantomPoints CreatePhantomPoints(int glyphId, int origin, int advance, int yMax)
    {
        int top;
        int advanceHeight;
        if (_face.HasVerticalMetrics)
        {
            top = yMax + _face.GetTopSideBearing(glyphId);
            advanceHeight = _face.GetAdvanceHeight(glyphId);
        }
        else
        {
            top = _face.HorizontalAscender;
            advanceHeight = _face.HorizontalAscender - _face.HorizontalDescender;
        }

        return new PhantomPoints
        {
            Origin = CreatePoint(origin, 0),
            Advance = CreatePoint(origin + advance, 0),
            Top = CreatePoint(origin, top),
            Bottom = CreatePoint(origin, top - advanceHeight),
        };
    }

    private HintPoint CreatePoint(int x, int y)
    {
        var point = new HintPoint
        {
            UnscaledX = x,
            UnscaledY = y,
            OriginalX = TrueTypeInterpreter.MultiplyFixed(x, _scale),
            OriginalY = TrueTypeInterpreter.MultiplyFixed(y, _scale),
        };

        point.X = point.OriginalX;
        point.Y = point.OriginalY;
        return point;
    }

    private void LoadSimple(ReadOnlySpan<byte> data, int contourCount, int dataOffset, GlyphScratch scratch, ref GlyphBudget budget, ref PhantomPoints phantoms)
    {
        scratch.Reset();
        GlyfOutlineReader.CollectSimple(data, contourCount, scratch, ref budget, out var instructionOffset, out var instructionLength);
        var source = scratch.Points;
        var count = source.Length;

        // The zone of the glyph: its points, scaled, then the phantom points, rounded to the grid.
        _interpreter.PrepareGlyphZone(count + PhantomPointCount, contourCount);
        var zone = _interpreter.GlyphPoints;
        var contours = _interpreter.ContourEnds;
        var contour = 0;
        for (var i = 0; i < count; i++)
        {
            zone[i] = CreatePoint((int)source[i].X, (int)source[i].Y);
            if ((source[i].Flags & GlyphPoint.OnCurve) != 0)
            {
                zone[i].Flags = HintPoint.OnCurve;
            }

            if ((source[i].Flags & GlyphPoint.EndOfContour) != 0)
            {
                contours[contour++] = i;
            }
        }

        RoundPhantomPoints(ref phantoms);
        phantoms.CopyTo(zone[count..]);

        if (instructionLength > 0 && RunsGlyphPrograms)
        {
            RestoreState();
            _interpreter.HasUnscaledPositions = true;
            _interpreter.RunGlyphProgram(dataOffset + instructionOffset, instructionLength, _glyphState, ref budget);
        }

        // The hinted points join the glyph being assembled.
        var first = AddPoints(count);
        zone[..count].CopyTo(PointStorage[first..]);
        foreach (var end in contours)
        {
            AddContour(first + end);
        }

        phantoms = PhantomPoints.From(zone[count..]);
    }

    private static void RoundPhantomPoints(ref PhantomPoints phantoms)
    {
        // Only where they are is rounded: where they were stays where the font puts them.
        phantoms.Origin.X = RoundToGrid(phantoms.Origin.OriginalX);
        phantoms.Advance.X = RoundToGrid(phantoms.Advance.OriginalX);
        phantoms.Top.Y = RoundToGrid(phantoms.Top.OriginalY);
        phantoms.Bottom.Y = RoundToGrid(phantoms.Bottom.OriginalY);
    }

    private void LoadComposite(in GlyfContext context, ReadOnlySpan<byte> data, int dataOffset, GlyphScratch scratch, ref GlyphBudget budget, int depth, ref PhantomPoints phantoms)
    {
        var glyphStart = _pointCount;
        var contourStart = _contourCount;
        var position = 10;
        var hasInstructions = false;
        RoundPhantomPoints(ref phantoms);
        while (true)
        {
            budget.Charge(1);
            var flags = FontBinary.UInt16(data, position);
            var component = FontBinary.UInt16(data, position + 2);
            position += 4;
            if (component >= context.GlyphCount)
                throw FontErrors.InvalidTable(FontTags.Glyf, $"has a composite glyph that refers to the glyph {component}, which the font does not have.");

            int argument1;
            int argument2;
            var isOffset = (flags & GlyfOutlineReader.ArgumentsAreXYValues) != 0;
            if ((flags & GlyfOutlineReader.ArgumentsAreWords) != 0)
            {
                argument1 = isOffset ? FontBinary.Int16(data, position) : FontBinary.UInt16(data, position);
                argument2 = isOffset ? FontBinary.Int16(data, position + 2) : FontBinary.UInt16(data, position + 2);
                position += 4;
            }
            else
            {
                argument1 = isOffset ? FontBinary.Int8(data, position) : FontBinary.UInt8(data, position);
                argument2 = isOffset ? FontBinary.Int8(data, position + 1) : FontBinary.UInt8(data, position + 1);
                position += 2;
            }

            // The transformation, in 2.14: x' = a x + c y, y' = b x + d y.
            int a = 0x4000, b = 0, c = 0, d = 0x4000;
            if ((flags & GlyfOutlineReader.HasScale) != 0)
            {
                a = d = FontBinary.Int16(data, position);
                position += 2;
            }
            else if ((flags & GlyfOutlineReader.HasXAndYScale) != 0)
            {
                a = FontBinary.Int16(data, position);
                d = FontBinary.Int16(data, position + 2);
                position += 4;
            }
            else if ((flags & GlyfOutlineReader.HasTwoByTwo) != 0)
            {
                a = FontBinary.Int16(data, position);
                b = FontBinary.Int16(data, position + 2);
                c = FontBinary.Int16(data, position + 4);
                d = FontBinary.Int16(data, position + 6);
                position += 8;
            }

            var componentStart = _pointCount;
            Load(context, component, scratch, ref budget, depth + 1, out var componentPhantoms);
            var points = PointStorage;
            var isTransformed = a != 0x4000 || b != 0 || c != 0 || d != 0x4000;
            if (isTransformed)
            {
                for (var i = componentStart; i < _pointCount; i++)
                {
                    Transform(ref points[i], a, b, c, d);
                }
            }

            int offsetX;
            int offsetY;
            int unscaledX;
            int unscaledY;
            if (isOffset)
            {
                unscaledX = argument1;
                unscaledY = argument2;
                if (isTransformed && (flags & GlyfOutlineReader.ScaledComponentOffset) != 0 && (flags & GlyfOutlineReader.UnscaledComponentOffset) == 0)
                {
                    (unscaledX, unscaledY) = (Fix14(unscaledX, a) + Fix14(unscaledY, c), Fix14(unscaledX, b) + Fix14(unscaledY, d));
                }

                offsetX = TrueTypeInterpreter.MultiplyFixed(unscaledX, _scale);
                offsetY = TrueTypeInterpreter.MultiplyFixed(unscaledY, _scale);
                // Only the vertical offset is rounded, as by the version 40 interpreter of FreeType: rounding the
                // horizontal one moves the components of a ligature apart at small sizes. Nothing is rounded when
                // the control value program turned the instructions off.
                if ((flags & GlyfOutlineReader.RoundXYToGrid) != 0 && RunsGlyphPrograms)
                {
                    offsetY = RoundToGrid(offsetY);
                }
            }
            else
            {
                // A point of the component is moved onto a point of the glyph assembled so far, where both are hinted.
                var parentIndex = glyphStart + argument1;
                var componentIndex = componentStart + argument2;
                if (parentIndex >= componentStart || componentIndex >= _pointCount)
                    throw FontErrors.InvalidTable(FontTags.Glyf, "has a composite glyph that matches a point that does not exist.");

                offsetX = points[parentIndex].X - points[componentIndex].X;
                offsetY = points[parentIndex].Y - points[componentIndex].Y;
                unscaledX = points[parentIndex].UnscaledX - points[componentIndex].UnscaledX;
                unscaledY = points[parentIndex].UnscaledY - points[componentIndex].UnscaledY;
            }

            for (var i = componentStart; i < _pointCount; i++)
            {
                ref var point = ref points[i];
                point.X += offsetX;
                point.Y += offsetY;
                point.OriginalX += offsetX;
                point.OriginalY += offsetY;
                point.UnscaledX += unscaledX;
                point.UnscaledY += unscaledY;
            }

            if ((flags & GlyfOutlineReader.UseMyMetrics) != 0)
            {
                phantoms = componentPhantoms;
            }

            hasInstructions |= (flags & GlyfOutlineReader.WeHaveInstructions) != 0;
            if ((flags & GlyfOutlineReader.MoreComponents) == 0)
                break;
        }

        if (!hasInstructions || !RunsGlyphPrograms)
            return;

        var instructionLength = FontBinary.UInt16(data, position);
        if (instructionLength == 0)
            return;

        if (position + 2 + instructionLength > data.Length)
            throw FontErrors.InvalidTable(FontTags.Glyf, "has a composite glyph whose instructions are truncated.");

        // The instructions of the composite glyph move the points of all its components: where the components put
        // them is their original position.
        var count = _pointCount - glyphStart;
        var contourCount = _contourCount - contourStart;
        budget.Charge(count);
        _interpreter.PrepareGlyphZone(count + PhantomPointCount, contourCount);
        var zone = _interpreter.GlyphPoints;
        var source = PointStorage.Slice(glyphStart, count);
        for (var i = 0; i < count; i++)
        {
            zone[i] = source[i];
            zone[i].OriginalX = zone[i].UnscaledX = zone[i].X;
            zone[i].OriginalY = zone[i].UnscaledY = zone[i].Y;
            zone[i].Flags &= HintPoint.OnCurve;
        }

        var contours = _interpreter.ContourEnds;
        var assembled = ContourStorage.Slice(contourStart, contourCount);
        for (var i = 0; i < contourCount; i++)
        {
            contours[i] = assembled[i] - glyphStart;
        }

        phantoms.CopyTo(zone[count..]);
        RestoreState();
        _interpreter.HasUnscaledPositions = false;
        _interpreter.RunGlyphProgram(dataOffset + position + 2, instructionLength, _glyphState, ref budget);
        zone[..count].CopyTo(source);
        phantoms = PhantomPoints.From(zone[count..]);
    }

    private static int Fix14(int value, int factor) => (int)(((long)value * factor + 0x2000) >> 14);

    private static void Transform(ref HintPoint point, int a, int b, int c, int d)
    {
        (point.X, point.Y) = (Fix14(point.X, a) + Fix14(point.Y, c), Fix14(point.X, b) + Fix14(point.Y, d));
        (point.OriginalX, point.OriginalY) = (Fix14(point.OriginalX, a) + Fix14(point.OriginalY, c), Fix14(point.OriginalX, b) + Fix14(point.OriginalY, d));
        (point.UnscaledX, point.UnscaledY) = (Fix14(point.UnscaledX, a) + Fix14(point.UnscaledY, c), Fix14(point.UnscaledX, b) + Fix14(point.UnscaledY, d));
    }

    /// <summary>The four phantom points of a glyph.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct PhantomPoints
    {
        public HintPoint Origin;
        public HintPoint Advance;
        public HintPoint Top;
        public HintPoint Bottom;

        public static PhantomPoints From(ReadOnlySpan<HintPoint> points) => new() { Origin = points[0], Advance = points[1], Top = points[2], Bottom = points[3] };

        public readonly void CopyTo(Span<HintPoint> points)
        {
            points[0] = Origin;
            points[1] = Advance;
            points[2] = Top;
            points[3] = Bottom;
        }
    }
}
