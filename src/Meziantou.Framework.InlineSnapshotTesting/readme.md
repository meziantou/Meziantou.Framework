# Meziantou.Framework.InlineSnapshotTesting

`Meziantou.Framework.InlineSnapshotTesting` is a snapshot testing library that simplifies the assertion of complex data models and documents. It is inspired by [Verify](https://github.com/VerifyTests/Verify).

`InlineSnapshot` is called on the test result during the assertion phase. It serializes that result and update the expected value. On the next test execution, the result is again serialized and compared to the existing value. The test will fail if the two snapshots do not match: either the change is unexpected, or the reference snapshot needs to be updated to the new result.

On the development machine, a diff tool prompt to compare the expected snapshot with the current snapshot. So, you can accept the new value or cancel. So, you can quickly iterate on your code and update snapshots.

Blog post: [Inline Snapshot testing in .NET](https://www.meziantou.net/inline-snapshot-testing-in-dotnet.htm)

# How does it work

First, you can write a test with the following code:

````c#
var data = new
{
    FirstName = "Gérald",
    LastName = "Barré",
    NickName = "meziantou",
};

// No need to write the expected value
InlineSnapshot.Validate(data);
````

Then, run the tests. It will show you a diff tool where you can compare the expected value and the new value.
Once you accept the change, the source code is updated:

````c#
var data = new
{
    FirstName = "Gérald",
    LastName = "Barré",
    NickName = "meziantou",
};

InlineSnapshot.Validate(data, """
    FirstName: Gérald,
    LastName: Barré,
    NickName: meziantou
    """);
````

# Other features

## Configuration

You can configure the default behavior of `Validate()` by settings `InlineSnapshotSettings.Default`. In the case of unit tests, you may want to update the configuration before running tests. You can use a `ModuleInitializer` to do so.

````c#
static class AssemblyInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        InlineSnapshotSettings.Default = InlineSnapshotSettings.Default with
        {
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.MergeTool,
            MergeTools = [MergeTool.VisualStudioCode],
        };
    }
}
````

You can also set the configuration per assert:

````c#
// InlineSnapshotSettings is a record, so you can use the "with" keyword to create a new instance
var settings = InlineSnapshotSettings.Default with
{
    SnapshotUpdateStrategy = SnapshotUpdateStrategy.Overwrite,
};

InlineSnapshot.CreateBuilder()
    .WithSettings(settings)
    .Validate(data, "");
````

If you prefer, you can use the alternative syntax:

````c#
InlineSnapshot.CreateBuilder()
    .WithSettings(settings => settings.SnapshotUpdateStrategy = SnapshotUpdateStrategy.Overwrite)
    .Validate(data, "");
````

## Environment variables

- `DiffEngine_Tool`: Set the default merge tool
- `DiffEngine_Disabled`: Disable the diff tool even if set explicitly in the configuration

## Using helper methods

If you want to use helper methods before calling `Validate()`, you need to decorate the methods with `[InlineSnapshotAssertion]` and use `[CallerFilePath]` and `[CallerLineNumber]`.

````c#
Helper(""); // This string will be updated
            
[InlineSnapshotAssertion(nameof(expected))]
static void Helper(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
{
    InlineSnapshot.Validate(new object(), expected, filePath, lineNumber);
}
````

# Examples

- [Display invisible characters](https://github.com/meziantou/Meziantou.Framework/blob/main/Samples/Meziantou.Framework.InlineSnapshotTesting.Samples/DisplayInvisibleCharacters.cs)
- [Configure allowed C# string formats](https://github.com/meziantou/Meziantou.Framework/blob/main/Samples/Meziantou.Framework.InlineSnapshotTesting.Samples/ConfigureCSharpStringFormat.cs)
- [Indent Json snapshot](https://github.com/meziantou/Meziantou.Framework/blob/main/Samples/Meziantou.Framework.InlineSnapshotTesting.Samples/HttpContent_format_json_response.cs)
- [Reorder Json properties](https://github.com/meziantou/Meziantou.Framework/blob/main/Samples/Meziantou.Framework.InlineSnapshotTesting.Samples/Json_reorder_properties.cs)
- [Redact nonce in html response](https://github.com/meziantou/Meziantou.Framework/blob/main/Samples/Meziantou.Framework.InlineSnapshotTesting.Samples/HttpContent_redact_nonce.cs)
- [Scrub guid](https://github.com/meziantou/Meziantou.Framework/blob/main/Samples/Meziantou.Framework.InlineSnapshotTesting.Samples/Scrub_guid.cs)
- [Scrub lines containing a specific substring](https://github.com/meziantou/Meziantou.Framework/blob/main/Samples/Meziantou.Framework.InlineSnapshotTesting.Samples/Scrub_lines_containing_specific_text.cs)
- [Scrub lines matching a predicate](https://github.com/meziantou/Meziantou.Framework/blob/main/Samples/Meziantou.Framework.InlineSnapshotTesting.Samples/Scrub_lines_matching_a_predicate.cs)
- [Scrub lines matching a regex](https://github.com/meziantou/Meziantou.Framework/blob/main/Samples/Meziantou.Framework.InlineSnapshotTesting.Samples/Scrub_lines_matching_a_regex.cs)
- [Scrub lines with rewriting](https://github.com/meziantou/Meziantou.Framework/blob/main/Samples/Meziantou.Framework.InlineSnapshotTesting.Samples/Srub_replace_line_content.cs)
