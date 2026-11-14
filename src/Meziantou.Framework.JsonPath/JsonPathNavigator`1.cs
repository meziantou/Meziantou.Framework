namespace Meziantou.Framework.Json;

/// <summary>Provides JSONPath navigation services for a custom object model.</summary>
/// <typeparam name="TValue">The node type used by the custom object model.</typeparam>
public abstract class JsonPathNavigator<TValue>
{
    internal virtual JsonPathNavigator<TValue> CreateEvaluationNavigator()
    {
        return this;
    }

    /// <summary>Gets whether navigation is side-effect-free and values remain stable throughout an evaluation.</summary>
    /// <remarks>Override with <see langword="true"/> to permit per-evaluation reuse of root-invariant built-in function results. The default is <see langword="false"/>.</remarks>
    public virtual bool IsPure => false;

    /// <summary>Gets the JSON node kind represented by <paramref name="value"/>.</summary>
    /// <param name="value">The value to inspect. Navigators may use <see langword="null"/> or a model-specific value to represent JSON <c language="json">null</c>.</param>
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

    /// <summary>Gets the exact text of a number scalar value, such as <c>9007199254740993</c> or <c>0.1</c>.</summary>
    /// <param name="value">The scalar value.</param>
    /// <param name="result">When this method returns, contains the number as JSON writes it.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> represents a number whose text is known; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// Filters compare the <see cref="double"/> values of <see cref="TryGetNumber"/> first. When two numbers round to the
    /// same <see cref="double"/>, they compare their texts, so that distinct numbers such as <c>9007199254740992</c> and
    /// <c>9007199254740993</c> do not compare as equal. Return <see langword="false"/> when the text is not known: the numbers
    /// then compare as <see cref="double"/> values. The default implementation returns <see langword="false"/>.
    /// The text has an optional minus sign, digits, an optional fraction and an optional exponent, like a JSON number;
    /// leading zeros and an uppercase <c>E</c> are accepted, so the result of <c>ToString(CultureInfo.InvariantCulture)</c> on
    /// a <see cref="long"/>, a <see cref="decimal"/> or a <see cref="System.Numerics.BigInteger"/> can be returned as is.
    /// </remarks>
    public virtual bool TryGetNumberText(TValue? value, [NotNullWhen(true)] out string? result)
    {
        result = null;
        return false;
    }

    /// <summary>Gets the exact text of a number, in UTF-8, preferably without allocating.</summary>
    /// <param name="value">The scalar value.</param>
    /// <param name="buffer">A buffer the text can be written to.</param>
    /// <param name="result">The text, in <paramref name="buffer"/> or elsewhere.</param>
    internal virtual bool TryGetNumberUtf8(TValue? value, Span<byte> buffer, out ReadOnlySpan<byte> result)
    {
        if (!TryGetNumberText(value, out var text))
        {
            result = default;
            return false;
        }

        // A number is written in ASCII. Other characters make the text invalid, which the comparison detects.
        result = Encoding.UTF8.GetMaxByteCount(text.Length) <= buffer.Length
            ? buffer[..Encoding.UTF8.GetBytes(text, buffer)]
            : Encoding.UTF8.GetBytes(text);
        return true;
    }

    /// <summary>Gets a boolean scalar value.</summary>
    /// <param name="value">The scalar value.</param>
    /// <param name="result">When this method returns, contains the boolean value.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> represents a boolean; otherwise, <see langword="false"/>.</returns>
    public abstract bool TryGetBoolean(TValue? value, out bool result);

    /// <summary>
    /// Gets whether <see cref="TryGetElement"/> takes constant time. When it does not, the evaluator copies an array
    /// with <see cref="CopyElements"/> before walking it, so that visiting every element stays linear.
    /// </summary>
    public virtual bool HasConstantTimeElementAccess => true;

    /// <summary>Gets whether <see cref="TryGetPropertyValue"/> takes constant time.</summary>
    /// <remarks>
    /// When this is <see langword="false"/>, the evaluator enumerates the right object once to build an ordinal lookup
    /// table when comparing objects for equality. If a name occurs more than once, the last value is used and earlier
    /// values are ignored, matching the semantics of <see cref="TryGetPropertyValue"/>.
    /// </remarks>
    public virtual bool HasConstantTimePropertyLookup => true;

    /// <summary>
    /// Gets whether <see cref="TryGetPropertyValue"/> returns the value of the last property that
    /// <see cref="GetProperties"/> returns with the same name, compared ordinally, and fails when there is none.
    /// </summary>
    /// <remarks>
    /// When this is <see langword="true"/>, a descendant segment matches its name selectors while it enumerates the
    /// properties of an object, instead of looking up each name.
    /// </remarks>
    internal virtual bool PropertyLookupMatchesEnumeration => false;

    /// <summary>Copies the elements of an array, in index order, to the start of <paramref name="destination"/>.</summary>
    /// <param name="value">The array value.</param>
    /// <param name="destination">A buffer holding at least <see cref="GetArrayLength"/> items.</param>
    /// <remarks>
    /// Implementations must write the first <see cref="GetArrayLength"/> items in index order. If an element cannot
    /// be retrieved, write <see langword="default"/> at its index.
    /// </remarks>
    public virtual void CopyElements(TValue? value, TValue?[] destination)
    {
        var length = GetArrayLength(value);
        for (var i = 0; i < length; i++)
        {
            destination[i] = TryGetElement(value, i, out var element) ? element : default;
        }
    }
}
