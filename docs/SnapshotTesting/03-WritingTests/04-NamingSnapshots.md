---
title: Naming snapshots
description: Several assertions in one test, parameterized tests, naming strategies, the test context and name collisions.
---

A snapshot is named after the test that validates it: `<Class>_<Test>` by default. The test class and method come from
the test framework when it exposes them (xUnit v3, NUnit and TUnit), and from the call stack otherwise (xUnit v2,
MSTest). A test declared in a base class is named after the class it ran in.

## Several assertions in one test

Each call to [Snapshot.Validate](cref:Meziantou.Framework.SnapshotTesting.Snapshot.Validate) in a test gets its own file. The first call keeps the name of the test, the next ones get
an ordinal suffix (`~2`, `~3`, ...):

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Tests/SnapshotTests.cs;region=Snapshot_SeveralAssertions;format=md-fenced-code-block;dedent=true -->
```csharp
var html = "<p>Hello</p>";
var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC");

Snapshot.Validate(html);                   // __snapshots__/SnapshotTests_ReadmeExample_SeveralAssertions.verified.txt
Snapshot.Validate(png, SnapshotType.Png);  // __snapshots__/SnapshotTests_ReadmeExample_SeveralAssertions~2.verified.png
```
<!-- endref -->

The calls are numbered by the line they are made from, in the order they first run, so they must run in a deterministic
order:

