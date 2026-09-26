using System;

namespace Tomlyn;

/// <summary>
/// Resolves type metadata used by <see cref="TomlSerializer"/>.
/// </summary>
public interface ITomlTypeInfoResolver
{
    /// <summary>
    /// Gets type metadata for a CLR type.
    /// </summary>
    /// <param name="type">The CLR type to resolve.</param>
    /// <param name="options">The serializer options in use.</param>
    /// <returns>The metadata for <paramref name="type"/>, or <see langword="null"/> when not available.</returns>
    TomlTypeInfo? GetTypeInfo(Type type, TomlSerializerOptions options);
}

