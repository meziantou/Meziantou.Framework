using System.Buffers;

namespace Meziantou.Framework.Csv.Internals;

internal sealed class BufferWriterCsvTarget<T>(IBufferWriter<T> writer) : CsvTarget<T>
    where T : unmanaged
{
    public override void Write(ReadOnlySpan<T> data) => writer.Write(data);

    public override ValueTask WriteAsync(ReadOnlyMemory<T> data, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled(cancellationToken);

        writer.Write(data.Span);
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
