using System;
using System.Runtime.InteropServices;

namespace Tomlyn.Syntax
{
    /// <summary>
    /// A position within a text (offset, line column)
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public struct TextPosition : IEquatable<TextPosition>
    {
        /// <summary>
        /// Gets a sentinel position representing end-of-file.
        /// </summary>
        public static readonly TextPosition Eof = new TextPosition(-1, -1, -1);

        /// <summary>
        /// Creates a new instance of a <see cref="TextPosition"/>
        /// </summary>
        /// <param name="offset">Offset in the source text</param>
        /// <param name="line">Line number - zero based</param>
        /// <param name="column">Column number - zero based</param>
        public TextPosition(int offset, int line, int column)
        {
            Offset = offset;
            Column = column;
            Line = line;
        }

        /// <summary>
        /// Gets or sets the offset.
        /// </summary>
        public int Offset { get; set; }

        /// <summary>
        /// Gets or sets the column number (zero based)
        /// </summary>
        public int Column { get; set; }

        /// <summary>
        /// Gets or sets the line number (zero based)
        /// </summary>
        public int Line { get; set; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"({Line+1},{Column+1})";
        }

        /// <inheritdoc />
        public bool Equals(TextPosition other)
        {
            return Offset == other.Offset && Column == other.Column && Line == other.Line;
        }

        /// <inheritdoc />
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is TextPosition && Equals((TextPosition) obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Offset;
        }

        /// <summary>
        /// Compares two <see cref="TextPosition"/> values for equality.
        /// </summary>
        /// <param name="left">The left value.</param>
        /// <param name="right">The right value.</param>
        /// <returns><c>true</c> if the values are equal; otherwise <c>false</c>.</returns>
        public static bool operator ==(TextPosition left, TextPosition right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two <see cref="TextPosition"/> values for inequality.
        /// </summary>
        /// <param name="left">The left value.</param>
        /// <param name="right">The right value.</param>
        /// <returns><c>true</c> if the values are not equal; otherwise <c>false</c>.</returns>
        public static bool operator !=(TextPosition left, TextPosition right)
        {
            return !left.Equals(right);
        }
    }
}