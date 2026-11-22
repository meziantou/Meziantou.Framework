namespace Meziantou.Framework.Csv.Serialization;

internal sealed class CsvSnakeCaseLowerNamingPolicy : CsvSeparatorNamingPolicy
{
    internal CsvSnakeCaseLowerNamingPolicy()
        : base('_', upperCase: false)
    {
    }
}
