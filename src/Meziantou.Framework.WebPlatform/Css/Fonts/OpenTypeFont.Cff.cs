using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

// The bounding boxes of the glyphs of the fonts with CFF outlines (https://learn.microsoft.com/typography/opentype/spec/cff): the
// Type 2 charstrings (Adobe Technical Note #5177) are interpreted to compute the exact bounds of their paths (the extrema of the
// curves). Used for the ink metrics of the MathML token elements (https://w3c.github.io/mathml-core/#layout-of-mtext) and the
// metrics of the fonts without OS/2 x-height.
internal sealed partial class OpenTypeFont
{
    private CffOutlines? _cff;
    private bool _cffParsed;
    private ConcurrentDictionary<ushort, GlyphBounds?>? _cffBounds;

    /// <summary>
    /// Returns the bounding box of the outline of a glyph of a font with CFF outlines (the CFF table, not CFF2), computed from its
    /// charstring and cached; false when the glyph has no outline.
    /// </summary>
    private bool TryGetCffGlyphBounds(ushort glyph, out GlyphBounds bounds)
    {
        bounds = default;
        if (!_cffParsed)
        {
            // The parsed data is immutable: a concurrent parse gives the same result
            _cff = CffOutlines.Parse(GetTableRange(OpenTypeTag.Cff), Data);
            _cffParsed = true;
        }

        if (_cff is null || glyph >= GlyphCount)
            return false;

        var cache = _cffBounds ??= new ConcurrentDictionary<ushort, GlyphBounds?>();
        var result = cache.GetOrAdd(glyph, static (glyph, font) => font._cff!.ComputeBounds(font.Data, glyph), this);
        if (result is not { } value)
            return false;

        bounds = value;
        return true;
    }

    /// <summary>The structure of a CFF table (https://adobe-type-tools.github.io/font-tech-notes/pdfs/5176.CFF.pdf) needed to interpret the charstrings.</summary>
    private sealed class CffOutlines
    {
        private const int MaxSubroutineDepth = 10;

        private readonly int _tableOffset;
        private readonly int _tableLength;
        private readonly CffIndex _charStrings;
        private readonly CffIndex _globalSubroutines;
        private readonly CffIndex[] _localSubroutines;
        private readonly int _fdSelect;
        private readonly bool _isCid;

        private CffOutlines(int tableOffset, int tableLength, CffIndex charStrings, CffIndex globalSubroutines, CffIndex[] localSubroutines, int fdSelect, bool isCid)
        {
            _tableOffset = tableOffset;
            _tableLength = tableLength;
            _charStrings = charStrings;
            _globalSubroutines = globalSubroutines;
            _localSubroutines = localSubroutines;
            _fdSelect = fdSelect;
            _isCid = isCid;
        }

        public static CffOutlines? Parse((int Offset, int Length) range, byte[] data)
        {
            if (range.Offset < 0)
                return null;

            var table = data.AsSpan(range.Offset, range.Length);
            if (FontBinary.U8(table, 0) != 1)
                return null;

            // Header, Name INDEX, Top DICT INDEX, String INDEX, Global Subr INDEX
            var nameIndex = CffIndex.Read(table, FontBinary.U8(table, 2));
            var topDictIndex = CffIndex.Read(table, nameIndex.End);
            var stringIndex = CffIndex.Read(table, topDictIndex.End);
            var globalSubroutines = CffIndex.Read(table, stringIndex.End);
            if (topDictIndex.Count == 0)
                return null;

            var (topStart, topLength) = topDictIndex.Get(table, 0);
            var topDict = ReadDict(table.Slice(topStart, topLength));
            if (!topDict.TryGetValue(17, out var charStringsOperands) || charStringsOperands.Length == 0)
                return null;

            // Type 2 charstrings only (CharstringType, 12 6)
            if (topDict.TryGetValue(1206, out var charStringType) && charStringType.Length > 0 && (int)charStringType[0] != 2)
                return null;

            var charStrings = CffIndex.Read(table, (int)charStringsOperands[0]);
            var isCid = topDict.ContainsKey(1230);
            CffIndex[] localSubroutines;
            var fdSelect = -1;
            if (isCid && topDict.TryGetValue(1236, out var fdArrayOperands) && fdArrayOperands.Length > 0 && topDict.TryGetValue(1237, out var fdSelectOperands) && fdSelectOperands.Length > 0)
            {
                // CID-keyed font: a Private DICT per font DICT of the FDArray
                var fdArray = CffIndex.Read(table, (int)fdArrayOperands[0]);
                localSubroutines = new CffIndex[fdArray.Count];
                for (var i = 0; i < fdArray.Count; i++)
                {
                    var (fdStart, fdLength) = fdArray.Get(table, i);
                    localSubroutines[i] = ReadLocalSubroutines(table, ReadDict(table.Slice(fdStart, fdLength)));
                }

                fdSelect = (int)fdSelectOperands[0];
            }
            else
            {
                isCid = false;
                localSubroutines = [ReadLocalSubroutines(table, topDict)];
            }

            return new CffOutlines(range.Offset, range.Length, charStrings, globalSubroutines, localSubroutines, fdSelect, isCid);
        }

