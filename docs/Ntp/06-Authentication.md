---
title: Authentication
description: Authenticate the requests and the replies with a symmetric key shared by the client and the server.
---

Without authentication, an attacker who can read and inject packets on the path to the server can choose the time the
client reports (see [Response validation](05-ResponseValidation.md#what-the-checks-do-not-protect-against)). A symmetric
key shared by the client and the server prevents it: each packet ends with a message authentication code (MAC), computed
from the packet and the key, and the other side ignores the packets whose MAC is not valid. This is the symmetric key
authentication of RFC 5905 and RFC 8573, which ntpd and chrony support.

## Share a key

<!-- ref:../../tests/Meziantou.Framework.NtpServer.Tests/NtpServerTests.cs;region=NtpServer_Authentication;format=md-fenced-code-block;dedent=true -->
```csharp
// The same key on both sides: a line "1 AES128CMAC 000102030405060708090a0b0c0d0e0f" of an ntpd or chrony keys file
var key = new NtpAuthenticationKey(
    keyId: 1,
    NtpAuthenticationAlgorithm.AesCmac,
    Convert.FromHexString("000102030405060708090a0b0c0d0e0f"));

var serverOptions = new NtpServerOptions
{
    Port = 0,
    BindAddress = IPAddress.Loopback,
    RequireAuthentication = true, // ignore the requests without a valid MAC
};
serverOptions.AuthenticationKeys.Add(key);

using var server = new NtpServer(serverOptions);
await server.StartAsync();

var client = new NtpClient("127.0.0.1", new NtpClientOptions
{
    Port = server.Port,
    AuthenticationKey = key, // ignore the replies without a valid MAC
});
var response = await client.QueryAsync();
```
<!-- endref -->

An [NtpAuthenticationKey](cref:Meziantou.Framework.Ntp.NtpAuthenticationKey) has an identifier, sent in every packet so that the server knows which key to use, an algorithm
and the secret key. They must be the same on both sides. With ntpd and chrony, they are a line of the keys file: the
identifier, the algorithm, and the key in hexadecimal.

| Algorithm | Key | Use |
| --- | --- | --- |
| [AesCmac](cref:Meziantou.Framework.Ntp.NtpAuthenticationAlgorithm.AesCmac) | 16 bytes | AES-128-CMAC (RFC 8573, `AES128CMAC` in ntpd and chrony). Prefer it. |
| [Sha1](cref:Meziantou.Framework.Ntp.NtpAuthenticationAlgorithm.Sha1) | Any length | SHA-1 of the key and the packet (`SHA1`), for servers that do not support AES-CMAC. |
| [Md5](cref:Meziantou.Framework.Ntp.NtpAuthenticationAlgorithm.Md5) | Any length | MD5 of the key and the packet (`MD5`). RFC 8573 deprecates it: use it only with a server that supports nothing else. |

Generate keys with a cryptographic random number generator, such as `RandomNumberGenerator.GetBytes(16)`, and keep them
secret: anyone who has the key can forge the time.

## The client

With [AuthenticationKey](cref:Meziantou.Framework.Ntp.NtpClientOptions.AuthenticationKey) set, the client adds a MAC to its requests, and ignores the replies that do not end with a valid
MAC computed with the same key, even with [ValidateResponse](cref:Meziantou.Framework.Ntp.NtpClientOptions.ValidateResponse) set to `false`. As for other invalid replies, it keeps waiting for
a valid one until [Timeout](cref:Meziantou.Framework.Ntp.NtpClientOptions.Timeout), so a forged reply cannot make the query fail.

A server that cannot authenticate the request, because it does not know the key identifier or the MAC is not valid,
answers with a crypto-NAK. The client ignores it too, since anyone could forge it, and the query ends with a
`TimeoutException` whose message names the crypto-NAK: check that the key identifier, the algorithm and the key are the
same on both sides.

## The server

| Option | Default | Use |
| --- | --- | --- |
| [AuthenticationKeys](cref:Meziantou.Framework.Ntp.NtpServerOptions.AuthenticationKeys) | Empty | The keys the server accepts. Their identifiers must be unique. |
| [RequireAuthentication](cref:Meziantou.Framework.Ntp.NtpServerOptions.RequireAuthentication) | `false` | Ignore the requests without a MAC. |

- A request with a valid MAC gets a reply authenticated with the same key.
- A request with an unknown key identifier or a MAC that is not valid gets a crypto-NAK, which carries no time.
- A request without a MAC gets an unauthenticated reply, or nothing when [RequireAuthentication](cref:Meziantou.Framework.Ntp.NtpServerOptions.RequireAuthentication) is `true`.

The rate limit applies before authentication, so a flood of forged requests does not cost the server a MAC computation
each.

## Network Time Security (NTS)

NTS (RFC 8915) authenticates the server with a TLS certificate, without a shared key. It is not implemented: it derives
its keys from the TLS session (RFC 5705 key export), which `SslStream` does not expose.
