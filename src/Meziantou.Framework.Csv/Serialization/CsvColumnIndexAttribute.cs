namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Sets the 0-based index of the column of a property or a field.</summary>
/// <param name="index">The 0-based index of the column.</param>
/// <remarks>
/// Without a header row, the serializer reads and writes the value of the member in this column. A member without this attribute uses the column at its
/// position in the list of the members, and the columns that no member uses are written as empty values. With a header row, the columns are read by name.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class CsvColumnIndexAttribute(int index) : CsvAttribute
{
    /// <summary>Gets the 0-based index of the column.</summary>
    public int Index { get; } = index;
}
