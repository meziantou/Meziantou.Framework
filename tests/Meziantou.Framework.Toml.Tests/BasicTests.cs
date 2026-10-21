using System;
using System.Collections.Generic;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Parsing;
using Meziantou.Framework.Toml.Serialization;
using Meziantou.Framework.Toml.Syntax;

namespace Meziantou.Framework.Toml.Tests;

#pragma warning disable CA1002 // The readme models use List<T> on purpose
#pragma warning disable CA1819 // The readme models use arrays on purpose
#pragma warning disable MA0048 // File name must match type name

public class BasicTests
{
    [Fact]
    public void TestTableArraysContainingPrimitiveArraysSerialize()
    {
        var test = @"[[table_array]]
primitive_list = [4, 5, 6]
";

        var model = TomlSerializer.Deserialize<TomlTable>(test);
        Assert.NotNull(model);
        var tomlOut = TomlSerializer.Serialize(model!);

        Assert.Equal(test.ReplaceLineEndings("\n"), tomlOut.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void TestHelloWorld()
    {
        var toml = @"global = ""this is a string""
# This is a comment of a table
[my_table]
key = 1 # Comment a key
value = true
 list = [4, 5, 6]
";

        var model = TomlSerializer.Deserialize<TomlTable>(toml);
        Assert.NotNull(model);
        var nonNullModel = model!;
        // Prints "this is a string"
        var global = nonNullModel["global"];
        TestContext.Current.TestOutputHelper?.WriteLine($"found global = \"{global}\"");
        Assert.Equal("this is a string", global);
        // Prints 1
        var key = ((TomlTable)nonNullModel["my_table"]!)["key"];
        TestContext.Current.TestOutputHelper?.WriteLine($"found key = {key}");
        Assert.Equal(1L, key);
        // Check list
        var list = (TomlArray)((TomlTable)nonNullModel["my_table"]!)["list"]!;
        TestContext.Current.TestOutputHelper?.WriteLine($"found list = {string.Join(", ", list)}");
        Assert.Equal(new TomlArray() { 4, 5, 6 }, list);
    }

    [Theory]
    [InlineData(7, 32, 0, 0)]
    [InlineData(7, 32, 0, 999)]
    [InlineData(0, 32, 0, 0)]
    public void TestLocalTime(int hour, int minute, int second, int millisecond)
    {
        var toml = $@"time = {hour:D2}:{minute:D2}:{second:D2}.{millisecond:D3}";
        var model = TomlSerializer.Deserialize<TomlTable>(toml);
        Assert.NotNull(model);
        var localTime = (TomlDateTime)model!["time"];

        Assert.Equal(hour, localTime.DateTime.Hour);
        Assert.Equal(minute, localTime.DateTime.Minute);
        Assert.Equal(second, localTime.DateTime.Second);
        Assert.Equal(millisecond, localTime.DateTime.Millisecond);
    }

    [Fact]
    public void TestEmptyComment()
    {
        var input = "#\n";
        var doc = SyntaxParser.Parse(input);
        var docAsStr = doc.ToString();
        Assert.Equal(input, docAsStr);
    }

    [Fact]
    public void TestIntegerOverflowIsRejected()
    {
        // TOML requires an error when an integer cannot be represented as a
        // signed 64-bit value; a positive literal in [2^63, 2^64-1] must not
        // silently wrap to a negative long.
        Assert.False(SyntaxParser.Parse("a = 9223372036854775807").HasErrors);  // long.MaxValue
        Assert.False(SyntaxParser.Parse("a = -9223372036854775808").HasErrors); // long.MinValue
        Assert.True(SyntaxParser.Parse("a = 9223372036854775808").HasErrors);   // 2^63
        Assert.True(SyntaxParser.Parse("a = 18446744073709551615").HasErrors);  // 2^64-1
    }

    [Fact]
    public void SimpleTest()
    {
        var test = @"[table-1]
key1 = ""some string""    # This is a comment
key2 = 123
Key3 = true
Key4 = false
Key5 = +inf

[table-2]
key1 = ""another string""
key2 = 456
";
        var doc = SyntaxParser.Parse(test);
        Assert.Equal(test, doc.ToString());
    }

    [Fact]
    public void TestInlineArray()
    {
        var input = @"x = [1,
2,
3
]
";
        var model = TomlSerializer.Deserialize<TomlTable>(input);
        Assert.NotNull(model);
        var array = model!["x"] as TomlArray;
        Assert.NotNull(array);
        var nonNullArray = array!;
        Assert.HasCount(3, nonNullArray);
        Assert.Equal(1L, nonNullArray[0]);
        Assert.Equal(2L, nonNullArray[1]);
        Assert.Equal(3L, nonNullArray[2]);
    }

    [Fact]
    public void ReadmeExample_SerializeAndDeserialize()
    {
        #region TomlSerializer_SerializeAndDeserialize
        var toml = TomlSerializer.Serialize(new Person("Ada", 37));
        Assert.Equal("""
            Name = "Ada"
            Age = 37

            """, toml);

        var person = TomlSerializer.Deserialize<Person>(toml);
        Assert.Equal(new Person("Ada", 37), person);
        #endregion
    }

    [Fact]
    public void ReadmeExample_TryDeserialize()
    {
        #region TomlSerializer_TryDeserialize
        var toml = "Port = \"not a number\"";

        var success = TomlSerializer.TryDeserialize<ServerConfig>(toml, out var config);
        Assert.False(success); // Invalid input
        Assert.Null(config);
        #endregion
    }

    [Fact]
    public void ReadmeExample_ArrayOfTables()
    {
        var toml = TomlSerializer.Serialize(new Config { Package = [new Package { Name = "core" }, new Package { Name = "tools" }] });

        Assert.Equal("""
            [[Package]]
            Name = "core"

            [[Package]]
            Name = "tools"

            """, toml);
    }

    [Fact]
    public void ReadmeExample_Options()
    {
        #region TomlSerializerOptions_Create
        var options = new TomlSerializerOptions
        {
            PropertyNamingPolicy = TomlNamingPolicy.SnakeCaseLower,
            PreferredObjectCreationHandling = TomlObjectCreationHandling.Replace,
            WriteIndented = true,
            IndentSize = 4,
            MaxDepth = 64,
            DefaultIgnoreCondition = TomlIgnoreCondition.WhenWritingNull,
        };

        var config = new ServerConfig { Host = "example.com" };
        var toml = TomlSerializer.Serialize(config, options);
        Assert.Equal("""
            host = "example.com"
            port = 8080

            """, toml);
        #endregion
    }

    [Fact]
    public void ReadmeExample_Populate()
    {
        var configuration = TomlSerializer.Deserialize<ReleaserConfiguration>("Channels = [\"beta\"]");

        Assert.Equal(["stable", "beta"], configuration!.Channels);
    }

    [Fact]
    public void ReadmeExample_SingleOrArray()
    {
        var single = TomlSerializer.Deserialize<PackagingConfiguration>("rid = \"win-x64\"");
        var array = TomlSerializer.Deserialize<PackagingConfiguration>("rid = [\"win-x64\", \"linux-x64\"]");

        Assert.Equal(["win-x64"], single!.RuntimeIdentifiers);
        Assert.Equal(["win-x64", "linux-x64"], array!.RuntimeIdentifiers);
    }

    [Fact]
    public void ReadmeExample_FormattingAttributes()
    {
        var toml = TomlSerializer.Serialize(new FormattedConfig
        {
            Package = [new Package { Name = "core" }],
            Owner = new Owner { Name = "Ada" },
        });

        Assert.Equal("""
            Description = 'plain text'
            Owner = {Name = "Ada"}
            [[Package]]
            Name = "core"

            """, toml);
    }

    [Fact]
    public void ReadmeExample_Attributes()
    {
        var database = TomlSerializer.Deserialize<DatabaseConfig>("Host = \"localhost\"\nport_number = 5432");
        var endpoint = TomlSerializer.Deserialize<Endpoint>("Host = \"localhost\"\nPort = 80");

        Assert.Equal("localhost:5432", database!.ConnectionString);
        Assert.Equal("Host = \"localhost\"\nport_number = 5432\n", TomlSerializer.Serialize(database));
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<DatabaseConfig>("port_number = 5432"));
        Assert.Equal(80, endpoint!.Port);
    }

