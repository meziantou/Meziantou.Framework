#pragma warning disable MA0101 // String contains an implicit end of line character
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Meziantou.Framework.StronglyTypedId.Tests
{
    public sealed class StronglyTypedIdSourceGeneratorTests
    {
        private static async Task<(GeneratorDriverRunResult GeneratorResult, Compilation OutputCompilation, byte[] Assembly)> GenerateFiles(string file, string[] assemblyLocations = null)
        {
            var refs = await NuGetHelpers.GetNuGetReferences("Microsoft.NETCore.App.Ref", "5.0.0", "ref/net5.0/");
            assemblyLocations ??= Array.Empty<string>();
            var references = assemblyLocations.Concat(refs).Select(loc => MetadataReference.CreateFromFile(loc)).ToArray();

            var compilation = CSharpCompilation.Create("compilation",
                new[] { CSharpSyntaxTree.ParseText(file) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generator = new StronglyTypedIdSourceGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                generators: new ISourceGenerator[] { generator });

            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
            Assert.Empty(diagnostics);

            // Validate the output project compiles
            using var ms = new MemoryStream();
            var result = outputCompilation.Emit(ms);

            return (driver.GetRunResult(), outputCompilation, result.Success ? ms.ToArray() : null);
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

            Assert.Empty(result.GeneratorResult.Diagnostics);
            Assert.Equal(2, result.GeneratorResult.GeneratedTrees.Length);

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

                    Assert.Equal("10", json);
                    Assert.Equal(instance, deserialized);
                    Assert.Equal(instance, deserialized2);
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

            Assert.Empty(result.GeneratorResult.Diagnostics);
            Assert.Equal(2, result.GeneratorResult.GeneratedTrees.Length);

            ValidateType(result.Assembly, "A.B.Test", "FromInt32", 10);
        }

        [Fact]
        public async Task GenerateStruct_Guid_New()
        {
            var sourceCode = @"
[StronglyTypedIdAttribute(typeof(System.Guid))]
public partial struct Test {}
";
            var result = await GenerateFiles(sourceCode);

            Assert.Empty(result.GeneratorResult.Diagnostics);
            Assert.Equal(2, result.GeneratorResult.GeneratedTrees.Length);

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
                    Assert.NotEqual(instance, newInstance);
                    Assert.NotEqual(emptyInstance, newInstance);
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

            Assert.Empty(result.GeneratorResult.Diagnostics);
            Assert.Equal(2, result.GeneratorResult.GeneratedTrees.Length);

            Assert.NotNull(result.Assembly);
        }

        [Theory]
        [InlineData("System.Boolean", "FromBoolean")]
        [InlineData("System.Byte", "FromByte")]
        [InlineData("System.DateTime", "FromDateTime")]
        [InlineData("System.DateTimeOffset", "FromDateTimeOffset")]
        [InlineData("System.Decimal", "FromDecimal")]
        [InlineData("System.Double", "FromDouble")]
        [InlineData("System.Guid", "FromGuid")]
        [InlineData("System.Int16", "FromInt16")]
        [InlineData("System.Int32", "FromInt32")]
        [InlineData("System.Int64", "FromInt64")]
        [InlineData("System.SByte", "FromSByte")]
        [InlineData("System.Single", "FromSingle")]
        [InlineData("System.String", "FromString")]
        [InlineData("System.UInt16", "FromUInt16")]
        [InlineData("System.UInt32", "FromUInt32")]
        [InlineData("System.UInt64", "FromUInt64")]
        public async Task CodeCompile(string type, string fromMethodName)
        {
            var sourceCode = @"
[StronglyTypedId(typeof(" + type + @"))]
public partial struct Test {}
";
            var result = await GenerateFiles(sourceCode);

            Assert.Empty(result.GeneratorResult.Diagnostics);
            Assert.Equal(2, result.GeneratorResult.GeneratedTrees.Length);

            var value = Type.GetType(type, throwOnError: true).FullName switch
            {
                "System.Boolean" => (object)true,
                "System.Byte" => (byte)1,
                "System.DateTime" => DateTime.UtcNow,
                "System.DateTimeOffset" => DateTimeOffset.UtcNow,
                "System.Decimal" => 1m,
                "System.Double" => 1d,
                "System.Guid" => Guid.NewGuid(),
                "System.Int16" => (short)1,
                "System.Int32" => 1,
                "System.Int64" => 1L,
                "System.SByte" => (sbyte)1,
                "System.Single" => 1f,
                "System.String" => "test",
                "System.UInt16" => (ushort)1,
                "System.UInt32" => (uint)1,
                "System.UInt64" => (ulong)1,
                _ => throw new InvalidOperationException("Type not supported"),
            };

            ValidateType(result.Assembly, "Test", fromMethodName, value);
        }

        private static void ValidateType(byte[] assembly, string typeName, string fromMethodName, object value)
        {
            var alc = new AssemblyLoadContext("test", isCollectible: true);
            try
            {
                alc.LoadFromStream(new MemoryStream(assembly));
                foreach (var a in alc.Assemblies)
                {
                    var type = a.GetType(typeName);
                    var from = (MethodInfo)type.GetMember(fromMethodName).Single();
                    var instance = from.Invoke(null, new object[] { value });

                    var json = System.Text.Json.JsonSerializer.Serialize(instance);
                    var deserialized = System.Text.Json.JsonSerializer.Deserialize(json, type);
                    var deserialized2 = System.Text.Json.JsonSerializer.Deserialize(@"{ ""a"": {}, ""b"": false, ""Value"": " + json + " }", type);

                    Assert.Equal(instance, deserialized);
                    Assert.Equal(instance, deserialized2);

                    var defaultValue = value.GetType() == typeof(string) ? null : Activator.CreateInstance(value.GetType());
                    var defaultInstance = from.Invoke(null, new object[] { defaultValue });
                    Assert.NotEqual(instance, defaultInstance);
                }
            }
            finally
            {
                alc.Unload();
            }
        }
    }
}
