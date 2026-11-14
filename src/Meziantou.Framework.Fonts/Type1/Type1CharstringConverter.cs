namespace Meziantou.Framework.Fonts;

/// <summary>
/// Runs the charstrings of a Type 1 font (Adobe Type 1 Font Format, chapter 6, "CharStrings Dictionary") and writes
/// the outline as a Type 2 charstring (Adobe Technical Note #5177) of moves, lines and curves: the subroutines are
/// expanded, the flex mechanism becomes its two curves and a <c>seac</c> glyph the outlines of its base character and
/// of its accent (chapter 8, "Using Subroutines", and appendix 3). The hints are dropped: stem hints, hint replacement
/// (which calls the subroutine 3, which does nothing) and counter control.
/// </summary>
/// <remarks>
/// The coordinates are transformed by the font matrix scaled to the units per em of the font, and written in the
/// fixed-point format of Type 2 charstrings (16.16), relative to the previous point: the points of the outline are
/// the transformed points rounded to 1/65536 of a unit. Every operator and every number is charged to a
/// <see cref="GlyphBudget"/>.
/// </remarks>
internal sealed class Type1CharstringConverter
{
    // The operand stack of Type 1 BuildChar holds 24 numbers (appendix 1); the stack of the values returned by the
    // other subroutines holds what they leave; subroutines nest at most 10 deep (callsubr).
    private const int MaxOperands = 24;
    private const int MaxCallDepth = 10;

    private readonly Type1Font _font;
    private readonly Dictionary<string, int> _glyphsByName;
    private readonly double[] _matrix;

    private readonly double[] _stack = new double[MaxOperands];
    private readonly double[] _otherSubroutineResults = new double[MaxOperands];
    private readonly (double X, double Y)[] _flexPoints = new (double X, double Y)[7];
    private Type2CharstringWriter _writer = null!;
    private int _count;
    private int _resultCount;
    private int _flexCount;
    private bool _inFlex;
    private bool _ended;
    private bool _hasWidth;
    private bool _isComponent;
    private double _x;
    private double _y;
    private double _offsetX;
    private double _offsetY;
    private double _sideBearingX;

    /// <param name="font">The font.</param>
    /// <param name="glyphsByName">The index of each glyph name in <see cref="Type1Font.CharStrings"/>.</param>
    /// <param name="matrix">The transform from the units of the charstrings to the units of the converted font.</param>
    public Type1CharstringConverter(Type1Font font, Dictionary<string, int> glyphsByName, double[] matrix)
    {
        _font = font;
        _glyphsByName = glyphsByName;
        _matrix = matrix;
    }

    /// <summary>Gets the number of operators and numbers the last conversion ran.</summary>
    public long Operations { get; private set; }

    /// <summary>Gets the advance width of the last converted glyph (from <c>hsbw</c> or <c>sbw</c>), in the units of the converted font, rounded to a whole number.</summary>
    public double AdvanceWidth { get; private set; }

    /// <summary>Converts the charstring of a glyph.</summary>
    /// <param name="charstring">The decrypted charstring.</param>
    /// <param name="writer">Receives the outline, as a Type 2 charstring.</param>
    /// <param name="budget">The budget of the glyph.</param>
    public void Convert(ReadOnlySpan<byte> charstring, Type2CharstringWriter writer, ref GlyphBudget budget)
    {
        _writer = writer;
        writer.Begin();
        AdvanceWidth = 0;
        Operations = 0;
        _sideBearingX = 0;
        _hasWidth = false;
        _isComponent = false;
        Run(charstring, offsetX: 0, offsetY: 0, ref budget);
        writer.Finish();
    }

    private void Run(ReadOnlySpan<byte> charstring, double offsetX, double offsetY, ref GlyphBudget budget)
    {
        _count = 0;
        _resultCount = 0;
        _flexCount = 0;
        _inFlex = false;
        _ended = false;
        _x = 0;
        _y = 0;
        _offsetX = offsetX;
        _offsetY = offsetY;
        Execute(charstring, depth: 0, ref budget);
    }

