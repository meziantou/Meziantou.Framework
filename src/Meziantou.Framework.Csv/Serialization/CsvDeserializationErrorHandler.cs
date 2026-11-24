namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Represents the method that decides what <see cref="CsvSerializer"/> does when a record cannot be converted to an object.</summary>
/// <param name="context">The description of the error and where it is.</param>
/// <returns>What the serializer does with the error.</returns>
public delegate CsvDeserializationErrorAction CsvDeserializationErrorHandler(CsvDeserializationErrorContext context);