    [Fact]
    public void ReadmeExample_LifecycleCallbacks()
    {
        Assert.Equal(8080, TomlSerializer.Deserialize<ValidatedConfig>("")!.Port);
        Assert.Throws<InvalidOperationException>(() => TomlSerializer.Deserialize<ValidatedConfig>("Port = 0"));
    }

    [Fact]
    public void ReadmeExample_CustomConverter()
    {
        #region TomlConverter_Register
        var options = new TomlSerializerOptions { Converters = [new UpperCaseStringConverter()] };

        var toml = TomlSerializer.Serialize(new ServerConfig(), options);
        Assert.Equal("Host = \"LOCALHOST\"\nPort = 8080\n", toml);
        #endregion

        var generated = TomlSerializer.Serialize(new ServerConfig(), ConverterContext.Default.ServerConfig);
        Assert.Equal("Host = \"LOCALHOST\"\nPort = 8080\n", generated);
        Assert.Equal("HOST", TomlSerializer.Deserialize<ServerConfig>("Host = \"host\"", options)!.Host);
    }

    [Fact]
    public void ReadmeExample_StringEnumConverter()
    {
        #region TomlStringEnumConverter_Options
        var options = new TomlSerializerOptions { Converters = [new TomlStringEnumConverter()] };
        #endregion

        Assert.Equal("Level = \"warn\"\n", TomlSerializer.Serialize(new Dictionary<string, LogLevel> { ["Level"] = LogLevel.Warning }));
        Assert.Equal("Level = \"warn\"\n", TomlSerializer.Serialize(new Dictionary<string, LogLevel> { ["Level"] = LogLevel.Warning }, options));
        Assert.Equal(LogLevel.Warning, TomlSerializer.Deserialize<Dictionary<string, LogLevel>>("Level = \"Warning\"")!["Level"]);
        Assert.Equal(LogLevel.Information, TomlSerializer.Deserialize<Dictionary<string, LogLevel>>("Level = \"information\"")!["Level"]);
    }

