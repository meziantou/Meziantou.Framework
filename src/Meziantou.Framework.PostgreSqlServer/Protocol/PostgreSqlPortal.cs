namespace Meziantou.Framework.PostgreSql.Protocol;

internal sealed class PostgreSqlPortal
{
    public required string Name { get; init; }

    public required PostgreSqlStatement Statement { get; init; }

    public IReadOnlyList<PostgreSqlBoundParameter> Parameters { get; init; } = [];

    public IReadOnlyList<int> ResultFormatCodes { get; init; } = [];

    public bool IsDescribed { get; set; }
}
