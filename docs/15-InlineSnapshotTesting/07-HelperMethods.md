---
title: Helper methods
description: Write your own assertion methods that update the inline snapshot of their caller.
---

A helper method can call `InlineSnapshot.Validate` to share settings or preparation between tests. The snapshot to update
is then an argument of the call to the helper, not of the call to `Validate`. Tell the library where it is:

1. Decorate the helper with `[InlineSnapshotAssertion]`, and give it the name of the parameter that holds the snapshot.
2. Add `[CallerFilePath]` and `[CallerLineNumber]` parameters to the helper, and forward them to `Validate`.

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_HelperMethod;format=md-fenced-code-block;dedent=true -->
```csharp
var instance = new { FirstName = "Gérald", LastName = "Barré" };
Helper(instance, """
    FirstName: String_0
    LastName: String_1
    """); // This string is updated when the snapshot changes

[InlineSnapshotAssertion(nameof(expected))] // name of the parameter that contains the snapshot
static void Helper(object data, string expected, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1)
{
    InlineSnapshot
        .WithSerializer(options => options.ScrubValue<string>())
        .Validate(data, expected, filePath, lineNumber);
}
```
<!-- endref -->

When the snapshot changes, the library updates the `expected` argument of the call to `Helper` in the test.

Without the forwarded caller information, the location points inside the helper instead of its caller: the update fails
and asks you to add the parameters. The helper can be a local function, as above, or a regular method.