        /// <summary>The local subroutines of the Private DICT referenced by a Top DICT or a font DICT (operator 18: size, offset; Subrs, operator 19, relative to the Private DICT).</summary>
        private static CffIndex ReadLocalSubroutines(ReadOnlySpan<byte> table, Dictionary<int, double[]> dict)
        {
            if (!dict.TryGetValue(18, out var privateOperands) || privateOperands.Length < 2)
                return CffIndex.Empty;

            var privateOffset = (int)privateOperands[1];
            var privateDict = ReadDict(FontBinary.Slice(table, privateOffset, (int)privateOperands[0]));
            return privateDict.TryGetValue(19, out var subrs) && subrs.Length > 0 ? CffIndex.Read(table, privateOffset + (int)subrs[0]) : CffIndex.Empty;
        }

        /// <summary>Reads a DICT (operands followed by an operator; the two-byte operators are 1200 + the second byte).</summary>
        private static Dictionary<int, double[]> ReadDict(ReadOnlySpan<byte> dict)
        {
            var result = new Dictionary<int, double[]>();
            var operands = new List<double>();
            var i = 0;
            while (i < dict.Length)
            {
                var b0 = dict[i];
                if (b0 <= 21)
                {
                    var op = b0 == 12 ? 1200 + FontBinary.U8(dict, i + 1) : b0;
                    i += b0 == 12 ? 2 : 1;
                    result[op] = [.. operands];
                    operands.Clear();
                }
                else if (b0 == 28)
                {
                    operands.Add(FontBinary.I16(dict, i + 1));
                    i += 3;
                }
                else if (b0 == 29)
                {
                    operands.Add(FontBinary.I32(dict, i + 1));
                    i += 5;
                }
                else if (b0 == 30)
                {
                    // A real number: nibbles until 0xf (the value is not needed by the charstring interpretation)
                    i++;
                    while (i < dict.Length && (dict[i] & 0x0F) != 0x0F && (dict[i] & 0xF0) != 0xF0)
                    {
                        i++;
                    }

                    i++;
                    operands.Add(0);
                }
                else if (b0 is >= 32 and <= 246)
                {
                    operands.Add(b0 - 139);
                    i++;
                }
                else if (b0 is >= 247 and <= 250)
                {
                    operands.Add(((b0 - 247) * 256) + FontBinary.U8(dict, i + 1) + 108);
                    i += 2;
                }
                else if (b0 is >= 251 and <= 254)
                {
                    operands.Add((-(b0 - 251) * 256) - FontBinary.U8(dict, i + 1) - 108);
                    i += 2;
                }
                else
                {
                    i++;
                }
            }

            return result;
        }

