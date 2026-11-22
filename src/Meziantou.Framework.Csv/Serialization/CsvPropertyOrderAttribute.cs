namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Sets the order of the column of a property or a field. The members are sorted by order, then in declaration order, base types first. Default is 0.</summary>
/// <param name="order">The order of the column.</param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class CsvPropertyOrderAttribute(int order) : CsvAttribute
{
    /// <summary>Gets the order of the column.</summary>
    public int Order { get; } = order;
}
