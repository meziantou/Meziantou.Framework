namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Specifies how the serializer handles a member that has no column in the header row.</summary>
public enum CsvMissingColumnHandling
{
    /// <summary>The member keeps its default value. A required member still throws a <see cref="CsvException"/>.</summary>
    Ignore,

    /// <summary>A <see cref="CsvException"/> is thrown.</summary>
    Throw,
}
