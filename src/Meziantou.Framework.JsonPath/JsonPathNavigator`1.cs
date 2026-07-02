namespace Meziantou.Framework.Json;

/// <summary>Provides JSONPath navigation services for a custom object model.</summary>
/// <typeparam name="TValue">The node type used by the custom object model.</typeparam>
public abstract class JsonPathNavigator<TValue>
    where TValue : class
{
    /// <summary>Gets the JSON node kind represented by <paramref name="value"/>.</summary>
    /// <param name="value">The value to inspect. A <see langword="null"/> value represents JSON <c>null</c>.</param>
    /// <returns>The JSON node kind represented by <paramref name="value"/>.</returns>
    public abstract JsonPathNodeKind GetKind(TValue? value);

    /// <summary>Gets an object property value.</summary>
    /// <param name="value">The object value.</param>
    /// <param name="name">The property name.</param>
    /// <param name="result">When this method returns, contains the property value if it exists.</param>
    /// <returns><see langword="true"/> when the property exists; otherwise, <see langword="false"/>.</returns>
    public abstract bool TryGetPropertyValue(TValue? value, string name, out TValue? result);

    /// <summary>Gets object properties in enumeration order.</summary>
    /// <param name="value">The object value.</param>
    /// <returns>The object properties in enumeration order.</returns>
    public abstract IEnumerable<JsonPathProperty<TValue>> GetProperties(TValue? value);

    /// <summary>Gets an array length.</summary>
    /// <param name="value">The array value.</param>
    /// <returns>The array length.</returns>
    public abstract int GetArrayLength(TValue? value);

    /// <summary>Gets an array element by zero-based index.</summary>
    /// <param name="value">The array value.</param>
    /// <param name="index">The zero-based array index.</param>
    /// <param name="result">When this method returns, contains the element value if it exists.</param>
    /// <returns><see langword="true"/> when the element exists; otherwise, <see langword="false"/>.</returns>
    public abstract bool TryGetElement(TValue? value, int index, out TValue? result);

    /// <summary>Gets a string scalar value.</summary>
    /// <param name="value">The scalar value.</param>
    /// <param name="result">When this method returns, contains the string value.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> represents a string; otherwise, <see langword="false"/>.</returns>
    public abstract bool TryGetString(TValue? value, out string? result);

    /// <summary>Gets a number scalar value.</summary>
    /// <param name="value">The scalar value.</param>
    /// <param name="result">When this method returns, contains the number value.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> represents a number; otherwise, <see langword="false"/>.</returns>
    public abstract bool TryGetNumber(TValue? value, out double result);

    /// <summary>Gets a boolean scalar value.</summary>
    /// <param name="value">The scalar value.</param>
    /// <param name="result">When this method returns, contains the boolean value.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> represents a boolean; otherwise, <see langword="false"/>.</returns>
    public abstract bool TryGetBoolean(TValue? value, out bool result);
}
