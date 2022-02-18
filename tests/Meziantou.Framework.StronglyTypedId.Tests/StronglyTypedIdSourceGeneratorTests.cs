#pragma warning disable MA0101 // String contains an implicit end of line character
using System.Reflection;
using System.Runtime.Loader;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.StronglyTypedId.Tests;

public sealed class StronglyTypedIdSourceGeneratorTests
{
    private static async Task<(GeneratorDriverRunResult GeneratorResult, Compilation OutputCompilation, byte[] Assembly)> GenerateFiles(string file, bool mustCompile = true, string[] assemblyLocations = null)
    {
        var netcoreRef = await NuGetHelpers.GetNuGetReferences("Microsoft.NETCore.App.Ref", "5.0.0", "ref/net5.0/");
        var newtonsoftJsonRef = await NuGetHelpers.GetNuGetReferences("Newtonsoft.Json", "12.0.3", "lib/netstandard2.0/");
        assemblyLocations ??= Array.Empty<string>();
        var references = assemblyLocations
            .Concat(netcoreRef)
            .Concat(newtonsoftJsonRef)
            .Select(loc => MetadataReference.CreateFromFile(loc))
            .ToArray();

        var compilation = CSharpCompilation.Create("compilation",
            new[] { CSharpSyntaxTree.ParseText(file) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new StronglyTypedIdSourceGenerator();
        var wrapperType = (ISourceGenerator)Activator.CreateInstance(Type.GetType("Microsoft.CodeAnalysis.IncrementalGeneratorWrapper, Microsoft.CodeAnalysis", throwOnError: true), generator);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new ISourceGenerator[] { wrapperType });

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
            result.Success.Should().BeTrue("Project cannot build:\n" + diags + "\n\n\n" + generated);
            result.Diagnostics.Should().BeEmpty();
        }

        return (runResult, outputCompilation, result.Success ? ms.ToArray() : null);
    }

    [Fact]
    public async Task GenerateStructInNamespaceAndClass()
    {
        var sourceCode = @"
namespace A
{
    namespace B
    {
        partial class C
        {
            [StronglyTypedId(typeof(int))]
            public partial struct Test {}
        }
    }
}";
        var result = await GenerateFiles(sourceCode);

        result.GeneratorResult.Diagnostics.Should().BeEmpty();
        result.GeneratorResult.GeneratedTrees.Length.Should().Be(2);

        var alc = new AssemblyLoadContext("test", isCollectible: true);
        try
        {
            alc.LoadFromStream(new MemoryStream(result.Assembly));
            foreach (var a in alc.Assemblies)
            {
                var type = a.GetType("A.B.C+Test");
                var from = (MethodInfo)type.GetMember("FromInt32").Single();
                var instance = from.Invoke(null, new object[] { 10 });
                var json = System.Text.Json.JsonSerializer.Serialize(instance);
                var deserialized = System.Text.Json.JsonSerializer.Deserialize(json, type);
                var deserialized2 = System.Text.Json.JsonSerializer.Deserialize(@"{ ""a"": {}, ""b"": false, ""Value"": 10 }", type);

                json.Should().Be("10");
                deserialized.Should().Be(instance);
                deserialized2.Should().Be(instance);
            }
        }
        finally
        {
            alc.Unload();
        }
    }

    [Fact]
    public async Task GenerateStructInNamespace()
    {
        var sourceCode = @"
namespace A
{
    namespace B
    {
        [StronglyTypedIdAttribute(typeof(int))]
        public partial struct Test {}
    }
}";
        var result = await GenerateFiles(sourceCode);

        result.GeneratorResult.Diagnostics.Should().BeEmpty();
        result.GeneratorResult.GeneratedTrees.Length.Should().Be(2);

        var alc = new AssemblyLoadContext("test", isCollectible: true);
        try
        {
            alc.LoadFromStream(new MemoryStream(result.Assembly));
            foreach (var a in alc.Assemblies)
            {
                var type = a.GetType("A.B.Test");
                var from = (MethodInfo)type.GetMember("FromInt32").Single();
                var instance = from.Invoke(null, new object[] { 10 });
                var json = System.Text.Json.JsonSerializer.Serialize(instance);
                var deserialized = System.Text.Json.JsonSerializer.Deserialize(json, type);
                var deserialized2 = System.Text.Json.JsonSerializer.Deserialize(@"{ ""a"": {}, ""b"": false, ""Value"": 10 }", type);

                json.Should().Be("10");
                deserialized.Should().Be(instance);
                deserialized2.Should().Be(instance);
            }
        }
        finally
        {
            alc.Unload();
        }
    }

