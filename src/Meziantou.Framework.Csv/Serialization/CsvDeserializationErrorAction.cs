namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Specifies what the serializer does with an error, as returned by <see cref="CsvSerializerOptions.DeserializationErrorHandler"/>.</summary>
public enum CsvDeserializationErrorAction
{
    /// <summary>The serializer throws the <see cref="CsvException"/> of <see cref="CsvDeserializationErrorContext.Exception"/>.</summary>
    Throw,

    /// <summary>The error is ignored: the member keeps its default value, or the column is ignored, and the record is returned.</summary>
    /// <remarks>
    /// A member set by the constructor gets the default value of its parameter. For an error of the header row (<see cref="CsvDeserializationErrorKind.MissingColumn"/>,
    /// or <see cref="CsvDeserializationErrorKind.UnmappedColumn"/> with a header row), this applies to every record.
    /// </remarks>
    Ignore,

    /// <summary>The record is not returned, and the serializer continues with the next record.</summary>
    /// <remarks>An error of the header row is not about a record: <see cref="SkipRecord"/> is the same as <see cref="Ignore"/> for it.</remarks>
    SkipRecord,
}
