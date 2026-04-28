namespace Meziantou.Framework.PostgreSql.Protocol;

internal sealed class PostgreSqlFrontendMessage
{
    public required byte Type { get; init; }

    public required byte[] Payload { get; init; }
}
