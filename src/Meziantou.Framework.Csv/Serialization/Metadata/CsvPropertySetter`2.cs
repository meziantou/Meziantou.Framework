namespace Meziantou.Framework.Csv.Serialization.Metadata;

/// <summary>Sets the value of a member of an instance, which can be a value type.</summary>
/// <typeparam name="TDeclaring">The type that declares the member.</typeparam>
/// <typeparam name="TValue">The type of the member.</typeparam>
/// <param name="obj">The instance.</param>
/// <param name="value">The value of the member.</param>
public delegate void CsvPropertySetter<TDeclaring, in TValue>(ref TDeclaring obj, TValue value);
