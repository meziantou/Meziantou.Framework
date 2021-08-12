using System.Collections.Generic;
using System.Threading.Tasks;

namespace Meziantou.Framework.DependencyScanning.Internals;

internal sealed class EmptyAsyncEnumerator<T> : IAsyncEnumerator<T>
{
    public static EmptyAsyncEnumerator<T> Instance { get; } = new EmptyAsyncEnumerator<T>();

    public T Current => default!;

    public ValueTask DisposeAsync() => default;

    public ValueTask<bool> MoveNextAsync() => new(result: false);
}