    [Fact]
    public void ReadmeExample_ExtensionData()
    {
        var config = TomlSerializer.Deserialize<ExtensibleConfig>("Name = \"app\"\nversion = 2");

        Assert.Equal("app", config!.Name);
        Assert.Equal(2L, config.Extra!["version"]);
    }

    [Fact]
    public void ReadmeExample_Polymorphism()
    {
        var shelter = TomlSerializer.Deserialize<Dictionary<string, List<Animal>>>("""
            [[animals]]
            kind = "cat"
            Name = "Whiskers"
            Indoor = true

            [[animals]]
            kind = "dog"
            Name = "Rex"
            Breed = "Labrador"
            """)!;

        var animals = shelter["animals"];
        Assert.True(Assert.IsType<Cat>(animals[0]).Indoor);
        Assert.Equal("Labrador", Assert.IsType<Dog>(animals[1]).Breed);
        Assert.Equal("Rex", animals[1].Name);
    }

    [Fact]
    public void ReadmeExample_SourceGeneration()
    {
        #region TomlSerializerContext_Use
        var config = new ServerConfig { Port = 443 };
        var toml = TomlSerializer.Serialize(config, ServerContext.Default.ServerConfig);
        Assert.Equal("host = \"localhost\"\nport = 443\n", toml);

        var roundTrip = TomlSerializer.Deserialize(toml, ServerContext.Default.ServerConfig);
        Assert.Equal(443, roundTrip!.Port);

        // APIs taking a Type accept a context
        var value = TomlSerializer.Deserialize(toml, typeof(ServerConfig), ServerContext.Default);
        Assert.IsType<ServerConfig>(value);
        #endregion
    }

