using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The calculator element: a function written as a sequence of operations on a stack of values, with temporary channels,
/// conditional streams of operations and sub-elements it can apply (ICC.2:2023 section 11.2.1).
/// </summary>
/// <remarks>
/// <para>
/// The function is checked once, when the profile is read (section 11.2.1.1): every operation is known, every channel it
/// addresses exists, and the stack never underflows. The streams of an 'if' and its 'else', and those of a 'sel', must
/// leave the same number of values (sections 11.2.1.10 and 11.2.1.11). The library also requires the number of values
/// after each operation not to depend on the colors: the stream of an 'if' without 'else', and the streams of a 'sel'
/// without 'dflt', which may not run, must leave the stack as they found it; a function where they do not is valid but
/// not supported.
/// </para>
/// <para>
/// The operands of a calculator are float32Number values: each result is rounded to single precision, as an
/// implementation of IEEE 754 single-precision arithmetic would compute it. The color appearance operations
/// (<c>tJab</c>, <c>fJab</c>) are not supported, and the CMM environment variables are those the specification requires:
/// <c>true</c> and <c>ndef</c>; the others are reported as not available.
/// </para>
/// </remarks>
internal sealed class IccMpeCalculator : IccMpeElement
{
    /// <summary>The largest nesting of conditional streams of operations: they are evaluated recursively.</summary>
    private const int MaxNesting = 64;

    /// <summary>The largest system of linear equations of the 'solv' operation.</summary>
    private const int MaxSolveSize = 64;

    private readonly Instruction[] _program;
    private readonly int[] _caseBodies;
    private readonly IccMpeElement[] _subElements;
    private readonly int _stackSize;
    private readonly int _tempCount;
    private readonly int _subInputs;
    private readonly int _subOutputs;
    private readonly int _subScratch;
    private readonly int _transposeSize;
    private readonly long _cost;

    private IccMpeCalculator(int inputs, int outputs, Instruction[] program, int[] caseBodies, IccMpeElement[] subElements, int stackSize, int tempCount, long cost, int transposeSize)
        : base(inputs, outputs)
    {
        _program = program;
        _caseBodies = caseBodies;
        _subElements = subElements;
        _stackSize = stackSize;
        _tempCount = tempCount;
        _cost = cost;
        _transposeSize = transposeSize;
        foreach (var element in subElements)
        {
            _subInputs = Math.Max(_subInputs, element.InputCount);
            _subOutputs = Math.Max(_subOutputs, element.OutputCount);
            _subScratch = Math.Max(_subScratch, element.ScratchSize);
        }
    }

    private enum OpCode : byte
    {
        Data, In, Out, TGet, TPut, TSav, Env, SubElement, Copy, Rotl, Rotr, Posd, Flip, Pop, Solv, Tran,
        Sum, Prod, Min, Max, And, Or, Noop, Scnt, Pi, PositiveInfinity, NegativeInfinity, NaN,
        Add, Sub, Mul, Div, Mod, Pow, Atn2, Lt, Le, Eq, Near, Ge, Gt, VMin, VMax, VAnd, VOr, VXor, Ctop, Ptoc,
        Gama, SAdd, SSub, SMul, SDiv,
        Sq, Sqrt, Cb, Cbrt, Abs, Neg, Rond, Flor, Ceil, Trnc, Sign, Exp, Log, Ln, Not, Sin, Cos, Tan, Asin, Acos, Atan, RNum,
        TLab, TXyz, If, Sel, Placeholder,
    }

    public override int ScratchSize => _stackSize + _tempCount + _subInputs + _subOutputs + _subScratch + _transposeSize;

    public override long Cost => _cost;

