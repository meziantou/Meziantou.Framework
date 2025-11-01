namespace Meziantou.Framework;

/// <summary>
/// Represents an enumerable that caches its values for multiple enumerations.
/// </summary>
/// <typeparam name="T">The type of elements in the enumerable.</typeparam>
public interface ICachedEnumerable<T> : IEnumerable<T>, IDisposable
{
}
