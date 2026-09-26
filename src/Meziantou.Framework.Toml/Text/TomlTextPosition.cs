using System;
using System.Runtime.InteropServices;

namespace Tomlyn.Text;

/// <summary>
/// Represents a position within a TOML payload (offset, line, column).
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct TomlTextPosition : IEquatable<TomlTextPosition>
{
    /// <summary>
    /// Gets a sentinel position representing end-of-file.
    /// </summary>
    public static readonly TomlTextPosition Eof = new(offset: -1, line: -1, column: -1);

    /// <summary>
    /// Initializes a new instance of the <see cref="TomlTextPosition"/> struct.
    /// </summary>
    /// <param name="offset">The offset in the source text (0-based).</param>
    /// <param name="line">The line number (0-based).</param>
    /// <param name="column">The column number (0-based).</param>
    public TomlTextPosition(int offset, int line, int column)
    {
        Offset = offset;
        Line = line;
        Column = column;
    }

    /// <summary>
    /// Gets the offset in the source text (0-based).
    /// </summary>
    public int Offset { get; }

    /// <summary>
    /// Gets the line number (0-based).
    /// </summary>
    public int Line { get; }

    /// <summary>
    /// Gets the column number (0-based).
    /// </summary>
    public int Column { get; }

    /// <inheritdoc />
    public override string ToString() => $"({Line + 1},{Column + 1})";

    /// <inheritdoc />
    public bool Equals(TomlTextPosition other) => Offset == other.Offset && Line == other.Line && Column == other.Column;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is TomlTextPosition other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Offset;

    /// <summary>
    /// Compares two <see cref="TomlTextPosition"/> values for equality.
    /// </summary>
    public static bool operator ==(TomlTextPosition left, TomlTextPosition right) => left.Equals(right);

    /// <summary>
    /// Compares two <see cref="TomlTextPosition"/> values for inequality.
    /// </summary>
    public static bool operator !=(TomlTextPosition left, TomlTextPosition right) => !left.Equals(right);
}

