---
title: Files and streams
description: Create valid file names, copy and delete folders reliably, read streams completely, and restrict what code can do with a stream.
---

## Create a valid file name

<!-- ref:../../tests/Meziantou.Framework.Tests/IOUtilitiesTests.cs;region=IOUtilities_ToValidFileName;format=md-fenced-code-block;dedent=true -->
```csharp
// Invalid characters are replaced by their code: '/' is 47
Assert.Equal("Q1_x47_Q2 report.txt", IOUtilities.ToValidFileName("Q1/Q2 report.txt"));

// Windows reserved device names are wrapped, even with an extension
Assert.Equal("_con.txt_", IOUtilities.ToValidFileName("con.txt"));

// Trailing periods and spaces, which Windows removes, are replaced too
Assert.Equal("draft_x46_", IOUtilities.ToValidFileName("draft."));
```
<!-- endref -->

[IOUtilities.ToValidFileName](cref:Meziantou.Framework.IOUtilities.ToValidFileName) turns any text, such as a title, into a file name:

- The characters that are invalid in a file name on the current operating system are replaced by `_x<code>_`, where
  `<code>` is the decimal code of the character. Windows forbids more characters than Linux and macOS, so the result
  depends on the operating system.
- Trailing periods and spaces are replaced the same way, because Windows silently removes them.
- The Windows reserved names (`con`, `prn`, `aux`, `nul`, `com0` to `com9` and `lpt0` to `lpt9`, in any case), with or
  without an extension, are wrapped in `_` characters, as are names made only of periods.

The two optional parameters change the formats used for reserved names (`_{0}_`) and characters (`_x{0}_`).

## Copy and delete folders

<!-- ref:../../tests/Meziantou.Framework.Tests/IOUtilitiesTests.cs;region=IOUtilities_CopyAndDelete;format=md-fenced-code-block;dedent=true -->
```csharp
var source = Directory.CreateTempSubdirectory();
var destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
var file = Path.Combine(source.FullName, "sub", "data.txt");

// Create the folder of a file
IOUtilities.PathCreateDirectory(file);
File.WriteAllText(file, "content");
File.SetAttributes(file, FileAttributes.ReadOnly);

// Copy a folder and its subfolders
IOUtilities.CopyDirectory(source.FullName, destination);
Assert.True(File.Exists(Path.Combine(destination, "sub", "data.txt")));

// Delete a folder recursively, read-only files included
IOUtilities.Delete(source);
IOUtilities.Delete(destination);
Assert.False(Directory.Exists(source.FullName));
Assert.False(Directory.Exists(destination));
```
<!-- endref -->

- [PathCreateDirectory](cref:Meziantou.Framework.IOUtilities.PathCreateDirectory) creates the folder that contains a file, and its parents, when they do not exist.
- [CopyDirectory](cref:Meziantou.Framework.IOUtilities.CopyDirectory) copies the files and subfolders of a folder. It throws a `DirectoryNotFoundException` when the source
  does not exist, and an `IOException` when a file already exists in the destination. A symbolic link to a folder is
  copied as a link, as `cp -R` does, instead of copying the content of its target. A link to a parent folder therefore
  does not make the copy recurse forever.
- [Delete](cref:Meziantou.Framework.IOUtilities.Delete) deletes a file, or a folder and its content, and does nothing when the path does not exist. It removes the
  read-only attribute that prevents deleting a file, makes read-only folders writable (on Linux and macOS, deleting a
  file requires write permission on its folder, as in the Go module cache), and retries when a file is temporarily locked by another process,
  such as an antivirus. Symbolic links and junctions are deleted as links: the content of their target is kept.
- [DeleteAsync](cref:Meziantou.Framework.IOUtilities.DeleteAsync) does the same and waits asynchronously between the retries.
- [PathUnprotect](cref:Meziantou.Framework.IOUtilities.PathUnprotect) removes the read-only attribute of a file, and [IsSharingViolation](cref:Meziantou.Framework.IOUtilities.IsSharingViolation) tells whether an `IOException` was
  caused by a file locked by another process.

