namespace Meziantou.Framework.Csv.Serialization.Metadata;

/// <summary>The serialization metadata of a column: a property, a field or a constructor parameter.</summary>
public abstract class CsvPropertyInfo
{
    private protected CsvPropertyInfo(string name, string memberName, Type propertyType, int order, int? columnIndex, bool isRequired, string? format, CsvConverter converter)
    {
        Name = name;
        MemberName = memberName;
        PropertyType = propertyType;
        Order = order;
        ColumnIndex = columnIndex;
        IsRequired = isRequired;
        Format = format;
        Converter = converter;
    }

    /// <summary>Gets the name of the column.</summary>
    public string Name { get; }

    /// <summary>Gets the name of the member.</summary>
    public string MemberName { get; }

    /// <summary>Gets the type of the values of the column.</summary>
    public Type PropertyType { get; }

    /// <summary>Gets the order set with <see cref="CsvPropertyOrderAttribute"/>.</summary>
    public int Order { get; }

    /// <summary>Gets the index set with <see cref="CsvColumnIndexAttribute"/>, or <see langword="null"/>.</summary>
    public int? ColumnIndex { get; }

    /// <summary>Gets a value indicating whether the column is required.</summary>
    public bool IsRequired { get; }

    /// <summary>Gets the format string set with <see cref="CsvFormatAttribute"/>, or <see langword="null"/>.</summary>
    public string? Format { get; }

    /// <summary>Gets the converter of the values of the column.</summary>
    public CsvConverter Converter { get; }

    /// <summary>Gets a value indicating whether the value of the column is written.</summary>
    public abstract bool CanSerialize { get; }

    /// <summary>Gets a value indicating whether the value of the column is read.</summary>
    public abstract bool CanDeserialize { get; }

    /// <inheritdoc/>
    public override string ToString() => Name;
}
