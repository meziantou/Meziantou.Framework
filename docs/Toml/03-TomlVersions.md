---
title: TOML 1.0 and 1.1
description: Which TOML versions the library reads and writes, and how to produce files that TOML 1.0 readers accept.
---

The library reads TOML 1.1 and writes documents that TOML 1.0 readers accept. There is no option to choose a version:

- **Reading.** Every document is read and validated with the TOML 1.1 rules. TOML 1.1 accepts every valid TOML 1.0
  document, so TOML 1.0 files are read too.
- **Writing.** The output uses no syntax that TOML 1.1 added, so tools that only know TOML 1.0 can read the files you
  write. The one exception is an option you turn on yourself, described below.

## Read TOML 1.1 documents

TOML 1.1 lets an inline table span several lines and end with a comma, adds the `\e` and `\xHH` escapes, and makes the
seconds of a time optional. The library reads all of it, and writes the same values with the TOML 1.0 syntax:

<!-- ref:../../tests/Meziantou.Framework.Toml.Tests/Toml11ParsingTests.cs;region=TomlSerializer_Toml11;format=md-fenced-code-block;dedent=true -->
```csharp
var document = TomlSerializer.Deserialize<TomlTable>("""
    # TOML 1.1: an inline table can span lines and end with a comma
    server = {
      host = "localhost",
      port = 8080,
    }
    title = "\e[1mMy App\e[0m" # the TOML 1.1 \e escape
    start = 07:30 # the seconds are optional
    """)!;

var server = (TomlTable)document["server"];
Assert.Equal(8080L, server["port"]);
Assert.Equal("\u001B[1mMy App\u001B[0m", document["title"]);
Assert.Equal("07:30:00", document["start"].ToString());

// The output uses no TOML 1.1 syntax, so TOML 1.0 readers can read it
Assert.Equal("""
    server = {host = "localhost", port = 8080}
    title = "\u001B[1mMy App\u001B[0m"
    start = 07:30:00

    """, TomlSerializer.Serialize(document));
```
<!-- endref -->

## Write the TOML 1.1 escapes

By default, control characters in strings and keys are written with `\uXXXX` escapes. Set
`TomlStringStylePreferences.AllowHexEscapes` to use the shorter TOML 1.1 escapes, `\e` and `\xHH`, when every reader of
your files supports TOML 1.1:

<!-- ref:../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlStringStylePreferences_AllowHexEscapes;format=md-fenced-code-block;dedent=true -->
```csharp
var value = new Dictionary<string, string> { ["prompt"] = "\u001B[1m>\u001B[0m" };

// By default, control characters use \uXXXX escapes, which TOML 1.0 readers accept
Assert.Equal("""
    prompt = "\u001B[1m>\u001B[0m"

    """, TomlSerializer.Serialize(value));

// The TOML 1.1 escapes are shorter, but TOML 1.0 readers reject them
var options = new TomlSerializerOptions
{
    StringStylePreferences = new TomlStringStylePreferences { AllowHexEscapes = true },
};
Assert.Equal("""
    prompt = "\e[1m>\e[0m"

    """, TomlSerializer.Serialize(value, options));
```
<!-- endref -->

`[TomlStringStyle(..., AllowHexEscapes = TomlBooleanPreference.True)]` sets it for one member. See
[Options](04-Serialization/03-Options.md#string-styles).

## Values that .NET cannot hold

Values are read into .NET types, so a few documents that TOML accepts are rejected. Every parser of the library reports
them as errors:

- A float that overflows a `double`, such as `1e400`, is an error rather than infinity.
- A leap second (`23:59:60`), the year `0000`, an offset further from UTC than ±14:00, and an offset date-time whose UTC
  instant is before the year 1 or after 9999 are errors, because `DateTime` and `DateTimeOffset` cannot hold them. The
  message says which limit applies.
- An integer is a signed 64-bit value, as TOML defines it: a `ulong`, `nuint`, `Int128` or `UInt128` member must hold a
  value within the range of `long` to be written.

## Read TOML 1.0 only

To reject the TOML 1.1 syntax, for example to check that a file works with a tool that only reads TOML 1.0, use the
`Meziantou.Framework.Language.Toml` package: its syntax tree reads TOML 1.0 on request.
