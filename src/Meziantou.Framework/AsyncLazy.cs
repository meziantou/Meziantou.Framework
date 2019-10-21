#nullable disable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Meziantou.Framework
{
    public static class AsyncLazy
    {
        public static AsyncLazy<T> Create<T>(Func<Task<T>> valueFactory)
        {
            return new AsyncLazy<T>(valueFactory);
        }
    }

    public class AsyncLazy<T> : IDisposable
    {
        private readonly Func<Task<T>> _valueFactory;
        private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);
        private Task<T> _value;

        public AsyncLazy(Func<Task<T>> valueFactory)
        {
            _valueFactory = valueFactory ?? throw new ArgumentNullException(nameof(valueFactory));
        }

        public bool HasValue { get; private set; }

        public Task<T> GetValueAsync() => GetValueAsync(CancellationToken.None);

        public Task<T> GetValueAsync(CancellationToken cancellationToken)
        {
            if (HasValue)
                return _value;

            return GetValueCoreAsync(cancellationToken);
        }

        private async Task<T> GetValueCoreAsync(CancellationToken cancellationToken)
        {
            await _semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (HasValue)
                    return await _value.ConfigureAwait(false);

                var value = await _valueFactory().ConfigureAwait(false);
                _value = Task.FromResult(value);
                HasValue = true;
                return value;
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }

        public void Dispose()
        {
            _semaphoreSlim.Dispose();
        }
    }
}
