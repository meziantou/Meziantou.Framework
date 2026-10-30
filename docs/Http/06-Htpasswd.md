---
title: Htpasswd files
description: Parse an Apache htpasswd file and verify a user name and a password against it, with bcrypt, MD5, SHA crypt and SHA-1 hashes.
---

An htpasswd file stores user names and password hashes, one `<user>:<hash>` entry per line. It is the format of Apache
HTTP basic authentication. `HtpasswdFile`, in the `Meziantou.Framework.Http.Htpasswd` package, reads it and verifies
credentials.

```bash
dotnet add package Meziantou.Framework.Http.Htpasswd
```

## Verify credentials

Parse the content of the file, then call `VerifyCredentials` with the user name and the password to check:

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

`VerifyCredentials` returns `false` for an unknown user, a wrong password, and a password longer than 1024 characters:
the cost of the crypt algorithms grows quadratically with the length of the password, so a long one supplied by a
client could tie up the server.

## Load a file

`LoadAsync` reads a file, which is decoded as UTF-8 unless it starts with a byte order mark:

<!-- ref:../../tests/Meziantou.Framework.Http.Htpasswd.Tests/HtpasswdFileTests.cs;region=HtpasswdFile_LoadAsync;format=md-fenced-code-block;dedent=true -->
```csharp
var htpasswd = await HtpasswdFile.LoadAsync(path, cancellationToken);
Assert.True(htpasswd.VerifyCredentials("alice", "password"));
```
<!-- endref -->

Another overload reads a `TextReader`, and `Parse` also accepts a `ReadOnlySpan<char>`. The file is read once:
reload it to see later changes.

## How the file is read

<!-- ref:../../tests/Meziantou.Framework.Http.Htpasswd.Tests/HtpasswdFileTests.cs;region=HtpasswdFile_FileFormat;format=md-fenced-code-block;dedent=true -->
```csharp
var htpasswd = HtpasswdFile.Parse("""
    # One entry per line: <username>:<hash>
    alice:{SHA}W6ph5Mm5Pz8GgiULbPgzG37mj9g=

    a line without a separator
    alice:{SHA}5en6G6MezRroT3XKqkdPOmY/BfQ=
    """);

// Comments, blank lines and malformed lines are skipped, and the first entry of a user wins
Assert.Equal(1, htpasswd.Count);
Assert.Equal(["alice"], htpasswd.Usernames);
Assert.True(htpasswd.VerifyCredentials("alice", "password"));

// User names are case-sensitive, and an unknown user is rejected like a wrong password
Assert.False(htpasswd.VerifyCredentials("Alice", "password"));
Assert.False(htpasswd.VerifyCredentials("bob", "password"));
```
<!-- endref -->

- Lines end with `\n`, `\r\n` or `\r`. Each line is trimmed, and empty lines and lines that start with `#` are
  skipped.
- The user name is what comes before the first `:`, and the hash what comes after it. A line without a user name or
  without a hash is skipped.
- When a user has several entries, the first one is used.
- User names are case-sensitive. `Usernames` lists them, and `Count` gives the number of entries.

## Supported hash formats

The format of each entry is detected from the prefix of its hash:

| Prefix | Format |
| --- | --- |
| `$2a$`, `$2b$`, `$2y$` | bcrypt |
| `$apr1$` | Apache MD5 |
| `$1$` | MD5 crypt |
| `$5$` | SHA-256 crypt |
| `$6$` | SHA-512 crypt |
| `{SHA}` | SHA-1 |

An entry of another format never matches a password. `{SHA}` hashes are not salted: support them to read existing
files, and use bcrypt for new entries.

A SHA-256 or SHA-512 crypt hash whose `rounds=` value is below 1,000 or above 10,000,000 never matches either, so a
file cannot make a verification take hours. The hashes are compared in constant time.

## Plaintext passwords

An entry whose hash has no recognized prefix is rejected by default. Pass `allowPlaintextPasswords: true` to compare it
with the supplied password as plaintext instead:

<!-- ref:../../tests/Meziantou.Framework.Http.Htpasswd.Tests/HtpasswdFileTests.cs;region=HtpasswdFile_PlaintextPasswords;format=md-fenced-code-block;dedent=true -->
```csharp
var htpasswd = HtpasswdFile.Parse("alice:password", allowPlaintextPasswords: true);
Assert.True(htpasswd.VerifyCredentials("alice", "password"));
```
<!-- endref -->

This also applies to the hash formats the library does not implement, such as the traditional DES crypt
(`htpasswd -d`): the stored hash itself would then be accepted as the password. Leave the option off unless the file
really holds plaintext passwords. `AllowPlaintextPasswords` tells whether a parsed file has it on.
