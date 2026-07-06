using System.Runtime.InteropServices;

namespace Meziantou.Framework.Yaml;

/// <summary>Represents a location inside a file</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct Mark
{
    /// <summary>Initializes a new mark at the specified source location.</summary>
    public Mark(int index, int line, int column)
    {
        Index = index;
        Line = line;
        Column = column;
    }

    /// <summary>Gets / sets the absolute offset in the file</summary>
    public int Index { get; }

    /// <summary>Gets / sets the number of the line</summary>
    public int Line { get; }

    /// <summary>Gets / sets the index of the column</summary>
    public int Column { get; }

    /// <summary>
    /// Gets a <see cref="Mark"/> with empty values.
    /// </summary>
    public static readonly Mark Empty;

    /// <summary>
    /// Returns a <see cref="System.String"/> that represents this instance.
    /// </summary>
    /// <returns>
    /// A <see cref="System.String"/> that represents this instance.
    /// </returns>
    public override string ToString()
    {
        return $"Lin: {Line}, Col: {Column}, Chr: {Index}";
    }
}