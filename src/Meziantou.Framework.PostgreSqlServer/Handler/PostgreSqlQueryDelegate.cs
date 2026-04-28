namespace Meziantou.Framework.PostgreSql.Handler;

/// <summary>Delegate used to process a PostgreSQL query request.</summary>
public delegate ValueTask<PostgreSqlQueryResult> PostgreSqlQueryDelegate(PostgreSqlQueryContext context, CancellationToken cancellationToken);