    /// <summary>Reads and checks a calculator element (ICC.2:2023 Table 85).</summary>
    public static IccMpeCalculator Parse(ReadOnlyMemory<byte> element, int inputs, int outputs, IccMpeParseContext context)
    {
        if (context.Depth >= IccMpeParseContext.MaxDepth)
            throw context.Unsupported(string.Create(CultureInfo.InvariantCulture, $"the calculator elements are nested more than {IccMpeParseContext.MaxDepth} times."), "ICC processing elements too deeply nested");

        var span = element.Span;
        if (span.Length < 24)
            throw context.Invalid("a calculator element is shorter than its header.");

        int subCount = IccReader.ReadUInt16(span[14..]);
        if (24 + (8L * subCount) > span.Length)
            throw context.Invalid("the sub-element positions of a calculator element do not fit in it.");

        // The sub-elements are charged before they are parsed (the calculator itself is charged by IccMpeParser.ParseElement):
        // sub-elements at overlapping positions of different sizes are distinct elements, so that nested calculators that
        // refer to them many times would otherwise parse and store the same data again and again, even when their functions
        // are empty
        context.Allocate(subCount);

        // Sub-elements with the same position are the same element
        var subElements = new IccMpeElement[subCount];
        var cache = new Dictionary<(uint Offset, uint Size), IccMpeElement>();
        context.Depth++;
        try
        {
            for (var i = 0; i < subCount; i++)
            {
                var key = (IccReader.ReadUInt32(span[(24 + (8 * i))..]), IccReader.ReadUInt32(span[(28 + (8 * i))..]));
                if (!cache.TryGetValue(key, out var subElement))
                {
                    subElement = IccMpeParser.ParseElement(IccMpeParser.GetPosition(element, 24 + (8 * i), context), context);
                    cache[key] = subElement;
                }

                subElements[i] = subElement;
            }
        }
        finally
        {
            context.Depth--;
        }

        // The optional limits (Table 85b): 0 is no limit
        uint maxStack = 0, maxTemps = 0, maxOperations = 0;
        var limits = 24 + (8 * subCount);
        if (span.Length >= limits + 28 && IccReader.ReadUInt32(span[limits..]) == 0x636C6D74) // 'clmt'
        {
            maxStack = IccReader.ReadUInt32(span[(limits + 8)..]);
            maxTemps = IccReader.ReadUInt32(span[(limits + 12)..]);
            maxOperations = IccReader.ReadUInt32(span[(limits + 16)..]);
        }

        var function = IccMpeParser.GetPosition(element, 16, context);
        if (function.Length < 12 || IccReader.ReadUInt32(function.Span) != 0x66756E63) // 'func'
            throw context.Invalid("the main function of a calculator element is not a function ('func').");

        var operationCount = IccReader.ReadUInt32(function.Span[8..]);
        if (12 + (8L * operationCount) > function.Length)
            throw context.Invalid(string.Create(CultureInfo.InvariantCulture, $"the main function of a calculator element declares {operationCount} operations, which do not fit in it."));

        // The values left on the stack at the end of the main function are not used
        var compiler = new Compiler(function.Slice(12, (int)(8 * operationCount)), inputs, outputs, subElements, context);
        _ = compiler.CompileRange(0, (int)operationCount, depth: 0, nesting: 0, out var cost);
        if ((maxStack != 0 && compiler.StackSize > maxStack) || (maxTemps != 0 && compiler.TempCount > maxTemps) || (maxOperations != 0 && cost > maxOperations))
            throw context.Invalid("the main function of a calculator element exceeds the limits it declares ('clmt').");

        if (compiler.StackSize > IccMpeParseContext.MaxChannels || compiler.TempCount > IccMpeParseContext.MaxChannels)
            throw context.Invalid("the main function of a calculator element uses more than 65,535 stack values or temporary channels.");

        // The operations copy one value at a time, and each evaluation clears the temporary channels and the outputs
        cost += compiler.TempCount + outputs;
        return new IccMpeCalculator(inputs, outputs, compiler.Program, [.. compiler.CaseBodies], subElements, compiler.StackSize, compiler.TempCount, cost, compiler.TransposeSize);
    }

    public override void Apply(ReadOnlySpan<double> input, Span<double> output, Span<double> scratch)
    {
        var machine = new Machine
        {
            Stack = scratch[.._stackSize],
            Temps = scratch.Slice(_stackSize, _tempCount),
            SubInput = scratch.Slice(_stackSize + _tempCount, _subInputs),
            SubOutput = scratch.Slice(_stackSize + _tempCount + _subInputs, _subOutputs),
            SubScratch = scratch.Slice(_stackSize + _tempCount + _subInputs + _subOutputs, _subScratch),
            Transpose = scratch.Slice(_stackSize + _tempCount + _subInputs + _subOutputs + _subScratch, _transposeSize),
            Input = input,
            Output = output,
        };

        // Temporary channels and outputs are zero until set (section 11.2.1.1)
        machine.Temps.Clear();
        output.Clear();
        Run(ref machine, 0, _program.Length);
    }

    private static double R(double value) => (float)value;