    private void Execute(ReadOnlySpan<byte> code, int depth, ref GlyphBudget budget)
    {
        var position = 0;
        while (position < code.Length && !_ended)
        {
            budget.Charge(1);
            Operations++;
            int value = code[position++];

            // Numbers: section 6.2, "Charstring Number Encoding"
            if (value >= 32)
            {
                Push(value switch
                {
                    <= 246 => value - 139,
                    <= 250 => ((value - 247) * 256) + ReadByte(code, ref position) + 108,
                    <= 254 => -((value - 251) * 256) - ReadByte(code, ref position) - 108,
                    _ => (ReadByte(code, ref position) << 24) | (ReadByte(code, ref position) << 16) | (ReadByte(code, ref position) << 8) | ReadByte(code, ref position),
                });
                continue;
            }

            switch (value)
            {
                case 1 or 3:
                    // hstem, vstem: hints
                    Clear();
                    break;

                case 4:
                    // vmoveto
                    MoveBy(0, Argument(0, 1));
                    break;

                case 5:
                    // rlineto
                    LineBy(Argument(0, 2), Argument(1, 2));
                    break;

                case 6:
                    // hlineto
                    LineBy(Argument(0, 1), 0);
                    break;

                case 7:
                    // vlineto
                    LineBy(0, Argument(0, 1));
                    break;

                case 8:
                    // rrcurveto
                    CurveBy(Argument(0, 6), Argument(1, 6), Argument(2, 6), Argument(3, 6), Argument(4, 6), Argument(5, 6));
                    break;

                case 9:
                    // closepath: the current point does not move
                    _writer.ClosePath();
                    Clear();
                    break;

                case 10:
                {
                    // callsubr
                    var index = Pop();
                    if (depth >= MaxCallDepth)
                        throw FontErrors.InvalidType1("the subroutines of a charstring are nested more than 10 deep.");

                    if (index != Math.Floor(index) || index is < 0 or > int.MaxValue || !_font.TryGetSubroutine((int)index, out var subroutine))
                        throw FontErrors.InvalidType1($"a charstring calls the subroutine {index}, which the font does not have.");

                    Execute(subroutine, depth + 1, ref budget);
                    break;
                }

                case 11:
                    // return
                    if (depth > 0)
                        return;

                    break;

                case 12:
                    Escape(ReadByte(code, ref position), ref budget);
                    break;

                case 13:
                    // hsbw: the left side bearing point and the width
                    SetSideBearing(Argument(0, 2), 0, Argument(1, 2));
                    break;

                case 14:
                    // endchar
                    _ended = true;
                    break;

                case 21:
                    // rmoveto
                    MoveBy(Argument(0, 2), Argument(1, 2));
                    break;

                case 22:
                    // hmoveto
                    MoveBy(Argument(0, 1), 0);
                    break;

                case 30:
                    // vhcurveto
                    CurveBy(0, Argument(0, 4), Argument(1, 4), Argument(2, 4), Argument(3, 4), 0);
                    break;

                case 31:
                    // hvcurveto
                    CurveBy(Argument(0, 4), 0, Argument(1, 4), Argument(2, 4), 0, Argument(3, 4));
                    break;

                default:
                    throw FontErrors.InvalidType1($"a charstring has the unknown operator {value}.");
            }
        }
    }

    private void Escape(int command, ref GlyphBudget budget)
    {
        switch (command)
        {
            case 0 or 1 or 2:
                // dotsection, vstem3, hstem3: hints
                Clear();
                break;

            case 6:
                Seac(Argument(0, 5), Argument(1, 5), Argument(2, 5), Argument(3, 5), Argument(4, 5), ref budget);
                break;

            case 7:
                // sbw: the side bearing point and the width vector
                SetSideBearing(Argument(0, 4), Argument(1, 4), Argument(2, 4));
                break;

            case 12:
            {
                // div
                var divisor = Pop();
                var dividend = Pop();
                if (divisor == 0)
                    throw FontErrors.InvalidType1("a charstring divides by zero.");

                Push(dividend / divisor);
                break;
            }

            case 16:
                CallOtherSubroutine();
                break;

            case 17:
                // pop: a value an other subroutine left on the stack of the PostScript interpreter
                if (_resultCount == 0)
                    throw FontErrors.InvalidType1("a charstring pops a value that no other subroutine returned.");

                Push(_otherSubroutineResults[--_resultCount]);
                break;

            case 33:
                // setcurrentpoint: the end of a flex
                _x = Argument(0, 2);
                _y = Argument(1, 2);
                Clear();
                break;

            default:
                throw FontErrors.InvalidType1($"a charstring has the unknown operator 12 {command}.");
        }
    }

