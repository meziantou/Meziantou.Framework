using Meziantou.Framework.Tds.Handler;

namespace Meziantou.Framework.Tds.QueryEngine;

/// <summary>
/// Evaluates whether the current query request can access a query-engine resource.
/// </summary>
/// <param name="context">The current query request context.</param>
/// <param name="resourceKind">The resource kind being accessed.</param>
/// <param name="resourceName">The resource name being accessed.</param>
/// <returns><see langword="true"/> when access is allowed; otherwise, <see langword="false"/>.</returns>
public delegate bool TdsQueryEngineAuthorizationHandler(TdsQueryContext context, TdsQueryEngineResourceKind resourceKind, string resourceName);