    private void Run(ref Machine m, int start, int end)
    {
        var stack = m.Stack;
        var i = start;
        while (i < end)
        {
            ref readonly var op = ref _program[i];
            var n = op.S + 1;
            switch (op.Code)
            {
                case OpCode.Data:
                    stack[m.Sp++] = op.Value;
                    break;

                case OpCode.In:
                    for (var k = 0; k <= op.T; k++)
                    {
                        stack[m.Sp++] = R(m.Input[op.S + k]);
                    }

                    break;

                case OpCode.Out:
                    m.Sp -= op.T + 1;
                    for (var k = 0; k <= op.T; k++)
                    {
                        m.Output[op.S + k] = stack[m.Sp + k];
                    }

                    break;

                case OpCode.TGet:
                    for (var k = 0; k <= op.T; k++)
                    {
                        stack[m.Sp++] = m.Temps[op.S + k];
                    }

                    break;

                case OpCode.TPut:
                    m.Sp -= op.T + 1;
                    stack.Slice(m.Sp, op.T + 1).CopyTo(m.Temps[op.S..]);
                    break;

                case OpCode.TSav:
                    stack.Slice(m.Sp - op.T - 1, op.T + 1).CopyTo(m.Temps[op.S..]);
                    break;

                case OpCode.Env:
                    // 'true' is available with the value 1; every other variable ('ndef' included) is not available
                    var available = op.Value == 1;
                    stack[m.Sp++] = available ? 1 : 0;
                    stack[m.Sp++] = available ? 1 : 0;
                    break;

                case OpCode.SubElement:
                {
                    var element = _subElements[op.S];
                    m.Sp -= element.InputCount;
                    stack.Slice(m.Sp, element.InputCount).CopyTo(m.SubInput);
                    element.Apply(m.SubInput[..element.InputCount], m.SubOutput[..element.OutputCount], m.SubScratch);
                    for (var k = 0; k < element.OutputCount; k++)
                    {
                        stack[m.Sp++] = R(m.SubOutput[k]);
                    }

                    break;
                }

                case OpCode.Copy:
                    for (var copy = 0; copy <= op.T; copy++)
                    {
                        stack.Slice(m.Sp - n, n).CopyTo(stack[m.Sp..]);
                        m.Sp += n;
                    }

                    break;

                case OpCode.Rotl:
                {
                    // The bottom value of the top S + 1 values moves to the top, T + 1 times
                    var group = stack.Slice(m.Sp - n, n);
                    var shift = (op.T + 1) % n;
                    Rotate(group, shift);
                    break;
                }

                case OpCode.Rotr:
                {
                    var group = stack.Slice(m.Sp - n, n);
                    var shift = (op.T + 1) % n;
                    Rotate(group, (n - shift) % n);
                    break;
                }

                case OpCode.Posd:
                {
                    var value = stack[m.Sp - 1 - op.S];
                    for (var k = 0; k <= op.T; k++)
                    {
                        stack[m.Sp++] = value;
                    }

                    break;
                }

                case OpCode.Flip:
                    stack.Slice(m.Sp - op.S - 2, op.S + 2).Reverse();
                    break;

                case OpCode.Pop:
                    m.Sp -= n;
                    break;

                case OpCode.Solv:
                    Solve(stack, ref m.Sp, op.S + 1, op.T + 1);
                    break;

                case OpCode.Tran:
                {
                    var rows = op.S + 1;
                    var columns = op.T + 1;
                    var matrix = stack.Slice(m.Sp - (rows * columns), rows * columns);
                    var copy = m.Transpose[..matrix.Length];
                    matrix.CopyTo(copy);
                    for (var row = 0; row < rows; row++)
                    {
                        for (var column = 0; column < columns; column++)
                        {
                            matrix[(column * rows) + row] = copy[(row * columns) + column];
                        }
                    }

                    break;
                }

                case OpCode.Sum or OpCode.Prod or OpCode.Min or OpCode.Max or OpCode.And or OpCode.Or:
                {
                    var count = op.S + 2;
                    var values = stack.Slice(m.Sp - count, count);
                    var result = values[0];
                    var all = values[0] >= 0.5;
                    var any = all;
                    for (var k = 1; k < count; k++)
                    {
                        var value = values[k];
                        result = op.Code switch
                        {
                            OpCode.Sum => R(result + value),
                            OpCode.Prod => R(result * value),
                            OpCode.Min => Math.Min(result, value),
                            OpCode.Max => Math.Max(result, value),
                            _ => result,
                        };
                        all &= value >= 0.5;
                        any |= value >= 0.5;
                    }

                    m.Sp -= count;
                    stack[m.Sp++] = op.Code switch
                    {
                        OpCode.And => all ? 1 : 0,
                        OpCode.Or => any ? 1 : 0,
                        _ => result,
                    };
                    break;
                }

                case OpCode.Noop:
                    break;

                case OpCode.Scnt:
                    stack[m.Sp] = m.Sp;
                    m.Sp++;
                    break;

                case OpCode.Pi:
                    stack[m.Sp++] = R(Math.PI);
                    break;

                case OpCode.PositiveInfinity:
                    stack[m.Sp++] = double.PositiveInfinity;
                    break;

                case OpCode.NegativeInfinity:
                    stack[m.Sp++] = double.NegativeInfinity;
                    break;

                case OpCode.NaN:
                    stack[m.Sp++] = double.NaN;
                    break;

                case >= OpCode.Add and <= OpCode.Ptoc:
                {
                    var x = stack.Slice(m.Sp - (2 * n), n);
                    var y = stack.Slice(m.Sp - n, n);
                    for (var k = 0; k < n; k++)
                    {
                        if (op.Code is OpCode.Ctop or OpCode.Ptoc)
                        {
                            (x[k], y[k]) = op.Code == OpCode.Ctop ? ToPolar(x[k], y[k]) : FromPolar(x[k], y[k]);
                            continue;
                        }

                        x[k] = Binary(op.Code, x[k], y[k]);
                    }

                    if (op.Code is not (OpCode.Ctop or OpCode.Ptoc))
                    {
                        m.Sp -= n;
                    }

                    break;
                }

                case >= OpCode.Gama and <= OpCode.SDiv:
                {
                    var scalar = stack[m.Sp - 1];
                    var x = stack.Slice(m.Sp - 1 - n, n);
                    for (var k = 0; k < n; k++)
                    {
                        x[k] = R(op.Code switch
                        {
                            OpCode.Gama => Math.Pow(x[k], scalar),
                            OpCode.SAdd => x[k] + scalar,
                            OpCode.SSub => x[k] - scalar,
                            OpCode.SMul => x[k] * scalar,
                            _ => x[k] / scalar,
                        });
                    }

                    m.Sp--;
                    break;
                }

                case >= OpCode.Sq and <= OpCode.RNum:
                {
                    var x = stack.Slice(m.Sp - n, n);
                    for (var k = 0; k < n; k++)
                    {
                        x[k] = Unary(op.Code, x[k]);
                    }

                    break;
                }

                case OpCode.TLab or OpCode.TXyz:
                {
                    var x = stack.Slice(m.Sp - (3 * n), n);
                    var y = stack.Slice(m.Sp - (2 * n), n);
                    var z = stack.Slice(m.Sp - n, n);
                    for (var k = 0; k < n; k++)
                    {
                        (x[k], y[k], z[k]) = op.Code == OpCode.TLab ? ToLab(x[k], y[k], z[k]) : ToXyz(x[k], y[k], z[k]);
                    }

                    break;
                }

                case OpCode.If:
                {
                    // The stream of the 'if' runs when the value is at least 0.5 or not a number
                    var condition = stack[--m.Sp];
                    if (condition >= 0.5 || double.IsNaN(condition))
                    {
                        Run(ref m, op.Start1, op.End1);
                    }
                    else
                    {
                        Run(ref m, op.End1, op.End2);
                    }

                    i = op.End2;
                    continue;
                }

                case OpCode.Sel:
                {
                    var selector = stack[--m.Sp];
                    var index = RoundHalfAwayFromZero(selector);
                    var cases = op.S;
                    var bodies = _caseBodies.AsSpan(op.T);
                    if (index >= 0 && index < cases)
                    {
                        Run(ref m, bodies[(int)index], bodies[(int)index + 1]);
                    }
                    else if (op.Value == 1)
                    {
                        Run(ref m, bodies[cases], bodies[cases + 1]);
                    }

                    i = op.End2;
                    continue;
                }

                default:
                    throw new InvalidOperationException("The operation was not compiled.");
            }

            i++;
        }
    }

