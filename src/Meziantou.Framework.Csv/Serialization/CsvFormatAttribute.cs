namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Sets the format string used to write the values of a property or a field, and to read the dates and times.</summary>
/// <param name="format">The format string, such as <c>yyyy-MM-dd</c> for a date or <c>F2</c> for a number.</param>
/// <remarks>
/// The values of <see cref="DateTime"/>, <see cref="DateTimeOffset"/>, <see cref="DateOnly"/>, <see cref="TimeOnly"/> and <see cref="TimeSpan"/> are read with
/// this exact format. The other values are written with this format and read as usual.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class CsvFormatAttribute(string format) : CsvAttribute
{
    /// <summary>Gets the format string.</summary>
    public string Format { get; } = format;
}
