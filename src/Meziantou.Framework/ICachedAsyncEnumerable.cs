namespace Meziantou.Framework;

/// <summary>Represents an async enumerable that caches its results to avoid re-enumeration of the underlying source.</summary>
public interface ICachedAsyncEnumerable<T> : IAsyncEnumerable<T>, IAsyncDisposable
{
}
