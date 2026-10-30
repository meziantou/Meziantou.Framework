---
title: Files, rolling and retention
description: Name the log files, start a new file every hour, day or month or at a size limit, keep the last files, and append to an existing file.
---

<!-- ref:../../../tests/Meziantou.Extensions.Logging.FileLogger.Tests/FileLoggerProviderTests.cs;region=FileLogger_RollingAndRetention;format=md-fenced-code-block;dedent=true -->
```csharp
var logsDirectory = Directory.CreateTempSubdirectory("logs").FullName;
var timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero));
var options = new FileLoggerOptions
{
    Directory = logsDirectory,
    FileNamePrefix = "app-",
    RollInterval = RollInterval.Daily,
    MaxRetainedFiles = 2,
    IncludeProcessIdInFileName = false,
};

await using (var provider = new FileLoggerProvider(options, timeProvider))
{
    var logger = provider.CreateLogger("Sample");
    for (var day = 0; day < 3; day++)
    {
        logger.LogInformation("Day {Day}", day);
        timeProvider.Advance(TimeSpan.FromDays(1));
    }
}

// One file per day; the file of the first day was deleted when the third one was created
var fileNames = Directory.GetFiles(logsDirectory).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal).ToArray();
Assert.Equal(["app-2024-01-03.log", "app-2024-01-04.log"], fileNames);
```
<!-- endref -->

## File names

A log file is named `{FileNamePrefix}{timestamp}-{processId}{FileNameExtension}`, for instance
`2024-01-02-1234.log`:

| Option | Default | Description |
| --- | --- | --- |
| `FileNamePrefix` | `""` | Text at the start of the name |
| `FileNameExtension` | `.log` | Extension of the name, including the dot |
| `IncludeProcessIdInFileName` | `true` | Add `-{processId}` after the timestamp |

The timestamp is the time at which the file is created, in UTC, with the precision of `RollInterval`: `yyyy-MM-dd-HH`
for `Hourly`, `yyyy-MM-dd` for `Daily`, `yyyy-MM` for `Monthly`, and `yyyy-MM-dd-HH-mm-ss` for `None`. As the timestamp
comes first, ordering the files by name orders them chronologically.

When the name is already used, for instance by another process or because the file was rolled on its size, a suffix is
added before the extension: `_001`, `_002`, and so on. Without `Append`, every file is new, so two providers never
write to the same file. A compressed file also gets the extension of its algorithm: see [Compression](04-Compression.md).

## Start a new file

| Option | Default | A new file is created |
| --- | --- | --- |
| `RollInterval` | `None` | When the hour, the day or the month changes (`Hourly`, `Daily`, `Monthly`) |
| `MaxFileSizeInBytes` | `null` (no limit) | When the next message would make the file bigger than the limit |

- The periods are in UTC. A message goes to the file of the period in which it was logged, even when the background
  thread writes it after the period ended.
- The size limit is checked before each message, so the files stay under the limit. A single message bigger than the
  limit is still written, alone in its file.
- Both options can be combined: a new file every day, and another one when the file of the day is full.

## Keep the last files

`MaxRetainedFiles` deletes the oldest files each time a new file is created, so that at most that many files remain.
The default is `null`: no file is deleted.

The retention policy only considers the files whose name matches the options of the provider: its prefix and
extension, with or without compression. When `IncludeProcessIdInFileName` is `true` (the default), it also only
considers the files that have the identifier of the current process in their name. That keeps it from deleting the
files that another process writes in the same directory, but also means **the files of the previous runs of the
application are never deleted**. Set `IncludeProcessIdInFileName` to `false`, as in the example above, to limit the
number of files across restarts.

The file being written is never deleted, and a file that cannot be deleted, for instance because another process uses
it, is left in place.

## Append to an existing file

By default, the provider creates a new file every time it starts. `Append` reuses an existing file instead:

<!-- ref:../../../tests/Meziantou.Extensions.Logging.FileLogger.Tests/FileLoggerProviderTests.cs;region=FileLogger_Append;format=md-fenced-code-block;dedent=true -->
```csharp
options.Append = true;
options.RollInterval = RollInterval.Daily;
options.IncludeProcessIdInFileName = false; // Required to reuse the file after a restart
```
<!-- endref -->

A file is reused only when its name is the one the provider computes when it starts, so the name must be stable:

- `RollInterval` must be set: with `RollInterval.None`, the name contains the seconds, so it changes on every start.
- `IncludeProcessIdInFileName` must be `false`, as the identifier of the process changes on every start.

With the default values of these two options, a new file is created on every start even when `Append` is `true`. A
file that another process is writing, or that already reached `MaxFileSizeInBytes`, is not reused either: the next name
is used. `Append` is ignored when the files are compressed as they are written.
