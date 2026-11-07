using System.Globalization;

namespace Meziantou.Framework.Assertions.Architecture;

/// <summary>A position in a source file, read from the portable PDB of an assembly.</summary>
/// <param name="FilePath">The path of the source file, as recorded in the PDB when the assembly was compiled.</param>
/// <param name="Line">The 1-based line.</param>
/// <param name="Column">The 1-based column.</param>
public readonly record struct SourceLocation(string FilePath, int Line, int Column)
{
    /// <summary>Returns the location as <c>path:line:column</c>.</summary>
    /// <returns>The location as <c>path:line:column</c>.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{FilePath}:{Line}:{Column}");
}
