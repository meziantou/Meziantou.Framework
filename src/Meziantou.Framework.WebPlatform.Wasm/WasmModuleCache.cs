using System.IO.Hashing;
using Engine = Meziantou.Framework.WasmEngine;

namespace Meziantou.Framework.WebPlatform.Wasm;

/// <summary>
/// The modules compiled by a <see cref="WasmWebAssemblyRuntime"/>, by the content of their binary: a binary compiled again
/// (the same application loaded by another page) is not decoded again, and the functions that its instances compiled to
/// .NET code run compiled in the new instances. The least recently used modules are removed when the total size of the
/// binaries exceeds the capacity. The cache is thread-safe: the pages of several threads can share the runtime.
/// </summary>
internal sealed class WasmModuleCache
{
    /// <summary>
    /// The size of the smallest binary that the cache keeps, in bytes. A small module compiles in about 0.1 ms, and its
    /// compiled module uses about 100 times the size of its binary: keeping the many small modules of a test suite (the
    /// web-platform-tests compile 30,000 modules of about 200 bytes) would cost memory and save nothing.
    /// </summary>
    public const int MinimumModuleSize = 1024;

    private readonly Lock _lock = new();
    private readonly Dictionary<ModuleKey, LinkedListNode<Entry>> _entries = new(ModuleKeyComparer.Instance);

    // The most recently used entry first
    private readonly LinkedList<Entry> _lru = new();
    private long _size;

    public WasmModuleCache(long capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        Capacity = capacity;
    }

    /// <summary>The maximum total size of the binaries of the cached modules, in bytes.</summary>
    public long Capacity { get; }

    /// <summary>The number of cached modules.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>The total size of the binaries of the cached modules, in bytes.</summary>
    public long Size
    {
        get
        {
            lock (_lock)
            {
                return _size;
            }
        }
    }

    /// <summary>
    /// Gets the module of a binary from the cache, or compiles it and adds it to the cache. A binary that cannot be compiled
    /// is not cached: <paramref name="compile"/> throws again for the next call. The cache keeps <paramref name="bytes"/>
    /// (the module keeps it too): do not change it.
    /// </summary>
    public Engine.WasmModule GetOrCompile(byte[] bytes, Func<byte[], Engine.WasmModule> compile)
    {
        if (bytes.Length < MinimumModuleSize || bytes.Length > Capacity)
            return compile(bytes);

        var key = new ModuleKey(bytes, XxHash3.HashToUInt64(bytes));
        lock (_lock)
        {
            if (TryGet(key, out var cached))
                return cached;
        }

        // The compilation runs outside of the lock: the pages of other threads compile their modules meanwhile. Two threads
        // that compile the same binary at the same time both compile it, and the module of the first one is kept
        var module = compile(bytes);
        lock (_lock)
        {
            if (TryGet(key, out var cached))
                return cached;

            _entries.Add(key, _lru.AddFirst(new Entry(key, module)));
            _size += bytes.Length;
            while (_size > Capacity && _lru.Last is { } last)
            {
                _lru.RemoveLast();
                _entries.Remove(last.Value.Key);
                _size -= last.Value.Key.Bytes.Length;
            }
        }

        return module;
    }

    /// <summary>Removes all the modules from the cache.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            _lru.Clear();
            _size = 0;
        }
    }

    private bool TryGet(ModuleKey key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Engine.WasmModule? module)
    {
        if (_entries.TryGetValue(key, out var node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            module = node.Value.Module;
            return true;
        }

        module = null;
        return false;
    }

    private sealed record Entry(ModuleKey Key, Engine.WasmModule Module);

    /// <summary>The binary of a module, and the hash of all its bytes: the modules of a page often share their first and last bytes.</summary>
    private readonly record struct ModuleKey(byte[] Bytes, ulong Hash);

    private sealed class ModuleKeyComparer : IEqualityComparer<ModuleKey>
    {
        public static ModuleKeyComparer Instance { get; } = new();

        public bool Equals(ModuleKey x, ModuleKey y) => x.Hash == y.Hash && x.Bytes.AsSpan().SequenceEqual(y.Bytes);

        public int GetHashCode(ModuleKey obj) => obj.Hash.GetHashCode();
    }
}
