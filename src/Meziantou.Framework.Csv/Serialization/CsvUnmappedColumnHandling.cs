namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Specifies how the serializer handles a column of the header row that has no member.</summary>
public enum CsvUnmappedColumnHandling
{
    /// <summary>The column is ignored.</summary>
    Ignore,

    /// <summary>A <see cref="CsvException"/> is thrown.</summary>
    Throw,
}
