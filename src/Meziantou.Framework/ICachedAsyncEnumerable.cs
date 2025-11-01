namespace Meziantou.Framework;

/// <summary>
/// Represents an asynchronous enumerable that caches its values for multiple enumerations.
/// </summary>
/// <typeparam name="T">The type of elements in the enumerable.</typeparam>
public interface ICachedAsyncEnumerable<T> : IAsyncEnumerable<T>, IAsyncDisposable
{
}
