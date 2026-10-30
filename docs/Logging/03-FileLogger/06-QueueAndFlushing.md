---
title: Queue and flushing
description: Choose what happens when the disk cannot keep up, know when the messages reach the disk, and how the logger handles errors.
---

## The queue

Logging a message formats it on the calling thread, then puts it in a bounded queue. A dedicated background thread,
not a thread of the thread pool, takes the messages from the queue and writes them to the file.

<!-- ref:../../../tests/Meziantou.Extensions.Logging.FileLogger.Tests/FileLoggerProviderTests.cs;region=FileLogger_Queue;format=md-fenced-code-block;dedent=true -->
```csharp
options.MaxQueueLength = 10_000;
options.QueueFullMode = FileLoggerQueueFullMode.DropOldest; // Never block the threads that log
options.FlushInterval = TimeSpan.FromSeconds(5);
```
<!-- endref -->

| Option | Default | Description |
| --- | --- | --- |
| [MaxQueueLength](cref:Meziantou.Extensions.Logging.FileLoggerOptions.MaxQueueLength) | `1024` | The number of messages the queue holds |
| [QueueFullMode](cref:Meziantou.Extensions.Logging.FileLoggerOptions.QueueFullMode) | [Wait](cref:Meziantou.Extensions.Logging.FileLoggerQueueFullMode.Wait) | What to do with a new message when the queue is full |
| [FlushInterval](cref:Meziantou.Extensions.Logging.FileLoggerOptions.FlushInterval) | 1 second | The longest time between two flushes while messages keep coming |

| [QueueFullMode](cref:Meziantou.Extensions.Logging.FileLoggerOptions.QueueFullMode) | When the queue is full |
| --- | --- |
| [Wait](cref:Meziantou.Extensions.Logging.FileLoggerQueueFullMode.Wait) | The thread that logs waits until the queue has room. No message is lost. |
| [DropWrite](cref:Meziantou.Extensions.Logging.FileLoggerQueueFullMode.DropWrite) | The new message is dropped. |
| [DropOldest](cref:Meziantou.Extensions.Logging.FileLoggerQueueFullMode.DropOldest) | The oldest queued message is dropped to make room for the new one. |

[Wait](cref:Meziantou.Extensions.Logging.FileLoggerQueueFullMode.Wait) slows the application down to the speed of the disk when it logs faster than the disk writes. Choose a drop mode
when the application must never wait for its logs, and accept that some messages are lost under load.

## When the messages reach the disk

- The pending messages are flushed as soon as the queue is empty, so in normal operation they reach the file right
  after they are logged.
- When messages keep coming and the queue is never empty, they are flushed at least every [FlushInterval](cref:Meziantou.Extensions.Logging.FileLoggerOptions.FlushInterval).
- [FlushAsync](cref:Meziantou.Extensions.Logging.FileLoggerProvider.FlushAsync) waits until the messages logged before the call are written. Use it before reading the file, as in
  tests.
- [Dispose](cref:Meziantou.Extensions.Logging.FileLoggerProvider.Dispose) and [DisposeAsync](cref:Meziantou.Extensions.Logging.FileLoggerProvider.DisposeAsync) write all the queued messages, then close the file. Dispose the provider when the
  application stops; a host does it for the providers it creates.

Prefer waiting with [FlushAsync](cref:Meziantou.Extensions.Logging.FileLoggerProvider.FlushAsync), or disposing the provider, over waiting a fixed time: the background thread can be
delayed for a long time on a busy machine.

## Errors

Logging never throws because of the file:

- When the first log file cannot be created, for instance because the directory is not writable, a warning is written
  to the standard error stream when the provider is created, [LogFilePath](cref:Meziantou.Extensions.Logging.FileLoggerProvider.LogFilePath) is `null`, and the provider drops every
  message for the rest of its lifetime.
- When a message cannot be written later, the message is lost, and a warning is written to the standard error stream.
  The warning is written once, and not again until a write succeeds, so a disk that stays unavailable does not flood
  the standard error stream.
- When the next file cannot be created as the file is rolled, the messages are dropped until the provider manages to
  create a file: it tries again a few seconds later, so the logging resumes when the directory becomes writable again.
