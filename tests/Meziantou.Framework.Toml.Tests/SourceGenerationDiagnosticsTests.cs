using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using Meziantou.Framework.Toml.Serialization;
using Meziantou.Framework.Toml.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Meziantou.Framework.Toml.Tests;

public sealed class SourceGenerationDiagnosticsTests
{
    [Fact]
    public void Generator_ResolvesKnownOptionBranchesAtBuildTime()
    {
        var source = """
            #nullable enable
            using System.Collections.Generic;
            using System.Text.Json.Serialization;
            using Meziantou.Framework.Toml;
            using Meziantou.Framework.Toml.Serialization;

            [TomlSourceGenerationOptions(
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = TomlIgnoreCondition.Never,
                DuplicateKeyHandling = TomlDuplicateKeyHandling.LastWins,
                MappingOrder = TomlMappingOrderPolicy.Alphabetical,
                DictionaryKeyPolicy = TomlKnownNamingPolicy.CamelCase,
                PreferredObjectCreationHandling = TomlObjectCreationHandling.Populate)]
            [TomlSerializable(typeof(Person))]
            [TomlSerializable(typeof(CtorPerson))]
            internal partial class Ctx : TomlSerializerContext { }

            public sealed class Person
            {
                public string? Name { get; set; }
                public int Age { get; set; }
                public Child Child { get; } = new();
                [TomlExtensionData]
                public Dictionary<string, string>? Extra { get; set; }
            }

            public sealed class CtorPerson
            {
                [JsonConstructor]
                public CtorPerson(string? name, int age)
                {
                    Name = name;
                    Age = age;
                }

                public string? Name { get; }
                public int Age { get; }
            }

            public sealed class Child
            {
                public string? Value { get; set; }
            }
            """;

        var generatedSource = RunGeneratorTest(source).GeneratedSources.Single();

        AssertNoDynamicKnownOptionBranches(generatedSource);
    }

    [Fact]
    public void Generator_ResolvesDefaultOptionBranchesAtBuildTime()
    {
        var source = """
            #nullable enable
            using System.Collections.Generic;
            using System.Text.Json.Serialization;
            using Meziantou.Framework.Toml.Serialization;

            [TomlSerializable(typeof(Person))]
            [TomlSerializable(typeof(CtorPerson))]
            internal partial class Ctx : TomlSerializerContext { }

            public sealed class Person
            {
                public string? Name { get; set; }
                public int Age { get; set; }
                public Child Child { get; } = new();
                [TomlExtensionData]
                public Dictionary<string, string>? Extra { get; set; }
            }

            public sealed class CtorPerson
            {
                [JsonConstructor]
                public CtorPerson(string? name, int age)
                {
                    Name = name;
                    Age = age;
                }

                public string? Name { get; }
                public int Age { get; }
            }

            public sealed class Child
            {
                public string? Value { get; set; }
            }
            """;

        var generatedSource = RunGeneratorTest(source).GeneratedSources.Single();

        AssertNoDynamicKnownOptionBranches(generatedSource);
    }

    private static void AssertNoDynamicKnownOptionBranches(string generatedSource)
    {
        Assert.DoesNotContain("Options.PropertyNameCaseInsensitive", generatedSource);
        Assert.DoesNotContain("Options.DefaultIgnoreCondition", generatedSource);
        Assert.DoesNotContain("Options.DuplicateKeyHandling", generatedSource);
        Assert.DoesNotContain("Options.MappingOrder", generatedSource);
        Assert.DoesNotContain("Options.DictionaryKeyPolicy", generatedSource);
        Assert.DoesNotContain("Options.PreferredObjectCreationHandling", generatedSource);
        Assert.DoesNotContain("if (Options.", generatedSource);
        Assert.DoesNotContain("switch (Options.", generatedSource);
    }

    [Fact]
    public void Generator_ReportsInvalidIndentSize()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            [TomlSourceGenerationOptions(IndentSize = 0)]
            [TomlSerializable(typeof(Person))]
            internal partial class Ctx : TomlSerializerContext { }

            public sealed class Person { public string Name { get; set; } = ""; }
            """;

        var diagnostics = RunGenerator(source);
        Assert.True(diagnostics.Any(d => d.Id == "MFTOML005"));
    }

    [Fact]
    public void Generator_ReportsInvalidEnumOption()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml;
            using Meziantou.Framework.Toml.Serialization;

            [TomlSourceGenerationOptions(NewLine = (TomlNewLineKind)42)]
            [TomlSerializable(typeof(Person))]
            internal partial class Ctx : TomlSerializerContext { }

            public sealed class Person { public string Name { get; set; } = ""; }
            """;