    /// <summary>
    /// Calls an entry of OtherSubrs (chapter 8): the flex mechanism (0, 1 and 2) and hint replacement (3) have the
    /// semantics the specification gives; counter control (12 and 13) returns nothing; the entries of multiple master
    /// fonts (14 to 18) are not supported; any other entry returns its arguments, the first one on top.
    /// </summary>
    private void CallOtherSubroutine()
    {
        var number = Pop();
        var argumentCount = Pop();
        if (argumentCount < 0 || argumentCount > _count || argumentCount != Math.Floor(argumentCount))
            throw FontErrors.InvalidType1("a charstring calls an other subroutine with more arguments than it has.");

        var count = (int)argumentCount;
        _count -= count;
        var arguments = _stack.AsSpan(_count, count);
        _resultCount = 0;
        switch (number)
        {
            case 0:
            {
                // The end of a flex (8.3): the reference point, then the points of two curves; the end point is the
                // new current point, given by setcurrentpoint after two pops
                if (!_inFlex || _flexCount != 7 || count != 3)
                    throw FontErrors.InvalidType1("a flex does not have its seven points.");

                _inFlex = false;
                var points = _flexPoints;
                CurveTo(points[1], points[2], points[3]);
                CurveTo(points[4], points[5], points[6]);
                _otherSubroutineResults[0] = arguments[2];
                _otherSubroutineResults[1] = arguments[1];
                _resultCount = 2;
                break;
            }

            case 1:
                // The start of a flex: the moves that follow are its points
                _inFlex = true;
                _flexCount = 0;
                break;

            case 2:
                // A point of a flex: the move before it recorded it
                if (!_inFlex)
                    throw FontErrors.InvalidType1("a charstring adds a point to a flex outside a flex.");

                break;

            case 3:
                // Hint replacement (8.1): the subroutine 3, which does nothing, is called in place of the hints
                _otherSubroutineResults[0] = 3;
                _resultCount = 1;
                break;

            case 12 or 13:
                // Counter control: hints
                break;

            case >= 14 and <= 18:
                throw FontErrors.Unsupported("The Type 1 font is a multiple master font, which is not supported.", "Multiple master fonts");

            default:
                for (var i = 0; i < count; i++)
                {
                    _otherSubroutineResults[count - 1 - i] = arguments[i];
                }

                _resultCount = count;
                break;
        }
    }

    /// <summary>
    /// An accented character (6.4 and appendix 3): the base character and the accent, two characters of the font given
    /// by their codes in the standard encoding. The base character is drawn where its own charstring puts it, and the
    /// accent is moved by (<c>adx</c> - <c>asb</c> + <c>sbx</c>, <c>ady</c>), where <c>sbx</c> is the side bearing of the
    /// accented character: its left side bearing point is at <c>adx</c> from the one of the accented character. The
    /// specification requires the side bearings of the accented character and of the base character to be the same;
    /// with a font that does not follow it, this is where FreeType draws the accent.
    /// </summary>
    private void Seac(double accentSideBearing, double accentX, double accentY, double baseCode, double accentCode, ref GlyphBudget budget)
    {
        if (_isComponent)
            throw FontErrors.InvalidType1("a component of an accented character is an accented character.");

        var baseCharstring = GetStandardEncodingCharstring(baseCode);
        var accentCharstring = GetStandardEncodingCharstring(accentCode);
        _isComponent = true;
        Run(baseCharstring, 0, 0, ref budget);
        _writer.ClosePath();
        Run(accentCharstring, accentX - accentSideBearing + _sideBearingX, accentY, ref budget);
        _isComponent = false;
        _ended = true;
    }

    private ReadOnlySpan<byte> GetStandardEncodingCharstring(double code)
    {
        var standard = CffFont.StandardEncoding.Value;
        if (code is >= 0 and < 256 && code == Math.Floor(code) && standard[(int)code] is { } name && _glyphsByName.TryGetValue(name, out var glyph))
            return _font.GetCharstring(glyph);

        throw FontErrors.InvalidType1($"an accented character has the component {code}, which is not a glyph of the font in the standard encoding.");
    }

