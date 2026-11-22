using System.Runtime.InteropServices;

namespace Meziantou.Framework.Graphviz;

/// <summary>A part of a valid DOT text that the flowcharts do not support, such as an attribute of the layout, and that is ignored or approximated.</summary>
/// <param name="Message">The description of what is ignored.</param>
/// <param name="Line">The line, from 1.</param>
/// <param name="Column">The column, from 1, in UTF-16 code units.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct GraphvizWarning(string Message, int Line, int Column)
{
    /// <summary>Returns the warning as <c>(line, column): message</c>.</summary>
    /// <returns>The description of the warning.</returns>
    public override string ToString() => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"({Line}, {Column}): {Message}");
}
