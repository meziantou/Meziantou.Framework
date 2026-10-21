using Meziantou.Framework.JavaScriptEngine.Text;

namespace Meziantou.Framework.JavaScriptEngine.Compiler;

/// <summary>
/// The compiled code of a function, a script, a module or eval code. It is immutable and does not reference any realm:
/// it can be shared by the engines of all the threads.
/// </summary>
internal sealed class FunctionTemplate
{
    public required string Name { get; init; }

    /// <summary>Gets the value of the <c>length</c> property (ExpectedArgumentCount).</summary>
    public required int Length { get; init; }

    public required FunctionTemplateFlags Flags { get; init; }

    public required int[] Code { get; init; }

    public required JsValue[] Constants { get; init; }

    public required PropertyKey[] Keys { get; init; }

    public required object[] Objects { get; init; }

    public required int RegisterCount { get; init; }

    public required int MaxStack { get; init; }

    public required ExceptionHandler[] Handlers { get; init; }

    /// <summary>Gets the pairs (pc, source offset) of the instructions, sorted by pc.</summary>
    public required int[] Positions { get; init; }

    public required SourceText Source { get; init; }

    /// <summary>Gets the start of the source text of the function (Function.prototype.toString).</summary>
    public required int SourceStart { get; init; }

    public required int SourceEnd { get; init; }

    /// <summary>Gets the environment of the name of a named function expression (null when the name is not captured).</summary>
    public ScopeInfo? NameScope { get; init; }

    /// <summary>Gets the register that holds the completion value of script and eval code (-1 otherwise).</summary>
    public int CompletionRegister { get; init; } = -1;

    public bool IsStrict => (Flags & FunctionTemplateFlags.Strict) != 0;

    public bool IsArrow => (Flags & FunctionTemplateFlags.Arrow) != 0;

    public bool IsAsync => (Flags & FunctionTemplateFlags.Async) != 0;

    public bool IsGenerator => (Flags & FunctionTemplateFlags.Generator) != 0;

    public bool IsClassConstructor => (Flags & FunctionTemplateFlags.ClassConstructor) != 0;

    public bool IsDerivedConstructor => (Flags & FunctionTemplateFlags.DerivedConstructor) != 0;

    public string GetSourceText() => Source.Text[SourceStart..SourceEnd];

    /// <summary>Gets the source offset of an instruction.</summary>
    public int GetSourceOffset(int pc)
    {
        var positions = Positions;
        var low = 0;
        var high = (positions.Length / 2) - 1;
        var result = -1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (positions[middle * 2] <= pc)
            {
                result = positions[(middle * 2) + 1];
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return result < 0 ? SourceStart : result;
    }
}
