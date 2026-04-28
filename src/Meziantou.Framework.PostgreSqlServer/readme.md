# Meziantou.Framework.PostgreSqlServer

`Meziantou.Framework.PostgreSqlServer` is a callback-based server library for accepting PostgreSQL protocol connections.

## Features

- Accepts PostgreSQL client connections over TCP
- Callback for authentication (cleartext, MD5, SCRAM-SHA-256)
- Callback for query handling (simple query and extended query flow)
- TLS negotiation (`SSLRequest`) on the same endpoint
- Query cancellation support (`CancelRequest`)
- ASP.NET Core hosting integration through `IHostApplicationBuilder`
