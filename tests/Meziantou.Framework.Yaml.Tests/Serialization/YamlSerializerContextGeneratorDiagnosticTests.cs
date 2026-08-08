using System.Collections.Immutable;
using Meziantou.Framework.Yaml.Serialization;
using Meziantou.Framework.Yaml.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public class YamlSerializerContextGeneratorDiagnosticTests
{
    private static readonly string[] NullableWarningIds = ["CS8600", "CS8601", "CS8602", "CS8603", "CS8604", "CS8618"];

    private static readonly ImmutableDictionary<string, ReportDiagnostic> NullableWarningsAsErrors =
        NullableWarningIds.ToImmutableDictionary(static id => id, static _ => ReportDiagnostic.Error);

    [Fact]
    public void GeneratorDoesNotReportNullableWarningsForMissingNullableInitOnlyProperties()
    {
        const string Source = """
            #nullable enable

            using Meziantou.Framework.Yaml.Serialization;

            public sealed class AppOptions
            {
                public string? NullableMock { get; init; }

                public string NonNullableMock { get; init; } = string.Empty;
            }

            [YamlSerializable(typeof(AppOptions))]
            internal partial class AppOptionsYamlContext : YamlSerializerContext
            {
            }
            """;

        var result = RunGenerator(Source);

        var nullableDiagnostics = result.Diagnostics
            .Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
            .Where(diagnostic => NullableWarningIds.Contains(diagnostic.Id, StringComparer.Ordinal))
            .ToArray();

        Assert.Empty(nullableDiagnostics);
        Assert.Contains("NullableMock", result.GeneratedSource);
        Assert.Contains("NonNullableMock", result.GeneratedSource);
    }

    [Fact]
    public void AnalyzerReportsErrorForNonAssignableDerivedTypeMapping()
    {
        const string Source = """
            using Meziantou.Framework.Yaml.Serialization;

            public abstract class Animal
            {
            }

            public sealed class Rock
            {
            }

            [YamlSerializable(typeof(Animal))]
            [YamlDerivedTypeMapping(typeof(Animal), typeof(Rock), "rock")]
            internal partial class InvalidMappingContext : YamlSerializerContext
            {
            }
            """;

        var diagnostics = RunAnalyzer(Source)
            .Where(static diagnostic => diagnostic.Id == "MFY020")
            .ToArray();

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Rock", diagnostic.GetMessage());
        Assert.Contains("Animal", diagnostic.GetMessage());

        var result = RunGenerator(Source);
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.GeneratedSource);
    }

    [Fact]
    public void AnalyzerWarnsWhenDerivedTypeMappingBaseUsesSerializerDefaults()
    {
        const string Source = """
            using Meziantou.Framework.Yaml.Serialization;

            public abstract class Animal
            {
                public string Name { get; set; } = string.Empty;
            }

            public sealed class Dog : Animal
            {
                public int BarkVolume { get; set; }
            }

            [YamlSerializable(typeof(Animal))]
            [YamlDerivedTypeMapping(typeof(Animal), typeof(Dog), "dog")]
            internal partial class MappingContext : YamlSerializerContext
            {
            }
            """;

        var diagnostics = RunAnalyzer(Source)
            .Where(static diagnostic => diagnostic.Id == "MFY021")
            .ToArray();

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("Animal", diagnostic.GetMessage());

        var result = RunGenerator(Source);
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Contains("value is global::Dog", result.GeneratedSource);
    }

    [Fact]
    public void MFY002_IsSuppressed_WhenContextLevelConverterHandlesMemberType()
    {
        const string Source = """
            using Meziantou.Framework.Yaml.Serialization;

            public sealed class CustomType
            {
                public string Value { get; set; } = string.Empty;
            }

            public sealed class CustomTypeConverter : YamlConverter<CustomType>
            {
                public override CustomType Read(YamlReader reader)
                {
                    var value = reader.GetScalarValue();
                    reader.Read();
                    return new CustomType { Value = value };
                }

                public override void Write(YamlWriter writer, CustomType value)
                {
                    writer.WriteScalar(value.Value);
                }
            }

            public sealed class ModelWithCustomType
            {
                public CustomType? Item { get; set; }
            }

            [YamlSerializable(typeof(ModelWithCustomType))]
            [YamlSourceGenerationOptions(Converters = [typeof(CustomTypeConverter)])]
            internal partial class TestContext : YamlSerializerContext
            {
            }
            """;

        var diagnostics = RunAnalyzer(Source)
            .Where(static d => d.Id == "MFY002")
            .ToArray();

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void MFY002_IsSuppressed_WhenMemberHasYamlConverterAttribute()
    {
        const string Source = """
            using Meziantou.Framework.Yaml.Serialization;

            public sealed class CustomType
            {
                public string Value { get; set; } = string.Empty;
            }

            public sealed class CustomTypeConverter : YamlConverter<CustomType>
            {
                public override CustomType Read(YamlReader reader)
                {
                    var value = reader.GetScalarValue();
                    reader.Read();
                    return new CustomType { Value = value };
                }

                public override void Write(YamlWriter writer, CustomType value)
                {
                    writer.WriteScalar(value.Value);
                }
            }

            public sealed class ModelWithConverterOnMember
            {
                [YamlConverter(typeof(CustomTypeConverter))]
                public CustomType? Item { get; set; }
            }

            [YamlSerializable(typeof(ModelWithConverterOnMember))]
            internal partial class TestContext : YamlSerializerContext
            {
            }
            """;

        var diagnostics = RunAnalyzer(Source)
            .Where(d => d.Id == "MFY002")
            .ToArray();

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void MFY002_IsSuppressed_WhenTypeHasYamlConverterAttribute()
    {
        const string Source = """
            using Meziantou.Framework.Yaml.Serialization;

            public sealed class TypeLevelConverter : YamlConverter<ConverterDecoratedType>
            {
                public override ConverterDecoratedType Read(YamlReader reader)
                {
                    var value = reader.GetScalarValue();
                    reader.Read();
                    return new ConverterDecoratedType { Value = value };
                }

                public override void Write(YamlWriter writer, ConverterDecoratedType value)
                {
                    writer.WriteScalar(value.Value);
                }
            }

            [YamlConverter(typeof(TypeLevelConverter))]
            public sealed class ConverterDecoratedType
            {
                public string Value { get; set; } = string.Empty;
            }

            public sealed class ModelWithTypeLevelConverter
            {
                public ConverterDecoratedType? Item { get; set; }
            }

            [YamlSerializable(typeof(ModelWithTypeLevelConverter))]
            internal partial class TestContext : YamlSerializerContext
            {
            }
            """;

        var diagnostics = RunAnalyzer(Source)
            .Where(static d => d.Id == "MFY002")
            .ToArray();

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void MFY002_IsSuppressed_WhenContextConverterHandlesArrayElementType()
    {
        const string Source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Yaml.Serialization;

            public sealed class CustomType
            {
                public string Value { get; set; } = string.Empty;
            }

            public sealed class CustomTypeConverter : YamlConverter<CustomType>
            {
                public override CustomType Read(YamlReader reader)
                {
                    var value = reader.GetScalarValue();
                    reader.Read();
                    return new CustomType { Value = value };
                }

                public override void Write(YamlWriter writer, CustomType value)
                {
                    writer.WriteScalar(value.Value);
                }
            }

            public sealed class ModelWithList
            {
                public List<CustomType>? Items { get; set; }
            }

            [YamlSerializable(typeof(ModelWithList))]
            [YamlSourceGenerationOptions(Converters = [typeof(CustomTypeConverter)])]
            internal partial class TestContext : YamlSerializerContext
            {
            }
            """;

        var diagnostics = RunAnalyzer(Source)
            .Where(static d => d.Id == "MFY002")
            .ToArray();

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void MFY002_IsSuppressed_WhenContextConverterHandlesDictionaryValueType()
    {
        const string Source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Yaml.Serialization;

            public sealed class CustomType
            {
                public string Value { get; set; } = string.Empty;
            }

            public sealed class CustomTypeConverter : YamlConverter<CustomType>
            {
                public override CustomType Read(YamlReader reader)
                {
                    var value = reader.GetScalarValue();
                    reader.Read();
                    return new CustomType { Value = value };
                }

                public override void Write(YamlWriter writer, CustomType value)
                {
                    writer.WriteScalar(value.Value);
                }
            }

            public sealed class ModelWithDictionary
            {
                public Dictionary<string, CustomType>? Items { get; set; }
            }

            [YamlSerializable(typeof(ModelWithDictionary))]
            [YamlSourceGenerationOptions(Converters = [typeof(CustomTypeConverter)])]
            internal partial class TestContext : YamlSerializerContext
            {
            }
            """;

        var diagnostics = RunAnalyzer(Source)
            .Where(static d => d.Id == "MFY002")
            .ToArray();

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void MFY002_StillFires_WhenNoConverterHandlesType()
    {
        const string Source = """
            using Meziantou.Framework.Yaml.Serialization;

            public sealed class UnhandledType
            {
                public string Value { get; set; } = string.Empty;
            }

            public sealed class ModelWithUnhandledType
            {
                public UnhandledType? Item { get; set; }
            }

            [YamlSerializable(typeof(ModelWithUnhandledType))]
            internal partial class TestContext : YamlSerializerContext
            {
            }
            """;

        var diagnostics = RunAnalyzer(Source)
            .Where(static d => d.Id == "MFY002")
            .ToArray();

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("UnhandledType", diagnostic.GetMessage());

        var result = RunGenerator(Source);
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.GeneratedSource);
    }

    [Fact]
    public void MFY002_StillFires_WhenConverterHandlesDifferentType()
    {
        const string Source = """
            using Meziantou.Framework.Yaml.Serialization;

            public sealed class TypeA
            {
                public string Value { get; set; } = string.Empty;
            }

            public sealed class TypeB
            {
                public string Value { get; set; } = string.Empty;
            }

            public sealed class TypeAConverter : YamlConverter<TypeA>
            {
                public override TypeA Read(YamlReader reader)
                {
                    var value = reader.GetScalarValue();
                    reader.Read();
                    return new TypeA { Value = value };
                }

                public override void Write(YamlWriter writer, TypeA value)
                {
                    writer.WriteScalar(value.Value);
                }
            }

            public sealed class ModelWithTypeB
            {
                public TypeB? Item { get; set; }
            }

            [YamlSerializable(typeof(ModelWithTypeB))]
            [YamlSourceGenerationOptions(Converters = [typeof(TypeAConverter)])]
            internal partial class TestContext : YamlSerializerContext
            {
            }
            """;

        var diagnostics = RunAnalyzer(Source)
            .Where(d => d.Id == "MFY002")
            .ToArray();

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("TypeB", diagnostic.GetMessage());
    }

    [Fact]
    public void MFY002_IsSuppressed_WhenConverterInheritsFromAnotherConverter()
    {
        const string Source = """
            using Meziantou.Framework.Yaml.Serialization;

            public sealed class CustomType
            {
                public string Value { get; set; } = string.Empty;
            }

            public class BaseCustomTypeConverter : YamlConverter<CustomType>
            {
                public override CustomType Read(YamlReader reader)
                {
                    var value = reader.GetScalarValue();
                    reader.Read();
                    return new CustomType { Value = value };
                }

                public override void Write(YamlWriter writer, CustomType value)
                {
                    writer.WriteScalar(value.Value);
                }
            }

            public sealed class DerivedCustomTypeConverter : BaseCustomTypeConverter
            {
            }

            public sealed class ModelWithCustomType
            {
                public CustomType? Item { get; set; }
            }

            [YamlSerializable(typeof(ModelWithCustomType))]
            [YamlSourceGenerationOptions(Converters = [typeof(DerivedCustomTypeConverter)])]
            internal partial class TestContext : YamlSerializerContext
            {
            }
            """;

        var diagnostics = RunAnalyzer(Source)
            .Where(d => d.Id == "MFY002")
            .ToArray();

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void MFY002_IsSuppressed_WhenContextConverterHandlesNullableValueType()
    {
        const string Source = """
            using Meziantou.Framework.Yaml.Serialization;

            public struct CustomStruct
            {
                public int Value { get; set; }
            }

            public sealed class CustomStructConverter : YamlConverter<CustomStruct>
            {
                public override CustomStruct Read(YamlReader reader)
                {
                    var value = reader.GetScalarValue();
                    reader.Read();
                    return new CustomStruct { Value = int.Parse(value) };
                }

                public override void Write(YamlWriter writer, CustomStruct value)
                {
                    writer.WriteScalar(value.Value.ToString());
                }
            }

            public sealed class ModelWithNullableStruct
            {
                public CustomStruct? Item { get; set; }
            }

            [YamlSerializable(typeof(ModelWithNullableStruct))]
            [YamlSourceGenerationOptions(Converters = [typeof(CustomStructConverter)])]
            internal partial class TestContext : YamlSerializerContext
            {
            }
            """;

        var diagnostics = RunAnalyzer(Source)
            .Where(d => d.Id == "MFY002")
            .ToArray();

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void AnalyzerReportsErrorForNonPartialContext()
    {
        const string Source = """
            using Meziantou.Framework.Yaml.Serialization;

            [YamlSerializable(typeof(string))]
            internal class TestContext : YamlSerializerContext
            {
            }
            """;

        var diagnostics = RunAnalyzer(Source)
            .Where(static diagnostic => diagnostic.Id == "MFY001")
            .ToArray();

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("TestContext", diagnostic.GetMessage());

        var result = RunGenerator(Source);
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.GeneratedSource);
    }

    [Fact]
    public void AnalyzerReportsErrorForUnsupportedExtensionDataMember()
    {
        const string Source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Yaml.Serialization;

            public sealed class Model
            {
                [YamlExtensionData]
                public Dictionary<string, int>? Extra { get; set; }
            }

            [YamlSerializable(typeof(Model))]
            internal partial class TestContext : YamlSerializerContext
            {
            }
            """;

        var diagnostics = RunAnalyzer(Source)
            .Where(static diagnostic => diagnostic.Id == "MFY003")
            .ToArray();

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Extra", diagnostic.GetMessage());
        Assert.Contains("Dictionary", diagnostic.GetMessage());
    }

    [Fact]
    public void AnalyzerReportsErrorForMultipleExtensionDataMembers()
    {
        const string Source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Yaml.Model;
            using Meziantou.Framework.Yaml.Serialization;

            public sealed class Model
            {
                [YamlExtensionData]
                public Dictionary<string, object?>? Extra { get; set; }

                [YamlExtensionData]
                public YamlMapping? Nodes { get; set; }
            }

            [YamlSerializable(typeof(Model))]
            internal partial class TestContext : YamlSerializerContext
            {
            }
            """;

        var diagnostics = RunAnalyzer(Source)
            .Where(static diagnostic => diagnostic.Id == "MFY004")
            .ToArray();

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Model", diagnostic.GetMessage());
    }

    [Fact]
    public void AnalyzerReportsErrorForInvalidSourceGenerationOption()
    {
        const string Source = """
            using Meziantou.Framework.Yaml.Serialization;

            [YamlSerializable(typeof(string))]
            [YamlSourceGenerationOptions(IndentSize = 0)]
            internal partial class TestContext : YamlSerializerContext
            {
            }
            """;

        var diagnostics = RunAnalyzer(Source)
            .Where(static diagnostic => diagnostic.Id == "MFY005")
            .ToArray();

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("IndentSize", diagnostic.GetMessage());
    }

    [Fact]
    public void AnalyzerReportsErrorForInvalidConverterType()
    {
        const string Source = """
            using Meziantou.Framework.Yaml.Serialization;

            public sealed class NotAConverter
            {
            }

            [YamlSerializable(typeof(string))]
            [YamlSourceGenerationOptions(Converters = [typeof(NotAConverter)])]
            internal partial class TestContext : YamlSerializerContext
            {
            }
            """;

        var diagnostics = RunAnalyzer(Source)
            .Where(static diagnostic => diagnostic.Id == "MFY006")
            .ToArray();

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("NotAConverter", diagnostic.GetMessage());
        Assert.Contains("derive", diagnostic.GetMessage());
    }

    private static Diagnostic[] RunAnalyzer([StringSyntax("c#-test")] string source)
    {
        var compilation = CreateCompilation(source);
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new YamlSerializerContextAnalyzer());
        var analyzerOptions = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty);
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            analyzers,
            new CompilationWithAnalyzersOptions(
                analyzerOptions,
                onAnalyzerException: null,
                concurrentAnalysis: true,
                logAnalyzerExecutionTime: false,
                reportSuppressedDiagnostics: false));

        return compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult().ToArray();
    }

    private static (Compilation OutputCompilation, Diagnostic[] GeneratorDiagnostics, Diagnostic[] Diagnostics, string GeneratedSource) RunGenerator([StringSyntax("c#-test")]string source)
    {
        var compilation = CreateCompilation(source);
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new YamlSerializerContextGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        var generatedSource = string.Join(
            Environment.NewLine,
            driver.GetRunResult().Results
                .SelectMany(result => result.GeneratedSources)
                .Select(generatedSourceResult => generatedSourceResult.SourceText.ToString()));

        return (outputCompilation, generatorDiagnostics.ToArray(), generatorDiagnostics.Concat(outputCompilation.GetDiagnostics()).ToArray(), generatedSource);
    }

    private static CSharpCompilation CreateCompilation([StringSyntax("c#-test")] string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        return CSharpCompilation.Create(
            assemblyName: "GeneratorWarningTests",
            syntaxTrees: new[] { syntaxTree },
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable)
                .WithSpecificDiagnosticOptions(NullableWarningsAsErrors));
    }

    private static MetadataReference[] GetMetadataReferences()
    {
        var platformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        return platformAssemblies
            .Concat(
            [
                typeof(object).Assembly.Location,
                typeof(YamlSerializerContext).Assembly.Location,
                typeof(YamlSerializerContextGenerator).Assembly.Location,
            ])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static string GenerateSourceFor([StringSyntax("c#-test")] string source, string hintName)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        var compilation = CSharpCompilation.Create(
            assemblyName: "YamlGeneratedSourceProbe",
            syntaxTrees: [syntaxTree],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new YamlSerializerContextGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        var errors = generatorDiagnostics
            .Concat(outputCompilation.GetDiagnostics())
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && !diagnostic.Location.SourceTree?.FilePath.EndsWith(".g.cs", StringComparison.Ordinal) == true)
            .ToArray();
        if (errors.Length != 0)
        {
            Assert.Fail(string.Join(Environment.NewLine, errors.Select(static diagnostic => diagnostic.ToString())));
        }

        var generatedSourceResult = driver.GetRunResult()
            .Results
            .SelectMany(static result => result.GeneratedSources)
            .SingleOrDefault(s => string.Equals(s.HintName, hintName, StringComparison.Ordinal));

        Assert.NotNull(generatedSourceResult.SourceText);
        return generatedSourceResult.SourceText.ToString();
    }

    private static void AssertGeneratedSourceDoesNotContain(string generatedSource, string text)
        => Assert.DoesNotContain(text, generatedSource);

    [Fact]
    public void GeneratedSource_ResolvesAttributeDrivenBranchesAtBuildTime()
    {
        const string source = """
            #nullable enable

            using System.Collections.Generic;
            using Meziantou.Framework.Yaml;
            using Meziantou.Framework.Yaml.Model;
            using Meziantou.Framework.Yaml.Serialization;

            namespace Meziantou.Framework.Yaml.GeneratedSourceProbe;

            [YamlPolymorphic]
            [YamlDerivedType(typeof(StaticAttributeDerived), "derived", Tag = "!derived")]
            internal class StaticAttributeBase
            {
                [YamlIgnore(Condition = YamlIgnoreCondition.Never)]
                public StaticAttributeChild? Child { get; } = new();

                [YamlIgnore(Condition = YamlIgnoreCondition.WhenWritingDefault)]
                public int DefaultValue { get; set; }

                public string? Optional { get; set; }

                public Dictionary<string, int>? Map { get; set; }

                [YamlIgnore(Condition = YamlIgnoreCondition.WhenWriting)]
                public int WriteOnly { get; set; }
            }

            internal sealed class StaticAttributeDerived : StaticAttributeBase
            {
                [YamlIgnore(Condition = YamlIgnoreCondition.Never)]
                public int Extra { get; set; }
            }

            internal sealed class StaticAttributeChild
            {
                [YamlIgnore(Condition = YamlIgnoreCondition.Never)]
                public int Value { get; set; }
            }

            internal sealed class StaticDictionaryMember
            {
                public Dictionary<string, int>? Values { get; set; }
            }

            internal sealed class StaticExtensionDictionary
            {
                [YamlExtensionData]
                public Dictionary<string, object>? Extra { get; set; }
            }

            internal sealed class StaticExtensionMapping
            {
                [YamlExtensionData]
                public YamlMapping? Extra { get; set; }
            }

            [YamlSourceGenerationOptions(
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = YamlIgnoreCondition.WhenWritingNull,
                MappingOrder = YamlMappingOrderPolicy.Sorted,
                Schema = YamlSchemaKind.Core,
                UnmappedMemberHandling = YamlUnmappedMemberHandling.Disallow,
                PreferredObjectCreationHandling = YamlObjectCreationHandling.Populate,
                DuplicateKeyHandling = YamlDuplicateKeyHandling.LastWins,
                DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag,
                TypeDiscriminatorPropertyName = "$kind",
                UnknownDerivedTypeHandling = YamlUnknownDerivedTypeHandling.FallBackToBase)]
            [YamlSerializable(typeof(StaticAttributeBase))]
            [YamlSerializable(typeof(StaticAttributeChild))]
            [YamlSerializable(typeof(StaticDictionaryMember))]
            [YamlSerializable(typeof(StaticExtensionDictionary))]
            [YamlSerializable(typeof(StaticExtensionMapping))]
            [YamlSerializable(typeof(Dictionary<string, int>))]
            internal partial class StaticAttributeContext : YamlSerializerContext
            {
            }
            """;

        var generatedSource = GenerateSourceFor(source, "StaticAttributeContext.g.cs");

        AssertGeneratedSourceDoesNotContain(generatedSource, "__ignoreCondition");
        AssertGeneratedSourceDoesNotContain(generatedSource, "options.DefaultIgnoreCondition");
        AssertGeneratedSourceDoesNotContain(generatedSource, "var discriminatorStyle =");
        AssertGeneratedSourceDoesNotContain(generatedSource, "if (discriminatorStyle");
        AssertGeneratedSourceDoesNotContain(generatedSource, "unknownDerivedTypeHandling");
        AssertGeneratedSourceDoesNotContain(generatedSource, "options.PolymorphismOptions");
        AssertGeneratedSourceDoesNotContain(generatedSource, "options.PropertyNameCaseInsensitive");
        AssertGeneratedSourceDoesNotContain(generatedSource, "options.Schema is");
        AssertGeneratedSourceDoesNotContain(generatedSource, "options.MappingOrder");
        AssertGeneratedSourceDoesNotContain(generatedSource, "options.UnmappedMemberHandling");
        AssertGeneratedSourceDoesNotContain(generatedSource, "options.PreferredObjectCreationHandling");
        AssertGeneratedSourceDoesNotContain(generatedSource, "options.DuplicateKeyHandling");
        AssertGeneratedSourceDoesNotContain(generatedSource, "if (options.");
        AssertGeneratedSourceDoesNotContain(generatedSource, "switch (options.");
        AssertGeneratedSourceDoesNotContain(generatedSource, "extensionData is not global::Meziantou.Framework.Yaml.Model.YamlMapping");
        AssertGeneratedSourceDoesNotContain(generatedSource, "writer.Options");
        AssertGeneratedSourceDoesNotContain(generatedSource, "reader.Options");
        AssertGeneratedSourceDoesNotContain(generatedSource, "hasCustomConverters");
        AssertGeneratedSourceDoesNotContain(generatedSource, "TryGetCustomConverter");
    }
}
