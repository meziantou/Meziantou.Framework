namespace Meziantou.Framework.PostgreSql.Handler;

/// <summary>Describes the high-level PostgreSQL query request type.</summary>
public enum PostgreSqlQueryRequestType
{
    /// <summary>A simple query request (<c>Q</c> message).</summary>
    SimpleQuery,

    /// <summary>An extended query request (<c>Parse/Bind/Execute</c> flow).</summary>
    ExtendedQuery,

    /// <summary>
    /// A request for the shape of a statement or portal (<c>Describe</c> message), issued before execution.
    /// The handler should return the columns it will produce, without necessarily executing the command.
    /// </summary>
    Describe,
}
