namespace Meziantou.Framework.PostgreSql.Handler;

/// <summary>Represents an error returned from query processing.</summary>
public sealed class PostgreSqlQueryError
{
    /// <summary>Gets or sets the PostgreSQL severity label.</summary>
    public string Severity { get; set; } = "ERROR";

    /// <summary>Gets or sets the PostgreSQL SQLSTATE code.</summary>
    public string Code { get; set; } = "XX000";

    /// <summary>Gets or sets the error message.</summary>
    public required string Message { get; set; }
}