        /// <summary>The index of the font DICT of a glyph of a CID-keyed font (FDSelect formats 0 and 3).</summary>
        private int GetFontDictIndex(ReadOnlySpan<byte> table, ushort glyph)
        {
            if (!_isCid || _fdSelect < 0)
                return 0;

            var format = FontBinary.U8(table, _fdSelect);
            if (format == 0)
                return FontBinary.U8(table, _fdSelect + 1 + glyph);

            if (format == 3)
            {
                var count = FontBinary.U16(table, _fdSelect + 1);
                for (var i = 0; i < count; i++)
                {
                    var record = _fdSelect + 3 + (i * 3);
                    var next = FontBinary.U16(table, record + 3);
                    if (glyph >= FontBinary.U16(table, record) && glyph < next)
                        return FontBinary.U8(table, record + 2);
                }
            }

            return 0;
        }

        /// <summary>Interprets the charstring of a glyph and returns the bounds of its path (rounded outwards), or null when it has none.</summary>
        public GlyphBounds? ComputeBounds(byte[] data, ushort glyph)
        {
            var table = data.AsSpan(_tableOffset, _tableLength);
            if (glyph >= _charStrings.Count)
                return null;

            var fontDict = GetFontDictIndex(table, glyph);
            var interpreter = new CharStringInterpreter(_globalSubroutines, (uint)fontDict < (uint)_localSubroutines.Length ? _localSubroutines[fontDict] : CffIndex.Empty);
            var (start, length) = _charStrings.Get(table, glyph);
            interpreter.Execute(table, table.Slice(start, length), depth: 0);
            if (!interpreter.HasBounds)
                return null;

            return new GlyphBounds(
                (short)Math.Clamp(Math.Floor(interpreter.MinX), short.MinValue, short.MaxValue),
                (short)Math.Clamp(Math.Floor(interpreter.MinY), short.MinValue, short.MaxValue),
                (short)Math.Clamp(Math.Ceiling(interpreter.MaxX), short.MinValue, short.MaxValue),
                (short)Math.Clamp(Math.Ceiling(interpreter.MaxY), short.MinValue, short.MaxValue));
        }

        /// <summary>An INDEX of a CFF table: <c>count</c> objects whose offsets are stored with <c>offSize</c> bytes.</summary>
        [StructLayout(LayoutKind.Auto)]
        private readonly struct CffIndex
        {
            private readonly int _offsetsStart;
            private readonly int _offSize;
            private readonly int _dataStart;

            private CffIndex(int count, int offsetsStart, int offSize, int dataStart, int end)
            {
                Count = count;
                _offsetsStart = offsetsStart;
                _offSize = offSize;
                _dataStart = dataStart;
                End = end;
            }

            public static CffIndex Empty => default;

            public int Count { get; }

            /// <summary>The offset of the byte after the INDEX.</summary>
            public int End { get; }

            public static CffIndex Read(ReadOnlySpan<byte> table, int offset)
            {
                var count = FontBinary.U16(table, offset);
                if (count == 0 || offset < 0)
                    return new CffIndex(0, 0, 0, 0, offset + 2);

                var offSize = FontBinary.U8(table, offset + 2);
                if (offSize is < 1 or > 4)
                    return new CffIndex(0, 0, 0, 0, offset + 2);

                var offsetsStart = offset + 3;
                var dataStart = offsetsStart + ((count + 1) * offSize) - 1;
                var index = new CffIndex(count, offsetsStart, offSize, dataStart, 0);
                return new CffIndex(count, offsetsStart, offSize, dataStart, dataStart + index.ReadOffset(table, count));
            }

            private int ReadOffset(ReadOnlySpan<byte> table, int index)
            {
                var position = _offsetsStart + (index * _offSize);
                return _offSize switch
                {
                    1 => FontBinary.U8(table, position),
                    2 => FontBinary.U16(table, position),
                    3 => (int)FontBinary.U24(table, position),
                    _ => FontBinary.I32(table, position),
                };
            }

            /// <summary>Returns the range of an object in the table (empty when it is outside of the table).</summary>
            public (int Start, int Length) Get(ReadOnlySpan<byte> table, int index)
            {
                if ((uint)index >= (uint)Count)
                    return (0, 0);

                var start = _dataStart + ReadOffset(table, index);
                var end = _dataStart + ReadOffset(table, index + 1);
                return start >= 0 && end >= start && end <= table.Length ? (start, end - start) : (0, 0);
            }
        }

