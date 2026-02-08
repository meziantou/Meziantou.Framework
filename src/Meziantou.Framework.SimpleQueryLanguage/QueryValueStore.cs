namespace Meziantou.Framework.SimpleQueryLanguage;

/// <summary>
/// A simple wrapper class to store a value for use in expression trees.
/// This allows the expression to reference the value via a field/property access
/// instead of embedding the constant directly, which is required for some LINQ providers.
/// </summary>
/// <typeparam name="TValue">The type of the value.</typeparam>
internal sealed class QueryValueStore<TValue>(TValue value)
{
    public TValue Value { get; } = value;
}
