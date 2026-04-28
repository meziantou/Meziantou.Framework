namespace Meziantou.Framework.PostgreSql.Handler;

/// <summary>Delegate used to authenticate an incoming PostgreSQL startup request.</summary>
public delegate ValueTask<PostgreSqlAuthenticationResult> PostgreSqlAuthenticationDelegate(PostgreSqlAuthenticationContext context, CancellationToken cancellationToken);