    private void SetSideBearing(double x, double y, double width)
    {
        // The width of an accented character is the one of its own charstring, not of its components
        if (!_isComponent && !_hasWidth)
        {
            // The advance widths of an OpenType font are whole numbers of units. The width is the first operand of
            // the Type 2 charstring: the outline cannot have started.
            AdvanceWidth = Math.Round(width * _matrix[0], MidpointRounding.AwayFromZero);
            if (!(Math.Abs(AdvanceWidth) <= ushort.MaxValue))
                throw FontErrors.InvalidType1("a glyph has an advance width larger than an OpenType font can have.");

            if (_writer.HasStarted)
                throw FontErrors.InvalidType1("the side bearing and the width of a glyph (hsbw or sbw) come after its outline starts.");

            _writer.Width = AdvanceWidth;
            _sideBearingX = x;
            _hasWidth = true;
        }

        _x = x;
        _y = y;
        Clear();

        // A line or a curve that follows without a move starts at the side bearing point
        _writer.MoveTo(TransformX(_x, _y), TransformY(_x, _y));
    }

    private void MoveBy(double dx, double dy)
    {
        _x += dx;
        _y += dy;
        Clear();
        if (_inFlex)
        {
            if (_flexCount == _flexPoints.Length)
                throw FontErrors.InvalidType1("a flex has more than seven points.");

            _flexPoints[_flexCount++] = (_x, _y);
            return;
        }

        _writer.MoveTo(TransformX(_x, _y), TransformY(_x, _y));
    }

    private void LineBy(double dx, double dy)
    {
        EnsureNotInFlex();
        _x += dx;
        _y += dy;
        Clear();
        _writer.LineTo(TransformX(_x, _y), TransformY(_x, _y));
    }

    private void CurveBy(double dx1, double dy1, double dx2, double dy2, double dx3, double dy3)
    {
        EnsureNotInFlex();
        var x1 = _x + dx1;
        var y1 = _y + dy1;
        var x2 = x1 + dx2;
        var y2 = y1 + dy2;
        _x = x2 + dx3;
        _y = y2 + dy3;
        Clear();
        CurveTo((x1, y1), (x2, y2), (_x, _y));
    }

    private void CurveTo((double X, double Y) control1, (double X, double Y) control2, (double X, double Y) end)
    {
        _writer.CurveTo(
            TransformX(control1.X, control1.Y), TransformY(control1.X, control1.Y),
            TransformX(control2.X, control2.Y), TransformY(control2.X, control2.Y),
            TransformX(end.X, end.Y), TransformY(end.X, end.Y));
    }

    private void EnsureNotInFlex()
    {
        if (_inFlex)
            throw FontErrors.InvalidType1("a flex has a line or a curve between its points.");
    }

    private double TransformX(double x, double y)
    {
        x += _offsetX;
        y += _offsetY;
        return (_matrix[0] * x) + (_matrix[2] * y) + _matrix[4];
    }

    private double TransformY(double x, double y)
    {
        x += _offsetX;
        y += _offsetY;
        return (_matrix[1] * x) + (_matrix[3] * y) + _matrix[5];
    }

    private void Push(double value)
    {
        if (_count == MaxOperands)
            throw FontErrors.InvalidType1("a charstring has more than 24 operands on its stack.");

        _stack[_count++] = value;
    }

    private double Pop()
    {
        if (_count == 0)
            throw FontErrors.InvalidType1("a charstring operator has fewer operands than it needs.");

        return _stack[--_count];
    }

    /// <summary>Gets an operand of an operator that clears the stack: the operators take their operands from the bottom of the stack.</summary>
    private double Argument(int index, int required)
    {
        if (_count < required)
            throw FontErrors.InvalidType1("a charstring operator has fewer operands than it needs.");

        return _stack[index];
    }

    private void Clear() => _count = 0;

    private static int ReadByte(ReadOnlySpan<byte> code, ref int position)
    {
        if (position >= code.Length)
            throw FontErrors.InvalidType1("a charstring ends in the middle of a number or an operator.");

        return code[position++];
    }
}
