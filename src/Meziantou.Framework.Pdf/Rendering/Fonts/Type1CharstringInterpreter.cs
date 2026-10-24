using Meziantou.Framework.Fonts;

namespace Meziantou.Framework.Pdf.Rendering.Fonts;

/// <summary>
/// Runs the charstrings of a Type 1 font program (Adobe Type 1 Font Format, 6): the path commands, the subroutines,
/// the flex and hint replacement mechanisms of the other subroutines, and <c>seac</c> accented characters. The hints
/// are not used.
/// </summary>
internal sealed class Type1CharstringInterpreter(Type1FontProgram program, OutlineSink sink, RenderCache cache)
{
    private const int MaxStack = 48;
    private const int MaxCallDepth = 10;
    private const int MaxOperations = 100_000;

    private readonly double[] _stack = new double[MaxStack];
    private readonly Stack<double> _postScriptStack = new();
    private readonly List<(double X, double Y)> _flexPoints = [];
    private int _count;
    private double _x;
    private double _y;
    private double _offsetX;
    private double _offsetY;
    private bool _isOpen;
    private bool _inFlex;
    private bool _ended;
    private int _operations;
    private bool _composing;

    /// <summary>Gets the x of the left side bearing set by <c>hsbw</c> or <c>sbw</c>.</summary>
    public double SideBearingX { get; private set; }

    public void Run(byte[] charstring)
    {
        Execute(charstring, depth: 0);
        if (_isOpen)
        {
            sink.Close();
            _isOpen = false;
        }
    }

    private void Execute(byte[] code, int depth)
    {
        if (depth > MaxCallDepth)
            throw new InvalidPdfContentException("The subroutines of a Type 1 charstring are nested too deeply.");

        var position = 0;
        while (position < code.Length && !_ended)
        {
            if (++_operations > MaxOperations)
                throw new InvalidPdfContentException("A Type 1 charstring runs too many operators.");

            int value = code[position++];
            if (value >= 32)
            {
                double number;
                if (value <= 246)
                {
                    number = value - 139;
                }
                else if (value <= 250)
                {
                    number = ((value - 247) * 256) + Byte(code, ref position) + 108;
                }
                else if (value <= 254)
                {
                    number = -((value - 251) * 256) - Byte(code, ref position) - 108;
                }
                else
                {
                    number = (Byte(code, ref position) << 24) | (Byte(code, ref position) << 16) | (Byte(code, ref position) << 8) | Byte(code, ref position);
                }

                Push(number);
                continue;
            }

            switch (value)
            {
                case 1 or 3:
                    // hstem, vstem
                    _count = 0;
                    break;
                case 4:
                    MoveBy(0, Arg(0));
                    break;
                case 5:
                    LineBy(Arg(0), Arg(1));
                    break;
                case 6:
                    LineBy(Arg(0), 0);
                    break;
                case 7:
                    LineBy(0, Arg(0));
                    break;
                case 8:
                    CurveBy(Arg(0), Arg(1), Arg(2), Arg(3), Arg(4), Arg(5));
                    break;
                case 9:
                    if (_isOpen)
                    {
                        sink.Close();
                        _isOpen = false;
                    }

                    _count = 0;
                    break;
                case 10:
                    {
                        var index = (int)Pop();
                        if (index >= 0 && index < program.Subroutines.Count)
                        {
                            Execute(program.Subroutines[index], depth + 1);
                        }

                        break;
                    }

                case 11:
                    return;
                case 12:
                    Escape(Byte(code, ref position));
                    break;
                case 13:
                    // hsbw: the left side bearing and the width
                    SetSideBearing(Arg(0), 0);
                    break;
                case 14:
                    _ended = true;
                    _count = 0;
                    break;
                case 21:
                    MoveBy(Arg(0), Arg(1));
                    break;
                case 22:
                    MoveBy(Arg(0), 0);
                    break;
                case 30:
                    CurveBy(0, Arg(0), Arg(1), Arg(2), Arg(3), 0);
                    break;
                case 31:
                    CurveBy(Arg(0), 0, Arg(1), Arg(2), 0, Arg(3));
                    break;
                default:
                    _count = 0;
                    break;
            }
        }
    }

    private void Escape(int command)
    {
        switch (command)
        {
            case 6:
                Seac(Arg(0), Arg(1), Arg(2), (int)Arg(3), (int)Arg(4));
                break;
            case 7:
                // sbw
                SetSideBearing(Arg(0), Arg(1));
                break;
            case 12:
                {
                    var b = Pop();
                    var a = Pop();
                    Push(b == 0 ? 0 : a / b);
                    return;
                }

            case 16:
                CallOtherSubroutine();
                return;
            case 17:
                // pop: a value the other subroutine left
                Push(_postScriptStack.Count > 0 ? _postScriptStack.Pop() : 0);
                return;
            case 33:
                // setcurrentpoint
                _x = Arg(0);
                _y = Arg(1);
                break;
        }

        _count = 0;
    }