        /// <summary>The interpreter of the Type 2 charstrings (https://adobe-type-tools.github.io/font-tech-notes/pdfs/5177.Type2.pdf), which computes the bounds of the path.</summary>
        private sealed class CharStringInterpreter(CffIndex globalSubroutines, CffIndex localSubroutines)
        {
            private readonly double[] _stack = new double[48];
            private int _count;
            private int _stems;
            private bool _hasWidth;
            private bool _ended;
            private double _x;
            private double _y;

            public bool HasBounds { get; private set; }

            public double MinX { get; private set; } = double.PositiveInfinity;

            public double MinY { get; private set; } = double.PositiveInfinity;

            public double MaxX { get; private set; } = double.NegativeInfinity;

            public double MaxY { get; private set; } = double.NegativeInfinity;

            private static int GetBias(int count) => count < 1240 ? 107 : count < 33900 ? 1131 : 32768;

            public void Execute(ReadOnlySpan<byte> table, ReadOnlySpan<byte> code, int depth)
            {
                var i = 0;
                while (i < code.Length && !_ended)
                {
                    var b0 = code[i];
                    if (b0 >= 32 || b0 == 28)
                    {
                        double value;
                        if (b0 == 28)
                        {
                            value = FontBinary.I16(code, i + 1);
                            i += 3;
                        }
                        else if (b0 <= 246)
                        {
                            value = b0 - 139;
                            i++;
                        }
                        else if (b0 <= 250)
                        {
                            value = ((b0 - 247) * 256) + FontBinary.U8(code, i + 1) + 108;
                            i += 2;
                        }
                        else if (b0 <= 254)
                        {
                            value = (-(b0 - 251) * 256) - FontBinary.U8(code, i + 1) - 108;
                            i += 2;
                        }
                        else
                        {
                            value = FontBinary.I32(code, i + 1) / 65536.0;
                            i += 5;
                        }

                        if (_count < _stack.Length)
                        {
                            _stack[_count++] = value;
                        }

                        continue;
                    }

                    i++;
                    switch (b0)
                    {
                        case 1 or 3 or 18 or 23: // hstem, vstem, hstemhm, vstemhm
                            TakeWidth(_count % 2 != 0);
                            _stems += _count / 2;
                            _count = 0;
                            break;
                        case 19 or 20: // hintmask, cntrmask (the operands are an implicit vstem)
                            TakeWidth(_count % 2 != 0);
                            _stems += _count / 2;
                            _count = 0;
                            i += (_stems + 7) / 8;
                            break;
                        case 21: // rmoveto
                            TakeWidth(_count > 2);
                            _x += Arg(0);
                            _y += Arg(1);
                            _count = 0;
                            break;
                        case 22: // hmoveto
                            TakeWidth(_count > 1);
                            _x += Arg(0);
                            _count = 0;
                            break;
                        case 4: // vmoveto
                            TakeWidth(_count > 1);
                            _y += Arg(0);
                            _count = 0;
                            break;
                        case 5: // rlineto
                            for (var a = 0; a + 1 < _count; a += 2)
                            {
                                LineTo(_x + _stack[a], _y + _stack[a + 1]);
                            }

                            _count = 0;
                            break;
                        case 6 or 7: // hlineto, vlineto
                        {
                            var horizontal = b0 == 6;
                            for (var a = 0; a < _count; a++)
                            {
                                if (horizontal)
                                {
                                    LineTo(_x + _stack[a], _y);
                                }
                                else
                                {
                                    LineTo(_x, _y + _stack[a]);
                                }

                                horizontal = !horizontal;
                            }

                            _count = 0;
                            break;
                        }

                        case 8: // rrcurveto
                            for (var a = 0; a + 5 < _count; a += 6)
                            {
                                RelativeCurve(_stack[a], _stack[a + 1], _stack[a + 2], _stack[a + 3], _stack[a + 4], _stack[a + 5]);
                            }

                            _count = 0;
                            break;
                        case 24: // rcurveline
                        {
                            var a = 0;
                            for (; a + 5 < _count - 2; a += 6)
                            {
                                RelativeCurve(_stack[a], _stack[a + 1], _stack[a + 2], _stack[a + 3], _stack[a + 4], _stack[a + 5]);
                            }

                            if (a + 1 < _count)
                            {
                                LineTo(_x + _stack[a], _y + _stack[a + 1]);
                            }

                            _count = 0;
                            break;
                        }

                        case 25: // rlinecurve
                        {
                            var a = 0;
                            for (; a + 1 < _count - 6; a += 2)
                            {
                                LineTo(_x + _stack[a], _y + _stack[a + 1]);
                            }

                            if (a + 5 < _count)
                            {
                                RelativeCurve(_stack[a], _stack[a + 1], _stack[a + 2], _stack[a + 3], _stack[a + 4], _stack[a + 5]);
                            }

                            _count = 0;
                            break;
                        }

                        case 26: // vvcurveto
                        {
                            var a = 0;
                            var dx1 = 0.0;
                            if (_count % 2 != 0)
                            {
                                dx1 = _stack[0];
                                a = 1;
                            }

                            for (; a + 3 < _count; a += 4)
                            {
                                RelativeCurve(dx1, _stack[a], _stack[a + 1], _stack[a + 2], 0, _stack[a + 3]);
                                dx1 = 0;
                            }

                            _count = 0;
                            break;
                        }

                        case 27: // hhcurveto
                        {
                            var a = 0;
                            var dy1 = 0.0;
                            if (_count % 2 != 0)
                            {
                                dy1 = _stack[0];
                                a = 1;
                            }

                            for (; a + 3 < _count; a += 4)
                            {
                                RelativeCurve(_stack[a], dy1, _stack[a + 1], _stack[a + 2], _stack[a + 3], 0);
                                dy1 = 0;
                            }

                            _count = 0;
                            break;
                        }

                        case 30 or 31: // vhcurveto, hvcurveto
                        {
                            var horizontal = b0 == 31;
                            for (var a = 0; a + 3 < _count; a += 4)
                            {
                                var last = a + 4 == _count - 1 ? _stack[a + 4] : 0;
                                if (horizontal)
                                {
                                    RelativeCurve(_stack[a], 0, _stack[a + 1], _stack[a + 2], last, _stack[a + 3]);
                                }
                                else
                                {
                                    RelativeCurve(0, _stack[a], _stack[a + 1], _stack[a + 2], _stack[a + 3], last);
                                }

                                horizontal = !horizontal;
                            }

                            _count = 0;
                            break;
                        }

                        case 10 or 29: // callsubr, callgsubr
                        {
                            if (_count == 0 || depth >= MaxSubroutineDepth)
                            {
                                _ended = true;
                                break;
                            }

                            var subroutines = b0 == 10 ? localSubroutines : globalSubroutines;
                            var index = (int)_stack[--_count] + GetBias(subroutines.Count);
                            var (start, length) = subroutines.Get(table, index);
                            if (length > 0)
                            {
                                Execute(table, table.Slice(start, length), depth + 1);
                            }

                            break;
                        }

                        case 11: // return
                            return;
                        case 14: // endchar
                            _ended = true;
                            break;
                        case 12:
                        {
                            var op = FontBinary.U8(code, i);
                            i++;
                            Flex(op);
                            _count = 0;
                            break;
                        }

                        default:
                            _count = 0;
                            break;
                    }
                }
            }

