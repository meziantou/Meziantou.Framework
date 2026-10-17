using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Reads the <c>COLR</c> table: the glyphs of a color font, as layers of colored outlines or as graphs of paints.</summary>
/// <remarks>
/// <para>
/// OpenType specification, "COLR - Color Table". Version 0 gives a glyph a list of layers, each one the outline of a
/// glyph filled with a color of the palette. Version 1 gives a glyph a graph of paint tables: fills with a color or
/// with a linear, radial or sweep gradient, the outline of a glyph as a clip, transformations, layers, the paint of
/// another glyph, and the composition of two paints. Every numeric field of a paint has a form that varies in a
/// variable font.
/// </para>
/// <para>
/// <see cref="Build"/> walks the graph of a glyph once and writes it as a display list (<see cref="ColorGlyph"/>),
/// with the transformations resolved. A paint that refers to a glyph or to a layer that is being painted, which
/// would never end, is left out. The walk is bounded by its depth and by the complexity budget of the glyph, since a
/// small graph can describe a very long list.
/// </para>
/// </remarks>
internal sealed class ColrTable
{
    /// <summary>The greatest depth of a graph of paints. The complexity budget bounds the total work.</summary>
    public const int MaxPaintDepth = 64;

    /// <summary>The cost of a paint table in the complexity budget of a glyph: an operation of the display list is larger than a point.</summary>
    private const int PaintCost = 16;

    private const uint NoVariation = 0xFFFFFFFF;

    private readonly int _baseGlyphCount;
    private readonly int _baseGlyphs;
    private readonly int _layers;
    private readonly int _layerCount;
    private readonly int _baseGlyphList;
    private readonly int _layerList;
    private readonly int _clipList;
    private readonly int _variationMap;

    private ColrTable(int baseGlyphCount, int baseGlyphs, int layers, int layerCount, int baseGlyphList, int layerList, int clipList, int variationMap, int variationStore)
    {
        _baseGlyphCount = baseGlyphCount;
        _baseGlyphs = baseGlyphs;
        _layers = layers;
        _layerCount = layerCount;
        _baseGlyphList = baseGlyphList;
        _layerList = layerList;
        _clipList = clipList;
        _variationMap = variationMap;
        VariationStore = variationStore;
    }

    /// <summary>Gets the position of the item variation store of the table; 0 when it has none.</summary>
    public int VariationStore { get; }

    /// <summary>Reads the header of the table, or returns <see langword="null"/> when its version is not known.</summary>
    public static ColrTable? Read(ReadOnlySpan<byte> table)
    {
        if (table.Length < 14)
            return null;

        var version = FontBinary.UInt16(table, 0);
        if (version > 1)
            return null;

        var baseGlyphCount = FontBinary.UInt16(table, 2);
        var baseGlyphs = FontBinary.Offset32(table, 4);
        var layers = FontBinary.Offset32(table, 8);
        var layerCount = FontBinary.UInt16(table, 12);
        FontBinary.EnsureRange(table, baseGlyphs, baseGlyphCount * 6L);
        FontBinary.EnsureRange(table, layers, layerCount * 4L);
        if (version == 0 || table.Length < 34)
            return new ColrTable(baseGlyphCount, baseGlyphs, layers, layerCount, 0, 0, 0, 0, 0);

        var baseGlyphList = ReadOffset(table, 14, 4);
        var layerList = ReadOffset(table, 18, 4);
        var clipList = ReadOffset(table, 22, 5);
        var variationMap = ReadOffset(table, 26, 2);
        var variationStore = ReadOffset(table, 30, 8);
        if (variationStore != 0 && FontBinary.UInt16(table, variationStore) != 1)
        {
            variationStore = 0;
        }

        if (clipList != 0 && FontBinary.UInt8(table, clipList) != 1)
        {
            clipList = 0;
        }

        return new ColrTable(baseGlyphCount, baseGlyphs, layers, layerCount, baseGlyphList, layerList, clipList, variationMap, variationStore);

        // A subtable that is not there, or whose first fields are outside the table, is left out.
        static int ReadOffset(ReadOnlySpan<byte> table, int position, int minimumLength)
        {
            var offset = FontBinary.UInt32(table, position);
            return offset != 0 && offset <= table.Length - minimumLength ? (int)offset : 0;
        }
    }

    /// <summary>Tells whether the table describes a glyph.</summary>
    public bool HasGlyph(ReadOnlySpan<byte> table, int glyphId) => FindPaint(table, glyphId) != 0 || FindLayers(table, glyphId, out _, out _);

