using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.Pdf.Rendering.Functions;

/// <summary>
/// A program of a PostScript calculator function (ISO 32000-2, 7.10.5): numbers, the arithmetic, relational, boolean,
/// bitwise and stack operators, and <c>if</c> and <c>ifelse</c> with procedures. It has no loop: its work is bounded by
/// its length.
/// </summary>
internal sealed class PostScriptProgram
{
    private const int MaxStack = 100;
    private const int MaxNesting = 64;
    private const int MaxInstructions = 1 << 16;

    private readonly Instruction[] _instructions;

    private PostScriptProgram(Instruction[] instructions) => _instructions = instructions;

    public static PostScriptProgram Parse(ReadOnlySpan<byte> data)
    {
        var tokens = Tokenize(Encoding.Latin1.GetString(data));
        var position = 0;
        while (position < tokens.Count && tokens[position] != "{")
        {
            position++;
        }

        if (position == tokens.Count)
            throw new InvalidPdfContentException("A PostScript calculator function has no procedure");

        position++;
        var instructions = new List<Instruction>();
        ParseProcedure(tokens, ref position, instructions, depth: 0);
        return new PostScriptProgram([.. instructions]);
    }

    public void Execute(ReadOnlySpan<double> inputs, Span<double> outputs)
    {
        Span<double> stack = stackalloc double[MaxStack];
        var count = 0;
        foreach (var input in inputs)
        {
            Push(stack, ref count, input);
        }

        Run(0, _instructions.Length, stack, ref count);

        // The outputs are the values left on the stack, the last one on top
        var outputCount = outputs.Length;
        for (var i = 0; i < outputCount; i++)
        {
            var index = count - outputCount + i;
            outputs[i] = index >= 0 ? stack[index] : 0;
        }
    }

    private void Run(int start, int end, Span<double> stack, ref int count)
    {
        var i = start;
        while (i < end)
        {
            var instruction = _instructions[i];
            switch (instruction.Kind)
            {
                case Kind.Number:
                    Push(stack, ref count, instruction.Value);
                    i++;
                    break;
                case Kind.If:
                    {
                        var condition = Pop(stack, ref count) != 0;
                        if (condition)
                        {
                            Run(i + 1, i + 1 + instruction.Length, stack, ref count);
                        }

                        i += 1 + instruction.Length;
                        break;
                    }

                case Kind.IfElse:
                    {
                        var condition = Pop(stack, ref count) != 0;
                        var elseLength = (int)instruction.Value;
                        if (condition)
                        {
                            Run(i + 1, i + 1 + instruction.Length, stack, ref count);
                        }
                        else
                        {
                            Run(i + 1 + instruction.Length, i + 1 + instruction.Length + elseLength, stack, ref count);
                        }

                        i += 1 + instruction.Length + elseLength;
                        break;
                    }

                default:
                    Apply(instruction.Operator!, stack, ref count);
                    i++;
                    break;
            }
        }
    }

