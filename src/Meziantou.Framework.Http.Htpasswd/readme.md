# Meziantou.Framework.Http.Htpasswd

This package parses Apache htpasswd files and verifies credentials.

Supported password formats:

- bcrypt (`$2a$`, `$2b$`, `$2y$`)
- Apache MD5 (`$apr1$`)
- MD5 crypt (`$1$`)
- SHA-256 crypt (`$5$`)
- SHA-512 crypt (`$6$`)
- SHA-1 (`{SHA}`)
- plaintext (opt-in, see [Plaintext passwords](#plaintext-passwords))

`{SHA}` and plaintext entries are unsalted, so they are only there to read existing files. Use bcrypt for
new ones.

```csharp
var htpasswd = HtpasswdFile.Parse("""
        alice:$2y$10$Q8mPjALzMV90Q6MlA4b9MOB7f1ehD6A0eTlM2P6xnQKibD4xWgRSO
        bob:{SHA}W6ph5Mm5Pz8GgiULbPgzG37mj9g=
        """);

var isAliceValid = htpasswd.VerifyCredentials("alice", "password");
var isBobValid = htpasswd.VerifyCredentials("bob", "password");
```

## Plaintext passwords

An entry whose format is not recognized is rejected. Pass `allowPlaintextPasswords: true` to compare it against the
supplied password as plaintext instead:

```csharp
var htpasswd = HtpasswdFile.Parse("alice:password", allowPlaintextPasswords: true);
```

Enabling this also makes hashes the library does not implement, such as traditional DES crypt (`htpasswd -d`), match
when the stored hash itself is supplied as the password. Leave it disabled unless the file really does contain
plaintext passwords.
