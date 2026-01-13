using System.Collections.Concurrent;
using System.Diagnostics;

namespace Meziantou.Framework.HumanReadable;

internal sealed class SerializationContext
{
    public int Count { get; set; }
    public ConcurrentDictionary<string, object?>? Data { get; set; }

    public T GetOrSetSerializationData<T>(string name, Func<T> addValue)
    {
        Data ??= new ConcurrentDictionary<string, object?>(StringComparer.Ordinal);
        return (T)Data.GetOrAdd(name, static (key, addValue) => addValue(), addValue)!;
    }

    public ScopeContext BeginScope()
    {
        Count++;
        return new ScopeContext(this);
    }

    private void Clear()
    {
        Count--;
        Debug.Assert(Count >= 0);

        if (Count == 0)
        {
            Data = null;
        }
    }

    public readonly struct ScopeContext : IDisposable
    {
        private readonly SerializationContext _context;

        public ScopeContext(SerializationContext context) => _context = context;
        public void Dispose() => _context.Clear();
    }
}
