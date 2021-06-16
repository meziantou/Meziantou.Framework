#pragma warning disable MA0101 // String contains an implicit end of line character
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Meziantou.Framework.FastEnumToStringGenerator.Tests
{
    public sealed class EnumToStringSourceGeneratorTests
    {
        private static async Task<(GeneratorDriverRunResult GeneratorResult, Compilation OutputCompilation, byte[] Assembly)> GenerateFiles(string file, bool mustCompile = true, string[] assemblyLocations = null)
        {
            var netcoreRef = await NuGetHelpers.GetNuGetReferences("Microsoft.NETCore.App.Ref", "5.0.0", "ref/net5.0/");
            assemblyLocations ??= Array.Empty<string>();
            var references = assemblyLocations
                .Concat(netcoreRef)
                .Select(loc => MetadataReference.CreateFromFile(loc))
                .ToArray();

            var compilation = CSharpCompilation.Create("compilation",
                new[] { CSharpSyntaxTree.ParseText(file) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generator = new EnumToStringSourceGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                generators: new ISourceGenerator[] { generator });

            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
            diagnostics.Should().BeEmpty();

            var runResult = driver.GetRunResult();

            // Validate the output project compiles
            using var ms = new MemoryStream();
            var result = outputCompilation.Emit(ms);
            if (mustCompile)
            {
                var diags = string.Join("\n", result.Diagnostics);
                var generated = (await runResult.GeneratedTrees[1].GetRootAsync()).ToFullString();
                result.Success.Should().BeTrue("Project should build build:\n" + diags + "\n\n\n" + generated);
                result.Diagnostics.Should().BeEmpty();
            }

            return (runResult, outputCompilation, result.Success ? ms.ToArray() : null);
        }

        [Fact]
        public async Task GenerateStructInNamespaceAndClass()
        {
            var sourceCode = @"
[assembly: FastEnumToStringAttribute(typeof(A.B.C.D))]
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
}";
            var (generatorResult, _, assembly) = await GenerateFiles(sourceCode);

            generatorResult.Diagnostics.Should().BeEmpty();
            generatorResult.GeneratedTrees.Length.Should().Be(2);

            var asm = Assembly.Load(assembly);
            var type = asm.GetType("A.B.C");
            var method = type.GetMethod("Sample", BindingFlags.Public | BindingFlags.Static);
            method.Invoke(null, new object[] { 1 }).Should().Be("Value2");
            method.Invoke(null, new object[] { 999 }).Should().Be("999");

        }
    }
}
