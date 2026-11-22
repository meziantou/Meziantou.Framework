namespace Meziantou.Framework.Csv.Serialization.Metadata;

/// <summary>The serialization metadata of a type: its columns and how to create its instances.</summary>
public abstract class CsvTypeInfo
{
    private protected CsvTypeInfo(Type type, CsvSerializerOptions options, IReadOnlyList<CsvPropertyInfo> properties)
    {
        Type = type;
        Options = options;
        Properties = properties;
    }

    /// <summary>Gets the type described by the metadata.</summary>
    public Type Type { get; }

    /// <summary>Gets the options the metadata is created for.</summary>
    public CsvSerializerOptions Options { get; }

    /// <summary>Gets the columns of the type, in the order they are written.</summary>
    public IReadOnlyList<CsvPropertyInfo> Properties { get; }
}