    private static void Apply(string name, Span<double> stack, ref int count)
    {
        double a;
        double b;
        switch (name)
        {
            case "abs":
                Push(stack, ref count, Math.Abs(Pop(stack, ref count)));
                break;
            case "add":
                b = Pop(stack, ref count);
                Push(stack, ref count, Pop(stack, ref count) + b);
                break;
            case "sub":
                b = Pop(stack, ref count);
                Push(stack, ref count, Pop(stack, ref count) - b);
                break;
            case "mul":
                b = Pop(stack, ref count);
                Push(stack, ref count, Pop(stack, ref count) * b);
                break;
            case "div":
                b = Pop(stack, ref count);
                a = Pop(stack, ref count);
                Push(stack, ref count, b == 0 ? 0 : a / b);
                break;
            case "idiv":
                b = Math.Truncate(Pop(stack, ref count));
                a = Math.Truncate(Pop(stack, ref count));
                Push(stack, ref count, b == 0 ? 0 : Math.Truncate(a / b));
                break;
            case "mod":
                b = Math.Truncate(Pop(stack, ref count));
                a = Math.Truncate(Pop(stack, ref count));
                Push(stack, ref count, b == 0 ? 0 : a % b);
                break;
            case "neg":
                Push(stack, ref count, -Pop(stack, ref count));
                break;
            case "atan":
                {
                    b = Pop(stack, ref count);
                    a = Pop(stack, ref count);

                    // The angle of the vector (b, a) in degrees, from 0 to 360
                    var angle = Math.Atan2(a, b) * 180 / Math.PI;
                    Push(stack, ref count, angle < 0 ? angle + 360 : angle);
                    break;
                }

            case "ceiling":
                Push(stack, ref count, Math.Ceiling(Pop(stack, ref count)));
                break;
            case "floor":
                Push(stack, ref count, Math.Floor(Pop(stack, ref count)));
                break;
            case "round":
                Push(stack, ref count, Math.Floor(Pop(stack, ref count) + 0.5));
                break;
            case "truncate" or "cvi":
                Push(stack, ref count, Math.Truncate(Pop(stack, ref count)));
                break;
            case "cvr":
                break;
            case "cos":
                Push(stack, ref count, Math.Cos(Pop(stack, ref count) * Math.PI / 180));
                break;
            case "sin":
                Push(stack, ref count, Math.Sin(Pop(stack, ref count) * Math.PI / 180));
                break;
            case "sqrt":
                Push(stack, ref count, Math.Sqrt(Math.Max(0, Pop(stack, ref count))));
                break;
            case "exp":
                b = Pop(stack, ref count);
                Push(stack, ref count, Math.Pow(Pop(stack, ref count), b));
                break;
            case "ln":
                Push(stack, ref count, Math.Log(Pop(stack, ref count)));
                break;
            case "log":
                Push(stack, ref count, Math.Log10(Pop(stack, ref count)));
                break;
            case "eq":
                Push(stack, ref count, Pop(stack, ref count) == Pop(stack, ref count) ? 1 : 0);
                break;
            case "ne":
                Push(stack, ref count, Pop(stack, ref count) != Pop(stack, ref count) ? 1 : 0);
                break;
            case "gt":
                b = Pop(stack, ref count);
                Push(stack, ref count, Pop(stack, ref count) > b ? 1 : 0);
                break;
            case "ge":
                b = Pop(stack, ref count);
                Push(stack, ref count, Pop(stack, ref count) >= b ? 1 : 0);
                break;
            case "lt":
                b = Pop(stack, ref count);
                Push(stack, ref count, Pop(stack, ref count) < b ? 1 : 0);
                break;
            case "le":
                b = Pop(stack, ref count);
                Push(stack, ref count, Pop(stack, ref count) <= b ? 1 : 0);
                break;
            case "true":
                Push(stack, ref count, 1);
                break;
            case "false":
                Push(stack, ref count, 0);
                break;
            case "and":
                b = Pop(stack, ref count);
                Push(stack, ref count, (long)Pop(stack, ref count) & (long)b);
                break;
            case "or":
                b = Pop(stack, ref count);
                Push(stack, ref count, (long)Pop(stack, ref count) | (long)b);
                break;
            case "xor":
                b = Pop(stack, ref count);
                Push(stack, ref count, (long)Pop(stack, ref count) ^ (long)b);
                break;
            case "not":
                {
                    // A boolean is 0 or 1; another integer is complemented bitwise
                    var value = (long)Pop(stack, ref count);
                    Push(stack, ref count, value is 0 or 1 ? 1 - value : ~value);
                    break;
                }

            case "bitshift":
                {
                    var shift = (int)Math.Clamp(Pop(stack, ref count), -63, 63);
                    var value = (long)Pop(stack, ref count);
                    Push(stack, ref count, shift >= 0 ? value << shift : value >> -shift);
                    break;
                }

            case "pop":
                Pop(stack, ref count);
                break;
            case "dup":
                a = Pop(stack, ref count);
                Push(stack, ref count, a);
                Push(stack, ref count, a);
                break;
            case "exch":
                b = Pop(stack, ref count);
                a = Pop(stack, ref count);
                Push(stack, ref count, b);
                Push(stack, ref count, a);
                break;
            case "copy":
                {
                    var n = (int)Pop(stack, ref count);
                    if (n < 0 || n > count || count + n > MaxStack)
                        throw new InvalidPdfContentException("A PostScript calculator function copies too many values");

                    stack.Slice(count - n, n).CopyTo(stack[count..]);
                    count += n;
                    break;
                }

            case "index":
                {
                    var n = (int)Pop(stack, ref count);
                    if (n < 0 || n >= count)
                        throw new InvalidPdfContentException("A PostScript calculator function indexes outside its stack");

                    Push(stack, ref count, stack[count - 1 - n]);
                    break;
                }

            case "roll":
                {
                    var j = (int)Pop(stack, ref count);
                    var n = (int)Pop(stack, ref count);
                    if (n < 0 || n > count)
                        throw new InvalidPdfContentException("A PostScript calculator function rolls outside its stack");

                    if (n > 0)
                    {
                        // Rolls the top n values by j positions: the values move toward the top
                        var values = stack.Slice(count - n, n);
                        j = ((j % n) + n) % n;
                        Span<double> copy = stackalloc double[n];
                        values.CopyTo(copy);
                        for (var i = 0; i < n; i++)
                        {
                            values[(i + j) % n] = copy[i];
                        }
                    }

                    break;
                }

            default:
                throw new InvalidPdfContentException($"Unknown operator '{name}' in a PostScript calculator function");
        }
    }

