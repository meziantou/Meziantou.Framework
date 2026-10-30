---
title: Input and pipes
description: Write text, bytes, a file or a stream to the standard input of a process, and connect processes together.
---

## Write to the standard input

<!-- ref:../../samples/Meziantou.Framework.ProcessWrapper.Samples/ReadmeExamples.cs;region=ProcessWrapper_InputStream;format=md-fenced-code-block;dedent=true -->
```csharp
// Pipe a string to stdin
var result = await ProcessWrapper.Create("cat")
    .WithInputStream(InputSource.FromText("Hello, World!"))
    .ExecuteBufferedAsync();

Console.WriteLine(result.Output.ToString());

// Pipe a file to stdin
var fileResult = await ProcessWrapper.Create("cat")
    .WithInputStream(InputSource.FromFile("input.txt"))
    .ExecuteBufferedAsync();

Console.WriteLine(fileResult.Output.ToString());

// Pipe from a TextReader
using var reader = new StringReader("Hello from reader");
var readerResult = await ProcessWrapper.Create("cat")
    .WithInputStream(InputSource.FromTextReader(reader))
    .ExecuteBufferedAsync();

Console.WriteLine(readerResult.Output.ToString());

// Pipe one process output to another process input
var pipe = new ProcessPipe(maxBufferSize: 256 * 1024);

var downstream = ProcessWrapper.Create("process-b")
    .WithInputStream(pipe)
    .ExecuteAsync();

await ProcessWrapper.Create("process-a")
    .WithArguments("--generate-data")
    .AddOutputStream(pipe)
    .ExecuteAsync();

await downstream;
```
<!-- endref -->

[WithInputStream](cref:Meziantou.Framework.ProcessWrapper.WithInputStream) takes an [InputSource](cref:Meziantou.Framework.InputSource):

| Source | Created with |
| --- | --- |
| A string, encoded in UTF-8 | [InputSource.FromText](cref:Meziantou.Framework.InputSource.FromText), or the string |
| Bytes | [InputSource.FromBytes](cref:Meziantou.Framework.InputSource.FromBytes), or a `byte[]` or `ReadOnlyMemory<byte>` |
| A file | [InputSource.FromFile](cref:Meziantou.Framework.InputSource.FromFile) |
| A `Stream` | [InputSource.FromStream](cref:Meziantou.Framework.InputSource.FromStream), or the stream |
| A `TextReader`, encoded in UTF-8 | [InputSource.FromTextReader](cref:Meziantou.Framework.InputSource.FromTextReader), or the reader |
| The output of another process | [InputSource.FromProcessPipe](cref:Meziantou.Framework.InputSource.FromProcessPipe), or a [ProcessPipe](cref:Meziantou.Framework.ProcessPipe) |

The standard input is closed once the whole source is written, so a process that reads until the end of its input
exits. Without [WithInputStream](cref:Meziantou.Framework.ProcessWrapper.WithInputStream), the standard input is not redirected.

## Connect two processes

A [ProcessPipe](cref:Meziantou.Framework.ProcessPipe) connects the output of one process to the input of another one, as in the last example above. Add the
pipe as an output target of the first process, and as the input source of the second one. Start the reading process
first, or at the same time: the pipe buffers the data written in advance, up to `maxBufferSize` bytes (64 KB by
default), then waits for the reader to consume it.

## The pipe operator

For a chain of commands, the `|` operator does the wiring:

<!-- ref:../../samples/Meziantou.Framework.ProcessWrapper.Samples/ReadmeExamples.cs;region=ProcessWrapper_PipeOperator;format=md-fenced-code-block;dedent=true -->
```csharp
var result = await (ProcessWrapper.Create("process-a")
    .WithArguments("--generate-data")
    | ProcessWrapper.Create("process-b")
    | ProcessWrapper.Create("process-c"))
    .ExecuteBufferedAsync();

// Buffered output corresponds to the last command (process-c)
foreach (var line in result.Output.StandardOutput)
{
    Console.WriteLine(line.Text);
}
```
<!-- endref -->

- The standard output of each command goes to the standard input of the next one.
- [ExecuteAsync](cref:Meziantou.Framework.ProcessWrapper.ExecuteAsync) and [ExecuteBufferedAsync](cref:Meziantou.Framework.ProcessWrapper.ExecuteBufferedAsync) on the pipeline start every command and return the result of the last one.
  With [ExecuteBufferedAsync](cref:Meziantou.Framework.ProcessWrapper.ExecuteBufferedAsync), the output is the output of the last command.
- The validation of every command applies: the pipeline fails when any command fails. See
  [Exit codes and errors](06-ExitCodesAndErrors.md).
