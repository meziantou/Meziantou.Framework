# Meziantou.Framework.Http.Htpasswd

This package parses Apache htpasswd files and verifies credentials.

Supported password formats:

- bcrypt (`$2a$`, `$2b$`, `$2y$`)
- Apache MD5 (`$apr1$`)
- SHA-256 crypt (`$5$`)
- SHA-512 crypt (`$6$`)
- SHA-1 (`{SHA}`)
- plaintext

```csharp
var htpasswd = HtpasswdFile.Parse("""
        alice:$2y$10$Q8mPjALzMV90Q6MlA4b9MOB7f1ehD6A0eTlM2P6xnQKibD4xWgRSO
        bob:{SHA}W6ph5Mm5Pz8GgiULbPgzG37mj9g=
        """);

var isAliceValid = htpasswd.VerifyCredentials("alice", "password");
var isBobValid = htpasswd.VerifyCredentials("bob", "password");
```