    [Fact]
    public void ReadmeExample_ReadDocumentObjectModel()
    {
        #region TomlTable_Read
        var table = TomlSerializer.Deserialize<TomlTable>("""
            title = "My App"

            [database]
            host = "localhost"
            ports = [8000, 8001]
            """)!;

        var title = (string)table["title"];
        var database = (TomlTable)table["database"];
        var firstPort = (long)((TomlArray)database["ports"])[0]!;

        Assert.Equal("My App", title);
        Assert.Equal(8000L, firstPort);
        #endregion
    }

    [Fact]
    public void ReadmeExample_BuildDocumentObjectModel()
    {
        #region TomlTable_Build
        var document = new TomlTable
        {
            ["title"] = "My App",
            ["owner"] = new TomlTable(inline: true) { ["name"] = "Ada" },
            ["database"] = new TomlTable
            {
                ["ports"] = new TomlArray { 8000L, 8001L },
            },
            ["servers"] = new TomlTableArray
            {
                new TomlTable { ["name"] = "alpha" },
                new TomlTable { ["name"] = "beta" },
            },
        };

        var toml = TomlSerializer.Serialize(document);
        Assert.Equal("""
            title = "My App"
            owner = {name = "Ada"}
            [database]
            ports = [8000, 8001]
            [[servers]]
            name = "alpha"

            [[servers]]
            name = "beta"

            """, toml);
        #endregion
    }

    [Fact]
    public void ReadmeExample_MetadataStore()
    {
        #region TomlMetadataStore_Read
        var toml = """
            # The name of the application
            title = 'My App' # shown in the title bar
            """;

        var store = new TomlMetadataStore();
        var options = new TomlSerializerOptions { MetadataStore = store };
        var model = TomlSerializer.Deserialize<TomlTable>(toml, options)!;

        Assert.True(store.TryGetProperties(model, out var metadata));
        Assert.True(metadata.TryGetProperty("title", out var property));
        Assert.Equal("# The name of the application", property.LeadingTrivia![0].Text);
        Assert.Equal("# shown in the title bar", property.TrailingTrivia![1].Text);
        Assert.Equal(TomlPropertyDisplayKind.StringLiteral, property.DisplayKind);
        #endregion
    }

    [Fact]
    public void ReadmeExample_SyntaxTree()
    {
        #region SyntaxParser_Parse
        var input = "# config\nname = \"Ada\"\n";
        var document = SyntaxParser.Parse(input, sourceName: "config.toml");
        Assert.False(document.HasErrors);

        Assert.Equal(input, document.ToString()); // Same text as the input

        var values = new List<string?>();
        foreach (var node in document.Descendants())
        {
            if (node is StringValueSyntax value)
                values.Add(value.Value);
        }

        Assert.Equal(["Ada"], values);
        #endregion
    }

    [Fact]
    public void ReadmeExample_LexerAndParser()
    {
        #region TomlLexer_TomlParser
        var tokens = new List<string>();
        var lexer = TomlLexer.Create("name = \"Ada\"", sourceName: "config.toml");
        while (lexer.MoveNext())
        {
            tokens.Add($"{lexer.Current.Kind} {lexer.CurrentSpan}");
        }

        Assert.Equal(
        [
            "BasicKey config.toml(1,1)-(1,4)",
            "Whitespaces config.toml(1,5)-(1,5)",
            "Equal config.toml(1,6)-(1,6)",
            "Whitespaces config.toml(1,7)-(1,7)",
            "String config.toml(1,8)-(1,12)",
            "Eof config.toml(1,13)-(1,13)",
        ], tokens);

        var events = new List<string>();
        var parser = TomlParser.Create("[server]\nport = 8080", new TomlParserOptions { Mode = TomlParserMode.Tolerant });
        while (parser.MoveNext())
        {
            var kind = parser.Current.Kind;
            events.Add(kind == TomlParseEventKind.PropertyName ? $"{kind} {parser.GetPropertyName()}" : kind.ToString());
        }

        Assert.Equal(
        [
            "StartDocument",
            "StartTable",
            "PropertyName server",
            "StartTable",
            "PropertyName port",
            "Integer",
            "EndTable",
            "EndTable",
            "EndDocument",
        ], events);
        #endregion
    }
}