    /// <summary>Builds the display list of a glyph.</summary>
    /// <param name="table">The table.</param>
    /// <param name="face">The font, which gives the outlines and the colors.</param>
    /// <param name="glyphId">The glyph.</param>
    /// <param name="palette">The palette the colors come from.</param>
    /// <param name="scratch">The working storage of the outlines.</param>
    /// <param name="budget">The complexity budget of the glyph.</param>
    /// <returns>The display list, or <see langword="null"/> when the table does not describe the glyph.</returns>
    /// <exception cref="InvalidImageContentException">The data of the glyph is malformed.</exception>
    /// <exception cref="ImageResourceLimitException">The glyph exceeds the complexity limit.</exception>
    public ColorGlyph? Build(ReadOnlySpan<byte> table, FontFace face, int glyphId, int palette, GlyphScratch scratch, ref GlyphBudget budget)
    {
        var builder = new Builder(this, table, face, palette, scratch, ref budget);
        var paint = FindPaint(table, glyphId);
        if (paint != 0)
        {
            builder.Enter(glyphId);
            var hasBox = builder.PushClipBox(glyphId, Matrix.Identity, isRoot: true);
            builder.Paint(paint, Matrix.Identity, depth: 0);
            if (hasBox)
            {
                builder.PopClip();
            }

            return builder.ToColorGlyph();
        }

        if (!FindLayers(table, glyphId, out var first, out var count))
            return null;

        // Version 0: the outline of each layer filled with a color, from the bottom to the top.
        if ((long)first + count > _layerCount)
            throw FontErrors.InvalidTable(FontTags.Colr, "has a glyph whose layers are outside the list of layers.");

        for (var i = 0; i < count; i++)
        {
            budget.Charge(PaintCost);
            var record = _layers + ((first + i) * 4);
            builder.FillGlyph(FontBinary.UInt16(table, record), Matrix.Identity, builder.AddSolid(FontBinary.UInt16(table, record + 2), alpha: 1));
        }

        return builder.ToColorGlyph();
    }

    /// <summary>Finds the root paint of a glyph in the list of version 1; 0 when the glyph is not in it.</summary>
    private int FindPaint(ReadOnlySpan<byte> table, int glyphId)
    {
        if (_baseGlyphList == 0)
            return 0;

        // The records are sorted by glyph.
        long low = 0;
        var high = (long)FontBinary.UInt32(table, _baseGlyphList) - 1;
        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            var record = FontBinary.Slice(table, _baseGlyphList + 4 + (middle * 6), 6);
            var glyph = FontBinary.UInt16(record, 0);
            if (glyph == glyphId)
            {
                var offset = FontBinary.Offset32(record, 2);
                return offset == 0 ? 0 : FontBinary.AddOffset(_baseGlyphList, offset);
            }

            if (glyph < glyphId)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return 0;
    }

    /// <summary>Finds the layers of a glyph in the records of version 0.</summary>
    private bool FindLayers(ReadOnlySpan<byte> table, int glyphId, out int first, out int count)
    {
        var low = 0;
        var high = _baseGlyphCount - 1;
        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            var record = _baseGlyphs + (middle * 6);
            var glyph = FontBinary.UInt16(table, record);
            if (glyph == glyphId)
            {
                first = FontBinary.UInt16(table, record + 2);
                count = FontBinary.UInt16(table, record + 4);
                return true;
            }

            if (glyph < glyphId)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        first = count = 0;
        return false;
    }

    /// <summary>Finds the clip box of a glyph: "ClipList". The ranges of glyphs are sorted.</summary>
    private int FindClipBox(ReadOnlySpan<byte> table, int glyphId)
    {
        if (_clipList == 0)
            return 0;

        long low = 0;
        var high = (long)FontBinary.UInt32(table, _clipList + 1) - 1;
        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            var record = FontBinary.Slice(table, _clipList + 5 + (middle * 7), 7);
            if (glyphId < FontBinary.UInt16(record, 0))
            {
                high = middle - 1;
            }
            else if (glyphId > FontBinary.UInt16(record, 2))
            {
                low = middle + 1;
            }
            else
            {
                var offset = FontBinary.UInt24(record, 4);
                return offset == 0 ? 0 : _clipList + offset;
            }
        }

