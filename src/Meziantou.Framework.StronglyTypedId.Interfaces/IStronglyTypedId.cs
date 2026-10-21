namespace Meziantou.Framework;

/// <summary>Provides non-generic access to a strongly-typed ID's underlying value as a string and its type information.</summary>
/// <example>
/// Working with strongly-typed IDs polymorphically:
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs" ref:region="StronglyTypedIdAttribute_Declaration" ref:dedent="true">
/// [StronglyTypedId(typeof(int))]
/// public partial struct UserId
/// {
/// }
/// </code>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs" ref:region="IStronglyTypedId_Usage" ref:dedent="true">
/// var userId = UserId.FromInt32(42);
/// IStronglyTypedId stronglyTypedId = userId;
/// Assert.Equal("42", stronglyTypedId.ValueAsString);
/// Assert.Equal(typeof(int), stronglyTypedId.UnderlyingType);
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
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs" ref:region="IStronglyTypedIdOfT_Declaration" ref:dedent="true">
/// [StronglyTypedId(typeof(Guid))]
/// public partial struct OrderId
/// {
/// }
/// </code>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs" ref:region="IStronglyTypedIdOfT_Usage" ref:dedent="true">
/// var guid = Guid.NewGuid();
/// var orderId = OrderId.FromGuid(guid);
/// IStronglyTypedId&lt;Guid&gt; typedId = orderId;
/// Guid value = typedId.Value; // Direct access to the typed value
/// Assert.Equal(guid, value);
/// </code>
/// </example>
public interface IStronglyTypedId<T> : IStronglyTypedId
{
    /// <summary>Gets the strongly-typed underlying value.</summary>
    T Value { get; }
}
