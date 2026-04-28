namespace Meziantou.Framework.PostgreSql.Protocol;

internal sealed class PostgreSqlBoundParameter
{
    public uint TypeOid { get; init; }

    public int FormatCode { get; init; }

    public byte[]? RawValue { get; init; }
}