## Read a stream completely

`Stream.Read` can return fewer bytes than requested, even before the end of the stream. The stream extensions loop
until they have what you asked for:

<!-- ref:../../tests/Meziantou.Framework.Tests/StreamExtensionsTests.cs;region=StreamExtensions_ReadAll;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = new MemoryStream([1, 2, 3, 4, 5]);

// Fill the buffer, unless the end of the stream is reached first
var header = new byte[2];
Assert.Equal(2, stream.TryReadAll(header));

// Read the rest of the stream
var rest = await stream.ReadToEndAsync();
Assert.Equal([3, 4, 5], rest);
```
<!-- endref -->

- [TryReadAll](cref:Meziantou.Framework.StreamExtensions.TryReadAll) fills the buffer, and returns the number of bytes read: fewer than the length of the buffer only when the
  end of the stream is reached first.
- [ReadToEnd](cref:Meziantou.Framework.StreamExtensions.ReadToEnd) and [ReadToEndAsync](cref:Meziantou.Framework.StreamExtensions.ReadToEndAsync) return the remaining bytes of the stream as an array. They read until the end of
  the stream, even when it grows while it is read, such as a log file being written. They return an empty array when
  the position is past the end.
- [ToMemoryStreamAsync](cref:Meziantou.Framework.StreamExtensions.ToMemoryStreamAsync) copies the remaining bytes to a new `MemoryStream`, positioned at its beginning.

## Restrict a stream

[RestrictedStream](cref:Meziantou.Framework.RestrictedStream) wraps a stream and only allows the operations you choose. Use it to give a stream to code you do not
control, or to test how your code behaves with a stream that cannot seek or returns small chunks:

<!-- ref:../../tests/Meziantou.Framework.Tests/RestrictedStreamTests.cs;region=RestrictedStream_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
using var inner = new MemoryStream([1, 2, 3, 4]);

// Give code a read-only, forward-only view of a stream, that returns at most 1 byte per read
using var stream = new RestrictedStream(inner, new RestrictedStreamOptions
{
    AllowReading = true,
    AllowSynchronousCalls = true,
    MaxReadLength = 1,
});

var buffer = new byte[4];
Assert.Equal(1, stream.Read(buffer));
Assert.False(stream.CanWrite);
Assert.False(stream.CanSeek);
Assert.Throws<NotSupportedException>(() => stream.Write([5]));
```
<!-- endref -->

| [RestrictedStreamOptions](cref:Meziantou.Framework.RestrictedStreamOptions) property | Allows |
| --- | --- |
| [AllowReading](cref:Meziantou.Framework.RestrictedStreamOptions.AllowReading) | Reading |
| [AllowWriting](cref:Meziantou.Framework.RestrictedStreamOptions.AllowWriting) | Writing |
| [AllowSeeking](cref:Meziantou.Framework.RestrictedStreamOptions.AllowSeeking) | Seeking, and getting or setting the position and the length |
| [AllowSynchronousCalls](cref:Meziantou.Framework.RestrictedStreamOptions.AllowSynchronousCalls) | The synchronous methods |
| [AllowAsynchronousCalls](cref:Meziantou.Framework.RestrictedStreamOptions.AllowAsynchronousCalls) | The asynchronous methods |
| [MaxReadLength](cref:Meziantou.Framework.RestrictedStreamOptions.MaxReadLength) | At most this number of bytes per read; 0 or less means no limit |

Every option is `false` by default. A forbidden operation throws a `NotSupportedException`, and `CanRead`, `CanWrite`
and `CanSeek` reflect the options and the wrapped stream. Changing the length requires both seeking and writing.
`Flush` and `FlushAsync` do nothing when writing is not allowed, as with a read-only `FileStream`, so wrappers such as
`BufferedStream` can flush the stream.
