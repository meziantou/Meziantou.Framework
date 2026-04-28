namespace Meziantou.Framework.PostgreSql.Protocol;

internal sealed class PostgreSqlStatement
{
    public required string Name { get; init; }

    public required string Query { get; init; }

    public IReadOnlyList<uint> ParameterTypeOids { get; init; } = [];
}
