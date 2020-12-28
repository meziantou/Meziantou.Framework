using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning
{
    internal sealed class TextLocation : Location, ILocationLineInfo
    {
        private static readonly char[] s_newLineCharacters = { '\r', '\n' };

        public TextLocation(string filePath, int line, int column, int length)
            : base(filePath)
        {
            if (line < 1)
                throw new ArgumentException("line must be greater or equal to 1", nameof(line));

            if (column < 1)
                throw new ArgumentException("column must be greater or equal to 1", nameof(column));

            LineNumber = line;
            LinePosition = column;
            Length = length;
        }

        public int LineNumber { get; }

        public int LinePosition { get; }

        public int Length { get; }

        public override bool IsUpdatable => true;

        protected internal override async Task UpdateAsync(Stream stream, string newVersion, CancellationToken cancellationToken)
        {
            if (newVersion.IndexOfAny(s_newLineCharacters) >= 0)
                throw new ArgumentException("New version contains a \\r or \\n", nameof(newVersion));

            // Line and Column are 1-based index.
            var line = LineNumber - 1;
            var column = LinePosition - 1;
            if (line >= 0 && column >= 0)
            {
                var encoding = await StreamUtilities.GetEncodingAsync(stream, cancellationToken).ConfigureAwait(false);
                stream.Seek(0, SeekOrigin.Begin);

                string content;
                using (var reader = StreamUtilities.CreateReader(stream, encoding))
                {
                    content = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                var currentLine = 0;
                var currentIndex = 0;
                int endOfLine;
                if (line > 0)
                {
                    while (currentLine < line && currentIndex < content.Length)
                    {
                        var newIndex = content.IndexOf('\n', currentIndex);
                        if (newIndex == -1)
                            throw new DependencyScannerException("Dependency not found. File was probably modified since last scan.");

                        currentIndex = newIndex + 1;
                        currentLine++;
                    }
                }

                endOfLine = content.IndexOf('\n', currentIndex);
                if (currentIndex + column + Length > (endOfLine == -1 ? content.Length : endOfLine))
                    throw new DependencyScannerException("Dependency not found. File was probably modified since last scan.");

                stream.SetLength(0);
                var writer = StreamUtilities.CreateWriter(stream, encoding);
                try
                {
                    var contentAsMemory = content.AsMemory();
                    await writer.WriteAsync(contentAsMemory[0..(currentIndex + column)], cancellationToken).ConfigureAwait(false);
                    await writer.WriteAsync(newVersion.AsMemory(), cancellationToken).ConfigureAwait(false);
                    await writer.WriteAsync(contentAsMemory[(currentIndex + column + Length)..], cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    await writer.DisposeAsync().ConfigureAwait(false);
                }
            }
            else
            {
                throw new DependencyScannerException("Dependency not found");
            }
        }

        public override string ToString()
        {
            return FormattableString.Invariant($"{FilePath}:{LineNumber},{LinePosition}-{LinePosition + Length}");
        }

        internal static TextLocation FromIndex(string filePath, string text, int index, int length)
        {
            var line = 1;
            var lineIndex = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (i == index)
                    return new TextLocation(filePath, line, index - lineIndex, length);

                if (text[i] == '\n')
                {
                    lineIndex = i;
                    line++;
                }
            }

            throw new ArgumentException("Index was not found in the text", nameof(index));
        }
    }
}