#region TomlSerializer_Person
public sealed record Person(string Name, int Age);
#endregion

#region TomlSerializer_ArrayOfTables
public sealed class Config
{
    public Package[] Package { get; set; } = [];
}

public sealed class Package
{
    public string Name { get; set; } = "";
}
#endregion

#region TomlObjectCreationHandling_Populate
[TomlObjectCreationHandling(TomlObjectCreationHandling.Populate)]
public sealed class ReleaserConfiguration
{
    public List<string> Channels { get; } = ["stable"];
}
#endregion

#region TomlSingleOrArray_Usage
public sealed class PackagingConfiguration
{
    [TomlSingleOrArray]
    [TomlPropertyName("rid")]
    public List<string> RuntimeIdentifiers { get; } = [];
}
#endregion

#region TomlFormattingAttributes_Usage
[TomlMappingOrder(TomlMappingOrderPolicy.Alphabetical)]
[TomlDottedKeyHandling(TomlDottedKeyHandling.Expand)]
public sealed class FormattedConfig
{
    [TomlTableArrayStyle(TomlTableArrayStyle.Headers)]
    public Package[] Package { get; set; } = [];

    [TomlInlineTable(TomlInlineTablePolicy.Always)]
    public Owner Owner { get; set; } = new();

    [TomlStringStyle(TomlStringStyle.Literal, PreferLiteralWhenNoEscapes = TomlBooleanPreference.True)]
    public string Description { get; set; } = "plain text";
}

public sealed class Owner
{
    public string Name { get; set; } = "";
}
#endregion

#region TomlAttributes_Usage
public sealed class DatabaseConfig
{
    [TomlRequired]
    public string Host { get; set; } = "";

    [TomlPropertyName("port_number")]
    public int Port { get; set; }

    [TomlIgnore]
    public string ConnectionString => $"{Host}:{Port}";
}

public sealed class Endpoint
{
    [TomlConstructor]
    public Endpoint(string host, int port)
    {
        Host = host;
        Port = port;
    }

    public string Host { get; }
    public int Port { get; }
}
#endregion

#region TomlLifecycleCallbacks_Usage
public sealed class ValidatedConfig : ITomlOnDeserialized
{
    public int Port { get; set; } = 8080;

    public void OnTomlDeserialized()
    {
        if (Port is < 1 or > 65535)
            throw new InvalidOperationException($"Invalid port: {Port}");
    }
}
#endregion

#region TomlConverter_Create
public sealed class UpperCaseStringConverter : TomlConverter<string>
{
    public override string Read(TomlReader reader) => reader.GetString().ToUpperInvariant();

    public override void Write(TomlWriter writer, string value) => writer.WriteStringValue(value.ToUpperInvariant());
}
#endregion

#region TomlConverter_Context
[TomlSourceGenerationOptions(Converters = [typeof(UpperCaseStringConverter)])]
[TomlSerializable(typeof(ServerConfig))]
internal sealed partial class ConverterContext : TomlSerializerContext;
#endregion

#region TomlStringEnumConverter_Enum
[TomlConverter(typeof(TomlStringEnumConverter))]
public enum LogLevel
{
    Information,
    [TomlStringEnumMemberName("warn")]
    Warning,
}
#endregion

#region TomlExtensionData_Usage
public sealed class ExtensibleConfig
{
    public string Name { get; set; } = "";

    [TomlExtensionData]
    public IDictionary<string, object?>? Extra { get; set; }
}
#endregion

#region TomlPolymorphism_Usage
[TomlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[TomlDerivedType(typeof(Cat), "cat")]
[TomlDerivedType(typeof(Dog), "dog")]
public abstract class Animal
{
    public string Name { get; set; } = "";
}

public sealed class Cat : Animal
{
    public bool Indoor { get; set; }
}

public sealed class Dog : Animal
{
    public string Breed { get; set; } = "";
}
#endregion

#region TomlSerializerContext_Declare
[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.SnakeCaseLower)]
[TomlSerializable(typeof(ServerConfig))]
internal sealed partial class ServerContext : TomlSerializerContext;

public sealed class ServerConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 8080;
}
#endregion