    private static double RoundHalfAwayFromZero(double value) => double.IsNaN(value) ? -1 : value < 0 ? Math.Truncate(value - 0.5) : Math.Truncate(value + 0.5);

    private static void Rotate(Span<double> values, int shift)
    {
        // Left rotation by shift positions: values[shift..] then values[..shift]
        if (shift == 0)
            return;

        values[..shift].Reverse();
        values[shift..].Reverse();
        values.Reverse();
    }

    private static double Binary(OpCode code, double x, double y) => code switch
    {
        OpCode.Add => R(x + y),
        OpCode.Sub => R(x - y),
        OpCode.Mul => R(x * y),
        OpCode.Div => R(x / y),
        OpCode.Mod => R(x - (Math.Truncate(R(x / y)) * y)),
        OpCode.Pow => R(Math.Pow(x, y)),
        OpCode.Atn2 => R(Math.Atan2(y, x)),
        OpCode.Lt => x < y ? 1 : 0,
        OpCode.Le => x <= y ? 1 : 0,
        // Section 11.2.1.1 lets a value be compared with the result of 'NaN ' to detect it: 'eq' finds two values that are
        // not numbers equal, as iccDEV does
        OpCode.Eq => x == y || (double.IsNaN(x) && double.IsNaN(y)) ? 1 : 0,
        OpCode.Near => y - 1e-8 <= x && x <= y + 1e-8 ? 1 : 0,
        OpCode.Ge => x >= y ? 1 : 0,
        OpCode.Gt => x > y ? 1 : 0,
        OpCode.VMin => Math.Min(x, y),
        OpCode.VMax => Math.Max(x, y),
        OpCode.VAnd => x >= 0.5 && y >= 0.5 ? 1 : 0,
        OpCode.VOr => x >= 0.5 || y >= 0.5 ? 1 : 0,
        _ => (x >= 0.5) != (y >= 0.5) ? 1 : 0, // VXor
    };

    private static double Unary(OpCode code, double x) => code switch
    {
        OpCode.Sq => R(x * x),
        OpCode.Sqrt => R(Math.Sqrt(x)),
        OpCode.Cb => R(x * x * x),
        OpCode.Cbrt => R(Math.Cbrt(x)),
        OpCode.Abs => Math.Abs(x),
        OpCode.Neg => -x,
        OpCode.Rond => double.IsNaN(x) ? x : RoundHalfAwayFromZero(x),
        OpCode.Flor => Math.Floor(x),
        OpCode.Ceil => Math.Ceiling(x),
        OpCode.Trnc => Math.Truncate(x),

        // Table 102 gives -1 for positive values too, an evident typographical error: the sign of a positive value is 1
        OpCode.Sign => x < 0 ? -1 : x > 0 ? 1 : 0,
        OpCode.Exp => R(Math.Exp(x)),
        OpCode.Log => R(Math.Log10(x)),
        OpCode.Ln => R(Math.Log(x)),
        OpCode.Not => x < 0.5 ? 1 : 0,
        OpCode.Sin => R(Math.Sin(x)),
        OpCode.Cos => R(Math.Cos(x)),
        OpCode.Tan => R(Math.Tan(x)),
        OpCode.Asin => R(Math.Asin(x)),
        OpCode.Acos => R(Math.Acos(x)),
        OpCode.Atan => R(Math.Atan(x)),
        _ => double.IsFinite(x) ? 1 : 0, // RNum
    };

    // 'ctop': the radius and the angle in degrees, in [0, 360)
    private static (double Radius, double Angle) ToPolar(double x, double y)
    {
        var angle = Math.Atan2(y, x) * 180 / Math.PI;
        if (angle < 0)
        {
            angle += 360;
        }

        return (R(Math.Sqrt((x * x) + (y * y))), R(angle));
    }

    private static (double X, double Y) FromPolar(double radius, double angle)
    {
        var radians = angle * Math.PI / 180;
        return (R(radius * Math.Cos(radians)), R(radius * Math.Sin(radians)));
    }

    // 'tLab' and 'tXYZ' (Table 102): CIELAB of colors whose CIEXYZ values are already relative to the white
    private static (double L, double A, double B) ToLab(double x, double y, double z)
    {
        var fx = LabFunction(x);
        var fy = LabFunction(y);
        var fz = LabFunction(z);
        return (R((116 * fy) - 16), R(500 * (fx - fy)), R(200 * (fy - fz)));

        static double LabFunction(double t) => t > 216.0 / 24389 ? Math.Cbrt(t) : (841.0 / 108 * t) + (4.0 / 29);
    }

