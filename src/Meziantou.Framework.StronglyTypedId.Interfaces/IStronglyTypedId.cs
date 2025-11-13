namespace Meziantou.Framework;

/// <summary>Provides non-generic access to a strongly-typed ID's underlying value as a string and its type information.</summary>
/// <example>
/// Working with strongly-typed IDs polymorphically:
/// <code>
/// [StronglyTypedId(typeof(int))]
/// public partial struct UserId { }
///
/// var userId = UserId.FromInt32(42);
/// IStronglyTypedId stronglyTypedId = userId;
/// Console.WriteLine(stronglyTypedId.ValueAsString); // "42"
/// Console.WriteLine(stronglyTypedId.UnderlyingType); // System.Int32
/// </code>
/// </example>
public interface IStronglyTypedId
{
    /// <summary>Gets the underlying value as a string representation.</summary>
    string ValueAsString { get; }

    /// <summary>Gets the underlying type of the strongly-typed ID value.</summary>
    Type UnderlyingType { get; }
}

/// <summary>Provides strongly-typed access to the underlying value of a strongly-typed ID.</summary>
/// <typeparam name="T">The type of the underlying value.</typeparam>
/// <example>
/// Accessing strongly-typed ID values with compile-time type safety:
/// <code>
/// [StronglyTypedId(typeof(Guid))]
/// public partial struct OrderId { }
///
/// var orderId = OrderId.FromGuid(Guid.NewGuid());
/// IStronglyTypedId&lt;Guid&gt; typedId = orderId;
/// Guid value = typedId.Value; // Direct access to the typed value
/// </code>
/// </example>
public interface IStronglyTypedId<T> : IStronglyTypedId
{
    /// <summary>Gets the strongly-typed underlying value.</summary>
    T Value { get; }
}
