---
title: Handling errors
description: Catch or test for an invalid CODEOWNERS file, read the kind and the location of the error, and parse a span.
---

## Catch the exception

`Parse` throws a `CodeOwnersParseException` when the file is invalid. Its `Error` says what is wrong and where, and its
message describes both:

<!-- ref:../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs;region=CodeOwners_ParseException;format=md-fenced-code-block;dedent=true -->
```csharp
var ex = Assert.Throws<CodeOwnersParseException>(() => CodeOwnersFile.Parse("[Section\n* @user1", CodeOwnersDialect.GitLab));
Assert.Equal(CodeOwnersParseErrorKind.UnterminatedSectionHeader, ex.Error.Kind);
Assert.Equal(1, ex.Error.LineNumber);
Assert.Equal(1, ex.Error.LinePosition);
Assert.Equal("The CODEOWNERS file is invalid at line 1, position 1: the section header is not terminated by ']'", ex.Message);
```
<!-- endref -->

Parsing stops at the first error, so a file with several errors reports the first one. Fix it and parse again to find the
next one.

## Test without throwing

Use `TryParse` when an invalid file is expected, for instance in a tool that checks pull requests. The overload with a
`CodeOwnersParseError` reports the same error as the exception, without allocating one:

<!-- ref:../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs;region=CodeOwners_TryParse;format=md-fenced-code-block;dedent=true -->
```csharp
var content = "[Section\n* @user1";
if (CodeOwnersFile.TryParse(content, CodeOwnersDialect.GitLab, out CodeOwnersFile? file, out CodeOwnersParseError error))
{
    Assert.Fail("The content is not a valid GitLab CODEOWNERS file");
}
else
{
    Assert.Null(file);
    Assert.Equal("line 1, position 1: the section header is not terminated by ']'", error.ToString());
}
```
<!-- endref -->

When `TryParse` returns `false`, the file is `null`: no method returns a partially parsed file. When it returns `true`,
the error is `default`, whose `Kind` is `None`.

## Read the error

`CodeOwnersParseError` has the `Kind` of the error and its location. `LineNumber` and `LinePosition` start at 1, and
`ToString` returns them with a description:

<!-- ref:../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs;region=CodeOwners_InvalidOwner;format=md-fenced-code-block;dedent=true -->
```csharp
var isValid = CodeOwnersFile.TryParse("*.md @docs-team docs-team", CodeOwnersDialect.GitHub, out _, out CodeOwnersParseError error);

// An owner must start with '@' or be an email address
Assert.False(isValid);
Assert.Equal(CodeOwnersParseErrorKind.InvalidOwner, error.Kind);
Assert.Equal(1, error.LineNumber);
Assert.Equal(17, error.LinePosition);
```
<!-- endref -->

| `CodeOwnersParseErrorKind` | Cause |
| --- | --- |
| `UnterminatedSectionHeader` | A section header has no closing `]` on its line (GitLab only) |
| `UnterminatedRequiredReviewerCount` | A required reviewer count, as in `[Name][2]`, has no closing `]` (GitLab only) |
| `InvalidRequiredReviewerCount` | A required reviewer count is not a positive integer (GitLab only) |
| `EmptyOwner` | An owner is a lone `@` |
| `InvalidOwner` | An owner is neither `@name` nor an email address, a username contains `@`, or a role is unknown |

The section errors only exist in the GitLab dialect: with `CodeOwnersDialect.GitHub`, a line that starts with `[` is a
pattern. The lines end with `\n`, `\r\n` or `\r`, and the location counts all of them.

## Parse a span

`Parse` and both `TryParse` overloads also accept a `ReadOnlySpan<char>`, so content read into a buffer does not have to
be turned into a `string` first:

<!-- ref:../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs;region=CodeOwners_ParseSpan;format=md-fenced-code-block;dedent=true -->
```csharp
var buffer = new char[4096];
using var reader = new StringReader("*.md @docs-team"); // For instance, File.OpenText("CODEOWNERS")
var length = reader.ReadBlock(buffer);

CodeOwnersFile file = CodeOwnersFile.Parse(buffer.AsSpan(0, length), CodeOwnersDialect.GitHub);
Assert.Equal("*.md", file.Entries[0].Pattern);
```
<!-- endref -->

The parsed `CodeOwnersFile` never references the span: every value it exposes is copied out of it, so the buffer can be
reused. Error positions are relative to the start of the span.