        return 0;
    }

    /// <summary>A transformation: x' = xx x + xy y + dx, y' = yx x + yy y + dy.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Matrix(double XX, double YX, double XY, double YY, double DX, double DY)
    {
        public static Matrix Identity => new(1, 0, 0, 1, 0, 0);

        public bool IsIdentity => XX == 1 && YX == 0 && XY == 0 && YY == 1 && DX == 0 && DY == 0;

        /// <summary>Gets the transformation that applies <paramref name="inner"/>, then this one.</summary>
        public Matrix Then(in Matrix inner) => new(
            (XX * inner.XX) + (XY * inner.YX),
            (YX * inner.XX) + (YY * inner.YX),
            (XX * inner.XY) + (XY * inner.YY),
            (YX * inner.XY) + (YY * inner.YY),
            (XX * inner.DX) + (XY * inner.DY) + DX,
            (YX * inner.DX) + (YY * inner.DY) + DY);

        public static Matrix Translate(double dx, double dy) => new(1, 0, 0, 1, dx, dy);

        /// <summary>Gets a transformation that keeps a point in place: it is moved to the origin, transformed, and moved back.</summary>
        public Matrix Around(double centerX, double centerY) => Translate(centerX, centerY).Then(this).Then(Translate(-centerX, -centerY));
    }

    /// <summary>Walks the graph of paints of a glyph and writes its display list.</summary>
    private ref struct Builder
    {
        private readonly ColrTable _colr;
        private readonly ReadOnlySpan<byte> _table;
        private readonly FontFace _face;
        private readonly int _palette;
        private readonly GlyphScratch _scratch;
        private readonly ref GlyphBudget _budget;
        private readonly GlyphBoundsSink _bounds = new();
        private readonly List<ColorOp> _ops = [];
        private readonly List<ColorPaint> _paints = [];
        private readonly List<ColorStop> _stops = [];
        private readonly List<double> _numbers = [];

        // The glyphs and the layers whose paints are being walked: a paint that refers to one of them refers to
        // itself, and is left out.
        private readonly HashSet<int> _activeGlyphs = [];
        private readonly HashSet<long> _activeLayers = [];
        private double _xMin = double.PositiveInfinity;
        private double _yMin = double.PositiveInfinity;
        private double _xMax = double.NegativeInfinity;
        private double _yMax = double.NegativeInfinity;
        private bool _hasRootBox;
        private bool _isUnbounded;
        private int _layerDepth;
        private int _maxLayerDepth;
        private int _clipDepth;
        private int _maxClipDepth;

        public Builder(ColrTable colr, ReadOnlySpan<byte> table, FontFace face, int palette, GlyphScratch scratch, ref GlyphBudget budget)
        {
            _colr = colr;
            _table = table;
            _face = face;
            _palette = palette;
            _scratch = scratch;
            _budget = ref budget;
        }

        /// <summary>Says that the paints of a glyph are being walked.</summary>
        public readonly void Enter(int glyph) => _activeGlyphs.Add(glyph);

        public readonly ColorGlyph ToColorGlyph()
        {
            var (xMin, yMin, xMax, yMax) = (_xMin, _yMin, _xMax, _yMax);
            if (_isUnbounded && !_hasRootBox)
            {
                // A paint that fills everything, in a glyph without clip box: the box of the line, one em wide.
                xMin = Math.Min(xMin, 0);
                yMin = Math.Min(yMin, Math.Min(_face.HorizontalDescender, 0));
                xMax = Math.Max(xMax, _face.UnitsPerEm);
                yMax = Math.Max(yMax, Math.Max(_face.HorizontalAscender, _face.UnitsPerEm));
            }

            if (!(xMax > xMin) || !(yMax > yMin))
            {
                xMin = yMin = xMax = yMax = 0;
            }

            FontErrors.EnsureCoordinates(xMin, yMin);
            FontErrors.EnsureCoordinates(xMax, yMax);
            return new ColorGlyph
            {
                Ops = [.. _ops],
                Paints = [.. _paints],
                Stops = [.. _stops],
                Numbers = [.. _numbers],
                XMin = xMin,
                YMin = yMin,
                XMax = xMax,
                YMax = yMax,
                LayerDepth = _maxLayerDepth + 1,
                ClipDepth = _maxClipDepth,
            };
        }

        /// <summary>Writes the display list of a paint table and of what it refers to.</summary>
        public void Paint(int paint, in Matrix transform, int depth)
        {
            if (depth >= MaxPaintDepth)
                throw FontErrors.InvalidTable(FontTags.Colr, "has paints nested too deeply, or that refer to each other.");

            _budget.Charge(PaintCost);
            var table = _table;
            var format = FontBinary.UInt8(table, paint);
            switch (format)
            {
                // PaintColrLayers: paints of the layer list, from the bottom to the top.
                case 1:
                    {
                        var count = FontBinary.UInt8(table, paint + 1);
                        var first = FontBinary.UInt32(table, paint + 2);
                        var list = _colr._layerList;
                        if (list == 0 || first + count > FontBinary.UInt32(table, list))
                            throw FontErrors.InvalidTable(FontTags.Colr, "has a paint whose layers are outside the list of layers.");

                        for (var i = 0; i < count; i++)
                        {
                            // A layer that is being painted is not painted inside itself.
                            var layer = first + i;
                            if (!_activeLayers.Add(layer))
                                continue;

                            Paint(FontBinary.AddOffset(list, FontBinary.Offset32(table, list + 4 + ((int)layer * 4))), transform, depth + 1);
                            _activeLayers.Remove(layer);
                        }

                        break;
                    }

                // The paints that fill: with nothing above them that restricts them, they fill the clip.
                case >= 2 and <= 9:
                    {
                        var index = CreatePaint(paint, format, transform);
                        if (index >= 0)
                        {
                            _ops.Add(new ColorOp(ColorOpKind.FillClip, 0, -1, index));
                            _isUnbounded |= _clipDepth == 0;
                        }

                        break;
                    }

                // PaintGlyph: the outline of a glyph clips a paint.
                case 10:
                    {
                        var glyph = FontBinary.UInt16(table, paint + 4);
                        var child = FontBinary.AddOffset(paint, FontBinary.UInt24(table, paint + 1));

                        // The usual case, a fill behind transformations only, is one operation.
                        var fill = child;
                        var fillTransform = transform;
                        var steps = 0;
                        while (steps < MaxPaintDepth && TryGetTransform(fill, out var next, out var step))
                        {
                            _budget.Charge(PaintCost);
                            fillTransform = fillTransform.Then(step);
                            fill = next;
                            steps++;
                        }

                        var fillFormat = FontBinary.UInt8(table, fill);
                        if (fillFormat is >= 2 and <= 9)
                        {
                            _budget.Charge(PaintCost);
                            var index = CreatePaint(fill, fillFormat, fillTransform);
                            if (index >= 0)
                            {
                                FillGlyph(glyph, transform, index);
                            }

                            break;
                        }

                        if (PushClipGlyph(glyph, transform))
                        {
                            Paint(child, transform, depth + 1);
                            PopClip();
                        }

                        break;
                    }

                // PaintColrGlyph: the paint of another glyph of the table, within its clip box.
                case 11:
                    {
                        // A glyph whose paints are being walked is not painted inside itself.
                        var glyph = FontBinary.UInt16(table, paint + 1);
                        var root = _colr.FindPaint(table, glyph);
                        if (root == 0 || !_activeGlyphs.Add(glyph))
                            break;

                        var hasBox = PushClipBox(glyph, transform, isRoot: false);
                        Paint(root, transform, depth + 1);
                        if (hasBox)
                        {
                            PopClip();
                        }

                        _activeGlyphs.Remove(glyph);
                        break;
                    }

                // The transformations.
                case >= 12 and <= 31:
                    {
                        if (!TryGetTransform(paint, out var child, out var step))
                            throw FontErrors.InvalidTable(FontTags.Colr, "has a transformation that cannot be read.");

                        Paint(child, transform.Then(step), depth + 1);
                        break;
                    }

                // PaintComposite: a paint is composed with a backdrop, each one on its own layer.
                case 32:
                    {
                        var source = FontBinary.AddOffset(paint, FontBinary.UInt24(table, paint + 1));
                        var mode = FontBinary.UInt8(table, paint + 4);
                        var backdrop = FontBinary.AddOffset(paint, FontBinary.UInt24(table, paint + 5));
                        PushLayer();
                        Paint(backdrop, transform, depth + 1);
                        PushLayer();
                        Paint(source, transform, depth + 1);
                        PopLayer(mode <= (int)ColorCompositeMode.Luminosity ? (ColorCompositeMode)mode : ColorCompositeMode.SourceOver);
                        PopLayer(ColorCompositeMode.SourceOver);
                        break;
                    }

                // A paint of a format that is not known draws nothing.
                default:
                    break;
            }
        }

        public void FillGlyph(int glyph, in Matrix transform, int paint)
        {
            if (!IncludeOutline(glyph, transform))
                return;

            _ops.Add(new ColorOp(ColorOpKind.FillGlyph, glyph, AddMatrix(transform), paint));
        }

        public int AddSolid(int paletteIndex, double alpha)
        {
            _stops.Add(GetColor(0, paletteIndex, alpha));
            _paints.Add(new ColorPaint(ColorPaintKind.Solid, -1, -1, _stops.Count - 1, 1, 0));
            return _paints.Count - 1;
        }

        /// <summary>Clips the next operations to the clip box of a glyph, when it has one.</summary>
        public bool PushClipBox(int glyph, in Matrix transform, bool isRoot)
        {
            var table = _table;
            var box = _colr.FindClipBox(table, glyph);
            if (box == 0)
                return false;

            // ClipBox: the sides of the box, which vary in format 2.
            var format = FontBinary.UInt8(table, box);
            if (format is not (1 or 2))
                return false;

            var variation = format == 2 ? FontBinary.UInt32(table, box + 9) : NoVariation;
            var xMin = FontBinary.Int16(table, box + 1) + Delta(variation, 0);
            var yMin = FontBinary.Int16(table, box + 3) + Delta(variation, 1);
            var xMax = FontBinary.Int16(table, box + 5) + Delta(variation, 2);
            var yMax = FontBinary.Int16(table, box + 7) + Delta(variation, 3);
            var sides = _numbers.Count;
            _numbers.AddRange([xMin, yMin, xMax, yMax]);
            _ops.Add(new ColorOp(ColorOpKind.PushClipBox, 0, AddMatrix(transform), sides));
            if (isRoot)
            {
                // The box of the glyph itself contains everything it draws.
                _hasRootBox = true;
                (_xMin, _yMin, _xMax, _yMax) = (xMin, yMin, xMax, yMax);
            }

            _clipDepth++;
            _maxClipDepth = Math.Max(_maxClipDepth, _clipDepth);
            return true;
        }

        public void PopClip()
        {
            _ops.Add(new ColorOp(ColorOpKind.PopClip, 0, -1, 0));
            _clipDepth--;
        }

        private bool PushClipGlyph(int glyph, in Matrix transform)
        {
            if (!IncludeOutline(glyph, transform))
                return false;

            _ops.Add(new ColorOp(ColorOpKind.PushClipGlyph, glyph, AddMatrix(transform), 0));
            _clipDepth++;
            _maxClipDepth = Math.Max(_maxClipDepth, _clipDepth);
            return true;
        }

        private void PushLayer()
        {
            _ops.Add(new ColorOp(ColorOpKind.PushLayer, 0, -1, 0));
            _layerDepth++;
            _maxLayerDepth = Math.Max(_maxLayerDepth, _layerDepth);
        }

        private void PopLayer(ColorCompositeMode mode)
        {
            _ops.Add(new ColorOp(ColorOpKind.PopLayer, 0, -1, (int)mode));
            _layerDepth--;
        }

        /// <summary>
        /// Decodes the outline of a glyph, which validates it, and adds its box to the box of the color glyph.
        /// </summary>
        /// <returns><see langword="false"/> when the glyph has no outline: there is nothing to fill or to clip with.</returns>
        private bool IncludeOutline(int glyph, in Matrix transform)
        {
            if ((uint)glyph >= (uint)_face.GlyphCount)
                return false;

            _bounds.Reset();
            _face.DecodeOutline(glyph, _bounds, _scratch, ref _budget);
            if (_bounds.IsEmpty)
                return false;

            if (!_hasRootBox)
            {
                Include(transform, _bounds.MinX, _bounds.MinY);
                Include(transform, _bounds.MinX, _bounds.MaxY);
                Include(transform, _bounds.MaxX, _bounds.MinY);
                Include(transform, _bounds.MaxX, _bounds.MaxY);
            }

            return true;
        }

        private void Include(in Matrix transform, double x, double y)
        {
            var tx = (transform.XX * x) + (transform.XY * y) + transform.DX;
            var ty = (transform.YX * x) + (transform.YY * y) + transform.DY;
            _xMin = Math.Min(_xMin, tx);
            _yMin = Math.Min(_yMin, ty);
            _xMax = Math.Max(_xMax, tx);
            _yMax = Math.Max(_yMax, ty);
        }

        private int AddMatrix(in Matrix matrix)
        {
            if (matrix.IsIdentity)
                return -1;

            if (!(double.IsFinite(matrix.XX) && double.IsFinite(matrix.YX) && double.IsFinite(matrix.XY) && double.IsFinite(matrix.YY) && double.IsFinite(matrix.DX) && double.IsFinite(matrix.DY)))
                throw FontErrors.InvalidTable(FontTags.Colr, "has transformations whose product is not a number.");

            _numbers.AddRange([matrix.XX, matrix.YX, matrix.XY, matrix.YY, matrix.DX, matrix.DY]);
            return _numbers.Count - 6;
        }

        /// <summary>Reads a transformation paint: the paint it transforms, and the transformation.</summary>
        /// <returns><see langword="false"/> when the paint is not a transformation.</returns>
        private readonly bool TryGetTransform(int paint, out int child, out Matrix matrix)
        {
            var table = _table;
            var format = FontBinary.UInt8(table, paint);
            child = 0;
            matrix = Matrix.Identity;
            if (format is < 12 or > 31)
                return false;

            child = FontBinary.AddOffset(paint, FontBinary.UInt24(table, paint + 1));

            // The formats come in pairs: the second one of each pair varies, with the index of its deltas after its
            // fields.
            var isVariable = (format & 1) != 0;
            var fields = paint + 4;
            switch (format)
            {
                // PaintTransform: a matrix of 16.16 numbers, in its own table.
                case 12 or 13:
                    {
                        var affine = FontBinary.AddOffset(paint, FontBinary.UInt24(table, paint + 4));
                        var variation = isVariable ? FontBinary.UInt32(table, affine + 24) : NoVariation;
                        matrix = new Matrix(
                            (FontBinary.Int32(table, affine) + Delta(variation, 0)) / 65536,
                            (FontBinary.Int32(table, affine + 4) + Delta(variation, 1)) / 65536,
                            (FontBinary.Int32(table, affine + 8) + Delta(variation, 2)) / 65536,
                            (FontBinary.Int32(table, affine + 12) + Delta(variation, 3)) / 65536,
                            (FontBinary.Int32(table, affine + 16) + Delta(variation, 4)) / 65536,
                            (FontBinary.Int32(table, affine + 20) + Delta(variation, 5)) / 65536);
                        break;
                    }

                // PaintTranslate.
                case 14 or 15:
                    {
                        var variation = isVariable ? FontBinary.UInt32(table, fields + 4) : NoVariation;
                        matrix = Matrix.Translate(FontBinary.Int16(table, fields) + Delta(variation, 0), FontBinary.Int16(table, fields + 2) + Delta(variation, 1));
                        break;
                    }

                // PaintScale, and around a center.
                case >= 16 and <= 19:
                    {
                        var hasCenter = format >= 18;
                        var variation = isVariable ? FontBinary.UInt32(table, fields + (hasCenter ? 8 : 4)) : NoVariation;
                        matrix = new Matrix(ReadF2Dot14(fields, variation, 0), 0, 0, ReadF2Dot14(fields + 2, variation, 1), 0, 0);
                        if (hasCenter)
                        {
                            matrix = matrix.Around(FontBinary.Int16(table, fields + 4) + Delta(variation, 2), FontBinary.Int16(table, fields + 6) + Delta(variation, 3));
                        }

                        break;
                    }

                // PaintScaleUniform, and around a center.
                case >= 20 and <= 23:
                    {
                        var hasCenter = format >= 22;
                        var variation = isVariable ? FontBinary.UInt32(table, fields + (hasCenter ? 6 : 2)) : NoVariation;
                        var scale = ReadF2Dot14(fields, variation, 0);
                        matrix = new Matrix(scale, 0, 0, scale, 0, 0);
                        if (hasCenter)
                        {
                            matrix = matrix.Around(FontBinary.Int16(table, fields + 2) + Delta(variation, 1), FontBinary.Int16(table, fields + 4) + Delta(variation, 2));
                        }

                        break;
                    }

                // PaintRotate, and around a center: counter-clockwise, in half turns.
                case >= 24 and <= 27:
                    {
                        var hasCenter = format >= 26;
                        var variation = isVariable ? FontBinary.UInt32(table, fields + (hasCenter ? 6 : 2)) : NoVariation;
                        DeterministicAngle.SinCos(ReadF2Dot14(fields, variation, 0), out var sin, out var cos);
                        matrix = new Matrix(cos, sin, -sin, cos, 0, 0);
                        if (hasCenter)
                        {
                            matrix = matrix.Around(FontBinary.Int16(table, fields + 2) + Delta(variation, 1), FontBinary.Int16(table, fields + 4) + Delta(variation, 2));
                        }

                        break;
                    }

                // PaintSkew, and around a center: the angles of the skews along each axis, in half turns.
                default:
                    {
                        var hasCenter = format >= 30;
                        var variation = isVariable ? FontBinary.UInt32(table, fields + (hasCenter ? 8 : 4)) : NoVariation;
                        var x = DeterministicAngle.Tan(ReadF2Dot14(fields, variation, 0));
                        var y = DeterministicAngle.Tan(ReadF2Dot14(fields + 2, variation, 1));
                        matrix = new Matrix(1, y, -x, 1, 0, 0);
                        if (hasCenter)
                        {
                            matrix = matrix.Around(FontBinary.Int16(table, fields + 4) + Delta(variation, 2), FontBinary.Int16(table, fields + 6) + Delta(variation, 3));
                        }

                        break;
                    }
            }

            return true;
        }

        /// <summary>Adds a paint that fills: a color or a gradient.</summary>
        /// <returns>The index of the paint, or -1 when it draws nothing.</returns>
        private int CreatePaint(int paint, int format, in Matrix transform)
        {
            var table = _table;
            var isVariable = (format & 1) != 0;
            switch (format)
            {
                // PaintSolid: a color of the palette and an opacity.
                case 2 or 3:
                    {
                        var variation = isVariable ? FontBinary.UInt32(table, paint + 5) : NoVariation;
                        return AddSolid(FontBinary.UInt16(table, paint + 1), Math.Clamp(ReadF2Dot14(paint + 3, variation, 0), 0, 1));
                    }

                // PaintLinearGradient: the color line goes from P0 to P1, and the lines of one color are parallel to
                // the line from P0 to P2. The gradient is the one from P0 to the projection of P1 on the perpendicular
                // of P0 P2 through P0.
                case 4 or 5:
                    {
                        var variation = isVariable ? FontBinary.UInt32(table, paint + 16) : NoVariation;
                        var x0 = FontBinary.Int16(table, paint + 4) + Delta(variation, 0);
                        var y0 = FontBinary.Int16(table, paint + 6) + Delta(variation, 1);
                        var x1 = FontBinary.Int16(table, paint + 8) + Delta(variation, 2);
                        var y1 = FontBinary.Int16(table, paint + 10) + Delta(variation, 3);
                        var x2 = FontBinary.Int16(table, paint + 12) + Delta(variation, 4);
                        var y2 = FontBinary.Int16(table, paint + 14) + Delta(variation, 5);
                        var dx = x1 - x0;
                        var dy = y1 - y0;
                        var rx = x2 - x0;
                        var ry = y2 - y0;
                        var rotation = (rx * rx) + (ry * ry);
                        if (rotation > 0)
                        {
                            var k = ((rx * dx) + (ry * dy)) / rotation;
                            dx -= k * rx;
                            dy -= k * ry;
                        }

                        // A gradient without length is not drawn.
                        var length = (dx * dx) + (dy * dy);
                        if (!(length > 0))
                            return -1;

                        var geometry = _numbers.Count;
                        _numbers.AddRange([x0, y0, dx / length, dy / length]);
                        return AddGradient(ColorPaintKind.Linear, paint, isVariable, transform, geometry);
                    }

                // PaintRadialGradient: two circles. In a variable font a radius can be negative: the gradient is then
                // painted where its interpolated radius is positive, as for any radii.
                case 6 or 7:
                    {
                        var variation = isVariable ? FontBinary.UInt32(table, paint + 16) : NoVariation;
                        var geometry = _numbers.Count;
                        _numbers.AddRange([
                            FontBinary.Int16(table, paint + 4) + Delta(variation, 0),
                            FontBinary.Int16(table, paint + 6) + Delta(variation, 1),
                            FontBinary.UInt16(table, paint + 8) + Delta(variation, 2),
                            FontBinary.Int16(table, paint + 10) + Delta(variation, 3),
                            FontBinary.Int16(table, paint + 12) + Delta(variation, 4),
                            FontBinary.UInt16(table, paint + 14) + Delta(variation, 5),
                        ]);
                        return AddGradient(ColorPaintKind.Radial, paint, isVariable, transform, geometry);
                    }

                // PaintSweepGradient: a center and two angles, stored as half turns minus one.
                default:
                    {
                        var variation = isVariable ? FontBinary.UInt32(table, paint + 12) : NoVariation;
                        var geometry = _numbers.Count;
                        var start = ReadF2Dot14(paint + 8, variation, 2) + 1;
                        var end = ReadF2Dot14(paint + 10, variation, 3) + 1;
                        _numbers.AddRange([FontBinary.Int16(table, paint + 4) + Delta(variation, 0), FontBinary.Int16(table, paint + 6) + Delta(variation, 1), start, end]);
                        return AddGradient(ColorPaintKind.Sweep, paint, isVariable, transform, geometry, hasLength: start != end);
                    }
            }
        }

        /// <summary>
        /// Reads the color line of a gradient and adds the gradient. The stops are sorted, and their offsets are brought
        /// to the range from 0 to 1: the two numbers that follow the geometry of the gradient are the offset of the first
        /// stop and the inverse of the length of the color line, or 0 when all the stops are at the same offset.
        /// </summary>
        /// <param name="kind">The kind of gradient.</param>
        /// <param name="paint">The position of the paint table.</param>
        /// <param name="isVariable">Whether the color line is the form that varies.</param>
        /// <param name="transform">The transformation from the space of the paint to the space of the glyph.</param>
        /// <param name="geometry">The index of the geometry of the gradient, which the two numbers of the color line follow.</param>
        /// <param name="hasLength">Whether the color line has a length in the geometry: a sweep between two equal angles has none.</param>
        /// <returns>The index of the paint, or -1 when it draws nothing.</returns>
        private int AddGradient(ColorPaintKind kind, int paint, bool isVariable, in Matrix transform, int geometry, bool hasLength = true)
        {
            var table = _table;
            var line = FontBinary.AddOffset(paint, FontBinary.UInt24(table, paint + 1));
            var extend = FontBinary.UInt8(table, line);
            var count = FontBinary.UInt16(table, line + 1);
            if (count == 0)
                return -1;

            _budget.Charge(count);
            var stopLength = isVariable ? 10 : 6;
            var first = _stops.Count;
            for (var i = 0; i < count; i++)
            {
                var stop = line + 3 + (i * stopLength);
                var variation = isVariable ? FontBinary.UInt32(table, stop + 6) : NoVariation;
                var offset = ReadF2Dot14(stop, variation, 0);
                var color = GetColor(offset, FontBinary.UInt16(table, stop + 2), Math.Clamp(ReadF2Dot14(stop + 4, variation, 1), 0, 1));

                // An insertion sort, which keeps the order of the stops at the same offset.
                var position = _stops.Count;
                while (position > first && _stops[position - 1].Offset > offset)
                {
                    position--;
                }

                _stops.Insert(position, color);
            }

            // "Color lines": when every stop is at the same offset, or when a sweep starts and ends at the same angle, a
            // color line that pads has two colors, the first one before that place and the last one from it; one that
            // repeats or reflects is ill-formed, and nothing is drawn.
            var start = _stops[first].Offset;
            var length = _stops[^1].Offset - start;
            if ((!(length > 0) || !hasLength) && extend is 1 or 2)
            {
                _stops.RemoveRange(first, _stops.Count - first);
                return -1;
            }

            for (var i = first; i < _stops.Count; i++)
            {
                _stops[i] = _stops[i] with { Offset = length > 0 ? (_stops[i].Offset - start) / length : 0 };
            }

            _numbers.AddRange([start, length > 0 ? 1 / length : 0]);
            _paints.Add(new ColorPaint(kind, AddMatrix(transform), geometry, first, count, extend is 1 or 2 ? extend : 0));
            return _paints.Count - 1;
        }

        /// <summary>Gets a color of the palette with an opacity; an entry the palette does not have is the color of the text.</summary>
        private readonly ColorStop GetColor(double offset, int paletteIndex, double alpha)
        {
            if (paletteIndex != CpalTable.Foreground && _face.TryGetPaletteColor(_palette, paletteIndex, out var red, out var green, out var blue, out var opacity))
                return new ColorStop(offset, red / 255.0, green / 255.0, blue / 255.0, alpha * (opacity / 255.0), IsForeground: false);

            return new ColorStop(offset, 0, 0, 0, alpha, IsForeground: true);
        }

        private readonly double ReadF2Dot14(int position, uint variation, int index)
            => (FontBinary.Int16(_table, position) + Delta(variation, index)) / 16384;

        /// <summary>Gets the delta of a field of a paint for the instance of a variable font: it is not rounded.</summary>
        private readonly double Delta(uint variation, int index)
        {
            var scalars = _face.Instance is null ? default : _face.Instance.ColorScalars.AsSpan();
            if (variation == NoVariation || scalars.IsEmpty || _colr.VariationStore == 0)
                return 0;

            var item = (long)variation + index;
            var outer = (int)(item >> 16);
            var inner = (int)(item & 0xFFFF);
            if (_colr._variationMap != 0)
            {
                ItemVariationStore.ReadIndexMap(_table, _colr._variationMap, (int)Math.Min(item, int.MaxValue), out outer, out inner);
            }

            return ItemVariationStore.GetDelta(_table, _colr.VariationStore, outer, inner, scalars);
        }
    }
}
