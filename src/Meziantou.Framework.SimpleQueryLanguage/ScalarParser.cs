namespace Meziantou.Framework.SimpleQueryLanguage;

/// <summary>Represents a method that attempts to parse a string value into a specified type.</summary>
/// <typeparam name="T">The type to parse the value into.</typeparam>
/// <param name="value">The string value to parse.</param>
/// <param name="result">When this method returns, contains the parsed value if parsing succeeded, or the default value if parsing failed.</param>
/// <returns><see langword="true"/> if parsing succeeded; otherwise, <see langword="false"/>.</returns>
public delegate bool ScalarParser<T>(string value, [MaybeNullWhen(false)] out T result);
