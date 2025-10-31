namespace Meziantou.Framework.SimpleQueryLanguage;

/// <summary>Represents a method that handles property queries that don't have a registered handler.</summary>
/// <typeparam name="T">The type of object being queried.</typeparam>
/// <param name="obj">The object to evaluate.</param>
/// <param name="propertyName">The name of the property in the query.</param>
/// <param name="operator">The comparison operator used in the query.</param>
/// <param name="value">The value to compare against.</param>
/// <returns><see langword="true"/> if the object matches the query; otherwise, <see langword="false"/>.</returns>
public delegate bool UnhandledPropertyDelegate<T>(T obj, string propertyName, KeyValueOperator @operator, string value);