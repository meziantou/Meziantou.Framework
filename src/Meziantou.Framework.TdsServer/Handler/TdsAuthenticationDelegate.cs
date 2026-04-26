namespace Meziantou.Framework.Tds.Handler;

/// <summary>Delegate used to authenticate an incoming TDS login request.</summary>
public delegate ValueTask<TdsAuthenticationResult> TdsAuthenticationDelegate(TdsAuthenticationContext context, CancellationToken cancellationToken);
