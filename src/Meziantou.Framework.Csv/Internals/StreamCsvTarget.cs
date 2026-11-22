namespace Meziantou.Framework.Csv.Internals;

internal sealed class StreamCsvTarget(Stream stream, bool leaveOpen) : CsvTarget<byte>
{
    public override void Write(ReadOnlySpan<byte> data) => stream.Write(data);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken) => stream.WriteAsync(data, cancellationToken);

    public override void Flush() => stream.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => stream.FlushAsync(cancellationToken);

    public override void Dispose()
    {
        if (!leaveOpen)
        {
            stream.Dispose();
        }
    }

    public override ValueTask DisposeAsync() => leaveOpen ? ValueTask.CompletedTask : stream.DisposeAsync();
}
