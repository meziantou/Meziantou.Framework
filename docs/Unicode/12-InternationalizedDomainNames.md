---
title: Internationalized domain names
description: Convert domain names with non-ASCII characters to their ASCII form and back, and encode Punycode, the same way on every platform.
---

## Convert a domain name

A domain name with non-ASCII characters, such as `bücher.example`, is sent to DNS in an ASCII form whose labels start
with `xn--`. `UnicodeIdna` implements Unicode IDNA Compatibility Processing
([UTS #46](https://www.unicode.org/reports/tr46/)) and Punycode ([RFC 3492](https://www.rfc-editor.org/rfc/rfc3492)):

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_Idna;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.True(UnicodeIdna.TryToAscii("B\u00FCcher.example", out var ascii));
Assert.Equal("xn--bcher-kva.example", ascii);

Assert.Equal("b\u00FCcher.example", UnicodeIdna.ToUnicode("xn--bcher-kva.example", out var hasErrors));
Assert.False(hasErrors);

// The flags of UTS #46: the defaults enable every check, with nontransitional processing
var options = new UnicodeIdnaOptions { UseStd3AsciiRules = false, VerifyDnsLength = false };
Assert.True(UnicodeIdna.TryToAscii("exa_mple.com", options, out ascii));
Assert.Equal("exa_mple.com", ascii);

Assert.True(UnicodeIdna.TryEncodePunycode("b\u00FCcher", out var encoded));
Assert.Equal("bcher-kva", encoded);
Assert.True(UnicodeIdna.TryDecodePunycode("bcher-kva", out var decoded));
Assert.Equal("b\u00FCcher", decoded);
```
<!-- endref -->

- `TryToAscii` returns `false` when the name is not valid; use it before looking up a name or comparing names.
- `ToUnicode` always gives a result, to display a name, and tells whether it found errors.
- Both map the name first: uppercase letters become lowercase, and the full-width forms and other variants become their
  usual characters.
- `TryEncodePunycode` and `TryDecodePunycode` convert one label, without the `xn--` prefix.

Unlike `System.Globalization.IdnMapping`, the conversion does not depend on the globalization library of the platform:
the results are the same everywhere, including in globalization-invariant mode.

## Options

The default options are the ones of the standard for registering and looking up a name: every check is enabled, with
nontransitional processing.

| `UnicodeIdnaOptions` | Default | Description |
| --- | --- | --- |
| `CheckHyphens` | `true` | A label cannot start or end with a hyphen, or have hyphens in its third and fourth positions |
| `CheckBidi` | `true` | The labels of a name that contains right-to-left characters must satisfy the Bidi Rule of RFC 5893 |
| `CheckJoiners` | `true` | The zero-width joiner and non-joiner must satisfy the ContextJ rules of RFC 5892 |
| `UseStd3AsciiRules` | `true` | The only ASCII characters a label accepts are the lowercase letters, the digits and the hyphen |
| `TransitionalProcessing` | `false` | Map the deviation characters, such as `ß`, as in IDNA2003; the standard deprecates it |
| `VerifyDnsLength` | `true` | `TryToAscii` rejects a name or a label that is empty or too long for DNS |
| `IgnoreInvalidPunycode` | `false` | Keep a label that starts with `xn--` but is not valid Punycode as it is, instead of reporting an error |

Turn off `UseStd3AsciiRules` and `VerifyDnsLength` for host names that are not DNS names, such as names with an
underscore.