            private double Arg(int index) => index < _count ? _stack[index] : 0;

            /// <summary>The first stack-clearing operator may have the advance width as an extra first operand.</summary>
            private void TakeWidth(bool hasExtraOperand)
            {
                if (_hasWidth)
                    return;

                _hasWidth = true;
                if (hasExtraOperand && _count > 0)
                {
                    Array.Copy(_stack, 1, _stack, 0, _count - 1);
                    _count--;
                }
            }

            private void Flex(byte op)
            {
                var s = _stack;
                switch (op)
                {
                    case 35 when _count >= 12: // flex
                        RelativeCurve(s[0], s[1], s[2], s[3], s[4], s[5]);
                        RelativeCurve(s[6], s[7], s[8], s[9], s[10], s[11]);
                        break;
                    case 34 when _count >= 7: // hflex
                    {
                        var y = _y;
                        RelativeCurve(s[0], 0, s[1], s[2], s[3], 0);
                        RelativeCurve(s[4], 0, s[5], y - _y, s[6], 0);
                        break;
                    }

                    case 36 when _count >= 9: // hflex1
                    {
                        var y = _y;
                        RelativeCurve(s[0], s[1], s[2], s[3], s[4], 0);
                        RelativeCurve(s[5], 0, s[6], s[7], s[8], y - (_y + s[7]));
                        break;
                    }

                    case 37 when _count >= 11: // flex1
                    {
                        var dx = s[0] + s[2] + s[4] + s[6] + s[8];
                        var dy = s[1] + s[3] + s[5] + s[7] + s[9];
                        var horizontal = Math.Abs(dx) > Math.Abs(dy);
                        RelativeCurve(s[0], s[1], s[2], s[3], s[4], s[5]);
                        RelativeCurve(s[6], s[7], s[8], s[9], horizontal ? s[10] : -dx, horizontal ? -dy : s[10]);
                        break;
                    }
                }
            }

