using System;
using System.Threading;
using System.Threading.Tasks;

namespace Meziantou.Framework.Utilities
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
        private T _value;

        public AsyncLazy(Func<Task<T>> valueFactory)
        {
            _valueFactory = valueFactory ?? throw new ArgumentNullException(nameof(valueFactory));
        }

        public bool HasValue { get; private set; }

        public async Task<T> GetValueAsync()
        {
            if (HasValue)
                return _value;

            await _semaphoreSlim.WaitAsync().ConfigureAwait(false);
            try
            {
                if (HasValue)
                    return _value;

                _value = await _valueFactory().ConfigureAwait(false);
                HasValue = true;
                return _value;
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
