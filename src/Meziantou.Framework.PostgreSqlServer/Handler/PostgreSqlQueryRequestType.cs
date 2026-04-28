namespace Meziantou.Framework.PostgreSql.Handler;

/// <summary>Describes the high-level PostgreSQL query request type.</summary>
public enum PostgreSqlQueryRequestType
{
    /// <summary>A simple query request (<c>Q</c> message).</summary>
    SimpleQuery,

    /// <summary>An extended query request (<c>Parse/Bind/Execute</c> flow).</summary>
    ExtendedQuery,
}
