using System.Reflection.Emit;

namespace Meziantou.Framework.WasmEngine;

/// <summary>
/// The on-stack replacement (OSR): an interpreted call whose loops run many times continues in .NET code. The OSR method
/// of a function reads the locals of the interpreted frame and the operands of a loop, starts at the loop (the index of
/// the loop is its last argument), and writes the results of the function to the frame, as the other compiled methods.
/// </summary>
internal sealed partial class IlCompiler
{
    // The targets of the backward branches: the entries of an OSR method
    private readonly SortedSet<int> _loops = [];

    /// <summary>Compiles the OSR method of a function, or returns null; <paramref name="loops"/> are the instructions of its entries, in the order of their indexes.</summary>
    public static JitOsrFunction? CompileOsr(CodeFunction function, out int[] loops)
    {
        loops = [];
        var code = function.Code;
        if (code.Heights.Length != code.Code.Length || code.Pops.Length != code.Code.Length || code.Code.Length == 0)
            return null;

        var compiler = new IlCompiler(code, function.Instance.Module.Info, osr: true);
        if (compiler.CompileMethod() is not JitOsrFunction osr)
            return null;

        loops = [.. compiler._loops];
        return osr;
    }

    private void AddLoop(int target, int pc)
    {
        if (target <= pc)
        {
            _hasLoops = true;
            _loops.Add(target);
        }
    }

    private Label[] DefineLoopEntries()
    {
        if (!_osr)
            return [];

        var labels = new Label[_loops.Count];
        for (var i = 0; i < labels.Length; i++)
        {
            labels[i] = _il.DefineLabel();
        }

        return labels;
    }

    /// <summary>The entries of the loops: they read the operands of the loop from the interpreted frame (after its locals), and jump to the loop.</summary>
    private void EmitLoopEntries(Label[] labels)
    {
        if (!_osr)
            return;

        var index = 0;
        foreach (var loop in _loops)
        {
            _il.MarkLabel(labels[index++]);
            for (var h = 0; h < _heights[loop]; h++)
            {
                EmitSlotAddress(_slots, baseLocal: null, _code.Locals.Length + h);
                EmitLoadSlot(_stackA[h], StackB(h), StackR(h));
            }

            _il.Emit(OpCodes.Br, _labels[loop]);
        }
    }
}

/// <summary>The OSR method of a function (see <see cref="IlCompiler.CompileOsr"/>): it continues the interpreted call of the frame at <c>fp</c> at the loop <c>entry</c>.</summary>
internal delegate void JitOsrFunction(Machine machine, ManagedInstance instance, int fp, int depth, int entry);
