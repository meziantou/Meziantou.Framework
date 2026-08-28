#pragma warning disable CA1034 // Nested types should not be visible
#pragma warning disable CA1819 // Properties should not return arrays
#pragma warning disable MA0101 // String contains an implicit end of line character
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Meziantou.Framework.Annotations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using TestUtilities;

namespace Meziantou.Framework.StronglyTypedId.Tests;

public sealed class StronglyTypedIdSourceGeneratorTests
{
    private static readonly string NetCoreVersion = TargetFrameworkHelper.GetMicrosoftNetCoreAppRefVersion();

    public sealed record NuGetReference(string Name, string Version, string ReferencePath);

    private static async Task<Compilation> CreateCompilation(string sourceText, NuGetReference[] nuGetReferences, bool referenceAnnotations = true)
    {
        var dlls = new List<string>();
        foreach (var nuGetReference in nuGetReferences)
        {
            dlls.AddRange(await NuGetHelpers.GetNuGetReferences(nuGetReference.Name, nuGetReference.Version, nuGetReference.ReferencePath));
        }

        MetadataReference[] references =
        [
            .. referenceAnnotations ? [MetadataReference.CreateFromFile(typeof(StronglyTypedIdAttribute).Assembly.Location)] : Array.Empty<MetadataReference>(),
            .. dlls.Select(loc => MetadataReference.CreateFromFile(loc)),
        ];

        return CSharpCompilation.Create("compilation",
            [CSharpSyntaxTree.ParseText(sourceText)],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                warningLevel: 9999,
                generalDiagnosticOption: ReportDiagnostic.Error,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static ISourceGenerator InstantiateGenerator() => new StronglyTypedIdSourceGenerator().AsSourceGenerator();

    private static async Task<(GeneratorDriverRunResult GeneratorResult, Compilation OutputCompilation, byte[]? Assembly, byte[] Symbols)> GenerateFiles(string sourceText, bool mustCompile = true, string? netCoreVersion = null, bool referenceAnnotations = true)
    {
        var compilation = await CreateCompilation(sourceText,
        [
            new NuGetReference("Microsoft.NETCore.App.Ref", netCoreVersion ?? NetCoreVersion, "ref/"),
            new NuGetReference("Newtonsoft.Json", "13.0.4", "lib/netstandard2.0/"),
        ], referenceAnnotations);
        var generator = InstantiateGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generators: [generator]);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        Assert.Empty(diagnostics);

        var runResult = driver.GetRunResult();

        var sources = outputCompilation.SyntaxTrees.Where(tree => !string.IsNullOrEmpty(tree.FilePath)).Select(tree => EmbeddedText.FromSource(tree.FilePath, tree.GetText())).ToArray();

        // Validate the output project compiles
        using var outputStream = new MemoryStream();
        using var pdbStream = new MemoryStream();
        var result = outputCompilation.Emit(outputStream, pdbStream, embeddedTexts: sources);
        if (mustCompile)
        {
            var diags = string.Join('\n', result.Diagnostics);
            var generated = runResult.GeneratedTrees.Length > 0 ? (await runResult.GeneratedTrees[0].GetRootAsync()).ToFullString() : "<no file generated>";

            Assert.Empty(result.Diagnostics);
            Assert.True(result.Success);
        }

        return (runResult, outputCompilation, result.Success ? outputStream.ToArray() : null, pdbStream.ToArray());
    }

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string sourceText, string? netCoreVersion = null, bool referenceAnnotations = true)
    {
        var compilation = await CreateCompilation(sourceText,
        [
            new NuGetReference("Microsoft.NETCore.App.Ref", netCoreVersion ?? NetCoreVersion, "ref/"),
            new NuGetReference("Newtonsoft.Json", "12.0.3", "lib/netstandard2.0/"),
        ], referenceAnnotations);

        var analyzerOptions = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty);
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new StronglyTypedIdAnalyzer());
        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers, new CompilationWithAnalyzersOptions(analyzerOptions, onAnalyzerException: null, concurrentAnalysis: true, logAnalyzerExecutionTime: false, reportSuppressedDiagnostics: false));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    [Fact]
    public async Task GenerateStructInNamespaceAndClass()
    {
        var sourceCode = """
            namespace A
            {
                namespace B
                {
                    partial class C
                    {
                        [Meziantou.Framework.Annotations.StronglyTypedId(typeof(int))]
                        public partial struct Test {}
                    }
                }
            }
            """;

        await TestGeneratedAssembly(sourceCode, typeName: "A.B.C+Test", type =>
        {
            var from = (MethodInfo)type.GetMember("FromInt32").Single();
            var instance = from.Invoke(null, [10]);
            var json = System.Text.Json.JsonSerializer.Serialize(instance);
            var deserialized = System.Text.Json.JsonSerializer.Deserialize(json, type);
            var deserialized2 = System.Text.Json.JsonSerializer.Deserialize(@"{ ""a"": {}, ""b"": false, ""Value"": 10 }", type);
            Assert.Equal("10", json);
            Assert.Equal(instance, deserialized);
            Assert.Equal(instance, deserialized2);
        });
    }

    [Fact]
    public async Task GenerateStructInNamespace()
    {
        var sourceCode = """
            namespace A
            {
                namespace B
                {
                    [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(int))]
                    public partial struct Test {}
                }
            }
            """;

        await TestGeneratedAssembly(sourceCode, typeName: "A.B.Test", type =>
        {
            var from = (MethodInfo)type.GetMember("FromInt32").Single();
            var instance = from.Invoke(null, [10]);
            var json = System.Text.Json.JsonSerializer.Serialize(instance);
            var deserialized = System.Text.Json.JsonSerializer.Deserialize(json, type);
            var deserialized2 = System.Text.Json.JsonSerializer.Deserialize(@"{ ""a"": {}, ""b"": false, ""Value"": 10 }", type);
            Assert.Equal("10", json);
            Assert.Equal(instance, deserialized);
            Assert.Equal(instance, deserialized2);
        });
    }

    [Fact]
    public async Task GenerateTypesWithSameNameInDifferentNamespaces()
    {
        var sourceCode = """
            namespace A
            {
                [Meziantou.Framework.Annotations.StronglyTypedId(typeof(int))]
                public partial struct Test {}
            }

            namespace B
            {
                [Meziantou.Framework.Annotations.StronglyTypedId(typeof(int))]
                public partial struct Test {}
            }
            """;

        var result = await GenerateFiles(sourceCode);
        Assert.Empty(result.GeneratorResult.Diagnostics);
        Assert.Equal(2, result.GeneratorResult.GeneratedTrees.Length);

        AssertGeneratedTypes(result, ["A.Test", "B.Test"]);
    }

    [Fact]
    public async Task GenerateNestedTypesWithSameNameInDifferentContainingTypes()
    {
        var sourceCode = """
            public partial class A
            {
                [Meziantou.Framework.Annotations.StronglyTypedId(typeof(int))]
                public partial struct Test {}
            }

            public partial class B
            {
                [Meziantou.Framework.Annotations.StronglyTypedId(typeof(int))]
                public partial struct Test {}
            }
            """;

        var result = await GenerateFiles(sourceCode);
        Assert.Empty(result.GeneratorResult.Diagnostics);
        Assert.Equal(2, result.GeneratorResult.GeneratedTrees.Length);

        AssertGeneratedTypes(result, ["A+Test", "B+Test"]);
    }

    [Fact]
    public async Task DummyAttribute()
    {
        var sourceCode = """
            [System.Obsolete]
            public partial struct Test { }
            """;

        await TestGeneratedAssembly(sourceCode, typeName: null, type => { }, mustGenerateTrees: false);
    }

    [Fact]
    public async Task UnsupportedType_ReportedByAnalyzer()
    {
        var sourceCode = """
            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(System.TimeSpan))]
            public partial struct Test { }
            """;

        var result = await GenerateFiles(sourceCode, mustCompile: false);
        Assert.Empty(result.GeneratorResult.Diagnostics);
        Assert.Empty(result.GeneratorResult.GeneratedTrees);

        var diagnostics = await Analyze(sourceCode);
        Assert.Collection(diagnostics, diag => Assert.Equal("MFSTID0001", diag.Id));
    }

    [Fact]
    public async Task MultipleAttribute()
    {
        var sourceCode = """
        [System.Obsolete]
        [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(System.Guid))]
        public partial struct Test { }
        """;

        await TestGeneratedAssembly(sourceCode, type =>
        {
            Assert.NotEmpty(type.GetMember("Parse"));
        });
    }

    [Fact]
    public async Task AttributeAlias()
    {
        var sourceCode = """
            using Dummy = Meziantou.Framework.Annotations.StronglyTypedIdAttribute;

            [Dummy(typeof(System.Guid))]
            public partial struct Test { }
            """;

        await TestGeneratedAssembly(sourceCode, type => { });
    }

    [Fact]
    public async Task GenericAttribute()
    {
        var sourceCode = """
            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute<System.Guid>]
            public partial struct Test { }
            """;
        await TestGeneratedAssembly(sourceCode, type => { });
    }

    [Fact]
    public async Task GenericAttribute_Using_Namespace()
    {
        var sourceCode = """
            using Meziantou.Framework.Annotations;
            [StronglyTypedIdAttribute<System.Guid>]
            public partial struct Test { }
            """;

        await TestGeneratedAssembly(sourceCode, type => { });
    }

    [Fact]
    public async Task GenerateStruct_Guid_New()
    {
        var sourceCode = """
            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(System.Guid))]
            public partial struct Test {}
            """;

        await TestGeneratedAssembly(sourceCode, type =>
        {
            var guid = Guid.NewGuid();
            var from = (MethodInfo)type.GetMember("FromGuid").Single();
            var newMethod = (MethodInfo)type.GetMember("New").Single();
            var emptyInstance = from.Invoke(null, [Guid.Empty]);
            var instance = from.Invoke(null, [guid]);
            var newInstance = newMethod.Invoke(null, null);
            Assert.NotEqual(instance, newInstance);
            Assert.NotEqual(emptyInstance, newInstance);
        });
    }

    [Fact]
    public async Task GenerateStruct_Guid_UserDefinedNew()
    {
        var sourceCode = """
            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(System.Guid))]
            public partial struct Test
            {
                public static Test New() => FromGuid(System.Guid.CreateVersion7());
            }
            """;

        await TestGeneratedAssembly(sourceCode, type =>
        {
            var newMethod = (MethodInfo)type.GetMember("New").Single();
            var newInstance = newMethod.Invoke(null, null);
            var value = (Guid)type.GetProperty("Value")!.GetValue(newInstance)!;
            Assert.Equal(7, value.Version);
        });
    }

    [Fact]
    public async Task GenerateStruct_Guid_New_DefaultStrategy()
    {
        var sourceCode = """
            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(System.Guid))]
            public partial struct Test {}
            """;

        await TestGeneratedAssembly(sourceCode, type =>
        {
            Assert.Equal(4, GetNewValue(type).Version);
        });
    }

    [Fact]
    public async Task GenerateStruct_Guid_New_Version4()
    {
        var sourceCode = """
            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(System.Guid), GuidGenerationStrategy = Meziantou.Framework.Annotations.GuidGenerationStrategy.Version4)]
            public partial struct Test {}
            """;

        await TestGeneratedAssembly(sourceCode, type =>
        {
            Assert.Equal(4, GetNewValue(type).Version);
        });
    }

    [Fact]
    public async Task GenerateStruct_Guid_New_Version7()
    {
        var sourceCode = """
            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(System.Guid), GuidGenerationStrategy = Meziantou.Framework.Annotations.GuidGenerationStrategy.Version7)]
            public partial struct Test {}
            """;

        await TestGeneratedAssembly(sourceCode, type =>
        {
            Assert.Equal(7, GetNewValue(type).Version);
        });
    }

    [Fact]
    public async Task GenerateStruct_Guid_New_Version7_GenericAttribute()
    {
        var sourceCode = """
            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute<System.Guid>(GuidGenerationStrategy = Meziantou.Framework.Annotations.GuidGenerationStrategy.Version7)]
            public partial struct Test {}
            """;

        await TestGeneratedAssembly(sourceCode, type =>
        {
            Assert.Equal(7, GetNewValue(type).Version);
        });
    }

    [Fact]
    public async Task GenerateStruct_Guid_New_None()
    {
        var sourceCode = """
            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(System.Guid), GuidGenerationStrategy = Meziantou.Framework.Annotations.GuidGenerationStrategy.None)]
            public partial struct Test {}
            """;

        await TestGeneratedAssembly(sourceCode, type =>
        {
            Assert.Empty(type.GetMember("New"));
            Assert.NotEmpty(type.GetMember("FromGuid"));
        });
    }

    [Fact]
    public async Task GenerateStruct_Guid_New_None_NotSupportedByTargetFramework()
    {
        // GuidGenerationStrategy.None does not use Guid.CreateVersion7(), so no diagnostic must be reported
        var sourceCode = AttributeSourceCode + """

            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(System.Guid), GuidGenerationStrategy = Meziantou.Framework.Annotations.GuidGenerationStrategy.None)]
            public partial struct Test {}
            """;

        var diagnostics = await Analyze(sourceCode, netCoreVersion: "8.0.0", referenceAnnotations: false);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task GenerateStruct_Guid_New_Version7_NotSupportedByTargetFramework()
    {
        // Guid.CreateVersion7() was introduced in .NET 9
        var sourceCode = AttributeSourceCode + """

            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(System.Guid), GuidGenerationStrategy = Meziantou.Framework.Annotations.GuidGenerationStrategy.Version7)]
            public partial struct Test {}
            """;

        var result = await GenerateFiles(sourceCode, netCoreVersion: "8.0.0", referenceAnnotations: false);
        var generatedCode = (await Assert.Single(result.GeneratorResult.GeneratedTrees).GetRootAsync(XunitCancellationToken)).ToFullString();
        Assert.Contains("global::System.Guid.NewGuid()", generatedCode);

        var diagnostics = await Analyze(sourceCode, netCoreVersion: "8.0.0", referenceAnnotations: false);
        Assert.Collection(diagnostics, diag => Assert.Equal("MFSTID0002", diag.Id));
    }

    [Fact]
    public async Task GenerateStruct_Guid_New_Version4_NotReportedOnOldTargetFramework()
    {
        var sourceCode = AttributeSourceCode + """

            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(System.Guid))]
            public partial struct Test {}
            """;

        var diagnostics = await Analyze(sourceCode, netCoreVersion: "8.0.0", referenceAnnotations: false);
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("typeof(int)")]
    [InlineData("typeof(string)")]
    public async Task GuidGenerationStrategy_OnNonGuidType_ReportedByAnalyzer(string idType)
    {
        var sourceCode = $$"""
            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute({{idType}}, GuidGenerationStrategy = Meziantou.Framework.Annotations.GuidGenerationStrategy.Version7)]
            public partial struct Test {}
            """;

        var diagnostics = await Analyze(sourceCode);
        Assert.Collection(diagnostics, diag => Assert.Equal("MFSTID0003", diag.Id));
    }

    [Fact]
    public async Task GuidGenerationStrategy_OnNonGuidGenericType_ReportedByAnalyzer()
    {
        var sourceCode = """
            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute<int>(GuidGenerationStrategy = Meziantou.Framework.Annotations.GuidGenerationStrategy.Version4)]
            public partial struct Test {}
            """;

        var diagnostics = await Analyze(sourceCode);
        Assert.Collection(diagnostics, diag => Assert.Equal("MFSTID0003", diag.Id));
    }

    [Fact]
    public async Task GuidGenerationStrategy_OnGuidType_NotReported()
    {
        var sourceCode = """
            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(System.Guid), GuidGenerationStrategy = Meziantou.Framework.Annotations.GuidGenerationStrategy.Version7)]
            public partial struct Test {}
            """;

        var diagnostics = await Analyze(sourceCode);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task GuidGenerationStrategy_NotSet_NotReported()
    {
        var sourceCode = """
            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(int))]
            public partial struct Test {}
            """;

        var diagnostics = await Analyze(sourceCode);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AssemblyDefaults_GuidGenerationStrategy()
    {
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.StronglyTypedIdDefaults(GuidGenerationStrategy = Meziantou.Framework.Annotations.GuidGenerationStrategy.Version7)]

            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(System.Guid))]
            public partial struct Test {}
            """;

        await TestGeneratedAssembly(sourceCode, type =>
        {
            Assert.Equal(7, GetNewValue(type).Version);
        });
    }

    [Fact]
    public async Task AssemblyDefaults_GuidGenerationStrategy_GenericAttribute()
    {
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.StronglyTypedIdDefaults(GuidGenerationStrategy = Meziantou.Framework.Annotations.GuidGenerationStrategy.Version7)]

            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute<System.Guid>]
            public partial struct Test {}
            """;

        await TestGeneratedAssembly(sourceCode, type =>
        {
            Assert.Equal(7, GetNewValue(type).Version);
        });
    }

    [Fact]
    public async Task AssemblyDefaults_GuidGenerationStrategy_OverriddenByType()
    {
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.StronglyTypedIdDefaults(GuidGenerationStrategy = Meziantou.Framework.Annotations.GuidGenerationStrategy.Version7)]

            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(System.Guid), GuidGenerationStrategy = Meziantou.Framework.Annotations.GuidGenerationStrategy.Version4)]
            public partial struct Test {}
            """;

        await TestGeneratedAssembly(sourceCode, type =>
        {
            Assert.Equal(4, GetNewValue(type).Version);
        });
    }

    [Fact]
    public async Task AssemblyDefaults_StringComparison()
    {
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.StronglyTypedIdDefaults(StringComparison = System.StringComparison.OrdinalIgnoreCase)]

            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(string))]
            public partial struct Test {}
            """;

        await TestGeneratedAssembly(sourceCode, type =>
        {
            var from = (MethodInfo)type.GetMember("FromString").Single();
            Assert.Equal(from.Invoke(null, ["TEST"]), from.Invoke(null, ["test"]));
        });
    }

    [Fact]
    public async Task AssemblyDefaults_GenerateToStringAsRecord()
    {
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.StronglyTypedIdDefaults(GenerateToStringAsRecord = false)]

            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(int))]
            public partial struct Test {}
            """;

        await TestGeneratedAssembly(sourceCode, type =>
        {
            var from = (MethodInfo)type.GetMember("FromInt32").Single();
            Assert.Equal("42", from.Invoke(null, [42])!.ToString());
        });
    }

    [Fact]
    public async Task AssemblyDefaults_AddCodeGeneratedAttribute()
    {
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.StronglyTypedIdDefaults(AddCodeGeneratedAttribute = false)]

            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(int))]
            public partial struct Test {}
            """;

        var generatedCode = await GenerateSingleFile(sourceCode);
        Assert.DoesNotContain("GeneratedCodeAttribute", generatedCode);
    }

    [Fact]
    public async Task AssemblyDefaults_AddCodeGeneratedAttribute_OverriddenByType()
    {
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.StronglyTypedIdDefaults(AddCodeGeneratedAttribute = false)]

            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(int), addCodeGeneratedAttribute: true)]
            public partial struct Test {}
            """;

        var generatedCode = await GenerateSingleFile(sourceCode);
        Assert.Contains("GeneratedCodeAttribute", generatedCode);
    }

    [Fact]
    public async Task AssemblyDefaults_Converters()
    {
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.StronglyTypedIdDefaults(GenerateSystemTextJsonConverter = false, GenerateNewtonsoftJsonConverter = false, GenerateSystemComponentModelTypeConverter = false, GenerateMongoDBBsonSerialization = false)]

            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(int))]
            public partial struct Test {}
            """;

        var generatedCode = await GenerateSingleFile(sourceCode);
        Assert.DoesNotContain("JsonConverterAttribute", generatedCode);
        Assert.DoesNotContain("TypeConverterAttribute", generatedCode);
    }

    [Fact]
    public async Task AssemblyDefaults_Converters_OverriddenByType()
    {
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.StronglyTypedIdDefaults(GenerateSystemTextJsonConverter = false, GenerateSystemComponentModelTypeConverter = false)]

            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(int), generateSystemTextJsonConverter: true)]
            public partial struct Test {}
            """;

        var generatedCode = await GenerateSingleFile(sourceCode);
        Assert.Contains("System.Text.Json.Serialization.JsonConverterAttribute", generatedCode);
        Assert.DoesNotContain("TypeConverterAttribute", generatedCode);
    }

    [Fact]
    public async Task AssemblyDefaults_Converters_OverriddenByTypeUsingPositionalArgument()
    {
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.StronglyTypedIdDefaults(GenerateSystemTextJsonConverter = false, GenerateNewtonsoftJsonConverter = false)]

            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(int), true)]
            public partial struct Test {}
            """;

        var generatedCode = await GenerateSingleFile(sourceCode);
        Assert.Contains("System.Text.Json.Serialization.JsonConverterAttribute", generatedCode);
        Assert.DoesNotContain("Newtonsoft.Json.JsonConverterAttribute", generatedCode);
    }

    [Fact]
    public async Task AssemblyDefaults_GuidGenerationStrategy_NotSupportedByTargetFramework()
    {
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.StronglyTypedIdDefaults(GuidGenerationStrategy = Meziantou.Framework.Annotations.GuidGenerationStrategy.Version7)]

            """ + AttributeSourceCode + """

            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(System.Guid))]
            public partial struct Test {}
            """;

        var result = await GenerateFiles(sourceCode, netCoreVersion: "8.0.0", referenceAnnotations: false);
        var generatedCode = (await Assert.Single(result.GeneratorResult.GeneratedTrees).GetRootAsync(XunitCancellationToken)).ToFullString();
        Assert.Contains("global::System.Guid.NewGuid()", generatedCode);

        var diagnostics = await Analyze(sourceCode, netCoreVersion: "8.0.0", referenceAnnotations: false);
        Assert.Collection(diagnostics, diag => Assert.Equal("MFSTID0002", diag.Id));
    }

    [Fact]
    public async Task AssemblyDefaults_GuidGenerationStrategy_OnNonGuidType_NotReported()
    {
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.StronglyTypedIdDefaults(GuidGenerationStrategy = Meziantou.Framework.Annotations.GuidGenerationStrategy.Version7)]

            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(int))]
            public partial struct Test {}
            """;

        var diagnostics = await Analyze(sourceCode);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AssemblyDefaults_UpdatingTheAttributeRegeneratesTheCode()
    {
        var generator = InstantiateGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator],
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        var compilation = await CreateCompilation("[Meziantou.Framework.Annotations.StronglyTypedId(typeof(System.Guid))] public partial struct Test { }",
        [
            new NuGetReference("Microsoft.NETCore.App.Ref", NetCoreVersion, "ref/"),
        ]);

        driver = driver.RunGenerators(compilation, XunitCancellationToken);
        Assert.Contains("global::System.Guid.NewGuid()", await GetGeneratedCode());

        // Add the assembly-level attribute in another syntax tree
        var attributeTree = CSharpSyntaxTree.ParseText("[assembly: Meziantou.Framework.Annotations.StronglyTypedIdDefaults(GuidGenerationStrategy = Meziantou.Framework.Annotations.GuidGenerationStrategy.Version7)]");
        compilation = compilation.AddSyntaxTrees(attributeTree);
        driver = driver.RunGenerators(compilation, XunitCancellationToken);
        Assert.Contains("global::System.Guid.CreateVersion7()", await GetGeneratedCode());

        // Remove the assembly-level attribute
        compilation = compilation.RemoveSyntaxTrees(attributeTree);
        driver = driver.RunGenerators(compilation, XunitCancellationToken);
        Assert.Contains("global::System.Guid.NewGuid()", await GetGeneratedCode());

        async Task<string> GetGeneratedCode()
        {
            var result = driver.GetRunResult().Results.Single();
            return (await Assert.Single(result.GeneratedSources).SyntaxTree.GetRootAsync(XunitCancellationToken)).ToFullString();
        }
    }

    private static async Task<string> GenerateSingleFile([StringSyntax("c#-test")] string sourceCode)
    {
        var result = await GenerateFiles(sourceCode);
        Assert.Empty(result.GeneratorResult.Diagnostics);
        return (await Assert.Single(result.GeneratorResult.GeneratedTrees).GetRootAsync(XunitCancellationToken)).ToFullString();
    }

    /// <summary>
    /// Declaration of the attribute, so the generator can run against reference assemblies that are older than the ones targeted by the annotations assembly.
    /// </summary>
    private const string AttributeSourceCode = """
        namespace Meziantou.Framework.Annotations
        {
            [System.AttributeUsage(System.AttributeTargets.Struct | System.AttributeTargets.Class)]
            public sealed class StronglyTypedIdAttribute : System.Attribute
            {
                public StronglyTypedIdAttribute(System.Type idType,
                                                bool generateSystemTextJsonConverter = true,
                                                bool generateNewtonsoftJsonConverter = true,
                                                bool generateSystemComponentModelTypeConverter = true,
                                                bool generateMongoDBBsonSerialization = true,
                                                bool addCodeGeneratedAttribute = true)
                {
                    IdType = idType;
                }

                public System.Type IdType { get; }
                public System.StringComparison StringComparison { get; set; }
                public bool GenerateToStringAsRecord { get; set; }
                public GuidGenerationStrategy GuidGenerationStrategy { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Assembly)]
            public sealed class StronglyTypedIdDefaultsAttribute : System.Attribute
            {
                public bool GenerateSystemTextJsonConverter { get; set; }
                public bool GenerateNewtonsoftJsonConverter { get; set; }
                public bool GenerateSystemComponentModelTypeConverter { get; set; }
                public bool GenerateMongoDBBsonSerialization { get; set; }
                public bool AddCodeGeneratedAttribute { get; set; }
                public System.StringComparison StringComparison { get; set; }
                public bool GenerateToStringAsRecord { get; set; }
                public GuidGenerationStrategy GuidGenerationStrategy { get; set; }
            }

            public enum GuidGenerationStrategy
            {
                None = 0,
                Version4 = 1,
                Version7 = 2,
            }
        }
        """;

    private static Guid GetNewValue(Type type)
    {
        var newMethod = (MethodInfo)type.GetMember("New").Single();
        var newInstance = newMethod.Invoke(null, null);
        return (Guid)type.GetProperty("Value")!.GetValue(newInstance)!;
    }

    [Fact]
    public async Task Generate_ExistingOperators()
    {
        var sourceCode = """
            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(int))]
            public partial struct Test : System.IEquatable<Test>
            {
                public override string? ToString() => null;
                public override bool Equals(object? a) => true;
                public override int GetHashCode() => 0;
                public bool Equals(Test? a) => true;
                public static bool operator ==(Test a, Test b) => true;
                public static bool operator !=(Test a, Test b) => true;
            }
            """;

        await TestGeneratedAssembly(sourceCode, type => { });
    }

    [Fact]
    public async Task Generate_StringComparison()
    {
        var sourceCode = """
            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(string), StringComparison = System.StringComparison.OrdinalIgnoreCase)]
            public partial struct Test {}
            """;

        await TestGeneratedAssembly(sourceCode, type =>
        {
            var from = (MethodInfo)type.GetMember("FromString").Single();
            var instance1 = from.Invoke(null, ["test"]);
            var instance2 = from.Invoke(null, ["TEST"]);
            Assert.Equal(instance2, instance1);
        });
    }

    [Fact]
    public async Task GenerateStruct_ToString()
    {
        var sourceCode = """
            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(int))]
            public partial struct Test {}
            """;

        await TestGeneratedAssembly(sourceCode, type =>
        {
            CultureInfoUtilities.UseCulture("sv-SE", () =>
            {
                var from = (MethodInfo)type.GetMember("FromInt32").Single();
                var instance = from.Invoke(null, [-42]);
                Assert.NotNull(instance);
                var str = instance.ToString();
                Assert.Equal("Test { Value = -42 }", str);
            });
        });
    }

    [Fact]
    public async Task GenerateStruct_TryParseDoesNotThrow()
    {
        var sourceCode = """
            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(int))]
            public partial struct Test
            {
                public Test(int value)
                {
                    if (value == 0)
                        throw new System.Exception();

                    _value = value;
                }
            }
            """;

        await TestGeneratedAssembly(sourceCode, type =>
        {
            var from = type.GetMember("TryParse").OfType<MethodInfo>().Single(m => m.GetParameters()[0].ParameterType == typeof(string));
            var parsed = from.Invoke(null, ["0", null]);
            Assert.NotNull(parsed);
            Assert.False((bool)parsed);
        });
    }

    [Fact]
    public async Task GenerateStruct_Parse_ReadOnlySpan()
    {
        var sourceCode = """
            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(int))]
            public partial struct Test {}
            """;

        await TestGeneratedAssembly(sourceCode, type =>
        {
            var parse = type.GetMember("Parse").Length;
            Assert.Equal(2, parse);
        });
    }

    [Fact]
    public async Task GenerateStruct_Parse_ReadOnlySpan_String()
    {
        var sourceCode = """
            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(string))]
            public partial struct Test {}
            """;

        await TestGeneratedAssembly(sourceCode, type =>
        {
            var parse = type.GetMember("Parse").Length;
            Assert.Equal(2, parse);
        });
    }

    [Fact]
    public async Task Generate_IStronglyTypedId()
    {
        var sourceCode = """
            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(string))]
            public partial struct Test {}

            namespace Meziantou.Framework
            {
                interface IStronglyTypedId {}
                interface IStronglyTypedId<T> {}
            }
            """;

        await TestGeneratedAssembly(sourceCode, type =>
        {
            var interfaces = type.GetInterfaces();
            Assert.Contains(interfaces, x => x.FullName == "Meziantou.Framework.IStronglyTypedId");
            Assert.Contains(interfaces, x => x.FullName is not null && x.FullName.StartsWith("Meziantou.Framework.IStronglyTypedId`1", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task Generate_IComparable_Struct_ReferenceType()
    {
        var sourceCode = """
            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(string))]
            public partial struct Test : System.IComparable<Test> {}
            """;

        await TestGeneratedAssembly(sourceCode, type => { });
    }

    [Fact]
    public async Task Generate_IComparable_Struct_ValueType()
    {
        var sourceCode = """
            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(int))]
            public partial struct Test : System.IComparable<Test> {}
            """;

        await TestGeneratedAssembly(sourceCode, type => { });
    }

    [Fact]
    public async Task Generate_IComparable_Class_ReferenceType()
    {
        var sourceCode = """
            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(string))]
            public partial class Test : System.IComparable<Test> {}
            """;

        await TestGeneratedAssembly(sourceCode, type => { });
    }

    [Fact]
    public async Task Generate_IComparable_Class_ValueType()
    {
        var sourceCode = """
            [Meziantou.Framework.Annotations.StronglyTypedIdAttribute(typeof(int))]
            public partial class Test : System.IComparable<Test> {}
            """;

        await TestGeneratedAssembly(sourceCode, type => { });
    }

    [Fact]
    public async Task TestIncrementalSupport()
    {
        var generator = InstantiateGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new ISourceGenerator[] { generator },
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        // Run the generator once
        var sourceCode = "[Meziantou.Framework.Annotations.StronglyTypedId(typeof(int))] public partial struct Test { }";
        var compilation = await CreateCompilation(sourceCode,
        [
            new NuGetReference("Microsoft.NETCore.App.Ref", NetCoreVersion, "ref/"),
        ]);
        var result = RunGenerator();

        // Add dummy syntax tree
        compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(""));
        result = RunGenerator();
        AssertSyntaxStepIsCached(result);
        AssertOutputIsCached(result);

        // Replace struct with record struct
        compilation = compilation.ReplaceSyntaxTree(compilation.SyntaxTrees.First(), CSharpSyntaxTree.ParseText("[Meziantou.Framework.Annotations.StronglyTypedId(typeof(int))] public partial record struct Test { }"));
        result = RunGenerator(validate: (_, symbol) => Assert.Equal((true, true), (symbol!.IsRecord, symbol.IsValueType)));
        AssertSyntaxStepIsNotCached(result);
        AssertOutputIsNotCached(result);

        // Update references
        var newReferences = await NuGetHelpers.GetNuGetReferences("Newtonsoft.Json", "12.0.3", "lib/netstandard2.0/");
        compilation = compilation.AddReferences(newReferences.Select(path => MetadataReference.CreateFromFile(path)));
        result = RunGenerator(validate: (_, symbol) => Assert.NotEmpty(symbol!.GetTypeMembers("TestNewtonsoftJsonConverter")));
        AssertOutputIsNotCached(result);

        // Update syntax
        compilation = compilation.ReplaceSyntaxTree(compilation.SyntaxTrees.First(), CSharpSyntaxTree.ParseText("public partial struct Test { }"));
        result = RunGenerator(shouldGenerateFiles: false);

        // Add dummy syntax tree
        compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("public partial record struct Test2 { }"));
        result = RunGenerator(shouldGenerateFiles: false);
        Assert.Empty(result.TrackedSteps);

        static void AssertOutputIsCached(GeneratorRunResult result)
        {
            Assert.All(result.TrackedOutputSteps.SelectMany(step => step.Value).SelectMany(value => value.Outputs), output => Assert.Equal(IncrementalStepRunReason.Cached, output.Reason));
        }

        static void AssertOutputIsNotCached(GeneratorRunResult result)
        {
            Assert.All(result.TrackedOutputSteps.SelectMany(step => step.Value).SelectMany(value => value.Outputs), output => Assert.NotEqual(IncrementalStepRunReason.Cached, output.Reason));
        }

        static void AssertSyntaxStepIsCached(GeneratorRunResult result)
        {
            Assert.All(result.TrackedSteps["Syntax"].SelectMany(step => step.Outputs), output => Assert.Equal(IncrementalStepRunReason.Cached, output.Reason));
        }

        static void AssertSyntaxStepIsNotCached(GeneratorRunResult result)
        {
            Assert.DoesNotContain(IncrementalStepRunReason.Cached, result.TrackedSteps["Syntax"].SelectMany(step => step.Outputs).Select(output => output.Reason));
        }

        GeneratorRunResult RunGenerator(bool shouldGenerateFiles = true, Action<Compilation, INamedTypeSymbol?>? validate = null)
        {
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics, XunitCancellationToken);
            Assert.Empty(diagnostics);

            var type = outputCompilation.GetTypeByMetadataName("Test");
            validate?.Invoke(outputCompilation, type);
            if (shouldGenerateFiles)
            {
                Assert.NotNull(type);
                Assert.NotEmpty(type.GetMembers("FromInt32"));

                // Run the driver twice to ensure the second invocation is cached
                var driver2 = driver.RunGenerators(compilation, XunitCancellationToken);
                AssertSyntaxStepIsCached(driver2.GetRunResult().Results.Single());
                AssertOutputIsCached(driver2.GetRunResult().Results.Single());
            }

            return driver.GetRunResult().Results.Single();
        }
    }

    public sealed record BuildMatrixArguments(string IdType, string TypeDeclaration, NuGetReference[] NuGetReferences);

    public static TheoryData<BuildMatrixArguments> BuildMatrixTestCases()
    {
        return [.. BuildMatrixTestCases()];

        static IEnumerable<BuildMatrixArguments> BuildMatrixTestCases()
        {
            var types = new[]
            {
                "global::System.Boolean",
                "global::System.Byte",
                "global::System.DateTime",
                "global::System.DateTimeOffset",
                "global::System.Decimal",
                "global::System.Double",
                "global::System.Guid",
                "global::System.Int16",
                "global::System.Int32",
                "global::System.Int64",
                "global::System.SByte",
                "global::System.Single",
                "global::System.String",
                "global::System.UInt16",
                "global::System.UInt32",
                "global::System.UInt64",
            };

            var declarations = new[]
            {
                "partial struct Test { }",
                "readonly partial struct Test { }",
                "partial class Test { }",
                "sealed partial class Test { }",
                "partial record Test { }",
                "sealed partial record Test { }",
                "partial record struct Test { }",
                "readonly partial record struct Test { }",
            };

            foreach (var type in types)
            {
                foreach (var declaration in declarations)
                {
                    foreach (var netFrameworkVersion in new[] { "472", "481" })
                    {
                        yield return new BuildMatrixArguments(type, declaration,
                        [
                            new NuGetReference("Microsoft.NETFramework.ReferenceAssemblies.net" + netFrameworkVersion, "1.0.3", ""),
                        ]);
                    }

                    yield return new BuildMatrixArguments(type, declaration, [new NuGetReference("Microsoft.NETCore.App.Ref", NetCoreVersion, "ref/")]);

                    foreach (var newtonsoftJson in new[] { "12.0.3" })
                    {
                        yield return new BuildMatrixArguments(type, declaration,
                        [
                            new NuGetReference("Microsoft.NETCore.App.Ref", NetCoreVersion, "ref/"),
                            new NuGetReference("Newtonsoft.Json", newtonsoftJson, "lib/netstandard2.0/"),
                        ]);
                    }

                    foreach (var mongodb in new[] { "2.18.0" })
                    {
                        yield return new BuildMatrixArguments(type, declaration,
                        [
                            new NuGetReference("Microsoft.NETCore.App.Ref", NetCoreVersion, "ref/"),
                            new NuGetReference("MongoDB.Bson", mongodb, "lib/netstandard2.1/"),
                        ]);
                    }
                }
            }

            // Add specific test cases
            foreach (var declaration in declarations)
            {
                yield return new BuildMatrixArguments("global::System.Half", declaration, [new NuGetReference("Microsoft.NETCore.App.Ref", NetCoreVersion, "ref/")]);
                yield return new BuildMatrixArguments("global::System.Int128", declaration, [new NuGetReference("Microsoft.NETCore.App.Ref", NetCoreVersion, "ref/")]);
                yield return new BuildMatrixArguments("global::System.UInt128", declaration, [new NuGetReference("Microsoft.NETCore.App.Ref", NetCoreVersion, "ref/")]);
                yield return new BuildMatrixArguments("global::System.Numerics.BigInteger", declaration, [new NuGetReference("Microsoft.NETCore.App.Ref", NetCoreVersion, "ref/")]);

                foreach (var mongodb in new[] { "2.29.0" })
                {
                    yield return new BuildMatrixArguments("global::MongoDB.Bson.ObjectId", declaration,
                    [
                        new NuGetReference("Microsoft.NETCore.App.Ref", NetCoreVersion, "ref/"),
                        new NuGetReference("MongoDB.Bson", mongodb, "lib/netstandard2.1/"),
                    ]);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(BuildMatrixTestCases))]
    public async Task BuildMatrix(BuildMatrixArguments arg)
    {
        var generator = InstantiateGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator],
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        // Run the generator once
        var sourceCode = $$"""
            namespace Dummy.System { }
            namespace Dummy.Newtonsoft { }
            namespace Dummy.MongoDB { }
            namespace Dummy
            {
                [Meziantou.Framework.Annotations.StronglyTypedId(typeof({{arg.IdType}}))] {{arg.TypeDeclaration}}
            }
            """;
        var compilation = await CreateCompilation(sourceCode, arg.NuGetReferences);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics, XunitCancellationToken);
        Assert.Empty(diagnostics);

        var runResult = driver.GetRunResult();

        // Validate the output project compiles
        using var ms = new MemoryStream();
        var compilationOutput = outputCompilation.Emit(ms, cancellationToken: XunitCancellationToken);

        var diags = string.Join('\n', compilationOutput.Diagnostics);
        var generated = runResult.GeneratedTrees.Length > 0 ? (await runResult.GeneratedTrees[0].GetRootAsync(XunitCancellationToken)).ToFullString() : "<no file generated>";
        Assert.True(compilationOutput.Success);
        Assert.Empty(compilationOutput.Diagnostics);
    }

    /// <summary>Loads the generated assembly and ensures the members are generated for every expected type.</summary>
    private static void AssertGeneratedTypes((GeneratorDriverRunResult GeneratorResult, Compilation OutputCompilation, byte[]? Assembly, byte[] Symbols) result, string[] typeNames)
    {
        Assert.NotNull(result.Assembly);

        var alc = new AssemblyLoadContext("test", isCollectible: true);
        try
        {
            var assembly = alc.LoadFromStream(new MemoryStream(result.Assembly), new MemoryStream(result.Symbols));
            foreach (var typeName in typeNames)
            {
                var type = assembly.GetType(typeName);
                Assert.NotNull(type);

                var from = (MethodInfo)type.GetMember("FromInt32").Single();
                var instance = from.Invoke(null, [10]);
                Assert.Equal("10", System.Text.Json.JsonSerializer.Serialize(instance));
            }
        }
        finally
        {
            alc.Unload();
        }
    }

    private static Task TestGeneratedAssembly([StringSyntax("c#-test")] string sourceCode, Action<Type> assert)
    {
        return TestGeneratedAssembly(sourceCode, typeName: null, assert);
    }

    private static async Task TestGeneratedAssembly([StringSyntax("c#-test")] string sourceCode, string? typeName, Action<Type> assert, bool mustGenerateTrees = true)
    {
        var result = await GenerateFiles(sourceCode);
        Assert.Empty(result.GeneratorResult.Diagnostics);

        if (mustGenerateTrees)
        {
            Assert.Single(result.GeneratorResult.GeneratedTrees);
        }
        else
        {
            Assert.Empty(result.GeneratorResult.GeneratedTrees);
            return;
        }

        var alc = new AssemblyLoadContext("test", isCollectible: true);
        try
        {
            Assert.NotNull(result.Assembly);
            var assembly = alc.LoadFromStream(new MemoryStream(result.Assembly), new MemoryStream(result.Symbols));
            var type = assembly.GetType(typeName ?? "Test");
            Assert.NotNull(type);

            assert(type);
        }
        finally
        {
            alc.Unload();
        }
    }
}