    private static void Push(Span<double> stack, ref int count, double value)
    {
        if (count >= MaxStack)
            throw new InvalidPdfContentException("The stack of a PostScript calculator function overflows");

        stack[count++] = value;
    }

    private static double Pop(Span<double> stack, ref int count)
    {
        if (count == 0)
            throw new InvalidPdfContentException("The stack of a PostScript calculator function underflows");

        return stack[--count];
    }

    /// <summary>Parses the instructions of a procedure up to its closing brace; the procedures of <c>if</c> and <c>ifelse</c> follow their instruction.</summary>
    private static void ParseProcedure(List<string> tokens, ref int position, List<Instruction> instructions, int depth)
    {
        if (depth > MaxNesting)
            throw new InvalidPdfContentException("The procedures of a PostScript calculator function are nested too deeply");

        // The procedures read but not used yet, which if and ifelse take
        var pending = new List<List<Instruction>>();
        while (position < tokens.Count)
        {
            var token = tokens[position++];
            if (instructions.Count > MaxInstructions)
                throw new PdfResourceLimitException("A PostScript calculator function is too long");

            switch (token)
            {
                case "}":
                    return;
                case "{":
                    {
                        var procedure = new List<Instruction>();
                        ParseProcedure(tokens, ref position, procedure, depth + 1);
                        pending.Add(procedure);
                        continue;
                    }

                case "if" when pending.Count >= 1:
                    {
                        var procedure = pending[^1];
                        pending.Clear();
                        instructions.Add(new Instruction(Kind.If, 0, null, procedure.Count));
                        instructions.AddRange(procedure);
                        continue;
                    }

                case "ifelse" when pending.Count >= 2:
                    {
                        var then = pending[^2];
                        var otherwise = pending[^1];
                        pending.Clear();
                        instructions.Add(new Instruction(Kind.IfElse, otherwise.Count, null, then.Count));
                        instructions.AddRange(then);
                        instructions.AddRange(otherwise);
                        continue;
                    }
            }

            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                instructions.Add(new Instruction(Kind.Number, number, null, 0));
            }
            else
            {
                instructions.Add(new Instruction(Kind.Operator, 0, token, 0));
            }
        }
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var builder = new StringBuilder();
        foreach (var c in text)
        {
            if (c is '{' or '}')
            {
                Flush();
                tokens.Add(c.ToString());
            }
            else if (char.IsWhiteSpace(c) || c == '\0')
            {
                Flush();
            }
            else
            {
                builder.Append(c);
            }
        }

        Flush();
        return tokens;

        void Flush()
        {
            if (builder.Length > 0)
            {
                tokens.Add(builder.ToString());
                builder.Clear();
            }
        }
    }

    private enum Kind
    {
        Number,
        Operator,
        If,
        IfElse,
    }

    /// <summary>An instruction: a number, an operator, or a conditional followed by its procedures (<see cref="Length"/> instructions, then for <c>ifelse</c> <see cref="Value"/> more).</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Instruction(Kind Kind, double Value, string? Operator, int Length);
}