- Calls made from the same line, in a loop or through a helper that does not forward the caller information, share a
  name. Give each of them its own name with the [test context](#name-a-snapshot-yourself).
- A call that does not run renumbers the calls after it. With `if (OperatingSystem.IsWindows()) Snapshot.Validate(png);`
  followed by `Snapshot.Validate(text);`, the text snapshot is `Name~2.verified.txt` on Windows and `Name.verified.txt`
  elsewhere. Give such calls their own name with the test context instead.
- The numbers are kept for the lifetime of the test process, so that a test that runs again in the same process (a
  retried test, the cases of a parameterized NUnit fixture) gets the same names. In a long-lived host, such as continuous
  testing in a single process, a call added above an existing one gets the next number until the process restarts.

The suffix is only added by the built-in naming strategies: a custom strategy may already tell the calls apart.

## Parameterized tests

The name of a parameterized test is the method name followed by its arguments: `SampleTests_Render_1.5_True`.

- `null` is written as `null`. The string `"null"` is quoted, so its name gets a hash and differs from the name of
  `null`.
- Arrays and lists are written element by element: `SampleTests_Render_1_2_<hash>` for `new[] { 1, 2 }`.
- `DateTime`, `DateTimeOffset` and `TimeOnly` use the round-trip format (`O`), so values that only differ by a fraction
  of a second or by their kind get distinct names.
- A custom display name (`DisplayName`, [TestName](cref:Meziantou.Framework.SnapshotTesting.SnapshotTestContext.TestName)) is used as it is, including its `.` characters.
- The arguments of a parameterized NUnit fixture (`[TestFixture("en-US")]`) or of a TUnit test class (`[Arguments]` on
  the class) are appended to the name, so each instance of the class gets its own snapshot: `FormatTests_Format_en-US`.
  NUnit 3 does not expose them: its instances are reported as a name collision.

## Name a snapshot yourself

Set [Snapshot.TestContext](cref:Meziantou.Framework.SnapshotTesting.Snapshot.TestContext) to give a call a name of your choice. It is an `AsyncLocal<SnapshotTestContext?>`: the value
flows to the calls made by the current test, and must be reset when you are done:

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Tests/SnapshotTests.cs;region=Snapshot_TestContext;format=md-fenced-code-block;dedent=true -->
```csharp
var documents = new Dictionary<string, string>
{
    ["json"] = """{ "name": "John" }""",
    ["yaml"] = "name: John",
};

try
{
    foreach (var (format, content) in documents)
    {
        // Calls made from the same line share a name: give each one its own
        Snapshot.TestContext.Value = new SnapshotTestContext(TestName: "Export_" + format);
        Snapshot.Validate(content, format); // __snapshots__/SnapshotTests_Export_json.verified.json, ...
    }
}
finally
{
    Snapshot.TestContext.Value = null;
}
```
<!-- endref -->

[SnapshotTestContext](cref:Meziantou.Framework.SnapshotTesting.SnapshotTestContext) takes a [TestName](cref:Meziantou.Framework.SnapshotTesting.SnapshotTestContext.TestName) and optional [Metadata](cref:Meziantou.Framework.SnapshotTesting.SnapshotTestContext.Metadata). The test name replaces the name of the test method;
the class name is still added by the default naming strategy. [Metadata](cref:Meziantou.Framework.SnapshotTesting.SnapshotTestContext.Metadata) is not part of the file name: it is available to
custom naming and path strategies. [ClassName](cref:Meziantou.Framework.SnapshotTesting.SnapshotTestContext.ClassName) and [MethodName](cref:Meziantou.Framework.SnapshotTesting.SnapshotTestContext.MethodName) can also be set with an object initializer.

## Naming strategies

[SnapshotSettings.SnapshotNamingStrategy](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.SnapshotNamingStrategy) builds the name from the test:

| Strategy | Name |
| --- | --- |
| [SnapshotNamingStrategies.ClassName\_TestName](cref:Meziantou.Framework.SnapshotTesting.SnapshotNamingStrategies.ClassName_TestName) (default) | `SampleTests_ValidateUser` |
| [SnapshotNamingStrategies.FullName](cref:Meziantou.Framework.SnapshotTesting.SnapshotNamingStrategies.FullName) | `SampleTests.ValidateUser` |
| [SnapshotNamingStrategies.TestName](cref:Meziantou.Framework.SnapshotTesting.SnapshotNamingStrategies.TestName) | `ValidateUser` |

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Tests/SnapshotTests.cs;region=Snapshot_NamingStrategy;format=md-fenced-code-block;dedent=true -->
```csharp
var settings = SnapshotSettings.Default with
{
    SnapshotNamingStrategy = SnapshotNamingStrategies.FullName,
};

Snapshot.Validate("Hello", settings); // __snapshots__/SnapshotTests.DocumentationExample_NamingStrategy.verified.txt
```
<!-- endref -->

[TestName](cref:Meziantou.Framework.SnapshotTesting.SnapshotNamingStrategies.TestName) drops the class name: two classes of the same folder with a test of the same name then get the same
snapshot name, a [name collision](#name-collisions).

A custom strategy is a [SnapshotNamingStrategy](cref:Meziantou.Framework.SnapshotTesting.SnapshotNamingStrategy) delegate that receives a [SnapshotPathContext](cref:Meziantou.Framework.SnapshotTesting.SnapshotPathContext) and returns the name. Its
[ClassName](cref:Meziantou.Framework.SnapshotTesting.SnapshotPathContext.ClassName) and [MethodName](cref:Meziantou.Framework.SnapshotTesting.SnapshotPathContext.MethodName) come from the test framework when it exposes them, and from the call stack otherwise; the
stack walk is the most expensive part of an assertion, and only runs when the strategy reads a name the framework did not
provide. Use [MemberName](cref:Meziantou.Framework.SnapshotTesting.SnapshotPathContext.MemberName), the name of the method that called [Snapshot.Validate](cref:Meziantou.Framework.SnapshotTesting.Snapshot.Validate) as captured by the compiler, when it is
enough.

## Name collisions

Two tests that get the same snapshot name would overwrite each other's snapshot. When they run in the same process, the
second one fails with a [SnapshotException](cref:Meziantou.Framework.SnapshotTesting.SnapshotException) that names both tests. This happens, for instance, with:

- two tests that have the same display name;
- test cases whose arguments do not override `ToString`, or only differ by type (`1`, `1L` and `"1"` passed to an
  `object` parameter);
- test contexts that only differ by [Metadata](cref:Meziantou.Framework.SnapshotTesting.SnapshotTestContext.Metadata);
- names that only differ by case (`Case_A` and `Case_a` are the same file on Windows and macOS).

Give one of the tests a distinct name, for example with [Snapshot.TestContext](cref:Meziantou.Framework.SnapshotTesting.Snapshot.TestContext). Tests that explicitly set the same
[SnapshotTestContext.TestName](cref:Meziantou.Framework.SnapshotTesting.SnapshotTestContext.TestName) may share a snapshot. Collisions are only detected with the default path strategy and a
built-in naming strategy.

An assertion that produces several files (`Name_0`, `Name_1`) cannot tell whether a `Name_2` file is one it produced in
an earlier run or the snapshot of a test named `Name_2`: such a file is reported as unexpected, but never deleted.

## Names from earlier versions

Earlier versions truncated a display name at its last `.`, named arrays after their type, formatted dates without their
fraction of a second and ignored the arguments of the test class. The snapshot files created with those names keep being
used as long as they exist and no other test uses the name; delete them to switch to the new names.
