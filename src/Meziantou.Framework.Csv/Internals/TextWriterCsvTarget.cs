namespace Meziantou.Framework.Csv.Internals;

internal sealed class TextWriterCsvTarget(TextWriter writer, bool leaveOpen) : CsvTarget<char>
{
    public override void Write(ReadOnlySpan<char> data) => writer.Write(data);

    public override ValueTask WriteAsync(ReadOnlyMemory<char> data, CancellationToken cancellationToken) => new(writer.WriteAsync(data, cancellationToken));

    public override void Flush() => writer.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => writer.FlushAsync(cancellationToken);

    public override void Dispose()
    {
        if (!leaveOpen)
        {
            writer.Dispose();
        }
    }

    public override ValueTask DisposeAsync() => leaveOpen ? ValueTask.CompletedTask : writer.DisposeAsync();
}