            private void Include(double x, double y)
            {
                HasBounds = true;
                MinX = Math.Min(MinX, x);
                MinY = Math.Min(MinY, y);
                MaxX = Math.Max(MaxX, x);
                MaxY = Math.Max(MaxY, y);
            }

            private void LineTo(double x, double y)
            {
                Include(_x, _y);
                Include(x, y);
                _x = x;
                _y = y;
            }

            private void RelativeCurve(double dx1, double dy1, double dx2, double dy2, double dx3, double dy3)
            {
                var x0 = _x;
                var y0 = _y;
                var x1 = x0 + dx1;
                var y1 = y0 + dy1;
                var x2 = x1 + dx2;
                var y2 = y1 + dy2;
                var x3 = x2 + dx3;
                var y3 = y2 + dy3;
                Include(x0, y0);
                Include(x3, y3);

                // The extrema of the cubic Bézier curve inside of the segment (the roots of its derivative)
                IncludeExtrema(x0, x1, x2, x3, y0, y1, y2, y3, horizontal: true);
                IncludeExtrema(y0, y1, y2, y3, x0, x1, x2, x3, horizontal: false);
                _x = x3;
                _y = y3;
            }

            private void IncludeExtrema(double p0, double p1, double p2, double p3, double q0, double q1, double q2, double q3, bool horizontal)
            {
                var a = -p0 + (3 * p1) - (3 * p2) + p3;
                var b = 2 * (p0 - (2 * p1) + p2);
                var c = p1 - p0;
                Span<double> roots = stackalloc double[2];
                var count = 0;
                if (Math.Abs(a) < 1e-12)
                {
                    if (Math.Abs(b) > 1e-12)
                    {
                        roots[count++] = -c / b;
                    }
                }
                else
                {
                    var discriminant = (b * b) - (4 * a * c);
                    if (discriminant >= 0)
                    {
                        var root = Math.Sqrt(discriminant);
                        roots[count++] = (-b + root) / (2 * a);
                        roots[count++] = (-b - root) / (2 * a);
                    }
                }

                for (var i = 0; i < count; i++)
                {
                    var t = roots[i];
                    if (t is > 0 and < 1)
                    {
                        var p = Evaluate(p0, p1, p2, p3, t);
                        var q = Evaluate(q0, q1, q2, q3, t);
                        if (horizontal)
                        {
                            Include(p, q);
                        }
                        else
                        {
                            Include(q, p);
                        }
                    }
                }
            }

            private static double Evaluate(double p0, double p1, double p2, double p3, double t)
            {
                var u = 1 - t;
                return (u * u * u * p0) + (3 * u * u * t * p1) + (3 * u * t * t * p2) + (t * t * t * p3);
            }
        }
    }
}