        var diagnostics = RunGenerator(source);
        Assert.True(diagnostics.Any(d => d.Id == "MFTOML005"));
    }

    [Fact]
    public void Generator_ReportsInvalidMaxDepth()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            [TomlSourceGenerationOptions(MaxDepth = -1)]
            [TomlSerializable(typeof(Person))]
            internal partial class Ctx : TomlSerializerContext { }

            public sealed class Person { public string Name { get; set; } = ""; }
            """;

        var diagnostics = RunGenerator(source);
        Assert.True(diagnostics.Any(d => d.Id == "MFTOML005"));
    }

    [Fact]
    public void Generator_ReportsInvalidConverterType()
    {
        var source = """
            #nullable enable
            using System;
            using Meziantou.Framework.Toml.Serialization;

            public sealed class NotAConverter { public NotAConverter() { } }

            [TomlSourceGenerationOptions(Converters = new [] { typeof(NotAConverter) })]
            [TomlSerializable(typeof(Person))]
            internal partial class Ctx : TomlSerializerContext { }

            public sealed class Person { public string Name { get; set; } = ""; }
            """;

        var diagnostics = RunGenerator(source);
        Assert.True(diagnostics.Any(d => d.Id == "MFTOML002"));
    }

    [Fact]
    public void Generator_WarnsForOptionsConverterFactory()
    {
        var source = """
            #nullable enable
            using System;
            using Meziantou.Framework.Toml;
            using Meziantou.Framework.Toml.Serialization;

            public sealed class Factory : TomlConverterFactory
            {
                public override bool CanConvert(Type typeToConvert) => false;
                public override TomlConverter CreateConverter(Type typeToConvert, TomlSerializerOptions options) => throw new NotSupportedException();
            }

            [TomlSourceGenerationOptions(Converters = new [] { typeof(Factory) })]
            [TomlSerializable(typeof(Person))]
            internal partial class Ctx : TomlSerializerContext { }

            public sealed class Person { public string Name { get; set; } = ""; }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Single(diagnostics, d => d.Id == "MFTOML012" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Theory]
    [InlineData("[TomlConverter(typeof(NotAConverter))]")]
    [InlineData("[System.Text.Json.Serialization.JsonConverter(typeof(NotAConverter))]")]
    [InlineData("[TomlConverter(typeof(Holder.PrivateConverter))]")]
    public void Generator_ReportsInvalidMemberConverter(string attribute)
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            public sealed class NotAConverter { }

            public sealed class Holder
            {
                private sealed class PrivateConverter : TomlConverter<string>
                {
                    public override string? Read(TomlReader reader) => reader.GetString();
                    public override void Write(TomlWriter writer, string value) => writer.WriteStringValue(value);
                }

                public static string Name => nameof(PrivateConverter);
            }

            [TomlSerializable(typeof(Person))]
            internal partial class Ctx : TomlSerializerContext { }

            public sealed class Person
            {
                ATTRIBUTE
                public string Name { get; set; } = "";

                public int Age { get; set; }
            }
            """.Replace("ATTRIBUTE", attribute, StringComparison.Ordinal);

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "MFTOML002" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_WarnsForJsonSerializableUsage()
    {
        var source = """
            #nullable enable
            using System.Text.Json.Serialization;
            using Meziantou.Framework.Toml.Serialization;

            [JsonSerializable(typeof(Person))]
            internal partial class Ctx : TomlSerializerContext { }

            public sealed class Person { public string Name { get; set; } = ""; }
            """;

        var diagnostics = RunGenerator(source);
        Assert.True(diagnostics.Any(d => d.Id == "MFTOML008"));
    }

    [Fact]
    public void Generator_ReportsInvalidTypeInfoPropertyName()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            [TomlSerializable(typeof(Person), TypeInfoPropertyName = "not-valid")]
            internal partial class Ctx : TomlSerializerContext { }

            public sealed class Person { public string Name { get; set; } = ""; }
            """;

        var diagnostics = RunGenerator(source);
        Assert.True(diagnostics.Any(d => d.Id == "MFTOML005" && d.GetMessage().Contains("TypeInfoPropertyName", StringComparison.Ordinal)));
    }

    [Fact]
    public void Generator_PreservesNullableReferenceLocals()
    {
        var source = """
            #nullable enable
            using System.Text.Json.Serialization;
            using Meziantou.Framework.Toml.Serialization;

            public sealed class InitOptions
            {
                public string? NullableMock { get; init; }
                public string NonNullableMock { get; init; } = string.Empty;
            }

            public sealed class CtorOptions
            {
                [JsonConstructor]
                public CtorOptions(string? nullableMock, string nonNullableMock)
                {
                    NullableMock = nullableMock;
                    NonNullableMock = nonNullableMock;
                }

                public string? NullableMock { get; }
                public string NonNullableMock { get; }
            }

            [TomlSerializable(typeof(InitOptions))]
            [TomlSerializable(typeof(CtorOptions))]
            internal partial class Ctx : TomlSerializerContext { }
            """;

        var result = RunGeneratorTest(source);
        var generatedSource = string.Join(Environment.NewLine, result.GeneratedSources);

        Assert.False(result.Diagnostics.Any(d => d.Id is "CS8600" or "CS8601"));
        Assert.Contains("string? __memberValue0 = default;", generatedSource);
        Assert.Contains("string __memberValue1 = default!;", generatedSource);
        Assert.Contains("string? __arg0 = default;", generatedSource);
        Assert.Contains("string __arg1 = default!;", generatedSource);
    }

    [Fact]
    public void Generator_EmitsGeneratedCodeCoverageExclusionAttributes()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            public sealed class MetadataPayload
            {
                public string Value { get; set; } = string.Empty;
            }

            [TomlSerializable(typeof(MetadataPayload))]
            internal partial class MetadataTomlContext : TomlSerializerContext { }
            """;

        var generatedSource = RunGeneratorTest(source).GeneratedSources.Single();
        var generatedCodeAttribute = "[global::System.CodeDom.Compiler.GeneratedCode(\"Meziantou.Framework.Toml.SourceGenerator\", \"" +
            (typeof(TomlSerializerContextGenerator).Assembly.GetName().Version?.ToString() ?? "0.0.0.0") +
            "\")]";
        const string ExcludeFromCoverageAttribute = "[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]";

        Assert.StartsWith("// <auto-generated/>", generatedSource);
        Assert.Contains(generatedCodeAttribute + Environment.NewLine +
            ExcludeFromCoverageAttribute + Environment.NewLine +
            "partial class MetadataTomlContext", generatedSource);
        Assert.Contains("    " + generatedCodeAttribute + Environment.NewLine +
            "    " + ExcludeFromCoverageAttribute + Environment.NewLine +
            "    private sealed class __TomlTypeInfo_MetadataPayload", generatedSource);
    }

    [Fact]
    public void Generator_DoesNotReferenceInternalRuntimeHelpers_FromConsumerAssembly()
    {
        var source = """
            #nullable enable
            using System.Text.Json.Serialization;
            using Meziantou.Framework.Toml.Serialization;

            public sealed class Root
            {
                [JsonPropertyName("child")]
                public Child Child { get; } = new();
            }

            public sealed class Child
            {
                public string Name { get; set; } = "";
            }

            [TomlSourceGenerationOptions(PreferredObjectCreationHandling = TomlObjectCreationHandling.Populate)]
            [TomlSerializable(typeof(Root))]
            internal partial class Ctx : TomlSerializerContext { }
            """;

        var result = RunGeneratorTest(source);
        var generatedSource = string.Join(Environment.NewLine, result.GeneratedSources);

        Assert.False(result.Diagnostics.Any(d => d.Id == "CS0122"));
        Assert.DoesNotContain("TomlTableHeaderExtensionHelper", generatedSource);
    }

    [Fact]
    public void Generator_DoesNotReferenceInternalSingleOrArrayHelper_FromConsumerAssembly()
    {
        var source = """
            #nullable enable
            using System.Collections.Generic;
            using Meziantou.Framework.Toml.Serialization;

            public sealed class ProviderConfig
            {
                [TomlSingleOrArray]
                public required IReadOnlyList<string> ApiKey { get; init; }
            }

            [TomlSerializable(typeof(ProviderConfig))]
            internal partial class Ctx : TomlSerializerContext { }
            """;

        var result = RunGeneratorTest(source);
        var generatedSource = string.Join(Environment.NewLine, result.GeneratedSources);

        Assert.False(result.Diagnostics.Any(d => d.Id == "CS0122"));
        Assert.DoesNotContain("TomlSingleOrArrayCollectionHelper", generatedSource);
    }

    [Fact]
    public void Generator_ReportsInvalidDerivedTypeMapping_WhenDerivedTypeIsNotAssignable()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            [TomlSerializable(typeof(Animal))]
            [TomlDerivedTypeMapping(typeof(Animal), typeof(NotAnimal), "cat")]
            internal partial class Ctx : TomlSerializerContext { }

            [TomlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
            public abstract class Animal { }
            public sealed class NotAnimal { }
            """;

        var diagnostics = RunGenerator(source);

        Assert.True(diagnostics.Any(d => d.Id == "MFTOML009"));
    }

    [Fact]
    public void Generator_ReportsInvalidDerivedTypeMapping_WhenDiscriminatorIsEmpty()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            [TomlSerializable(typeof(Animal))]
            [TomlDerivedTypeMapping(typeof(Animal), typeof(Cat), "")]
            internal partial class Ctx : TomlSerializerContext { }

            [TomlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
            public abstract class Animal { }
            public sealed class Cat : Animal { }
            """;

        var diagnostics = RunGenerator(source);

        Assert.True(diagnostics.Any(d => d.Id == "MFTOML009"));
    }

    [Fact]
    public void Generator_WarnsWhenDerivedTypeMappingBaseHasNoPolymorphicConfiguration()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            [TomlSerializable(typeof(Animal))]
            [TomlDerivedTypeMapping(typeof(Animal), typeof(Cat), "cat")]
            internal partial class Ctx : TomlSerializerContext { }

            public abstract class Animal { public string Name { get; set; } = ""; }
            public sealed class Cat : Animal { public int Lives { get; set; } }
            """;

        var diagnostics = RunGenerator(source);

        Assert.True(diagnostics.Any(d => d.Id == "MFTOML010"));
    }

    private static ImmutableArray<Diagnostic> RunGenerator(string source)
        => RunGeneratorTest(source).Diagnostics;

    private static GeneratorTestResult RunGeneratorTest(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = CSharpCompilation.Create(
            assemblyName: "Meziantou.Framework.Toml.SourceGeneration.Tests.Input",
            syntaxTrees: new[] { syntaxTree },
            references: CreateReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        IIncrementalGenerator generator = new TomlSerializerContextGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        var inputSyntaxTrees = compilation.SyntaxTrees.ToHashSet();
        var generatedSources = driver.GetRunResult()
            .Results
            .SelectMany(static result => result.GeneratedSources)
            .Select(static generated => generated.SourceText.ToString())
            .ToImmutableArray();
        var outputDiagnostics = outputCompilation.GetDiagnostics();
        var generatedCodeWarnings = outputDiagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Warning &&
                        d.Location.SourceTree is { } tree &&
                        !inputSyntaxTrees.Contains(tree))
            .ToImmutableArray();

        if (!generatedCodeWarnings.IsDefaultOrEmpty)
        {
            Assert.Fail(
                "Generated code produced compiler warnings:" + Environment.NewLine +
                string.Join(Environment.NewLine, generatedCodeWarnings.Select(static d => d.ToString())));
        }

        return new GeneratorTestResult(generatorDiagnostics.Concat(outputDiagnostics).ToImmutableArray(), generatedSources);
    }

    private readonly record struct GeneratorTestResult(ImmutableArray<Diagnostic> Diagnostics, ImmutableArray<string> GeneratedSources);

    private static List<MetadataReference> CreateReferences()
    {
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = new List<MetadataReference>();

        void Add(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            if (unique.Add(path))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        // Prefer the trusted platform assemblies list to get a coherent reference set.
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (tpa is { Length: > 0 } tpaValue)
        {
            foreach (var path in tpaValue.Split(Path.PathSeparator))
            {
                Add(path);
            }
        }
        else
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                {
                    continue;
                }

                Add(assembly.Location);
            }
        }

        Add(typeof(object).Assembly.Location);
        Add(typeof(Enumerable).Assembly.Location);
        Add(typeof(List<>).Assembly.Location);
        Add(typeof(JsonSerializableAttribute).Assembly.Location);
        Add(typeof(TomlSerializerContext).Assembly.Location);

        return references;
    }
}
