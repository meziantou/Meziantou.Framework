using System.Globalization;
using System.Text;
using Meziantou.Framework.JavaScriptEngine.Compiler;

namespace Meziantou.Framework.JavaScriptEngine.Benchmarks;

/// <summary>Prints the bytecode of a script and of its nested functions (the <c>dump</c> command).</summary>
internal static class Disassembler
{
    public static string Disassemble(string code)
    {
        var script = JsPreparedScript.Parse(code);
        var builder = new StringBuilder();
        Disassemble(script.Template, builder);
        return builder.ToString();
    }

    private static void Disassemble(FunctionTemplate template, StringBuilder builder)
    {
        builder.Append(CultureInfo.InvariantCulture, $"== {template.Name} (registers {template.RegisterCount}, stack {template.MaxStack})\n");
        var code = template.Code;
        var pc = 0;
        while (pc < code.Length)
        {
            var op = (OpCode)code[pc];
            var count = BytecodeBuilder.GetOperandCount(op);
            builder.Append(CultureInfo.InvariantCulture, $"{pc,5}: {op}");
            for (var i = 0; i < count; i++)
            {
                builder.Append(CultureInfo.InvariantCulture, $" {code[pc + 1 + i]}");
            }

            builder.Append('\n');
            pc += 1 + count;
        }

        foreach (var value in template.Objects)
        {
            if (value is FunctionTemplate nested)
            {
                Disassemble(nested, builder);
            }
        }
    }
}
