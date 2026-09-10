namespace Meziantou.Framework.Threading;

/// <summary>Base class for the per-key state tracked by a <see cref="KeyedEntryTable{TKey, TEntry}"/>.</summary>
internal abstract class KeyedEntry
{
    /// <summary>Gets or sets the number of callers holding or waiting for the key. Guarded by the owning shard.</summary>
    public int ReferenceCount { get; set; }
}
