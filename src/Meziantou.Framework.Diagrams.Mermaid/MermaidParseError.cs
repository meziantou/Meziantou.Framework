using System.Runtime.InteropServices;

namespace Meziantou.Framework.Diagrams.Mermaid;

/// <summary>An error of a Mermaid text, with its position.</summary>
/// <param name="Message">The description of the error.</param>
/// <param name="Line">The line of the error, from 1.</param>
/// <param name="Column">The column of the error, from 1, in UTF-16 code units.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct MermaidParseError(string Message, int Line, int Column)
{
    /// <summary>Returns the error as <c>(line, column): message</c>.</summary>
    /// <returns>The description of the error.</returns>
    public override string ToString() => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"({Line}, {Column}): {Message}");
}
