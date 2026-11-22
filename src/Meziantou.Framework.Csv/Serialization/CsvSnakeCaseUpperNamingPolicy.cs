namespace Meziantou.Framework.Csv.Serialization;

internal sealed class CsvSnakeCaseUpperNamingPolicy : CsvSeparatorNamingPolicy
{
    internal CsvSnakeCaseUpperNamingPolicy()
        : base('_', upperCase: true)
    {
    }
}
