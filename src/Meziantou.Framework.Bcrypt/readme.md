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

- `2` (`$2$`, legacy parsing only)
- `2a` (`$2a$`)
- `2b` (`$2b$`)
- `2x` (`$2x$`)
- `2y` (`$2y$`)

BCrypt processes passwords as UTF-8 and uses at most 72 bytes.