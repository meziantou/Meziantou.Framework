using Meziantou.Framework.Csv.Serialization;

namespace Meziantou.Framework.Csv.Fuzz;

#pragma warning disable MA0048 // File name must match type name: the context is declared with the model
#pragma warning disable CA1819 // The record covers the array converter

public enum FuzzKind
{
    None,
    First,
    Second,
}

/// <summary>The record of the serializer targets: one member per kind of converter.</summary>
public sealed class FuzzRecord
{
    public string? Text { get; set; }

    public int Number { get; set; }

    public double? Ratio { get; set; }

    public FuzzKind Kind { get; set; }

    public DateOnly? Date { get; set; }

    public bool Flag { get; set; }

    [CsvPropertyName("bytes")]
    public byte[]? Bytes { get; set; }
}

[CsvSerializable(typeof(FuzzRecord))]
internal sealed partial class FuzzCsvContext : CsvSerializerContext
{
}
