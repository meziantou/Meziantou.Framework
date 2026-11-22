namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Sets the options of the <c>Default</c> instance of a generated <see cref="CsvSerializerContext"/>, and the members the generator includes.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class CsvSourceGenerationOptionsAttribute : CsvAttribute
{
    /// <summary>Gets or sets the separator. Default is <c>,</c>.</summary>
    public char Separator { get; set; } = CsvReaderOptions.DefaultSeparator;

    /// <summary>Gets or sets a value indicating whether the data has a header row. Default is <see langword="true"/>.</summary>
    public bool HasHeaderRow { get; set; } = true;

    /// <summary>Gets or sets the string written at the end of each row. Default is <c>\r\n</c>.</summary>
    public string NewLine { get; set; } = "\r\n";

    /// <summary>Gets or sets the naming policy of the column names. Default is <see cref="CsvKnownNamingPolicy.Unspecified"/>: the names of the members.</summary>
    public CsvKnownNamingPolicy PropertyNamingPolicy { get; set; }

    /// <summary>Gets or sets a value indicating whether column names are matched ignoring case. Default is <see langword="false"/>.</summary>
    public bool PropertyNameCaseInsensitive { get; set; }

    /// <summary>Gets or sets a value indicating whether public fields are serialized. Default is <see langword="false"/>.</summary>
    public bool IncludeFields { get; set; }

    /// <summary>Gets or sets how a member without a column is handled. Default is <see cref="CsvMissingColumnHandling.Ignore"/>.</summary>
    public CsvMissingColumnHandling MissingColumnHandling { get; set; }

    /// <summary>Gets or sets how a column without a member is handled. Default is <see cref="CsvUnmappedColumnHandling.Ignore"/>.</summary>
    public CsvUnmappedColumnHandling UnmappedColumnHandling { get; set; }
}
