using Meziantou.Framework.Csv.Serialization.Metadata;

namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Provides the serialization metadata of types.</summary>
public interface ICsvTypeInfoResolver
{
    /// <summary>Gets the serialization metadata of a type.</summary>
    /// <param name="type">The type.</param>
    /// <param name="options">The options the metadata is created for.</param>
    /// <returns>The metadata, or <see langword="null"/> when the resolver does not support <paramref name="type"/>.</returns>
    CsvTypeInfo? GetTypeInfo(Type type, CsvSerializerOptions options);
}