    [Fact]
    public async Task GenerateStruct_Guid_New()
    {
        var sourceCode = @"
[StronglyTypedIdAttribute(typeof(System.Guid))]
public partial struct Test {}
";
        var result = await GenerateFiles(sourceCode);

        result.GeneratorResult.Diagnostics.Should().BeEmpty();
        result.GeneratorResult.GeneratedTrees.Length.Should().Be(2);

        var alc = new AssemblyLoadContext("test", isCollectible: true);
        try
        {
            alc.LoadFromStream(new MemoryStream(result.Assembly));
            foreach (var a in alc.Assemblies)
            {
                var guid = Guid.NewGuid();
                var type = a.GetType("Test");
                var from = (MethodInfo)type.GetMember("FromGuid").Single();
                var newMethod = (MethodInfo)type.GetMember("New").Single();
                var emptyInstance = from.Invoke(null, new object[] { Guid.Empty });
                var instance = from.Invoke(null, new object[] { guid });
                var newInstance = newMethod.Invoke(null, null);
                newInstance.Should().NotBe(instance);
                newInstance.Should().NotBe(emptyInstance);
            }
        }
        finally
        {
            alc.Unload();
        }
    }

    [Fact]
    public async Task Generate_ExistingOperators()
    {
        var sourceCode = @"
[StronglyTypedIdAttribute(typeof(int))]
public partial struct Test : System.IEquatable<Test>
{
    public override string ToString() => null;
    public override bool Equals(object a) => true;
    public override int GetHashCode() => 0;    
    public bool Equals(Test a) => true;
    public static bool operator ==(Test a, Test b) => true;
    public static bool operator !=(Test a, Test b) => true;
}
";
        var result = await GenerateFiles(sourceCode);

        result.GeneratorResult.Diagnostics.Should().BeEmpty();
        result.GeneratorResult.GeneratedTrees.Length.Should().Be(2);

        result.Assembly.Should().NotBeNull();
    }

    [Fact]
    public async Task GenerateStruct_ToString()
    {
        var sourceCode = @"
[StronglyTypedIdAttribute(typeof(int))]
public partial struct Test {}
";
        var result = await GenerateFiles(sourceCode);

        result.GeneratorResult.Diagnostics.Should().BeEmpty();
        result.GeneratorResult.GeneratedTrees.Length.Should().Be(2);

        var alc = new AssemblyLoadContext("test", isCollectible: true);
        try
        {
            alc.LoadFromStream(new MemoryStream(result.Assembly));
            foreach (var a in alc.Assemblies)
            {
                CultureInfoUtilities.UseCulture("sv-SE", () =>
                {
                    var type = a.GetType("Test");
                    var from = (MethodInfo)type.GetMember("FromInt32").Single();
                    var instance = from.Invoke(null, new object[] { -42 });
                    var str = instance.ToString();

                    str.Should().Be("Test { Value = -42 }");
                });
            }
        }
        finally
        {
            alc.Unload();
        }
    }

    [Fact]
    public async Task GenerateStruct_Parse_ReadOnlySpan()
    {
        var sourceCode = @"
[StronglyTypedIdAttribute(typeof(int))]
public partial struct Test {}
";
        var result = await GenerateFiles(sourceCode);

        result.GeneratorResult.Diagnostics.Should().BeEmpty();
        result.GeneratorResult.GeneratedTrees.Length.Should().Be(2);

        var alc = new AssemblyLoadContext("test", isCollectible: true);
        try
        {
            alc.LoadFromStream(new MemoryStream(result.Assembly));
            foreach (var a in alc.Assemblies)
            {
                var type = a.GetType("Test");
                var parse = type.GetMember("Parse").Length;
                parse.Should().Be(2);
            }
        }
        finally
        {
            alc.Unload();
        }
    }

    [Fact]
    public async Task GenerateStruct_Parse_ReadOnlySpan_String()
    {
        var sourceCode = @"
[StronglyTypedIdAttribute(typeof(string))]
public partial struct Test {}
";
        var result = await GenerateFiles(sourceCode);

        result.GeneratorResult.Diagnostics.Should().BeEmpty();
        result.GeneratorResult.GeneratedTrees.Length.Should().Be(2);

        var alc = new AssemblyLoadContext("test", isCollectible: true);
        try
        {
            alc.LoadFromStream(new MemoryStream(result.Assembly));
            foreach (var a in alc.Assemblies)
            {
                var type = a.GetType("Test");
                var parse = type.GetMember("Parse").Length;
                parse.Should().Be(2);
            }
        }
        finally
        {
            alc.Unload();
        }
    }
}
