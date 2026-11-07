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

    public int LinePosition { get; private set; }

    public int Length { get; private set; }

    public override bool IsUpdatable => CanWriteFile;

    private protected override bool TracksWrittenValue => true;

    protected internal override async Task UpdateCoreAsync(string? oldValue, string newValue, CancellationToken cancellationToken)
    {
        if (newValue.IndexOfAny(NewLineCharacters) >= 0)
            throw new ArgumentException("New version contains a \\r or \\n", nameof(newValue));

        // The recorded span alone cannot tell whether the file still holds the dependency there
        if (oldValue is null)
            throw new DependencyScannerException("The value expected at the location is unknown, so the file cannot be updated safely. Provide the expected value.");

        var stream = FileSystem.OpenReadWrite(FilePath);
        try
        {
            var file = await StreamUtilities.ReadForUpdateAsync(stream, isXml: false, cancellationToken).ConfigureAwait(false);
            var content = file.Text;

            // Line and Column are 1-based index.
            var lineStart = new TextLineMap(content).GetLineStart(LineNumber);
            if (lineStart < 0)
                throw new DependencyScannerException("Dependency not found. File was probably modified since last scan.");

            var line = content.AsSpan(lineStart, TextLineMap.GetLineEnd(content, lineStart) - lineStart);
            var column = FindOldValue(line, oldValue);
            var start = lineStart + column;
            var updatedContent = string.Concat(content.AsSpan(0, start), newValue, content.AsSpan(start + oldValue.Length));
            await StreamUtilities.WriteForUpdateAsync(stream, file, updatedContent, cancellationToken).ConfigureAwait(false);

            LinePosition = column + 1;
            Length = newValue.Length;
            SetCurrentValue(newValue);
            NotifyValueReplaced(column, oldValue.Length, newValue.Length);
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private protected override void OnSiblingValueReplaced(Location sibling, int start, int oldLength, int newLength)
    {
        if (sibling is TextLocation other && other.LineNumber == LineNumber && string.Equals(other.FilePath, FilePath, StringComparison.Ordinal) && LinePosition - 1 >= start + oldLength)
        {
            LinePosition += newLength - oldLength;
        }
    }

    private int FindOldValue(ReadOnlySpan<char> line, string oldValue)
    {
        var recordedColumn = LinePosition - 1;
        if (recordedColumn <= line.Length && oldValue.Length <= line.Length - recordedColumn &&
            line.Slice(recordedColumn, oldValue.Length).Equals(oldValue, StringComparison.Ordinal))
        {
            return recordedColumn;
        }

        // Several locations can share a line, such as the name and the version of "FROM node:18". Updating one of them
        // moves the ones after it, so the value is searched again, but only where it can have moved to: anywhere after
        // the recorded column when the line grew, or at its end when the line shrank. Anything before that window
        // belongs to another part of the line, and the value is only replaced when it occurs exactly once in the window.
        if (oldValue.Length > 0 && oldValue.Length <= line.Length)
        {
            var searchStart = Math.Max(0, Math.Min(recordedColumn, line.Length - oldValue.Length));
            var index = line[searchStart..].IndexOf(oldValue, StringComparison.Ordinal);
            if (index >= 0)
            {
                index += searchStart;
                if (line[(index + 1)..].IndexOf(oldValue, StringComparison.Ordinal) < 0)
                    return index;
            }
        }

        throw new DependencyScannerException($"Expected value not found at the location. File was probably modified since last scan.\nCurrent line: {line}\nExpected value: {oldValue}");
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
