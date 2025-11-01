namespace Meziantou.Framework;

/// <summary>
/// Represents a strongly-typed identifier with a string representation and underlying type information.
/// </summary>
public interface IStronglyTypedId
{
    /// <summary>
    /// Gets the value of the identifier as a string.
    /// </summary>
    string ValueAsString { get; }

    /// <summary>
    /// Gets the underlying type of the identifier value.
    /// </summary>
    Type UnderlyingType { get;  }
}

/// <summary>
/// Represents a strongly-typed identifier with a typed value.
/// </summary>
/// <typeparam name="T">The type of the identifier value.</typeparam>
public interface IStronglyTypedId<T> : IStronglyTypedId
{
    /// <summary>
    /// Gets the typed value of the identifier.
    /// </summary>
    T Value { get; }
}
