# Meziantou.Framework.InlineSnapshotTesting

`Meziantou.Framework.InlineSnapshotTesting` is a snapshot testing library that simplifies the assertion of complex data models and documents. It is inspired by [Verify](https://github.com/VerifyTests/Verify).

`InlineSnapshot` is called on the test result during the assertion phase. It serializes that result and update the expected value. On the next test execution, the result is again serialized and compared to the existing value. The test will fail if the two snapshots do not match: either the change is unexpected, or the reference snapshot needs to be updated to the new result.

On the development machine, a diff tool prompt to compare the expected snapshot with the current snapshot. So, you can accept the new value or cancel. So, you can quickly iterate on your code and update snapshots.

Blog post: [Inline Snapshot testing in .NET](https://www.meziantou.net/inline-snapshot-testing-in-dotnet.htm)

# Getting Started

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

# Documentation

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

## Serializer

By default, `InlineSnapshot` uses the [`HumanReadableSerializer`](https://www.nuget.org/packages/Meziantou.Framework.HumanReadableSerializer) to serialize the object. This is the recommended serializer for most cases. However, you can provide your own serializer if needed.

````c#
// Configure the HumanReadableSerializer
InlineSnapshot.CreateBuilder()
    .WithSerializer(options => options.PropertyOrder = StringComparer.Ordinal)
    .Validate(data);
````

````c#
// Use System.Text.Json
InlineSnapshot.CreateBuilder()
    .WithSerializer(new JsonSnapshotSerializer())
    .Validate(data);
````

If you use Verify and want to use the same serializer, you can use the `Meziantou.Framework.InlineSnapshotTesting.Serializers.Argon` package.

````c#
InlineSnapshot.CreateBuilder()
    .WithSerializer(new ArgonSnapshotSerializer())
    .Validate(data);
````

### HumanReadableSerializer

The HumanReadableSerializer has many options to make the snapshot deterministic and easy to read. You can scrub values, scrub lines, show invisible characters, and more.

- Ordering properties: Recent versions of .NET have a deterministic order of properties. However, .NET Framework does not have a deterministic order. You can use the `PropertyOrder` option to order the properties alphabetically:

    ````c#
    InlineSnapshot
        .WithSerializer(options =>
        {
            options.PropertyOrder = StringComparer.Ordinal;
            options.DictionaryKeyOrder = StringComparer.Ordinal;
        })
        .Validate(...);
    ````

- Formatting content

    ````c#
    InlineSnapshot
        .WithSerializer(options =>
        {
            options.AddJsonFormatter(new JsonFormatterOptions
            {
                OrderProperties = true,
                WriteIndented = true,
                FormatAsStandardObject = false,
            });

            options.AddXmlFormatter(new XmlFormatterOptions
            {
                OrderAttributes = true,
                WriteIndented = true,
            });

            options.AddHtmlFormatter(new HtmlFormatterOptions
            {
                OrderAttributes = true,
                AttributeQuote = HtmlAttributeQuote.DoubleQuote,
                RedactContentSecurityPolicyNonce = true,
            });

            options.AddUrlEncodedFormFormatter(new UrlEncodedFormFormatterOptions
            {
                OrderProperties = true,
                UnescapeValues = true,
                PrettyFormat = true,

            });
        })
        .Validate(...);
    ````

- Ignore members

    ````c#
    InlineSnapshot
        .WithSerializer(options =>
        {
            options.IgnoreMember<TestClass>(x => x.Property);
            options.IgnoreMember<TestClass>(x => new { x.Property1, x.Property2 });
            options.IgnoreMembersWithType<int>(); // ignore all properties of type int

            // ignore properties that throw an exception when accessed
            options.IgnoreMembersThatThrow();
            options.IgnoreMembersThatThrow<NotImplementedException>();
        })
        .Validate(...);
    ````

- Ignoring null/default values

    ````c#
    InlineSnapshot
        .WithSerializer(options => options.DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingDefault)
        .Validate(...);
    ````

## Diff tool

When a snapshot is updated, a diff tool is used to compare the expected value and the new value. By default, it uses one of the following tools
- The diff tool configured by the `DiffEngine_Tool` environment variable
- The merge tool from the local git configuration
- The diff tool from the local git configuration
- The diff tool from the current IDE (support VS Code, VS, Rider)
- The first available diff tool (rely on [DiffEngine](https://github.com/VerifyTests/DiffEngine?tab=readme-ov-file#supported-tools))

You can disable the diff tool by setting the `DiffEngine_Disabled` environment variable.

## Using helper methods

If you want to use helper methods before calling `Validate()`, you need to decorate the methods with `[InlineSnapshotAssertion]` and use the `[CallerFilePath]` and `[CallerLineNumber]` attribute.

````c#
var instance = new { FirstName = "Gérald", LastName = "Barré" };
Helper(instance, ""); // This string will be updated

[InlineSnapshotAssertion(nameof(expected))] // name of the parameter that contains the snapshot
static void Helper(object data, string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
{
    InlineSnapshot
        .WithSerializer(options => options.ScrubValue<string>())
        .Validate(data, expected, filePath, lineNumber);
}
````

## Scrubbing

Some data are not deterministic and should not be part of the snapshot. You can scrub the data at two locations. First, you can scrub the data during serialization. You have access to the actual values which can be useful. Second, you can scrub the data after serialization.

````c#
var data = new string[] { "a", "a", "b" };
InlineSnapshot
    .WithSerializer(options => options.ScrubValue<string>())
    .Validate(data, """
        - String_0
        - String_0
        - String_1
    """);
````

````c#
var data = new string[] { "a", "A", "b" };
InlineSnapshot
    .WithSerializer(options => options.ScrubValue<string>(StringComparer.OrdinalIgnoreCase))
    .Validate(data, """
        - String_0
        - String_0
        - String_1
    """);
````

````c#
var data = new string[] { "a", "A", "b" };
InlineSnapshot
    .WithSerializer(options => options.ScrubValue<string>((value, index) => $"{value}_{index}", StringComparer.OrdinalIgnoreCase))
    .Validate(data, """
        - a_0
        - a_0
        - b_1
    """);
````

````c#
var data = new Guid[] { Guid.NewGuid(), Guid.NewGuid(), Guid.Empty };
InlineSnapshot
    .WithSerializer(options => options.ScrubGuid())
    .Validate(data, """
        - 00000000-0000-0000-0000-000000000001
        - 00000000-0000-0000-0000-000000000002
        - 00000000-0000-0000-0000-000000000000
    """);
````

````c#
var now = DateTime.UtcNow;
var data = now.AddSeconds(10);
InlineSnapshot
    .WithSerializer(options => options.UseRelativeDateTime(now))
    .Validate(data, "00:00:10"); // TimeSpan relative to the now variable
````

````c#
InlineSnapshot
    .WithSettings(settings => settings.ScrubLines(line => line.Contains("dummy")))
    .Validate("abc\ndummy", "abc");
````

````c#
InlineSnapshot
    .WithSettings(settings => settings.ScrubLinesContaining("dummy"))
    .Validate("abc\ndummy", "abc");
````

````c#
InlineSnapshot
    .WithSettings(settings => settings.ScrubLinesMatching("d.*y"))
    .Validate("abc\ndummy", "abc");
````

````c#
InlineSnapshot
    .WithSettings(settings => settings.ScrubLinesWithReplace(line => line.Replace("abc", "123")))
    .Validate("abcdef", "123def");
````

````c#
var data = JsonNode.Parse("""{ "prop": "value" }""");
InlineSnapshot
    .WithSettings(settings => settings.ScrubJsonValue("$.prop", node => "[redacted]"))
    .Validate(data, """
        {
          "prop": "[redacted]"
        }
        """);
````

````c#
var data = XDocument.Parse("""
                <root>
                  <item attr="1">test1</item>
                </root>
                """);
InlineSnapshot
    .WithSettings(settings => settings.ScrubXmlAttribute("//item/@attr", attribute => "[redacted]"))
    .Validate(data, """
        <root>
          <item attr="[redacted]">test1</item>
        </root>
        """);
````

## Invisible characters

If spaces or new lines are important, you can display them as visible characters.

````c#
InlineSnapshot
    .WithSerializer(options => options.ShowInvisibleCharactersInValues = true)
    .Validate("line 1\r\nline\t2", """
        line␠1␍␊
        line␉2
        """);
````

## String formats

By default, the snapshot can use string, verbatim string, or raw string. It uses the information from the PDB file to determine which C# features are available. You can override the default behavior by setting the `CSharpStringFormat` property.

````c#
InlineSnapshot
    .WithSerializer(options => options.AllowedStringFormats = CSharpStringFormats.Quoted | CSharpStringFormats.Verbatim | CSharpStringFormats.Raw)
    .Validate(...);
````

You can also change the indentation of raw strings:
- `CSharpStringFormats.Raw`: Same indentation as the calling method
- `CSharpStringFormats.LeftAlignedRaw`: Align the raw string to the left (first column)

````c#
InlineSnapshot
    .WithSerializer(options => options.AllowedStringFormats = CSharpStringFormats.Raw)
    .Validate(new object(), """
        {}
        """);
````

````c#
InlineSnapshot
    .WithSerializer(options => options.AllowedStringFormats = CSharpStringFormats.LeftAlignedRaw)
    .Validate(new object(), """
{}
""");
````

You can also change the indentation, end of line, and the encoding of the file if the default behavior does not suit your needs.

````c#
InlineSnapshot
    .WithSerializer(options => options.CSharpStringFormat = new CSharpStringFormat
    {
        Indentation = "    ",
        EndOfLine = "\r\n",
        FileEncoding = Encoding.UTF8,
    })
    .Validate(...);
````

## CI environment

When running in a CI environment, the snapshot is never updated. To detect CI environment, the library uses the environment variables created by the major CI tools (GitHub Actions, Azure Pipelines, TeamCity etc.).
You can disable this behavior by setting `AutoDetectContinuousEnvironment` to `false`.

````c#
InlineSnapshot
    .WithSettings(settings => settings.AutoDetectContinuousEnvironment = false)
    .Validate(...);
````

You can also set the strategy to `SnapshotUpdateStrategy.Disallow` to disable updating snapshots.

````c#
InlineSnapshot
    .WithSettings(settings => settings.SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow)
    .Validate(...);
````

## Snapshot update strategies

- `SnapshotUpdateStrategy.Overwrite`: Overwrite the snapshot with the new value
- `SnapshotUpdateStrategy.OverwriteWithoutFailure`: Overwrite the snapshot with the new value without failing the test
- `SnapshotUpdateStrategy.MergeTool`: Use a merge tool to compare the snapshot with the new value
- `SnapshotUpdateStrategy.MergeToolSync`: Use a merge tool to compare the snapshot with the new value and wait for the merge tool to close
- `SnapshotUpdateStrategy.Disallow`: Do not update the snapshot

## Recreate the snapshots

You can force the update of all snapshots by setting the `InlineSnapshotSettings.ForceUpdateSnapshots` property to `true` and setting the update strategy to `OverwriteWithoutFailure`.

````c#
InlineSnapshotSettings.Default = InlineSnapshotSettings.Default with
{
    ForceUpdateSnapshots = true, // Override the snapshot even if the value matches the expected value
    SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
};
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