    private static (double X, double Y, double Z) ToXyz(double l, double a, double b)
    {
        var fy = (l + 16) / 116;
        return (R(Inverse(fy + (a / 500))), R(Inverse(fy)), R(Inverse(fy - (b / 200))));

        static double Inverse(double f) => f > 6.0 / 29 ? f * f * f : 108.0 / 841 * (f - (4.0 / 29));
    }

    // 'solv' (Table 98): the matrix of S + 1 rows and T + 1 columns, then y, are replaced by x and a success flag. Only
    // square systems are solved (Gauss elimination with partial pivoting, in place on the stack); the others report a
    // failure, as the specification allows an implementation to
    private static void Solve(Span<double> stack, ref int sp, int rows, int columns)
    {
        var start = sp - (rows * columns) - rows;
        var matrix = stack.Slice(start, rows * columns);
        var y = stack.Slice(start + (rows * columns), rows);
        var success = rows == columns;
        if (success)
        {
            var size = rows;
            for (var pivot = 0; pivot < size && success; pivot++)
            {
                var best = pivot;
                for (var row = pivot + 1; row < size; row++)
                {
                    if (Math.Abs(matrix[(row * size) + pivot]) > Math.Abs(matrix[(best * size) + pivot]))
                    {
                        best = row;
                    }
                }

                if (!(Math.Abs(matrix[(best * size) + pivot]) > 0))
                {
                    success = false;
                    break;
                }

                if (best != pivot)
                {
                    for (var column = 0; column < size; column++)
                    {
                        (matrix[(pivot * size) + column], matrix[(best * size) + column]) = (matrix[(best * size) + column], matrix[(pivot * size) + column]);
                    }

                    (y[pivot], y[best]) = (y[best], y[pivot]);
                }

                for (var row = pivot + 1; row < size; row++)
                {
                    var factor = matrix[(row * size) + pivot] / matrix[(pivot * size) + pivot];
                    for (var column = pivot; column < size; column++)
                    {
                        matrix[(row * size) + column] -= factor * matrix[(pivot * size) + column];
                    }

                    y[row] -= factor * y[pivot];
                }
            }

            if (success)
            {
                for (var row = size - 1; row >= 0; row--)
                {
                    var sum = y[row];
                    for (var column = row + 1; column < size; column++)
                    {
                        sum -= matrix[(row * size) + column] * y[column];
                    }

                    y[row] = sum / matrix[(row * size) + row];
                }
            }
        }

        // x has T + 1 values: those of y for a square system, zeros otherwise
        for (var k = 0; k < columns; k++)
        {
            stack[start + k] = success ? R(y[k]) : 0;
        }

        stack[start + columns] = success ? 1 : 0;
        sp = start + columns + 1;
    }

    [StructLayout(LayoutKind.Auto)]
    private ref struct Machine
    {
        public Span<double> Stack;
        public Span<double> Temps;
        public Span<double> SubInput;
        public Span<double> SubOutput;
        public Span<double> SubScratch;
        public Span<double> Transpose;
        public ReadOnlySpan<double> Input;
        public Span<double> Output;
        public int Sp;
    }

    /// <summary>
    /// An operation. <see cref="S"/> and <see cref="T"/> are the parameters of the operation; for an 'if',
    /// [<see cref="Start1"/>, <see cref="End1"/>) is its stream and [<see cref="End1"/>, <see cref="End2"/>) the stream of
    /// its 'else'; for a 'sel', <see cref="S"/> is the number of cases, <see cref="T"/> the index of its bodies in the case
    /// table, <see cref="Value"/> 1 when it has a default, and <see cref="End2"/> the next operation.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Instruction(OpCode Code, int S, int T, double Value, int Start1, int End1, int End2);

    private sealed class Compiler
    {
        private readonly ReadOnlyMemory<byte> _operations;
        private readonly int _inputs;
        private readonly int _outputs;
        private readonly IccMpeElement[] _subElements;
        private readonly IccMpeParseContext _context;

        public Compiler(ReadOnlyMemory<byte> operations, int inputs, int outputs, IccMpeElement[] subElements, IccMpeParseContext context)
        {
            _operations = operations;
            _inputs = inputs;
            _outputs = outputs;
            _subElements = subElements;
            _context = context;
            context.Allocate(operations.Length / 8);
            Program = new Instruction[operations.Length / 8];
        }

        public Instruction[] Program { get; }

        public List<int> CaseBodies { get; } = [];

        public int StackSize { get; private set; }

        public int TempCount { get; private set; }

        public int TransposeSize { get; private set; }

        private uint Signature(int index) => IccReader.ReadUInt32(_operations.Span[(8 * index)..]);

        private uint Parameter(int index) => IccReader.ReadUInt32(_operations.Span[((8 * index) + 4)..]);

