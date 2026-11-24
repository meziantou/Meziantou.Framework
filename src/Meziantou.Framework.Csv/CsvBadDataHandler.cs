namespace Meziantou.Framework.Csv;

/// <summary>Represents the method that decides what <see cref="CsvReader"/> and <see cref="Utf8CsvReader"/> do with malformed data.</summary>
/// <param name="context">The description of the malformed data and where it is.</param>
/// <returns>What the reader does with the data.</returns>
public delegate CsvBadDataAction CsvBadDataHandler(CsvBadDataContext context);
