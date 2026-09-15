using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning;

internal sealed class TextLocation : Location, ILocationLineInfo
{
    private static readonly char[] NewLineCharacters = ['\r', '\n'];

    public TextLocation(IFileSystem fileSystem, string filePath, int line, int column, int length)
        : base(fileSystem, filePath)
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

    protected internal override async Task UpdateCoreAsync(string? oldValue, string newValue, CancellationToken cancellationToken)
    {
        if (newValue.IndexOfAny(NewLineCharacters) >= 0)
            throw new ArgumentException("New version contains a \\r or \\n", nameof(newValue));

        var stream = FileSystem.OpenReadWrite(FilePath);
        try
        {
            var file = await StreamUtilities.ReadForUpdateAsync(stream, isXml: false, cancellationToken).ConfigureAwait(false);
            var content = file.Text;

            // Line and Column are 1-based index.
            var lineStart = new TextLineMap(content).GetLineStart(LineNumber);
            if (lineStart < 0)
                throw new DependencyScannerException("Dependency not found. File was probably modified since last scan.");

            var start = lineStart + LinePosition - 1;
            if (start + Length > TextLineMap.GetLineEnd(content, lineStart))
                throw new DependencyScannerException("Dependency not found. File was probably modified since last scan.");

            if (oldValue is not null)
            {
                var currentValue = content.Substring(start, Length);
                if (currentValue != oldValue)
                    throw new DependencyScannerException($"Expected value not found at the location. File was probably modified since last scan.\nCurrent value: {currentValue}\nExpected value: {oldValue}");
            }

            var updatedContent = string.Concat(content.AsSpan(0, start), newValue, content.AsSpan(start + Length));
            await StreamUtilities.WriteForUpdateAsync(stream, file, updatedContent, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{FilePath}:{LineNumber},{LinePosition}-{LinePosition + Length}");
    }

    internal static TextLocation FromIndex(IFileSystem fileSystem, string filePath, string text, int index, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, text.Length);

        return FromIndex(fileSystem, filePath, new TextLineMap(text), index, length);
    }

    /// <summary>Creates a location from an offset in a text whose line map was computed once, so that many locations in the same text cost O(log n) each.</summary>
    internal static TextLocation FromIndex(IFileSystem fileSystem, string filePath, TextLineMap lineMap, int index, int length)
    {
        // LineNumber and LinePosition are 1-based
        var (line, column) = lineMap.GetLinePosition(index);
        return new TextLocation(fileSystem, filePath, line, column, length);
    }
}