        /// <summary>Compiles the operations [start, end) entered with <paramref name="depth"/> values on the stack, and returns the number of values they leave.</summary>
        public int CompileRange(int start, int end, int depth, int nesting, out long cost)
        {
            if (nesting > MaxNesting)
                throw _context.Unsupported(string.Create(CultureInfo.InvariantCulture, $"the conditional operations of a calculator element are nested more than {MaxNesting} times."), "ICC processing elements too deeply nested");

            cost = 0;
            var i = start;
            while (i < end)
            {
                var signature = Signature(i);
                switch (signature)
                {
                    case 0x69662020: // 'if '
                    {
                        Pop(ref depth, 1, signature);
                        var hasElse = i + 1 < end && Signature(i + 1) == 0x656C7365; // 'else'
                        var thenStart = (long)i + (hasElse ? 2 : 1);
                        var thenEnd = thenStart + Parameter(i);
                        var elseEnd = thenEnd + (hasElse ? Parameter(i + 1) : 0);
                        if (elseEnd > end)
                            throw _context.Invalid("the operations of an 'if' or 'else' of a calculator element go beyond their function.");

                        var thenDepth = CompileRange((int)thenStart, (int)thenEnd, depth, nesting + 1, out var thenCost);
                        var elseDepth = CompileRange((int)thenEnd, (int)elseEnd, depth, nesting + 1, out var elseCost);
                        if (thenDepth != elseDepth && hasElse)
                            throw _context.Invalid("the two streams of an 'if' of a calculator element leave different numbers of values on the stack.");

                        if (thenDepth != elseDepth)
                            throw _context.Unsupported("the stream of an 'if' without 'else' of a calculator element changes the number of values on the stack, which then depends on the colors.", "ICC calculator stream that changes the stack size");

                        if (hasElse)
                        {
                            Program[i + 1] = new Instruction(OpCode.Placeholder, 0, 0, 0, 0, 0, 0);
                        }

                        Program[i] = new Instruction(OpCode.If, 0, 0, 0, (int)thenStart, (int)thenEnd, (int)elseEnd);
                        depth = thenDepth;
                        cost += 1 + Math.Max(thenCost, elseCost);
                        i = (int)elseEnd;
                        continue;
                    }

                    case 0x73656C20: // 'sel '
                    {
                        Pop(ref depth, 1, signature);
                        var cases = 0;
                        while (i + 1 + cases < end && Signature(i + 1 + cases) == 0x63617365) // 'case'
                        {
                            cases++;
                        }

                        if (cases == 0)
                            throw _context.Invalid("a 'sel' operation of a calculator element is not followed by a 'case' operation.");

                        var hasDefault = i + 1 + cases < end && Signature(i + 1 + cases) == 0x64666C74; // 'dflt'
                        var headers = cases + (hasDefault ? 1 : 0);
                        // The boundaries of the streams are adjacent in the table: it is reserved before the streams,
                        // whose nested selections add their own tables after it
                        var tableIndex = CaseBodies.Count;
                        long bodyStart = i + 1 + headers;
                        CaseBodies.Add((int)bodyStart);
                        for (var k = 0; k < headers; k++)
                        {
                            CaseBodies.Add(0);
                        }

                        int? bodyDepth = null;
                        long bodiesCost = 0;
                        for (var k = 0; k < headers; k++)
                        {
                            var bodyEnd = bodyStart + Parameter(i + 1 + k);
                            if (bodyEnd > end)
                                throw _context.Invalid("the operations of a 'case' or 'dflt' of a calculator element go beyond their function.");

                            var result = CompileRange((int)bodyStart, (int)bodyEnd, depth, nesting + 1, out var bodyCost);
                            if (bodyDepth is { } expected && result != expected)
                                throw _context.Invalid("the streams of a 'sel' of a calculator element leave different numbers of values on the stack.");

                            bodyDepth = result;
                            bodiesCost = Math.Max(bodiesCost, bodyCost);
                            Program[i + 1 + k] = new Instruction(OpCode.Placeholder, 0, 0, 0, 0, 0, 0);
                            CaseBodies[tableIndex + 1 + k] = (int)bodyEnd;
                            bodyStart = bodyEnd;
                        }

                        // Without default, no stream may run: the streams must leave the stack as they found it
                        if (!hasDefault && bodyDepth != depth)
                            throw _context.Unsupported("the streams of a 'sel' without 'dflt' of a calculator element change the number of values on the stack, which then depends on the colors.", "ICC calculator stream that changes the stack size");

                        Program[i] = new Instruction(OpCode.Sel, cases, tableIndex, hasDefault ? 1 : 0, 0, 0, (int)bodyStart);
                        depth = bodyDepth!.Value;
                        cost += 1 + bodiesCost;
                        i = (int)bodyStart;
                        continue;
                    }

                    case 0x656C7365 or 0x63617365 or 0x64666C74: // 'else', 'case', 'dflt'
                        throw _context.Invalid($"a {IccReader.FormatSignature(signature)} operation of a calculator element does not follow an 'if' or a 'sel'.");

                    default:
                        Program[i] = CompileOperation(i, signature, ref depth, out var operationCost);
                        cost += operationCost;
                        break;
                }

                StackSize = Math.Max(StackSize, depth);
                if (cost > IccMpeParseContext.MaxCost)
                    throw _context.Unsupported(string.Create(CultureInfo.InvariantCulture, $"a calculator element needs more than {IccMpeParseContext.MaxCost} operations per color."), "ICC processing elements too costly to evaluate");

                i++;
            }

            StackSize = Math.Max(StackSize, depth);
            return depth;
        }

