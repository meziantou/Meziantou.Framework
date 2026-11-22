namespace Meziantou.Framework.Csv.Internals;

internal abstract class CsvTarget<T>
    where T : unmanaged
{
    public abstract void Write(ReadOnlySpan<T> data);

    public abstract ValueTask WriteAsync(ReadOnlyMemory<T> data, CancellationToken cancellationToken);

    public abstract void Flush();

    public abstract Task FlushAsync(CancellationToken cancellationToken);

    public abstract void Dispose();

    public abstract ValueTask DisposeAsync();
}
