namespace Meziantou.Framework.Csv.Internals;

internal abstract class CsvSource<T> : IDisposable
    where T : unmanaged
{
    public abstract int Read(Span<T> buffer);

    public abstract ValueTask<int> ReadAsync(Memory<T> buffer, CancellationToken cancellationToken);

    public abstract void Dispose();
}
