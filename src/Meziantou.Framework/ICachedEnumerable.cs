namespace Meziantou.Framework;

/// <summary>Represents an enumerable that caches its results to avoid re-enumeration of the underlying source.</summary>
public interface ICachedEnumerable<T> : IEnumerable<T>, IDisposable
{
}