        private Instruction CompileOperation(int index, uint signature, ref int depth, out long cost)
        {
            var parameter = Parameter(index);
            var s = (int)(parameter >> 16);
            var t = (int)(parameter & 0xFFFF);
            var n = s + 1;
            cost = 1;
            switch (signature)
            {
                case 0x64617461: // 'data'
                {
                    var value = BitConverter.Int32BitsToSingle((int)parameter);
                    Push(ref depth, 1);
                    return new Instruction(OpCode.Data, 0, 0, value, 0, 0, 0);
                }

                case 0x696E2020: // 'in '
                    if (s + t >= _inputs)
                        throw _context.Invalid(string.Create(CultureInfo.InvariantCulture, $"an 'in' operation of a calculator element reads the input channel {s + t}, but it has {_inputs}."));

                    Push(ref depth, t + 1);
                    cost = t + 1;
                    return Simple(OpCode.In, s, t);

                case 0x6F757420: // 'out '
                    if (s + t >= _outputs)
                        throw _context.Invalid(string.Create(CultureInfo.InvariantCulture, $"an 'out' operation of a calculator element writes the output channel {s + t}, but it has {_outputs}."));

                    Pop(ref depth, t + 1, signature);
                    cost = t + 1;
                    return Simple(OpCode.Out, s, t);

                case 0x74676574: // 'tget'
                    TempCount = Math.Max(TempCount, s + t + 1);
                    Push(ref depth, t + 1);
                    cost = t + 1;
                    return Simple(OpCode.TGet, s, t);

                case 0x74707574: // 'tput'
                    TempCount = Math.Max(TempCount, s + t + 1);
                    Pop(ref depth, t + 1, signature);
                    cost = t + 1;
                    return Simple(OpCode.TPut, s, t);

                case 0x74736176: // 'tsav'
                    TempCount = Math.Max(TempCount, s + t + 1);
                    Need(depth, t + 1, signature);
                    cost = t + 1;
                    return Simple(OpCode.TSav, s, t);

                case 0x656E7620: // 'env '
                    Push(ref depth, 2);
                    return new Instruction(OpCode.Env, 0, 0, parameter == 0x74727565 ? 1 : 0, 0, 0, 0); // 'true'

                case 0x63757276 or 0x6D747820 or 0x636C7574 or 0x63616C63 or 0x74696E74 or 0x656C656D: // 'curv', 'mtx ', 'clut', 'calc', 'tint', 'elem'
                {
                    if (s >= _subElements.Length)
                        throw _context.Invalid(string.Create(CultureInfo.InvariantCulture, $"a {IccReader.FormatSignature(signature)} operation of a calculator element applies the sub-element {s}, but it has {_subElements.Length}."));

                    var element = _subElements[s];
                    var expected = signature switch
                    {
                        0x63757276 => element is IccMpeCurveSet,
                        0x6D747820 => element is IccMpeMatrix,
                        0x636C7574 => element is IccMpeClut,
                        0x63616C63 => element is IccMpeCalculator,
                        0x74696E74 => element is IccMpeTintArray,
                        _ => true,
                    };

                    if (!expected)
                        throw _context.Invalid($"a {IccReader.FormatSignature(signature)} operation of a calculator element applies a sub-element of another type.");

                    Pop(ref depth, element.InputCount, signature);
                    Push(ref depth, element.OutputCount);
                    cost = 1 + element.InputCount + element.OutputCount + element.Cost;
                    return Simple(OpCode.SubElement, s, 0);
                }

                case 0x744A6162 or 0x664A6162: // 'tJab', 'fJab'
                    throw _context.Unsupported("the color appearance operations of the calculator element ('tJab', 'fJab') are not supported.", "ICC color appearance processing element");

                case 0x636F7079: // 'copy'
                    Need(depth, n, signature);
                    Push(ref depth, (long)n * (t + 1));
                    cost = (long)n * (t + 1);
                    return Simple(OpCode.Copy, s, t);

                case 0x726F746C or 0x726F7472: // 'rotl', 'rotr'
                    Need(depth, n, signature);
                    cost = n;
                    return Simple(signature == 0x726F746C ? OpCode.Rotl : OpCode.Rotr, s, t);

                case 0x706F7364: // 'posd'
                    Need(depth, n, signature);
                    Push(ref depth, t + 1);
                    cost = t + 1;
                    return Simple(OpCode.Posd, s, t);

                case 0x666C6970: // 'flip'
                    Need(depth, s + 2, signature);
                    cost = s + 2;
                    return Simple(OpCode.Flip, s, 0);

                case 0x706F7020: // 'pop '
                    Pop(ref depth, n, signature);
                    return Simple(OpCode.Pop, s, 0);

                case 0x736F6C76: // 'solv'
                {
                    var rows = s + 1;
                    var columns = t + 1;
                    if (rows == columns && rows > MaxSolveSize)
                        throw _context.Unsupported(string.Create(CultureInfo.InvariantCulture, $"a 'solv' operation of a calculator element solves {rows} equations, more than the {MaxSolveSize} supported."), "ICC calculator system of equations too large");

                    Pop(ref depth, ((long)rows * columns) + rows, signature);
                    Push(ref depth, columns + 1);
                    cost = (long)rows * rows * rows;
                    return Simple(OpCode.Solv, s, t);
                }

                case 0x7472616E: // 'tran'
                {
                    // Within the stack, so the product of the dimensions fits in an int
                    var size = (long)n * (t + 1);
                    Need(depth, size, signature);
                    TransposeSize = Math.Max(TransposeSize, (int)size);
                    cost = size;
                    return Simple(OpCode.Tran, s, t);
                }

                case 0x73756D20 or 0x70726F64 or 0x6D696E20 or 0x6D617820 or 0x616E6420 or 0x6F722020: // 'sum ', 'prod', 'min ', 'max ', 'and ', 'or  '
                    Pop(ref depth, s + 2, signature);
                    Push(ref depth, 1);
                    cost = s + 2;
                    return Simple(signature switch
                    {
                        0x73756D20 => OpCode.Sum,
                        0x70726F64 => OpCode.Prod,
                        0x6D696E20 => OpCode.Min,
                        0x6D617820 => OpCode.Max,
                        0x616E6420 => OpCode.And,
                        _ => OpCode.Or,
                    }, s, 0);

                case 0x6E6F6F70 or 0x73636E74 or 0x70692020 or 0x2B494E46 or 0x2D494E46 or 0x4E614E20: // 'noop', 'scnt', 'pi  ', '+INF', '-INF', 'NaN '
                    if (s != 0)
                        throw _context.Invalid($"the {IccReader.FormatSignature(signature)} operation of a calculator element has a size, which shall be zero.");

                    if (signature != 0x6E6F6F70)
                    {
                        Push(ref depth, 1);
                    }

                    return Simple(signature switch
                    {
                        0x6E6F6F70 => OpCode.Noop,
                        0x73636E74 => OpCode.Scnt,
                        0x70692020 => OpCode.Pi,
                        0x2B494E46 => OpCode.PositiveInfinity,
                        0x2D494E46 => OpCode.NegativeInfinity,
                        _ => OpCode.NaN,
                    }, 0, 0);
            }

            if (GetBinary(signature) is { } binary)
            {
                // X0..XS then Y0..YS: the results replace X (ctop and ptoc replace both)
                Need(depth, 2 * n, signature);
                if (binary is not (OpCode.Ctop or OpCode.Ptoc))
                {
                    Pop(ref depth, n, signature);
                }

                cost = n;
                return Simple(binary, s, 0);
            }

            if (GetScalar(signature) is { } scalar)
            {
                Need(depth, n + 1, signature);
                Pop(ref depth, 1, signature);
                cost = n;
                return Simple(scalar, s, 0);
            }

            if (GetUnary(signature) is { } unary)
            {
                Need(depth, n, signature);
                cost = n;
                return Simple(unary, s, 0);
            }

            if (signature is 0x744C6162 or 0x7458595A) // 'tLab', 'tXYZ'
            {
                Need(depth, 3 * n, signature);
                cost = 3 * n;
                return Simple(signature == 0x744C6162 ? OpCode.TLab : OpCode.TXyz, s, 0);
            }

            throw _context.Unsupported($"the calculator element has the unknown operation {IccReader.FormatSignature(signature)}.", "ICC calculator operation");
        }

