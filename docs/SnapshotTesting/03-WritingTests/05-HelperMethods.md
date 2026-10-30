---
title: Helper methods
description: Wrap Snapshot.Validate in your own assertion methods and keep the snapshots next to the tests.
---

Wrap [Snapshot.Validate](cref:Meziantou.Framework.SnapshotTesting.Snapshot.Validate) in a helper method to share settings or preparation between tests. Forward the caller
information, so that the snapshots are stored next to the test file rather than next to the helper:

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Tests/SnapshotTests.cs;region=Snapshot_HelperMethod;format=md-fenced-code-block;dedent=true -->
```csharp
public static class ApiSnapshot
{
    public static void ValidateOpenApiSpec(
        string spec,
        [CallerFilePath] string? filePath = null,
        [CallerLineNumber] int lineNumber = -1)
    {
        var settings = SnapshotSettings.Default with { /* shared configuration */ };
        Snapshot.Validate(spec, "yaml", settings, filePath, lineNumber);
    }
}
```
<!-- endref -->

In a test:

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Tests/SnapshotTests.cs;region=Snapshot_HelperMethodUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var spec = """
    openapi: 3.1.0
    info:
      title: Sample API
    """;

ApiSnapshot.ValidateOpenApiSpec(spec);
// => __snapshots__/SnapshotTests_ReadmeExample_HelperMethod.verified.yaml
```
<!-- endref -->

No custom naming or path strategy is needed.

## How the test is found

- **The name** of the snapshot is the name of the test, not of the helper. The test is read from the test framework
  (xUnit v3, NUnit, TUnit) or, when the framework exposes nothing (xUnit v2, MSTest), by walking the call stack until a
  method with a test attribute is found: `[Fact]`, `[Theory]`, `[Test]`, `[TestMethod]`, or an attribute that derives
  from one of them, such as `[DataTestMethod]` or `[SkippableFact]`.
- **The folder** comes from `[CallerFilePath]`, which is the file that declares the helper unless the helper forwards it.
- **The line** given by `[CallerLineNumber]` numbers the calls of a test. Forward it, so that a test that calls the helper
  several times gets one snapshot per call (see [Several assertions in one test](04-NamingSnapshots.md#several-assertions-in-one-test)).
  Otherwise, give each call its own name with [Snapshot.TestContext](cref:Meziantou.Framework.SnapshotTesting.Snapshot.TestContext).

## Asynchronous helpers

A helper can be `async` and await before calling [Snapshot.Validate](cref:Meziantou.Framework.SnapshotTesting.Snapshot.Validate) when the test framework exposes the running test.
Under xUnit v2 and MSTest, the test method must still be on the call stack: await inside the helper after the call to
[Snapshot.Validate](cref:Meziantou.Framework.SnapshotTesting.Snapshot.Validate), not before.
