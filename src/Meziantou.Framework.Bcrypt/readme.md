# Meziantou.Framework.Bcrypt

Helpers to hash and verify passwords using BCrypt.

````c#
var hash = Bcrypt.HashPassword("my password");
var ok = Bcrypt.Verify("my password", hash);
````

You can select the BCrypt revision and cost (work factor):

````c#
var hash = Bcrypt.HashPassword(
    password: "my password",
    workFactor: 12,
    version: BcryptVersion.Revision2B);

var shouldRehash = Bcrypt.NeedsRehash(hash, workFactor: 13, version: BcryptVersion.Revision2B);
````

Supported revisions are:

- `2` (`$2$`) - verified only; salts cannot be generated
- `2a` (`$2a$`)
- `2b` (`$2b$`) - the default
- `2y` (`$2y$`) - identical to `$2b$`

`$2x$` is **not** supported. It exists only to reproduce the crypt_blowfish sign-extension bug, which
this library does not emulate, so hashing or verifying a `$2x$` hash throws `NotSupportedException`
rather than silently mis-verifying passwords that contain non-ASCII characters. `ParseHash` still
reads `$2x$` hashes so you can find them in an existing database and re-hash those passwords.

BCrypt processes passwords as UTF-8 and uses at most 72 bytes.