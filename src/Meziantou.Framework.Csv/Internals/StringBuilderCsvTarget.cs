namespace Meziantou.Framework.Csv.Internals;

internal sealed class StringBuilderCsvTarget(StringBuilder builder) : CsvTarget<char>
{
    public override void Write(ReadOnlySpan<char> data) => builder.Append(data);

    public override ValueTask WriteAsync(ReadOnlyMemory<char> data, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled(cancellationToken);

        builder.Append(data.Span);
        return ValueTask.CompletedTask;
    }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;

    public override void Dispose()
    {
    }

    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
