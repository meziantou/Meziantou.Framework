using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Runs a Type 2 charstring, the program that draws a glyph of a CFF font, and emits its outline. Adobe Technical Note
/// #5177, "The Type 2 Charstring Format".
/// </summary>
/// <remarks>
/// <para>
/// The hints are parsed and ignored: the library does not hint. The width a charstring can start with is skipped: the
/// advances of an OpenType font are in its <c>hmtx</c> table.
/// </para>
/// <para>
/// The charstrings of a CFF2 table (OpenType specification, "CFF2 CharString Format") are the same programs without
/// width and without <c>endchar</c>, with two more operators: <c>blend</c> replaces values of the default instance and
/// their deltas by the values of an instance of a variable font, and <c>vsindex</c> selects the regions the deltas are
/// for. The values of an instance are not rounded.
/// </para>
/// <para>
/// A contour is closed when the next one starts or when the charstring ends, by a line to its first point when its last
/// point is another one. A contour that draws nothing emits nothing.
/// </para>
/// <para>
/// Every operator is charged to the budget of the glyph, so that subroutines that call each other cannot run for a time
/// that is out of proportion with the size of the font.
/// </para>
/// </remarks>
internal ref struct CffCharstringInterpreter
{
    /// <summary>The size of the argument stack. The Type 2 format allows 48 arguments and CFF2 513: the larger one is used for both.</summary>
    public const int StackSize = 513;

    /// <summary>The depth of subroutine calls: appendix B, "Type 2 Charstring Implementation Limits".</summary>
    private const int MaxCallDepth = 10;

    /// <summary>The distance, in font units, under which the last point of a contour is its first point.</summary>
    private const double ClosingTolerance = 1.0 / (1 << 20);

    /// <summary>The size of the transient array of the <c>put</c> and <c>get</c> operators.</summary>
    private const int TransientArraySize = 32;

    private readonly ReadOnlySpan<byte> _table;
    private readonly CffIndex _globalSubroutines;
    private readonly CffIndex _localSubroutines;
    private readonly OutlineSink _sink;
    private readonly ref GlyphBudget _budget;
    private readonly bool _isCff2;
    private readonly int _variationStore;
    private readonly ReadOnlySpan<double> _scalars;

    // The scalars of the regions the charstring blends with, in the frame of the caller: CFF2 only.
    private readonly Span<double> _blend;
    private int _variationStoreIndex;
    private int _regionCount;
    private Buffer _buffer;
    private int _count;
    private int _hintCount;
    private bool _hasWidth;
    private bool _isPathOpen;
    private double _x;
    private double _y;
    private double _startX;
    private double _startY;

    public CffCharstringInterpreter(ReadOnlySpan<byte> table, CffIndex globalSubroutines, CffIndex localSubroutines, OutlineSink sink, ref GlyphBudget budget)
    {
        _table = table;
        _globalSubroutines = globalSubroutines;
        _localSubroutines = localSubroutines;
        _sink = sink;
        _budget = ref budget;
    }

    /// <summary>Creates an interpreter of the charstrings of a CFF2 table, which have no width and blend their arguments for an instance.</summary>
    /// <param name="table">The table.</param>
    /// <param name="globalSubroutines">The global subroutines.</param>
    /// <param name="localSubroutines">The subroutines of the font DICT of the glyph.</param>
    /// <param name="sink">The sink.</param>
    /// <param name="budget">The complexity budget of the glyph.</param>
    /// <param name="variationStore">The position of the item variation store, or 0.</param>
    /// <param name="variationStoreIndex">The subtable of the store the font DICT of the glyph blends with.</param>
    /// <param name="scalars">The scalars of the regions of the store for the instance; empty for the default instance.</param>
    /// <param name="blend">The storage of the scalars of the regions a charstring blends with: <see cref="StackSize"/> numbers, set to zero.</param>
    public CffCharstringInterpreter(ReadOnlySpan<byte> table, CffIndex globalSubroutines, CffIndex localSubroutines, OutlineSink sink, ref GlyphBudget budget, int variationStore, int variationStoreIndex, ReadOnlySpan<double> scalars, Span<double> blend)
        : this(table, globalSubroutines, localSubroutines, sink, ref budget)
    {
        _isCff2 = true;
        _blend = blend;
        _hasWidth = true;
        _variationStore = variationStore;
        _variationStoreIndex = variationStoreIndex;
        _scalars = scalars;
        _regionCount = -1;
    }

    /// <summary>Runs the charstring of a glyph.</summary>
    public void Run(ReadOnlySpan<byte> charstring)
    {
        // A charstring ends with endchar; one that does not ends where its bytes end.
        Execute(charstring, depth: 0);
        ClosePath();
    }

    /// <summary>Runs a charstring or a subroutine.</summary>
    /// <returns><see langword="true"/> when the glyph is finished.</returns>
    private bool Execute(ReadOnlySpan<byte> code, int depth)
    {
        Span<double> stack = _buffer.Stack;
        var position = 0;
        while (position < code.Length)
        {
            var b0 = code[position++];
            if (b0 >= 32 || b0 == 28)
            {
                if (_count == StackSize)
                    throw Invalid("pushes more arguments than the stack can hold");

                // Section 3.2, "Charstring Number Encoding".
                switch (b0)
                {
                    case 28:
                        stack[_count++] = FontBinary.Int16(code, position);
                        position += 2;
                        break;

                    case <= 246:
                        stack[_count++] = b0 - 139;
                        break;

                    case <= 250:
                        stack[_count++] = ((b0 - 247) * 256) + FontBinary.UInt8(code, position++) + 108;
                        break;

                    case <= 254:
                        stack[_count++] = (-(b0 - 251) * 256) - FontBinary.UInt8(code, position++) - 108;
                        break;

                    default:
                        // A 16.16 fixed-point number.
                        stack[_count++] = FontBinary.Int32(code, position) / 65536.0;
                        position += 4;
                        break;
                }

                continue;
            }

            _budget.Charge(1);
            switch (b0)
            {
                // hstem, vstem, hstemhm, vstemhm: pairs of hint positions.
                case 1 or 3 or 18 or 23:
                    SkipWidth(stack, (_count & 1) != 0);
                    _hintCount += _count / 2;
                    _count = 0;
                    break;

                // hintmask, cntrmask: one bit per hint follows. Arguments before the first mask are vertical hints.
                case 19 or 20:
                    SkipWidth(stack, (_count & 1) != 0);
                    _hintCount += _count / 2;
                    _count = 0;
                    position += (_hintCount + 7) / 8;
                    if (position > code.Length)
                        throw Invalid("ends inside a hint mask");

                    break;

                // rmoveto, hmoveto, vmoveto: a new contour.
                case 21:
                    SkipWidth(stack, _count > 2);
                    Require(2);
                    MoveTo(stack[0], stack[1]);
                    break;

                case 22:
                    SkipWidth(stack, _count > 1);
                    Require(1);
                    MoveTo(stack[0], 0);
                    break;

                case 4:
                    SkipWidth(stack, _count > 1);
                    Require(1);
                    MoveTo(0, stack[0]);
                    break;

                // rlineto: {dx dy}+
                case 5:
                    Require(2);
                    for (var i = 0; i + 1 < _count; i += 2)
                    {
                        LineTo(stack[i], stack[i + 1]);
                    }

                    _count = 0;
                    break;

                // hlineto, vlineto: lines that alternate between the two directions.
                case 6 or 7:
                    {
                        Require(1);
                        var isHorizontal = b0 == 6;
                        for (var i = 0; i < _count; i++)
                        {
                            LineTo(isHorizontal ? stack[i] : 0, isHorizontal ? 0 : stack[i]);
                            isHorizontal = !isHorizontal;
                        }

                        _count = 0;
                        break;
                    }

                // rrcurveto: {dxa dya dxb dyb dxc dyc}+
                case 8:
                    Require(6);
                    for (var i = 0; i + 5 < _count; i += 6)
                    {
                        CurveTo(stack[i], stack[i + 1], stack[i + 2], stack[i + 3], stack[i + 4], stack[i + 5]);
                    }

                    _count = 0;
                    break;

                // rcurveline: curves, then one line.
                case 24:
                    {
                        Require(8);
                        var i = 0;
                        for (; i + 7 < _count; i += 6)
                        {
                            CurveTo(stack[i], stack[i + 1], stack[i + 2], stack[i + 3], stack[i + 4], stack[i + 5]);
                        }

                        LineTo(stack[i], stack[i + 1]);
                        _count = 0;
                        break;
                    }

                // rlinecurve: lines, then one curve.
                case 25:
                    {
                        Require(8);
                        var i = 0;
                        for (; i + 7 < _count; i += 2)
                        {
                            LineTo(stack[i], stack[i + 1]);
                        }

                        CurveTo(stack[i], stack[i + 1], stack[i + 2], stack[i + 3], stack[i + 4], stack[i + 5]);
                        _count = 0;
                        break;
                    }

                // vvcurveto: dx1? {dya dxb dyb dyc}+. The curves start and end vertical.
                case 26:
                    {
                        Require(4);
                        var i = _count & 1;
                        var dx1 = i == 1 ? stack[0] : 0;
                        for (; i + 3 < _count; i += 4)
                        {
                            CurveTo(dx1, stack[i], stack[i + 1], stack[i + 2], 0, stack[i + 3]);
                            dx1 = 0;
                        }

                        _count = 0;
                        break;
                    }

                // hhcurveto: dy1? {dxa dxb dyb dxc}+. The curves start and end horizontal.
                case 27:
                    {
                        Require(4);
                        var i = _count & 1;
                        var dy1 = i == 1 ? stack[0] : 0;
                        for (; i + 3 < _count; i += 4)
                        {
                            CurveTo(stack[i], dy1, stack[i + 1], stack[i + 2], stack[i + 3], 0);
                            dy1 = 0;
                        }

                        _count = 0;
                        break;
                    }

                // vhcurveto, hvcurveto: curves that start in one direction and end in the other, alternately. The last
                // curve can have a fifth argument: the coordinate that would otherwise be zero.
                case 30 or 31:
                    {
                        Require(4);
                        var isHorizontal = b0 == 31;
                        var i = 0;
                        while (_count - i >= 4)
                        {
                            var last = _count - i == 5 ? stack[i + 4] : 0;
                            if (isHorizontal)
                            {
                                CurveTo(stack[i], 0, stack[i + 1], stack[i + 2], last, stack[i + 3]);
                            }
                            else
                            {
                                CurveTo(0, stack[i], stack[i + 1], stack[i + 2], stack[i + 3], last);
                            }

                            isHorizontal = !isHorizontal;
                            i += 4;
                        }

                        _count = 0;
                        break;
                    }

                // callsubr, callgsubr. The number of a subroutine is biased by the size of its INDEX.
                case 10 or 29:
                    {
                        Require(1);
                        var subroutines = b0 == 10 ? _localSubroutines : _globalSubroutines;
                        var bias = subroutines.Count < 1240 ? 107 : subroutines.Count < 33900 ? 1131 : 32768;
                        var number = stack[--_count] + bias;
                        if (!(number >= 0 && number < subroutines.Count) || number != Math.Floor(number))
                            throw Invalid("calls a subroutine that does not exist");

                        if (depth == MaxCallDepth)
                            throw Invalid("nests more subroutine calls than the format allows");

                        if (Execute(subroutines.Get(_table, (int)number), depth + 1))
                            return true;

                        break;
                    }

                // return
                case 11:
                    return false;

                // endchar. Four arguments make a glyph of two other glyphs, a deprecated form of Type 1.
                case 14:
                    SkipWidth(stack, _count is 1 or 5);
                    if (_count >= 4)
                        throw FontErrors.Unsupported("The font builds an accented glyph with the deprecated 'seac' form of the endchar operator, which is not supported.", "CFF seac");

                    _count = 0;
                    ClosePath();
                    return true;

                case 12:
                    ExecuteEscape(stack, FontBinary.UInt8(code, position++));
                    break;

                // vsindex, of CFF2: the subtable of the variation store the next blends use.
                case 15 when _isCff2:
                    Require(1);
                    _variationStoreIndex = (int)Math.Clamp(stack[_count - 1], -1, ushort.MaxValue + 1);
                    _regionCount = -1;
                    _count = 0;
                    break;

                // blend, of CFF2.
                case 16 when _isCff2:
                    Blend(stack);
                    break;

                default:
                    throw Invalid($"uses the reserved operator {b0}");
            }
        }

        return false;
    }

    /// <summary>Runs a two-byte operator: the flex curves and the arithmetic operators.</summary>
    private void ExecuteEscape(scoped Span<double> stack, int @operator)
    {
        switch (@operator)
        {
            // dotsection, deprecated: nothing to do.
            case 0:
                _count = 0;
                break;

            // hflex: dx1 dx2 dy2 dx3 dx4 dx5 dx6. Two curves that start and end on the same horizontal line.
            case 34:
                Require(7);
                CurveTo(stack[0], 0, stack[1], stack[2], stack[3], 0);
                CurveTo(stack[4], 0, stack[5], -stack[2], stack[6], 0);
                _count = 0;
                break;

            // flex: the six points of two curves, then the flex depth, which only matters to a hinter.
            case 35:
                Require(13);
                CurveTo(stack[0], stack[1], stack[2], stack[3], stack[4], stack[5]);
                CurveTo(stack[6], stack[7], stack[8], stack[9], stack[10], stack[11]);
                _count = 0;
                break;

            // hflex1: dx1 dy1 dx2 dy2 dx3 dx4 dx5 dy5 dx6. The end is on the horizontal line of the start.
            case 36:
                Require(9);
                CurveTo(stack[0], stack[1], stack[2], stack[3], stack[4], 0);
                CurveTo(stack[5], 0, stack[6], stack[7], stack[8], -(stack[1] + stack[3] + stack[7]));
                _count = 0;
                break;

            // flex1: five points, then one coordinate of the last point. The other one is the one of the start: the x
            // when the curve is taller than wide, the y otherwise.
            case 37:
                {
                    Require(11);
                    var dx = stack[0] + stack[2] + stack[4] + stack[6] + stack[8];
                    var dy = stack[1] + stack[3] + stack[5] + stack[7] + stack[9];
                    CurveTo(stack[0], stack[1], stack[2], stack[3], stack[4], stack[5]);
                    if (Math.Abs(dx) > Math.Abs(dy))
                    {
                        CurveTo(stack[6], stack[7], stack[8], stack[9], stack[10], -dy);
                    }
                    else
                    {
                        CurveTo(stack[6], stack[7], stack[8], stack[9], -dx, stack[10]);
                    }

                    _count = 0;
                    break;
                }

            // and, or, not: 0 is false, any other value is true.
            case 3:
                Require(2);
                stack[_count - 2] = stack[_count - 2] != 0 && stack[_count - 1] != 0 ? 1 : 0;
                _count--;
                break;

            case 4:
                Require(2);
                stack[_count - 2] = stack[_count - 2] != 0 || stack[_count - 1] != 0 ? 1 : 0;
                _count--;
                break;

            case 5:
                Require(1);
                stack[_count - 1] = stack[_count - 1] == 0 ? 1 : 0;
                break;

            // abs, add, sub, div, neg, eq, drop
            case 9:
                Require(1);
                stack[_count - 1] = Math.Abs(stack[_count - 1]);
                break;

            case 10:
                Require(2);
                stack[_count - 2] += stack[_count - 1];
                _count--;
                break;

            case 11:
                Require(2);
                stack[_count - 2] -= stack[_count - 1];
                _count--;
                break;

            case 12:
                Require(2);
                if (stack[_count - 1] == 0)
                    throw Invalid("divides by zero");

                stack[_count - 2] /= stack[_count - 1];
                _count--;
                break;

            case 14:
                Require(1);
                stack[_count - 1] = -stack[_count - 1];
                break;

            case 15:
                Require(2);
                stack[_count - 2] = stack[_count - 2] == stack[_count - 1] ? 1 : 0;
                _count--;
                break;

            case 18:
                Require(1);
                _count--;
                break;

            // put, get: the transient array.
            case 20:
                {
                    Require(2);
                    var index = stack[_count - 1];
                    if (!(index >= 0 && index < TransientArraySize) || index != Math.Floor(index))
                        throw Invalid("stores a value outside the transient array");

                    _buffer.Transient[(int)index] = stack[_count - 2];
                    _count -= 2;
                    break;
                }

            case 21:
                {
                    Require(1);
                    var index = stack[_count - 1];
                    if (!(index >= 0 && index < TransientArraySize) || index != Math.Floor(index))
                        throw Invalid("reads a value outside the transient array");

                    stack[_count - 1] = _buffer.Transient[(int)index];
                    break;
                }

            // ifelse: s1 s2 v1 v2 gives s1 when v1 <= v2, s2 otherwise.
            case 22:
                Require(4);
                if (!(stack[_count - 2] <= stack[_count - 1]))
                {
                    stack[_count - 4] = stack[_count - 3];
                }

                _count -= 3;
                break;

            // random: a number greater than 0 and at most 1. A glyph is drawn the same way every time: it is always 1.
            case 23:
                if (_count == StackSize)
                    throw Invalid("pushes more arguments than the stack can hold");

                stack[_count++] = 1;
                break;

            case 24:
                Require(2);
                stack[_count - 2] *= stack[_count - 1];
                _count--;
                break;

            case 26:
                Require(1);
                if (stack[_count - 1] < 0)
                    throw Invalid("takes the square root of a negative number");

                stack[_count - 1] = Math.Sqrt(stack[_count - 1]);
                break;

            // dup, exch
            case 27:
                Require(1);
                if (_count == StackSize)
                    throw Invalid("pushes more arguments than the stack can hold");

                stack[_count] = stack[_count - 1];
                _count++;
                break;

            case 28:
                Require(2);
                (stack[_count - 2], stack[_count - 1]) = (stack[_count - 1], stack[_count - 2]);
                break;

            // index: replaces a position, counted from the top, by the value at that position; a negative position is the top.
            case 29:
                {
                    Require(2);
                    var index = stack[_count - 1];
                    index = index < 0 ? 0 : Math.Floor(index);
                    if (index > _count - 2)
                        throw Invalid("reads an argument below the stack");

                    stack[_count - 1] = stack[_count - 2 - (int)index];
                    break;
                }

            // roll: N J rotates the N values under the two arguments by J positions, toward the top when J is positive.
            case 30:
                {
                    Require(2);
                    var shift = stack[_count - 1];
                    var length = stack[_count - 2];
                    _count -= 2;
                    if (!(length >= 0 && length <= _count) || length != Math.Floor(length) || !(Math.Abs(shift) <= int.MaxValue) || shift != Math.Floor(shift))
                        throw Invalid("rolls more arguments than the stack holds");

                    Roll(stack.Slice(_count - (int)length, (int)length), (int)shift);
                    break;
                }

            default:
                throw Invalid($"uses the reserved operator 12 {@operator}");
        }
    }

    /// <summary>
    /// Replaces n values of the default instance, followed by their deltas for each region and by n, with the n values
    /// of the instance: OpenType specification, "CFF2 CharString Format", "blend".
    /// </summary>
    private void Blend(scoped Span<double> stack)
    {
        if (_regionCount < 0)
        {
            // The scalar of each region the deltas are for: none of them applies to the default instance.
            var blend = _blend;
            _regionCount = 0;
            if (_variationStore != 0 && (uint)_variationStoreIndex <= ushort.MaxValue)
            {
                if (_scalars.IsEmpty)
                {
                    _regionCount = ItemVariationStore.GetRegionCount(_table, _variationStore, _variationStoreIndex);
                    if (_regionCount > blend.Length)
                        throw Invalid("blends more regions than the stack can hold");

                    blend[.._regionCount].Clear();
                }
                else
                {
                    _regionCount = ItemVariationStore.GetRegionScalars(_table, _variationStore, _variationStoreIndex, _scalars, blend);
                }
            }
        }

        Require(1);
        var count = stack[--_count];
        if (!(count >= 0 && count <= StackSize) || count != Math.Floor(count))
            throw Invalid("blends a number of values that is not a count");

        var valueCount = (int)count;
        var first = _count - (valueCount * (_regionCount + 1));
        if (first < 0)
            throw Invalid("has an operator with too few arguments");

        var scalars = _blend[.._regionCount];
        var deltas = stack.Slice(first + valueCount, valueCount * _regionCount);
        for (var i = 0; i < valueCount; i++)
        {
            var value = stack[first + i];
            for (var region = 0; region < scalars.Length; region++)
            {
                value += scalars[region] * deltas[(i * _regionCount) + region];
            }

            stack[first + i] = value;
        }

        _count = first + valueCount;
    }

    private static void Roll(Span<double> values, int shift)
    {
        if (values.Length < 2)
            return;

        // Rotating toward the top by J moves the value at the index i to the index (i + J) modulo N: three reversals.
        shift %= values.Length;
        if (shift < 0)
        {
            shift += values.Length;
        }

        values.Reverse();
        values[..shift].Reverse();
        values[shift..].Reverse();
    }

    /// <summary>
    /// Removes the width that can precede the arguments of the first operator that clears the stack: section 3.1. Its
    /// presence shows in the number of arguments, which is one more than the operator takes.
    /// </summary>
    private void SkipWidth(scoped Span<double> stack, bool hasExtraArgument)
    {
        if (_hasWidth)
            return;

        _hasWidth = true;
        if (hasExtraArgument)
        {
            stack[1.._count].CopyTo(stack);
            _count--;
        }
    }

    private readonly void Require(int count)
    {
        if (_count < count)
            throw Invalid("has an operator with too few arguments");
    }

    private void MoveTo(double dx, double dy)
    {
        ClosePath();
        _x += dx;
        _y += dy;
        _count = 0;
    }

    private void LineTo(double dx, double dy)
    {
        OpenPath();
        _x += dx;
        _y += dy;
        FontErrors.EnsureCoordinates(_x, _y);
        _sink.LineTo(_x, _y);
    }

    private void CurveTo(double dx1, double dy1, double dx2, double dy2, double dx3, double dy3)
    {
        OpenPath();
        var x1 = _x + dx1;
        var y1 = _y + dy1;
        var x2 = x1 + dx2;
        var y2 = y1 + dy2;
        _x = x2 + dx3;
        _y = y2 + dy3;
        FontErrors.EnsureCoordinates(x1, y1);
        FontErrors.EnsureCoordinates(x2, y2);
        FontErrors.EnsureCoordinates(_x, _y);
        _sink.CubicTo(x1, y1, x2, y2, _x, _y);
    }

    private void OpenPath()
    {
        _budget.Charge(1);
        if (_isPathOpen)
            return;

        FontErrors.EnsureCoordinates(_x, _y);
        _isPathOpen = true;
        _startX = _x;
        _startY = _y;
        _sink.MoveTo(_x, _y);
    }

    private void ClosePath()
    {
        if (!_isPathOpen)
            return;

        // The values an instance of a variable font blends are fractions, whose sums are not exact: a contour that
        // comes back to its first point misses it by a rounding error, which is not a line to draw.
        _isPathOpen = false;
        if (Math.Abs(_x - _startX) > ClosingTolerance || Math.Abs(_y - _startY) > ClosingTolerance)
        {
            _sink.LineTo(_startX, _startY);
        }

        _sink.Close();
    }

    private static InvalidImageContentException Invalid(string message) => FontErrors.InvalidTable(FontTags.Cff, $"has a charstring that {message}.");

    /// <summary>The argument stack and the transient array, in the frame of the caller: decoding a glyph allocates nothing.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct Buffer
    {
        private StackValues _stack;
        private TransientValues _transient;

        [UnscopedRef]
        public Span<double> Stack => _stack;

        [UnscopedRef]
        public Span<double> Transient => _transient;
    }

    [InlineArray(StackSize)]
    private struct StackValues
    {
        private double _first;
    }

    [InlineArray(TransientArraySize)]
    private struct TransientValues
    {
        private double _first;
    }

}
