namespace Meziantou.Framework.Csv;

/// <summary>Specifies the kind of malformed data reported to <see cref="CsvReaderOptions.BadDataHandler"/>.</summary>
public enum CsvBadDataKind
{
    /// <summary>A value that is not quoted contains a quote character, such as <c>a"b</c>.</summary>
    QuoteInUnquotedValue,

    /// <summary>The closing quote of a value is followed by characters that are not a separator or a line terminator, such as <c>"a"b</c>.</summary>
    CharactersAfterClosingQuote,

    /// <summary>A quoted value is not closed before the end of the data.</summary>
    UnterminatedQuotedValue,

    /// <summary>The record does not have the expected number of columns. Only reported when <see cref="CsvReaderOptions.ValidateColumnCount"/> is <see langword="true"/>.</summary>
    ColumnCountMismatch,
}
