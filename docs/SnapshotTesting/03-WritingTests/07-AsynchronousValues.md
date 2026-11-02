---
title: Asynchronous values
description: Validate a stream or an asynchronous sequence without blocking the test, and cancel the read.
---

[Snapshot.Validate](cref:Meziantou.Framework.SnapshotTesting.Snapshot.Validate) reads its value synchronously. A `Stream` that only supports asynchronous reads makes it
fail, and an `IAsyncEnumerable<T>` is enumerated by blocking the test until every item is produced.
[Snapshot.ValidateAsync](cref:Meziantou.Framework.SnapshotTesting.Snapshot.ValidateAsync) reads both asynchronously and accepts a `CancellationToken`. It takes the same type and
settings as [Snapshot.Validate](cref:Meziantou.Framework.SnapshotTesting.Snapshot.Validate), and the snapshot gets the same name.

## Validate a stream

The stream is read with `CopyToAsync` and stored as its bytes:

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Tests/SnapshotTests.cs;region=Snapshot_ValidateAsync;format=md-fenced-code-block;dedent=true -->
```csharp
await using var stream = new MemoryStream("Hello, world!"u8.ToArray());

// The stream is read with CopyToAsync, from its current position, and is neither rewound nor disposed
await Snapshot.ValidateAsync(stream, TestContext.Current.CancellationToken); // __snapshots__/SnapshotTests_DocumentationExample_ValidateAsync.verified.txt
```
<!-- endref -->

- The stream is read from its current position to its end. It is neither rewound nor disposed: a stream you have
  just written to must be rewound first, or the snapshot is empty.
- Once read, the content is validated like a `Stream` passed to [Snapshot.Validate](cref:Meziantou.Framework.SnapshotTesting.Snapshot.Validate), so a serializer registered
  for streams still applies.

## Validate an asynchronous sequence

The sequence is enumerated with `await foreach`, and the items are written like a list:

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Tests/SnapshotTests.cs;region=Snapshot_ValidateAsyncEnumerable;format=md-fenced-code-block;dedent=true -->
```csharp
static async IAsyncEnumerable<string> GetNamesAsync()
{
    foreach (var name in new[] { "John", "Jane" })
    {
        await Task.Yield(); // For instance, a page of results from a database or a web API
        yield return name;
    }
}

// The sequence is enumerated with await foreach instead of blocking the test
await Snapshot.ValidateAsync(GetNamesAsync(), TestContext.Current.CancellationToken); // __snapshots__/SnapshotTests_DocumentationExample_ValidateAsyncEnumerable.verified.txt
```
<!-- endref -->

- The snapshot is the same as the one [Snapshot.Validate](cref:Meziantou.Framework.SnapshotTesting.Snapshot.Validate) writes for the sequence, including for a sequence of
  `KeyValuePair<string, T>`, which is written like a dictionary.
- The serializers receive the items as a `List<T>`, so a serializer registered for `IAsyncEnumerable<T>` does not
  apply.
- Only the value you pass is enumerated asynchronously. An `IAsyncEnumerable<T>` in a property of an object is still
  enumerated by blocking.

## Completion and cancellation

- The value is read into memory before anything else happens. No snapshot file is read or written before the stream
  is fully read or the sequence fully enumerated.
- The task completes after the comparison and the update of the snapshot files, and fails with the same exception as
  [Snapshot.Validate](cref:Meziantou.Framework.SnapshotTesting.Snapshot.Validate) when the snapshot differs.
- Canceling the token while the value is read throws an `OperationCanceledException` and leaves the snapshot files
  untouched.
