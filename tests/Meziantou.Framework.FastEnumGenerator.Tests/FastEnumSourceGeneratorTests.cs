#pragma warning disable MA0101 // String contains an implicit end of line character
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Meziantou.Xunit;
using Meziantou.Framework.FastEnumGenerator;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.FastEnumGenerator.Tests;

public sealed class FastEnumSourceGeneratorTests
{
    private static async Task<(GeneratorDriverRunResult GeneratorResult, Compilation OutputCompilation, byte[]? Assembly)> GenerateFiles(string file, LanguageVersion languageVersion, bool mustCompile = true, string[]? assemblyLocations = null)
    {
        var netcoreRef = await NuGetHelpers.GetNuGetReferences("Microsoft.NETCore.App.Ref", "8.0.0", "ref/net8.0/");
        assemblyLocations ??= [];
        var references = assemblyLocations
            .Concat(netcoreRef)
            .Select(loc => MetadataReference.CreateFromFile(loc))
            .ToArray();

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(languageVersion);
        var compilation = CSharpCompilation.Create("compilation",
            [CSharpSyntaxTree.ParseText(file, parseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new FastEnumSourceGenerator().AsSourceGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        Assert.Empty(diagnostics);

        var runResult = driver.GetRunResult();

        // Validate the output project compiles
        using var ms = new MemoryStream();
        var result = outputCompilation.Emit(ms);
        if (mustCompile)
        {
            var diags = string.Join('\n', result.Diagnostics);
            Assert.True(result.Success, diags);
            Assert.Empty(result.Diagnostics);
        }

        return (runResult, outputCompilation, result.Success ? ms.ToArray() : null);
    }

    [Fact]
    public async Task GenerateStructInNamespaceAndClass()
    {
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(A.B.C.D))]
            namespace A
            {
                namespace B
                {
                    class C
                    {
                        public static string Sample(D value) => value.ToStringFast();

                        public enum D
                        {
                            Value1,
                            Value2,
                        }
                    }
                }
            }
            """;
        var (generatorResult, _, assembly) = await GenerateFiles(sourceCode, LanguageVersion.Preview);
        Assert.NotNull(assembly);
        Assert.Empty(generatorResult.Diagnostics);
        Assert.Equal(3, generatorResult.GeneratedTrees.Length);
        Assert.Contains(generatorResult.GeneratedTrees, static tree => tree.FilePath.EndsWith("FastEnumExtensions.g.cs", StringComparison.Ordinal));
        Assert.Contains(generatorResult.GeneratedTrees, static tree => tree.FilePath.EndsWith("Microsoft.CodeAnalysis.EmbeddedAttribute.g.cs", StringComparison.Ordinal));
        Assert.Contains(generatorResult.GeneratedTrees, static tree => tree.FilePath.EndsWith("Meziantou.Framework.Annotations.FastEnumAttribute.g.cs", StringComparison.Ordinal));

        var asm = Assembly.Load(assembly);
        var type = asm.GetType("A.B.C");
        Assert.NotNull(type);
        var method = type.GetMethod("Sample", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.Equal("Value2", method.Invoke(null, [1]));
        Assert.Equal("999", method.Invoke(null, [999]));

    }

    [Fact]
    public async Task GeneratePublicType()
    {
        var sourceCode = """
            using SampleNs1;

            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(A.B.D), IsPublic = true, ExtensionMethodNamespace = "SampleNs1")]
            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(A.B.E), IsPublic = false, ExtensionMethodNamespace = "SampleNs1")]
            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(A.B.F), ExtensionMethodNamespace = "SampleNs3")]
            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(A.B.G), ExtensionMethodNamespace = "SampleNs4")]

            namespace A
            {
                namespace B
                {
                    public class C
                    {
                        public static string Sample(D value) => value.ToStringFast();
                    }

                    public enum D { Value1 }
                    public enum E { Value1 }
                    internal enum F { Value1 }
                    public enum G { Value1 }
                }
            }
            """;
        var (generatorResult, _, assembly) = await GenerateFiles(sourceCode, LanguageVersion.Preview);
        Assert.NotNull(assembly);
        Assert.Empty(generatorResult.Diagnostics);
        Assert.Equal(3, generatorResult.GeneratedTrees.Length);
        Assert.Contains(generatorResult.GeneratedTrees, static tree => tree.FilePath.EndsWith("FastEnumExtensions.g.cs", StringComparison.Ordinal));
        Assert.Contains(generatorResult.GeneratedTrees, static tree => tree.FilePath.EndsWith("Microsoft.CodeAnalysis.EmbeddedAttribute.g.cs", StringComparison.Ordinal));
        Assert.Contains(generatorResult.GeneratedTrees, static tree => tree.FilePath.EndsWith("Meziantou.Framework.Annotations.FastEnumAttribute.g.cs", StringComparison.Ordinal));

        var asm = Assembly.Load(assembly);
        var ns1Types = asm.GetTypes()
            .Where(type => string.Equals(type.Namespace, "SampleNs1", StringComparison.Ordinal) && type.Name.StartsWith("FastEnumExtensions_", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(ns1Types);

        var methods1 = ns1Types
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(m => m.Name == "ToStringFast" && m.GetParameters().Length == 1)
            .OrderBy(m => m.GetParameters()[0].ParameterType.FullName, StringComparer.Ordinal);

        Assert.Contains(ns1Types, static type => type.IsPublic);
        Assert.Collection(methods1,
            m => Assert.True(m.IsPublic),
            m => Assert.False(m.IsPublic));

        var ns3Type = Assert.Single(asm.GetTypes().Where(type => string.Equals(type.Namespace, "SampleNs3", StringComparison.Ordinal) && type.Name.StartsWith("FastEnumExtensions_", StringComparison.Ordinal)));
        var methods3 = ns3Type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => string.Equals(m.Name, "ToStringFast", StringComparison.Ordinal) && m.GetParameters().Length == 1)
            .OrderBy(m => m.GetParameters()[0].ParameterType.FullName, StringComparer.Ordinal);

        Assert.False(ns3Type.IsPublic);

        var ns4Type = Assert.Single(asm.GetTypes().Where(type => string.Equals(type.Namespace, "SampleNs4", StringComparison.Ordinal) && type.Name.StartsWith("FastEnumExtensions_", StringComparison.Ordinal)));
        var methods4 = ns4Type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.Name == "ToStringFast" && m.GetParameters().Length == 1)
            .OrderBy(m => m.GetParameters()[0].ParameterType.FullName, StringComparer.Ordinal);

        Assert.True(ns4Type.IsPublic);
        var m = Assert.Single(methods4);
        Assert.True(m.IsPublic);
    }

    [Fact]
    public async Task GenerateMetadataAwareMethods()
    {
        var sourceCode = """
            using System;
            using System.ComponentModel.DataAnnotations;
            using System.Runtime.Serialization;

            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Sample.Color))]
            namespace Sample
            {
                public class Helpers
                {
                    public static string GetName(Color value, bool useMetadata) => value.ToStringFast(useMetadata);
                    public static bool HasFlag(Color value, Color flag) => value.HasFlag(flag);
                    public static string GetNameOnly(Color value) => value.GetName();
                }

                [Flags]
                public enum Color
                {
                    [Display(Name = "Blue metadata")]
                    Blue = 1,
                    [EnumMember(Value = "red metadata")]
                    Red = 2,
                }
            }
            """;

        var (generatorResult, _, assembly) = await GenerateFiles(sourceCode, LanguageVersion.Preview);
        Assert.NotNull(assembly);
        var generatedCode = await GetGeneratedCode(generatorResult, "FastEnumExtensions.g.cs");
        Assert.Contains("ToStringFast(this global::Sample.Color value, bool useMetadata)", generatedCode, StringComparison.Ordinal);
        Assert.Contains("HasFlag(this global::Sample.Color instance, global::Sample.Color flag)", generatedCode, StringComparison.Ordinal);
        Assert.Contains("GetName(this global::Sample.Color instance)", generatedCode, StringComparison.Ordinal);

        var asm = Assembly.Load(assembly);
        var helperType = asm.GetType("Sample.Helpers");
        Assert.NotNull(helperType);

        var toStringMethod = helperType.GetMethod("GetName", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(toStringMethod);
        Assert.Equal("Blue", toStringMethod.Invoke(null, [(int)1, false]));
        Assert.Equal("Blue metadata", toStringMethod.Invoke(null, [(int)1, true]));

        var hasFlagMethod = helperType.GetMethod("HasFlag", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(hasFlagMethod);
        Assert.Equal(true, hasFlagMethod.Invoke(null, [(int)3, (int)2]));

        var getNameMethod = helperType.GetMethod("GetNameOnly", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(getNameMethod);
        Assert.Equal("Red", getNameMethod.Invoke(null, [(int)2]));
    }

    [Fact]
    public async Task GenerateCSharp14ExtensionMembers()
    {
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Sample.Color))]
            namespace Sample
            {
                public enum Color
                {
                    Blue,
                    Red,
                }
            }
            """;

        var (generatorResult, _, assembly) = await GenerateFiles(sourceCode, LanguageVersion.Preview);
        Assert.NotNull(assembly);
        var generatedCode = await GetGeneratedCode(generatorResult, "FastEnumExtensions.g.cs");
        Assert.Contains("extension(global::Sample.Color)", generatedCode, StringComparison.Ordinal);
        Assert.Contains("static global::Sample.Color Parse(string value, bool ignoreCase)", generatedCode, StringComparison.Ordinal);
        Assert.Contains("static bool TryParse(global::System.ReadOnlySpan<char> value, bool ignoreCase, bool useMetadata, out global::Sample.Color result)", generatedCode, StringComparison.Ordinal);
        Assert.Contains("static string[] GetNames(bool useMetadata)", generatedCode, StringComparison.Ordinal);
        Assert.Contains("static global::Sample.Color[] GetValues()", generatedCode, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoNotGenerateCSharp14ExtensionMembersWhenDisabled()
    {
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Sample.Color))]
            namespace Sample
            {
                public enum Color
                {
                    Blue,
                    Red,
                }
            }
            """;

        var (generatorResult, _, assembly) = await GenerateFiles(sourceCode, LanguageVersion.CSharp12);
        Assert.NotNull(assembly);
        var generatedCode = await GetGeneratedCode(generatorResult, "FastEnumExtensions.g.cs");
        Assert.DoesNotContain("extension(global::Sample.Color)", generatedCode, StringComparison.Ordinal);
        Assert.DoesNotContain("GetNames(bool useMetadata)", generatedCode, StringComparison.Ordinal);
        Assert.DoesNotContain("GetValues()", generatedCode, StringComparison.Ordinal);
    }

    private static async Task<string> GetGeneratedCode(GeneratorDriverRunResult generatorResult, string fileName)
    {
        var tree = Assert.Single(generatorResult.GeneratedTrees, tree => tree.FilePath.EndsWith(fileName, StringComparison.Ordinal));
        return (await tree.GetRootAsync()).ToFullString();
    }
}
