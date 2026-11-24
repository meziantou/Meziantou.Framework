namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Specifies the kind of error reported to <see cref="CsvSerializerOptions.DeserializationErrorHandler"/>.</summary>
public enum CsvDeserializationErrorKind
{
    /// <summary>The value of a column cannot be converted to the type of its member. <see cref="CsvDeserializationErrorContext.Exception"/> has the error of the converter as its inner exception.</summary>
    ConversionFailed,

    /// <summary>The record has no value for a required member, because it has fewer columns than the data without a header row describes.</summary>
    MissingRequiredValue,

    /// <summary>The header row has no column for a required member, or for a member when <see cref="CsvSerializerOptions.MissingColumnHandling"/> is <see cref="CsvMissingColumnHandling.Throw"/>.</summary>
    MissingColumn,

    /// <summary>A column has no member and <see cref="CsvSerializerOptions.UnmappedColumnHandling"/> is <see cref="CsvUnmappedColumnHandling.Throw"/>: a column of the header row, or a column after the last member in data without a header row.</summary>
    UnmappedColumn,
}
