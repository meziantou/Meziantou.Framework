namespace Meziantou.Framework.Csv.Internals;

internal sealed class StreamCsvSource(Stream stream, bool leaveOpen) : CsvSource<byte>
{
    public override int Read(Span<byte> buffer) => stream.Read(buffer);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) => stream.ReadAsync(buffer, cancellationToken);

    public override void Dispose()
    {
        if (!leaveOpen)
        {
            stream.Dispose();
        }
    }
}
