# Meziantou.Framework.Http.Htpasswd

This package parses Apache htpasswd files and verifies credentials.

`VerifyCredentials` rejects any password longer than 1024 characters, because the cost of the crypt algorithms
grows quadratically with the length of the supplied password.

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

<!-- ref:../../tests/Meziantou.Framework.Http.Htpasswd.Tests/HtpasswdFileTests.cs;region=HtpasswdFile_VerifyCredentials;format=md-fenced-code-block;dedent=true -->
```csharp
var htpasswd = HtpasswdFile.Parse("""
        alice:$2y$10$bXuOalpYagnenorrJU65m.z5VzYstr4rBZl2WUHNwhGjItODNoDrG
        bob:{SHA}W6ph5Mm5Pz8GgiULbPgzG37mj9g=
        """);

var isAliceValid = htpasswd.VerifyCredentials("alice", "password");
var isBobValid = htpasswd.VerifyCredentials("bob", "password");
Assert.True(isAliceValid);
Assert.True(isBobValid);
```
<!-- endref -->

## Plaintext passwords

An entry whose format is not recognized is rejected. Pass `allowPlaintextPasswords: true` to compare it against the
supplied password as plaintext instead:

<!-- ref:../../tests/Meziantou.Framework.Http.Htpasswd.Tests/HtpasswdFileTests.cs;region=HtpasswdFile_PlaintextPasswords;format=md-fenced-code-block;dedent=true -->
```csharp
var htpasswd = HtpasswdFile.Parse("alice:password", allowPlaintextPasswords: true);
Assert.True(htpasswd.VerifyCredentials("alice", "password"));
```
<!-- endref -->

Enabling this also makes hashes the library does not implement, such as traditional DES crypt (`htpasswd -d`), match
when the stored hash itself is supplied as the password. Leave it disabled unless the file really does contain
plaintext passwords.
