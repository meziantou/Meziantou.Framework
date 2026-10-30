---
title: Securing a Basic endpoint
description: Use HTTPS, rate limiting and lockout to protect an HTTP Basic endpoint against eavesdropping and password guessing.
---

HTTP Basic is simple for clients, and just as simple for attackers. Check these points before exposing an endpoint.

## Use HTTPS

The credentials travel with every request, readable by anyone who sees the traffic. Do not expose a Basic endpoint
over plain HTTP, except on the loopback interface. The [service defaults](../03-ServiceDefaults/01-Overview.md) redirect
HTTP to HTTPS and enable HSTS.

## Rate limit the endpoint

There is no session, no CSRF token and no interactive step, so an attacker can try passwords as fast as the server
answers. Put the endpoint behind the rate limiting middleware of ASP.NET Core
(`AddRateLimiter`, `UseRateLimiter`, `RequireRateLimiting`).

Rate limiting also protects the CPU. The credentials are validated from scratch on every request, and with
ASP.NET Core Identity that means hashing the password each time: a single client can use a large share of the
server's CPU.

## Lock out accounts, or not

With ASP.NET Core Identity, `lockoutOnFailure: true` stops the guessing on an account after the configured number of
failures, but lets anyone lock an account on purpose. Choose it, or rate limiting, or both; doing neither leaves an
unthrottled password oracle. See [Account lockout](03-AspNetCoreIdentity.md#account-lockout).

## Compare secrets safely

In your own `ValidateCredentials` delegate, do not store plain passwords: compare a hash, for instance with
`PasswordHasher<TUser>` or the key derivation functions of .NET. The handler answers every rejection with the same
`401 Unauthorized`, so returning `null` for an unknown user and for a wrong password does not tell the client which one
was wrong.

## Limit the payload

`MaxCredentialLength` (4096 characters by default) bounds the size of the Base64 payload before it is decoded. Lower it
when your usernames and passwords are short.

## Log carefully

The username comes from the client: escape it before writing it to a log, and never log the password or the
`Authorization` header.