    private void CallOtherSubroutine()
    {
        var number = (int)Pop();
        var argumentCount = Math.Clamp((int)Pop(), 0, _count);
        var arguments = new double[argumentCount];
        for (var i = argumentCount - 1; i >= 0; i--)
        {
            arguments[i] = Pop();
        }

        switch (number)
        {
            case 0 when _inFlex:
                {
                    // The end of a flex: the reference point and the six points of the two curves
                    _inFlex = false;
                    if (_flexPoints.Count >= 7)
                    {
                        var points = _flexPoints;
                        EnsureOpen();
                        sink.CubicTo(points[1].X + _offsetX, points[1].Y + _offsetY, points[2].X + _offsetX, points[2].Y + _offsetY, points[3].X + _offsetX, points[3].Y + _offsetY);
                        sink.CubicTo(points[4].X + _offsetX, points[4].Y + _offsetY, points[5].X + _offsetX, points[5].Y + _offsetY, points[6].X + _offsetX, points[6].Y + _offsetY);
                        (_x, _y) = points[6];
                    }

                    // pop pop setcurrentpoint follows: the end point
                    _postScriptStack.Push(_y);
                    _postScriptStack.Push(_x);
                    break;
                }

            case 1:
                _inFlex = true;
                _flexPoints.Clear();
                break;
            case 2:
                break;
            case 3:
                // Hint replacement: the subroutine number is returned, then called (it holds hints only)
                _postScriptStack.Push(3);
                break;
            default:
                for (var i = arguments.Length - 1; i >= 0; i--)
                {
                    _postScriptStack.Push(arguments[i]);
                }

                break;
        }
    }

    /// <summary>An accented character (Type 1 Font Format, appendix 3): the base character, then the accent moved by (adx - asb, ady).</summary>
    private void Seac(double asb, double adx, double ady, int baseCode, int accentCode)
    {
        if (_composing)
            return;

        var baseName = baseCode is >= 0 and < 256 ? Pdf.Fonts.FontEncodings.Standard[baseCode] : null;
        var accentName = accentCode is >= 0 and < 256 ? Pdf.Fonts.FontEncodings.Standard[accentCode] : null;
        _composing = true;
        if (baseName is not null && program.GetCharstring(baseName) is { } baseCharstring)
        {
            RunComponent(baseCharstring, 0, 0);
        }

        if (accentName is not null && program.GetCharstring(accentName) is { } accentCharstring)
        {
            RunComponent(accentCharstring, adx - asb, ady);
        }

        _ended = true;
    }

    private void RunComponent(byte[] charstring, double offsetX, double offsetY)
    {
        var interpreter = new Type1CharstringInterpreter(program, sink, cache)
        {
            _offsetX = offsetX,
            _offsetY = offsetY,
            _composing = true,
        };
        interpreter.Run(charstring);
    }

    private void SetSideBearing(double x, double y)
    {
        SideBearingX = x;
        _x = x;
        _y = y;
        _count = 0;
    }

    private void MoveBy(double dx, double dy)
    {
        _x += dx;
        _y += dy;
        _count = 0;
        if (_inFlex)
        {
            _flexPoints.Add((_x, _y));
            return;
        }

        if (_isOpen)
        {
            sink.Close();
        }

        sink.MoveTo(_x + _offsetX, _y + _offsetY);
        _isOpen = true;
    }

    private void LineBy(double dx, double dy)
    {
        EnsureOpen();
        _x += dx;
        _y += dy;
        sink.LineTo(_x + _offsetX, _y + _offsetY);
        _count = 0;
    }

    private void CurveBy(double dx1, double dy1, double dx2, double dy2, double dx3, double dy3)
    {
        EnsureOpen();
        var x1 = _x + dx1;
        var y1 = _y + dy1;
        var x2 = x1 + dx2;
        var y2 = y1 + dy2;
        _x = x2 + dx3;
        _y = y2 + dy3;
        sink.CubicTo(x1 + _offsetX, y1 + _offsetY, x2 + _offsetX, y2 + _offsetY, _x + _offsetX, _y + _offsetY);
        _count = 0;
    }

    private void EnsureOpen()
    {
        if (!_isOpen)
        {
            sink.MoveTo(_x + _offsetX, _y + _offsetY);
            _isOpen = true;
        }
    }

    private void Push(double value)
    {
        if (_count < MaxStack)
        {
            _stack[_count++] = value;
        }
    }

    private double Pop() => _count > 0 ? _stack[--_count] : 0;

    /// <summary>Gets an argument from the bottom of the stack: the commands take their arguments in order.</summary>
    private double Arg(int index) => index < _count ? _stack[index] : 0;

    private static int Byte(byte[] code, ref int position) => position < code.Length ? code[position++] : 0;
}
