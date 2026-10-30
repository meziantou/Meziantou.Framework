---
title: Reading the output
description: Stream the output of a process line by line, collect it, write it to a stream, and choose its encoding.
---

## Send the output somewhere

[WithOutputStream](cref:Meziantou.Framework.ProcessWrapper.WithOutputStream) and [WithErrorStream](cref:Meziantou.Framework.ProcessWrapper.WithErrorStream) choose where standard output and standard error go while the process runs:

<!-- ref:../../samples/Meziantou.Framework.ProcessWrapper.Samples/ReadmeExamples.cs;region=ProcessWrapper_OutputHandling;format=md-fenced-code-block;dedent=true -->
```csharp
// Stream output line by line
await ProcessWrapper.Create("dotnet")
    .WithArguments("build")
    .WithOutputStream(OutputTarget.ToTextDelegate(line => Console.WriteLine($"[OUT] {line}")))
    .WithErrorStream(OutputTarget.ToTextDelegate(line => Console.Error.WriteLine($"[ERR] {line}")))
    .ExecuteAsync();

// Collect output into a StringBuilder
var sb = new StringBuilder();
await ProcessWrapper.Create("dotnet")
    .WithArguments("build")
    .WithOutputStream(sb)
    .ExecuteAsync();

Console.WriteLine(sb.ToString());

// Stream lines to a TextWriter
using var writer = new StringWriter();
await ProcessWrapper.Create("dotnet")
    .WithArguments("--version")
    .WithOutputStream(OutputTarget.ToTextWriter(writer))
    .ExecuteAsync();

// Collect into a ProcessOutputCollection
var output = new ProcessOutputCollection();
await ProcessWrapper.Create("dotnet")
    .WithArguments("build")
    .WithOutputStream(output)
    .WithErrorStream(output)
    .ExecuteAsync();

foreach (var line in output.StandardError)
{
    Console.Error.WriteLine($"[{line.TimestampUtc:O}] {line.Text}");
}

// Capture raw bytes from stdout/stderr
await using var stdout = File.Create("stdout.bin");
await using var stderr = File.Create("stderr.bin");

await ProcessWrapper.Create("my-command")
    .WithOutputStream(stdout)
    .WithErrorStream(stderr)
    .ExecuteAsync();

// Text and binary handlers can be combined on the same stream
await using var rawOutput = File.Create("raw-output.bin");
await ProcessWrapper.Create("dotnet")
    .WithArguments("--version")
    .WithOutputStream(rawOutput)
    .AddOutputStream(OutputTarget.ToTextDelegate(line => Console.WriteLine($"Version line: {line}")))
    .ExecuteAsync();

// Capture raw bytes using a delegate
await ProcessWrapper.Create("dotnet")
    .WithArguments("--version")
    .WithOutputStream(OutputTarget.ToBytesDelegate(bytes => Console.WriteLine($"Chunk size: {bytes.Length}")))
    .ExecuteAsync();

// Override stdout/stderr decoding when process output is not UTF-8
await ProcessWrapper.Create("my-command")
    .WithOutputEncoding(Encoding.Latin1)
    .WithErrorEncoding(Encoding.Latin1)
    .WithOutputStream(OutputTarget.ToTextDelegate(line => Console.WriteLine(line)))
    .WithErrorStream(OutputTarget.ToTextDelegate(line => Console.Error.WriteLine(line)))
    .ExecuteAsync();
```
<!-- endref -->

The targets are [OutputTarget](cref:Meziantou.Framework.OutputTarget) values. Create them with its static methods, or pass the object directly, which converts
implicitly:

| Target | Created with | Receives |
| --- | --- | --- |
| A callback | [OutputTarget.ToTextDelegate](cref:Meziantou.Framework.OutputTarget.ToTextDelegate), or an `Action<string>` | Each line, without its line break |
| A `TextWriter` | [OutputTarget.ToTextWriter](cref:Meziantou.Framework.OutputTarget.ToTextWriter), or the writer | Each line, written with `WriteLine` |
| A [ProcessOutputCollection](cref:Meziantou.Framework.ProcessOutputCollection) | [OutputTarget.ToProcessOutputCollection](cref:Meziantou.Framework.OutputTarget.ToProcessOutputCollection), or the collection | Each line, with its stream and the time it was received |
| A `StringBuilder` | [OutputTarget.ToStringBuilder](cref:Meziantou.Framework.OutputTarget.ToStringBuilder), or the builder | The decoded text, line breaks included |
| A `Stream` | [OutputTarget.ToStream](cref:Meziantou.Framework.OutputTarget.ToStream), or the stream | The raw bytes |
| A byte callback | [OutputTarget.ToBytesDelegate](cref:Meziantou.Framework.OutputTarget.ToBytesDelegate) | Each chunk of raw bytes |
| A [ProcessPipe](cref:Meziantou.Framework.ProcessPipe) | [OutputTarget.ToProcessPipe](cref:Meziantou.Framework.OutputTarget.ToProcessPipe), or the pipe | The raw bytes, for another process: see [Input and pipes](05-InputAndPipes.md) |

- `With*` replaces the targets of the stream, and `Add*` ([AddOutputStream](cref:Meziantou.Framework.ProcessWrapper.AddOutputStream), [AddErrorStream](cref:Meziantou.Framework.ProcessWrapper.AddErrorStream)) adds targets to the
  existing ones. Both accept several targets at once.
- Text and byte targets can be combined on the same stream: each one receives the whole output.
- A stream that has no target is not redirected: the process writes to the console of the current process.

Several processes can write to the same [ProcessOutputCollection](cref:Meziantou.Framework.ProcessOutputCollection), `StringBuilder`, `TextWriter` or `Stream`: the
writes are synchronized.

## Capture everything

[ExecuteBufferedAsync](cref:Meziantou.Framework.ProcessWrapper.ExecuteBufferedAsync) adds a [ProcessOutputCollection](cref:Meziantou.Framework.ProcessOutputCollection) that receives both streams, and returns it in
`result.Output` (see [Getting started](02-GettingStarted.md)). The targets you configured still receive the output.

[ProcessOutputCollection](cref:Meziantou.Framework.ProcessOutputCollection) is thread-safe. Enumerating it, or its [StandardOutput](cref:Meziantou.Framework.ProcessOutputCollection.StandardOutput) and [StandardError](cref:Meziantou.Framework.ProcessOutputCollection.StandardError) properties,
enumerates a snapshot. Each [ProcessOutput](cref:Meziantou.Framework.ProcessOutput) has a [Type](cref:Meziantou.Framework.ProcessOutput.Type) ([StandardOutput](cref:Meziantou.Framework.ProcessOutputCollection.StandardOutput) or [StandardError](cref:Meziantou.Framework.ProcessOutputCollection.StandardError)), a [Text](cref:Meziantou.Framework.ProcessOutput.Text) and a
[TimestampUtc](cref:Meziantou.Framework.ProcessOutput.TimestampUtc), and deconstructs into `(type, text)` or `(type, text, timestampUtc)`. `ToString` on the collection
returns all the lines, one per line.

## Encoding

The output is decoded as UTF-8. Use [WithOutputEncoding](cref:Meziantou.Framework.ProcessWrapper.WithOutputEncoding) and [WithErrorEncoding](cref:Meziantou.Framework.ProcessWrapper.WithErrorEncoding) for a process that writes another
encoding, as in the last example above. The encoding only matters to the text targets: byte targets receive the bytes
as the process wrote them.

When a process writes a last line without a line break, the line targets still receive it when the stream closes.
