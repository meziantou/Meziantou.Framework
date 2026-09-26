namespace Tomlyn.Syntax
{
    /// <summary>
    /// A textual source span.
    /// </summary>
    public struct SourceSpan
    {
        /// <summary>
        /// Creates a source span.
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        public SourceSpan(string fileName, TextPosition start, TextPosition end)
        {
            FileName = fileName;
            Start = start;
            End = end;
        }

        /// <summary>
        /// Gets or sets the filename.
        /// </summary>
#pragma warning disable CA1051 // Public fields are part of the Tomlyn syntax API
        public string FileName;
#pragma warning restore CA1051

        /// <summary>
        /// Gets the starting offset of this span.
        /// </summary>
        public int Offset => Start.Offset;

        /// <summary>
        /// Gets the length of this span.
        /// </summary>
        public int Length => End.Offset - Start.Offset + 1;

        /// <summary>
        /// Gets or sets the starting text position.
        /// </summary>
#pragma warning disable CA1051 // Public fields are part of the Tomlyn syntax API
        public TextPosition Start;
#pragma warning restore CA1051

        /// <summary>
        /// Gets or sets the ending text position.
        /// </summary>
#pragma warning disable CA1051 // Public fields are part of the Tomlyn syntax API
        public TextPosition End;
#pragma warning restore CA1051

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{FileName}{Start}-{End}";
        }

        /// <summary>
        /// A string representation of this source span not including the <see cref="End"/> position.
        /// </summary>
        public string ToStringSimple()
        {
            return $"{FileName}{Start}";
        }
    }
}