        private static Instruction Simple(OpCode code, int s, int t) => new(code, s, t, 0, 0, 0, 0);

        private static OpCode? GetBinary(uint signature) => signature switch
        {
            0x61646420 => OpCode.Add,
            0x73756220 => OpCode.Sub,
            0x6D756C20 => OpCode.Mul,
            0x64697620 => OpCode.Div,
            0x6D6F6420 => OpCode.Mod,
            0x706F7720 => OpCode.Pow,
            0x61746E32 => OpCode.Atn2,
            0x6C742020 => OpCode.Lt,
            0x6C652020 => OpCode.Le,
            0x65712020 => OpCode.Eq,
            0x6E656172 => OpCode.Near,
            0x67652020 => OpCode.Ge,
            0x67742020 => OpCode.Gt,
            0x766D696E => OpCode.VMin,
            0x766D6178 => OpCode.VMax,
            0x76616E64 => OpCode.VAnd,
            0x766F7220 => OpCode.VOr,
            0x76786F72 => OpCode.VXor,
            0x63746F70 => OpCode.Ctop,
            0x70746F63 => OpCode.Ptoc,
            _ => null,
        };

        private static OpCode? GetScalar(uint signature) => signature switch
        {
            0x67616D61 => OpCode.Gama,
            0x73616464 => OpCode.SAdd,
            0x73737562 => OpCode.SSub,
            0x736D756C => OpCode.SMul,
            0x73646976 => OpCode.SDiv,
            _ => null,
        };

        private static OpCode? GetUnary(uint signature) => signature switch
        {
            0x73712020 => OpCode.Sq,
            0x73717274 => OpCode.Sqrt,
            0x63622020 => OpCode.Cb,
            0x63627274 => OpCode.Cbrt,
            0x61627320 => OpCode.Abs,
            0x6E656720 => OpCode.Neg,
            0x726F6E64 => OpCode.Rond,
            0x666C6F72 => OpCode.Flor,
            0x6365696C => OpCode.Ceil,
            0x74726E63 => OpCode.Trnc,
            0x7369676E => OpCode.Sign,
            0x65787020 => OpCode.Exp,
            0x6C6F6720 => OpCode.Log,
            0x6C6E2020 => OpCode.Ln,
            0x6E6F7420 => OpCode.Not,
            0x73696E20 => OpCode.Sin,
            0x636F7320 => OpCode.Cos,
            0x74616E20 => OpCode.Tan,
            0x6173696E => OpCode.Asin,
            0x61636F73 => OpCode.Acos,
            0x6174616E => OpCode.Atan,
            0x726E756D => OpCode.RNum,
            _ => null,
        };

        private void Need(int depth, long count, uint signature)
        {
            if (depth < count)
                throw _context.Invalid($"a {IccReader.FormatSignature(signature)} operation of a calculator element reads more values than the stack holds.");
        }

        private void Pop(ref int depth, long count, uint signature)
        {
            Need(depth, count, signature);
            depth -= (int)count;
        }

        private void Push(ref int depth, long count)
        {
            if (depth + count > IccMpeParseContext.MaxChannels)
                throw _context.Invalid("the main function of a calculator element puts more than 65,535 values on the stack.");

            depth += (int)count;
            StackSize = Math.Max(StackSize, depth);
        }
    }
}
