using System.Collections.Generic;
using System.Threading;

namespace Meziantou.Framework.DependencyScanning.Internals
{
    internal sealed class EmptyAsyncEnumerable<T> : IAsyncEnumerable<T>
    {
        public static EmptyAsyncEnumerable<T> Instance { get; } = new EmptyAsyncEnumerable<T>();

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return EmptyAsyncEnumerator<T>.Instance;
        }
    }
}
