#pragma warning disable MA0101 // String contains an implicit end of line character
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using FluentAssertions.Execution;
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

        [Fact]
        public async Task GeneratePublicType()
        {
            var sourceCode = @"
using SampleNs1;

[assembly: FastEnumToStringAttribute(typeof(A.B.D), IsPublic = true, ExtensionMethodNamespace = ""SampleNs1"")]
[assembly: FastEnumToStringAttribute(typeof(A.B.E), IsPublic = false, ExtensionMethodNamespace = ""SampleNs1"")]
[assembly: FastEnumToStringAttribute(typeof(A.B.F), ExtensionMethodNamespace = ""SampleNs3"")]
[assembly: FastEnumToStringAttribute(typeof(A.B.G), ExtensionMethodNamespace = ""SampleNs4"")]

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
}";
            var (generatorResult, _, assembly) = await GenerateFiles(sourceCode);

            generatorResult.Diagnostics.Should().BeEmpty();
            generatorResult.GeneratedTrees.Length.Should().Be(2);

            var asm = Assembly.Load(assembly);
            var ns1Type = asm.GetType("SampleNs1.FastEnumToStringExtensions");
            var methods1 = ns1Type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(m => m.Name == "ToStringFast")
                .OrderBy(m => m.GetParameters()[0].ParameterType.FullName);

            using (new AssertionScope())
            {
                ns1Type.Should().HaveAccessModifier(FluentAssertions.Common.CSharpAccessModifier.Public);
                methods1.Should().SatisfyRespectively(
                    m => m.Should().HaveAccessModifier(FluentAssertions.Common.CSharpAccessModifier.Public),
                    m => m.Should().HaveAccessModifier(FluentAssertions.Common.CSharpAccessModifier.Internal));

                var ns3Type = asm.GetType("SampleNs3.FastEnumToStringExtensions");
                var methods3 = ns3Type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .Where(m => m.Name == "ToStringFast")
                    .OrderBy(m => m.GetParameters()[0].ParameterType.FullName);

                ns3Type.Should().HaveAccessModifier(FluentAssertions.Common.CSharpAccessModifier.Internal);
                methods3.Should().SatisfyRespectively(
                    m => m.Should().HaveAccessModifier(FluentAssertions.Common.CSharpAccessModifier.Internal));

                var ns4Type = asm.GetType("SampleNs4.FastEnumToStringExtensions");
                var methods4 = ns4Type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .Where(m => m.Name == "ToStringFast")
                    .OrderBy(m => m.GetParameters()[0].ParameterType.FullName);

                ns4Type.Should().HaveAccessModifier(FluentAssertions.Common.CSharpAccessModifier.Public);
                methods4.Should().SatisfyRespectively(
                    m => m.Should().HaveAccessModifier(FluentAssertions.Common.CSharpAccessModifier.Public));
            }
        }
    }
}
