namespace Meziantou.Framework.Csv.Internals;

internal sealed class TextReaderCsvSource(TextReader reader, bool leaveOpen) : CsvSource<char>
{
    public override int Read(Span<char> buffer) => reader.Read(buffer);

    public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken) => reader.ReadAsync(buffer, cancellationToken);

    public override void Dispose()
    {
        if (!leaveOpen)
        {
            reader.Dispose();
        }
    }
}
