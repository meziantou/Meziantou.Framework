---
title: Security
description: Restrict who can read the log files, and keep untrusted data from forging log entries.
---

## Permissions of the log files

On Unix, the log files are created with the default mode of the platform, which usually lets every local user read
them. Set [UnixCreateMode](cref:Meziantou.Extensions.Logging.FileLoggerOptions.UnixCreateMode) when the messages can contain sensitive data:

<!-- ref:../../../tests/Meziantou.Extensions.Logging.FileLogger.Tests/FileLoggerProviderTests.cs;region=FileLogger_UnixCreateMode;format=md-fenced-code-block;dedent=true -->
```csharp
options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
```
<!-- endref -->

- The value is ignored on Windows, and the umask of the process still applies.
- The mode of the directory is not changed. Create the directory yourself, with the permissions you need, when it must
  be private too.

## Untrusted data in the messages

The text format writes the messages as they are, so a value that contains a line break produces several lines. When the
value comes from an untrusted source, such as a request, it can forge entries that look exactly like real ones:

<!-- ref:../../../tests/Meziantou.Extensions.Logging.FileLogger.Tests/FileLoggerProviderTests.cs;region=FileLogger_UntrustedData;format=md-fenced-code-block;dedent=true -->
```csharp
logger.LogInformation("User said: {Text}", "hello\n[2020-01-01 00:00:00.000] [CRIT] [Security] admin login succeeded");
```
<!-- endref -->

Set [EscapeControlCharacters](cref:Meziantou.Extensions.Logging.FileLoggerOptions.EscapeControlCharacters) to `true` to write the control characters of the messages, the scopes and the exceptions
as escape sequences: `\n`, `\r`, `\t`, `\\` for a backslash, and `\uXXXX` for the others. An entry then always spans a
single line, including the stack trace of its exception, which is harder to read.

The [JSON format](05-MessageFormat.md#the-json-format) is not affected: the JSON writer already escapes the control
characters.
