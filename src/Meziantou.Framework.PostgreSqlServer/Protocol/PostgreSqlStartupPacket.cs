namespace Meziantou.Framework.PostgreSql.Protocol;

internal sealed class PostgreSqlStartupPacket
{
    public required int RequestCode { get; init; }

    public required byte[] Payload { get; init; }
}
