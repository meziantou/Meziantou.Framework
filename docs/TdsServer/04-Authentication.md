---
title: Authentication
description: Accept or reject logins, attach the identity of the user to the connection, and return SQL Server-style login errors.
---

Every connection starts with a login. The server calls the authentication handler with what the client sent, and the
handler accepts or rejects the connection:

<!-- ref:../../tests/Meziantou.Framework.TdsServer.Tests/TdsServerProtocolTests.cs;region=TdsServer_Authentication;format=md-fenced-code-block;dedent=true -->
```csharp
TdsAuthenticationDelegate authenticate = (context, cancellationToken) =>
{
    if (context.UserName != "app" || context.Password != "Password123!")
    {
        return ValueTask.FromResult(TdsAuthenticationResult.Fail($"Login failed for user '{context.UserName}'."));
    }

    var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "app"), new Claim(ClaimTypes.Role, "reader")], authenticationType: "password");
    return ValueTask.FromResult(TdsAuthenticationResult.Success(database: "Sales", userContext: new ClaimsPrincipal(identity)));
};

TdsQueryDelegate query = (context, cancellationToken) =>
{
    var user = context.UserContext; // the ClaimsPrincipal returned by the authentication handler

    var resultSet = new TdsResultSet();
    resultSet.Columns.Add(new TdsColumn("UserName", TdsColumnType.NVarChar));
    resultSet.Columns.Add(new TdsColumn("IsReader", TdsColumnType.Boolean));
    resultSet.Rows.Add([user?.Identity?.Name, user?.IsInRole("reader") ?? false]);

    var result = new TdsQueryResult();
    result.ResultSets.Add(resultSet);
    return ValueTask.FromResult(result);
};
```
<!-- endref -->

With these callbacks, a client that logs in as `app` gets `Sales` as its current database (`SqlConnection.Database`),
and its queries return `app` and `true`. A wrong password makes `SqlConnection.Open` throw a `SqlException` with the
number 18456 and the message `Login failed for user 'app'.`

## What the handler receives

[TdsAuthenticationContext](cref:Meziantou.Framework.Tds.Handler.TdsAuthenticationContext) holds the login sent by the client:

| Property | Value |
| --- | --- |
| [UserName](cref:Meziantou.Framework.Tds.Handler.TdsAuthenticationContext.UserName), `Password` | The SQL login (`User ID` and `Password` in the connection string), or `null` when missing |
| `Database` | The requested database (`Initial Catalog` or `Database`), or `null` |
| [ApplicationName](cref:Meziantou.Framework.Tds.Handler.TdsAuthenticationContext.ApplicationName) | The `Application Name` of the connection string, or the default name of the client |
| [RemoteEndPoint](cref:Meziantou.Framework.Tds.Handler.TdsAuthenticationContext.RemoteEndPoint) | The address and port of the client |
| [AuthenticationToken](cref:Meziantou.Framework.Tds.Handler.TdsAuthenticationContext.AuthenticationToken) | The SSPI data of the login packet, Base64-encoded, or `null` when the client sent none |
| [IsIntegratedSecurity](cref:Meziantou.Framework.Tds.Handler.TdsAuthenticationContext.IsIntegratedSecurity) | `true` when the client asks for Windows authentication (`Integrated Security=true`) |
| [SspiToken](cref:Meziantou.Framework.Tds.Handler.TdsAuthenticationContext.SspiToken) | The SSPI token of the current round of the [SSPI exchange](#integrated-windows-authentication-sspi) |
| [Items](cref:Meziantou.Framework.Tds.Handler.TdsAuthenticationContext.Items) | State kept across the rounds of the SSPI exchange |

Microsoft Entra ID authentication (`Authentication=Active Directory …`) is not supported.

Without TLS, the password travels with the obfuscation of the TDS protocol only, which anyone on the network can
reverse. See [Encryption](09-Encryption.md).

## Accept a login

[TdsAuthenticationResult.Success](cref:Meziantou.Framework.Tds.Handler.TdsAuthenticationResult.Success) accepts the connection. Both of its parameters are optional:

- `database`: the database reported to the client after the login.
- `userContext`: a `ClaimsPrincipal` that represents the user. The server attaches it to the connection, and every
  query of the connection gets it in [TdsQueryContext.UserContext](cref:Meziantou.Framework.Tds.Handler.TdsQueryContext.UserContext). The [query engine](07-QueryEngine.md) passes it to
  the query roots, the stored procedures and the authorization callback, so you can filter data per user.

## Reject a login

`TdsAuthenticationResult.Fail(message)` rejects the connection with a SQL Server-style error: the client throws a
`SqlException` with that message, and the server closes the connection. The error number defaults to 18456, the
number SQL Server uses for failed logins, the state to 1 and the class to 14; pass `errorNumber`, `errorState` and
`errorClass` to change them.

The handler runs for every connection, before its first query: keep it fast. An exception thrown by the handler
closes the connection without a login error, so the client only sees a broken connection: return [Fail](cref:Meziantou.Framework.Tds.Handler.TdsAuthenticationResult.Fail) instead. Its
`CancellationToken` is canceled when the server stops.

## Integrated Windows authentication (SSPI)

With `Integrated Security=true`, the client logs in with an SSPI token (NTLM or Kerberos) instead of a user name and a
password. The exchange takes several round trips, and the authentication handler takes part in each of them:

1. The server calls the handler with the first token of the client in
   [SspiToken](cref:Meziantou.Framework.Tds.Handler.TdsAuthenticationContext.SspiToken).
2. When the exchange needs another round, the handler returns
   [TdsAuthenticationResult.ContinueSspi](cref:Meziantou.Framework.Tds.Handler.TdsAuthenticationResult.ContinueSspi) with the token for the client. The server sends it, waits
   for the answer of the client, and calls the handler again with the same context, whose
   [SspiToken](cref:Meziantou.Framework.Tds.Handler.TdsAuthenticationContext.SspiToken) is now that answer.
3. The handler ends the exchange with [Success](cref:Meziantou.Framework.Tds.Handler.TdsAuthenticationResult.Success), which takes the last token of the exchange when there is
   one (Kerberos mutual authentication), or with [Fail](cref:Meziantou.Framework.Tds.Handler.TdsAuthenticationResult.Fail).

The server does not validate the tokens: the handler does, for instance with `NegotiateAuthentication`:

<!-- ref:../../samples/Meziantou.Framework.TdsServer.Samples/Examples.cs;region=TdsServer_IntegratedSecurity;format=md-fenced-code-block;dedent=true -->
```csharp
TdsAuthenticationDelegate authenticate = (context, cancellationToken) =>
{
    if (!context.IsIntegratedSecurity)
    {
        return ValueTask.FromResult(TdsAuthenticationResult.Fail("Use Integrated Security=true."));
    }

    // Every round of the exchange gets the same context: keep the state of the exchange in Items. The server
    // disposes the NegotiateAuthentication instance once the login completes.
    if (!context.Items.TryGetValue(typeof(NegotiateAuthentication), out var value) || value is not NegotiateAuthentication negotiate)
    {
        negotiate = new NegotiateAuthentication(new NegotiateAuthenticationServerOptions());
        context.Items[typeof(NegotiateAuthentication)] = negotiate;
    }

    var outgoingToken = negotiate.GetOutgoingBlob(context.SspiToken.Span, out var status);
    var result = status switch
    {
        NegotiateAuthenticationStatusCode.ContinueNeeded => TdsAuthenticationResult.ContinueSspi(outgoingToken),
        NegotiateAuthenticationStatusCode.Completed => TdsAuthenticationResult.Success(outgoingToken, userContext: new ClaimsPrincipal(negotiate.RemoteIdentity)),
        _ => TdsAuthenticationResult.Fail("Login failed."),
    };

    return ValueTask.FromResult(result);
};
```
<!-- endref -->

[Items](cref:Meziantou.Framework.Tds.Handler.TdsAuthenticationContext.Items) keeps the state of the exchange from one round to the next. The server disposes its
`IDisposable` values once the login succeeds or fails, including when the client disconnects in the middle of the
exchange. A login takes at most 16 rounds.
