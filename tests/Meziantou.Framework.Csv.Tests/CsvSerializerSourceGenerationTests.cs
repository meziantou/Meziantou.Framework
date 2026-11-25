using System.Text;
using Meziantou.Framework.Csv.Serialization;
using Meziantou.Framework.Csv.Serialization.Metadata;
using Meziantou.Framework.Csv.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Meziantou.Framework.Csv.Tests;

#pragma warning disable MA0048 // File name must match type name: the contexts are declared with the tests

public class CsvSerializerSourceGenerationTests
{
    [Fact]
    public void UseContext()
    {
        #region CsvSerializerContext_Use
        Product[] products = [new(1, "Keyboard", 49.90m), new(2, "Mouse", 19.99m)];

        // The metadata comes from the generated code: no reflection, compatible with trimming and Native AOT
        var csv = CsvSerializer.Serialize(products, AppCsvContext.Default.Product);
        Assert.Equal("Id;Name;Price\r\n1;Keyboard;49.90\r\n2;Mouse;19.99\r\n", csv);

        var result = CsvSerializer.Deserialize(csv, AppCsvContext.Default.Product);
        Assert.Equal(products, result);
        #endregion
    }

    [Fact]
    public void ContextOptions()
    {
        var context = new AppCsvContext(new CsvSerializerOptions { Separator = '|' });
        Assert.Same(context, context.Options.TypeInfoResolver);
        Assert.Equal("Id|Name|Price\r\n1|a|2\r\n", CsvSerializer.Serialize([new Product(1, "a", 2)], context.Product));
        Assert.Same(AppCsvContext.Default.Product, AppCsvContext.Default.Product);
    }

    [Fact]
    public void ContextWithReflectionOptions()
    {
        // A context does not fall back to reflection for the types it does not declare
        Assert.Throws<NotSupportedException>(() => CsvSerializer.Deserialize<Order>("Id\n1", AppCsvContext.Default.Options));
    }

    [Fact]
    public void SameResultAsReflection()
    {
        AssertSame([new Person { Name = "Alice, A", Age = 30, BirthDate = new DateOnly(1990, 1, 2) }, new Person { Name = "Bob" }], TestCsvContext.Default.Person);
        AssertSame([new Product(1, "Keyboard", 49.90m)], TestCsvContext.Default.Product);
        AssertSame([new Order { Id = 1, Customer = "Alice", Date = new DateTime(2026, 11, 21), Secret = "x" }], TestCsvContext.Default.Order);
        AssertSame([AllTypes.CreateSample(), new AllTypes { String = "" }], TestCsvContext.Default.AllTypes);
        AssertSame([new StructModel { X = 1, Y = 2 }], TestCsvContext.Default.StructModel);
        AssertSame([new DerivedModel { Base = 1, Derived = 2, Virtual = 3 }], TestCsvContext.Default.DerivedModel);
        AssertSame([new RequiredModel { Value = 1, Attributed = 2, Optional = "x" }], TestCsvContext.Default.RequiredModel);
        AssertSame([new DefaultValuesModel(1, 2, "c")], TestCsvContext.Default.DefaultValuesModel);
        AssertSame([new ParameterOnlyModel(2, 3)], TestCsvContext.Default.ParameterOnlyModel);
        AssertSame([new SelectedConstructorModel("x")], TestCsvContext.Default.SelectedConstructorModel);
        AssertSame([new BooleanModel { Value = true }], TestCsvContext.Default.BooleanModel);

        var headerless = new TestCsvContext(new CsvSerializerOptions { HasHeaderRow = false });
        AssertSame([new Measure { Date = new DateOnly(2026, 11, 21), Value = 1.5 }], headerless.Measure);

        var withConverter = new TestCsvContext(new CsvSerializerOptions { Converters = [new PointConverter()] });
        AssertSame([new Shape { Name = "line", Start = new Point(1, 2) }], withConverter.Shape);

        AssertSame([new FieldsModel { A = 1, B = "b" }], FieldsCsvContext.Default.FieldsModel);
    }

    [Fact]
    public void MembersReader()
    {
        // The generated members reader sets the members when the columns are in their declaration order, and the setters otherwise: the records, the
        // missing values and the errors are the ones of reflection, in UTF-16 and UTF-8
        Assert.NotNull(TestCsvContext.Default.Person.MembersReader);
        Assert.NotNull(TestCsvContext.Default.Person.Utf8MembersReader);
        Assert.NotNull(TestCsvContext.Default.MembersModel.MembersReader);
        Assert.NotNull(TestCsvContext.Default.StructModel.MembersReader);
        Assert.Null(TestCsvContext.Default.ParameterOnlyModel.MembersReader);

        string[] people =
        [
            "Name,Age,BirthDate\nAlice,30,1990-01-02\nBob,,",
            "BirthDate,Age,Name\n1990-01-02,30,Alice",
            "Name\nAlice",
            "Name,Extra,Age\nAlice,x,30",
            "Name,Age,BirthDate\nAlice\nBob,3",
            "Name,Age\nAlice,x",
            "Age,Name\nx,Alice",
            "Name,Age,BirthDate\nAlice,30,1990-13-45",
            "BirthDate,Age\n1990-13-45,x",
        ];
        foreach (var text in people)
        {
            AssertSameDeserialization(text, TestCsvContext.Default.Person);
        }

        AssertSameDeserialization("Value\n1\n-1", TestCsvContext.Default.ValidatingSetterModel);
        AssertSameDeserialization("Init,Required,Private,PrivateSetter,Field,ReadOnly\n1,2,3,4,5,6", TestCsvContext.Default.MembersModel);
        AssertSameDeserialization("X,Y\n1,2", TestCsvContext.Default.StructModel);
        AssertSameDeserialization("Id,Name,Price\n1,\"a,b\",2.5", TestCsvContext.Default.Product);
    }

    // The generated metadata, with its members reader, gives the records or the error of the reflection-based metadata
    private static void AssertSameDeserialization<T>(string text, CsvTypeInfo<T> typeInfo)
    {
        var reflectionOptions = new CsvSerializerOptions(typeInfo.Options) { TypeInfoResolver = null };
        Assert.Equal(Describe(() => CsvSerializer.Deserialize<T>(text, reflectionOptions)), Describe(() => CsvSerializer.Deserialize(text, typeInfo)));
        Assert.Equal(Describe(() => CsvSerializer.Deserialize<T>(Encoding.UTF8.GetBytes(text), reflectionOptions)), Describe(() => CsvSerializer.Deserialize(Encoding.UTF8.GetBytes(text), typeInfo)));

        string Describe(Func<List<T>> deserialize)
        {
            try
            {
                return CsvSerializer.Serialize(deserialize(), reflectionOptions);
            }
            catch (CsvException ex)
            {
                return ex.GetType().Name + ": " + ex.Message + " " + ex.LineNumber + " " + ex.ColumnIndex + " " + ex.InnerException?.GetType().Name;
            }
        }
    }

    [Fact]
    public void DeserializationErrorHandler()
    {
        // The generated metadata goes through the same binder as reflection
        var errors = new List<CsvDeserializationErrorContext>();
        foreach (var action in (CsvDeserializationErrorAction[])[CsvDeserializationErrorAction.Ignore, CsvDeserializationErrorAction.SkipRecord])
        {
            errors.Clear();
            var context = new TestCsvContext(new CsvSerializerOptions { DeserializationErrorHandler = c => { errors.Add(c); return action; } });
            const string Csv = "Id,Name,Price\n1,a,x\n2,b,3";
            var generated = CsvSerializer.Deserialize(Csv, context.Product);
            var reflection = CsvSerializer.Deserialize<Product>(Csv, new CsvSerializerOptions { DeserializationErrorHandler = _ => action });
            Assert.Equal(reflection, generated);
            Assert.Equal(action is CsvDeserializationErrorAction.Ignore ? [new Product(1, "a", 0), new Product(2, "b", 3)] : [new Product(2, "b", 3)], generated);
            Assert.Equal(CsvDeserializationErrorKind.ConversionFailed, Assert.Single(errors).Kind);

            errors.Clear();
            var people = CsvSerializer.Deserialize(Encoding.UTF8.GetBytes("Name,Age\nAlice,x\nBob,3"), context.Person);
            Assert.Equal(action is CsvDeserializationErrorAction.Ignore ? ["Alice", "Bob"] : ["Bob"], people.Select(p => p.Name));
            Assert.Equal("x", Assert.Single(errors).RawValue);
        }
    }

    [Fact]
    public void Members()
    {
        var model = CsvSerializer.Deserialize("Init,Required,Private,PrivateSetter,Field,ReadOnly\n1,2,3,4,5,6", TestCsvContext.Default.MembersModel)[0];
        Assert.Equal(1, model.Init);
        Assert.Equal(2, model.Required);
        Assert.Equal(3, model.GetPrivate());
        Assert.Equal(0, model.PrivateSetter);
        Assert.Equal(5, model.Field);
        Assert.Equal(CsvSerializer.Serialize([model]), CsvSerializer.Serialize([model], TestCsvContext.Default.MembersModel));
    }

    [Fact]
    public void Callbacks()
    {
        var model = CsvSerializer.Deserialize("Value\n1", TestCsvContext.Default.CallbackModel)[0];
        CsvSerializer.Serialize([model], TestCsvContext.Default.CallbackModel);
        Assert.Equal(["OnDeserializing", "OnDeserialized", "OnSerializing", "OnSerialized"], model.Calls);
    }

    [Fact]
    public void Utf8()
    {
        using var stream = new MemoryStream();
        CsvSerializer.Serialize(stream, [AllTypes.CreateSample()], TestCsvContext.Default.AllTypes);
        stream.Position = 0;
        Assert.Equal(AllTypes.CreateSample().Describe(), CsvSerializer.Deserialize(stream, TestCsvContext.Default.AllTypes)[0].Describe());
    }

    [Fact]
    public void Generator_NotPartial()
    {
        var result = RunGenerator("""
            using Meziantou.Framework.Csv.Serialization;
            public sealed record Item(int Id);
            [CsvSerializable(typeof(Item))]
            public class Context : CsvSerializerContext
            {
                public Context() : base(null) { }
                public override Meziantou.Framework.Csv.Serialization.Metadata.CsvTypeInfo GetTypeInfo(System.Type type, CsvSerializerOptions options) => null;
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "MFCSV001");
    }

    [Fact]
    public void Generator_NotAContext()
    {
        var result = RunGenerator("""
            using Meziantou.Framework.Csv.Serialization;
            public sealed record Item(int Id);
            [CsvSerializable(typeof(Item))]
            public partial class Context
            {
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "MFCSV002");
    }

    [Fact]
    public void Generator_GenericContext()
    {
        var result = RunGenerator("""
            using Meziantou.Framework.Csv.Serialization;
            public sealed record Item(int Id);
            [CsvSerializable(typeof(Item))]
            public partial class Context<T> : CsvSerializerContext
            {
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "MFCSV003");
    }

    [Theory]
    [InlineData("public abstract class Item { public int Id { get; set; } }")]
    [InlineData("public interface Item { }")]
    [InlineData("public sealed class Item { public Item(int a) { } public Item(string b) { } }")]
    [InlineData("public sealed class Item { [CsvConstructor] public Item(int a) { } [CsvConstructor] public Item(string b) { } }")]
    public void Generator_TypeNotSupported(string declaration)
    {
        var result = RunGenerator($$"""
            using Meziantou.Framework.Csv.Serialization;
            {{declaration}}
            [CsvSerializable(typeof(Item))]
            public partial class Context : CsvSerializerContext
            {
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "MFCSV004");
    }

    [Fact]
    public void Generator_InvalidConverter()
    {
        var result = RunGenerator("""
            using Meziantou.Framework.Csv.Serialization;
            public sealed class Item { [CsvConverter(typeof(string))] public int Id { get; set; } }
            [CsvSerializable(typeof(Item))]
            public partial class Context : CsvSerializerContext
            {
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "MFCSV005");
    }

    [Fact]
    public void Generator_InaccessibleMemberOfGenericType()
    {
        var result = RunGenerator("""
            using Meziantou.Framework.Csv.Serialization;
            public sealed class Item<T> { public int Id { get; init; } }
            [CsvSerializable(typeof(Item<int>))]
            public partial class Context : CsvSerializerContext
            {
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "MFCSV006");
    }

    [Fact]
    public void Generator_DuplicateColumnName()
    {
        var result = RunGenerator("""
            using Meziantou.Framework.Csv.Serialization;
            public sealed class Item
            {
                [CsvPropertyName("A")] public int First { get; set; }
                [CsvPropertyName("A")] public int Second { get; set; }
            }
            [CsvSerializable(typeof(Item))]
            public partial class Context : CsvSerializerContext
            {
            }
            """);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "MFCSV007");
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_DuplicateTypeInfoPropertyName()
    {
        var result = RunGenerator("""
            using Meziantou.Framework.Csv.Serialization;
            namespace A { public sealed class Item { public int Id { get; set; } } }
            namespace B { public sealed class Item { public int Id { get; set; } } }
            [CsvSerializable(typeof(A.Item))]
            [CsvSerializable(typeof(B.Item))]
            public partial class Context : CsvSerializerContext
            {
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "MFCSV008");
    }

    [Fact]
    public void Generator_GeneratedCodeCompiles()
    {
        var result = RunGenerator("""
            #nullable disable
            #pragma warning disable CS0169
            using System;
            using Meziantou.Framework.Csv.Serialization;
            namespace Sample
            {
                public enum Kind { A, B }
                public struct Point : ISpanParsable<Point>, ISpanFormattable
                {
                    public static Point Parse(string s, IFormatProvider provider) => default;
                    public static bool TryParse(string s, IFormatProvider provider, out Point result) { result = default; return true; }
                    public static Point Parse(ReadOnlySpan<char> s, IFormatProvider provider) => default;
                    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider provider, out Point result) { result = default; return true; }
                    public string ToString(string format, IFormatProvider formatProvider) => "";
                    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider provider) { charsWritten = 0; return true; }
                }
                public sealed class Item
                {
                    private Item() { }
                    [CsvConstructor]
                    private Item(int id, string @class = "x", double ratio = 1.5, Kind kind = Kind.B) { Id = id; Class = @class; }
                    public int Id { get; }
                    public string Class { get; }
                    public required Kind? NullableKind { get; set; }
                    public Point Point { get; init; }
                    [CsvInclude] internal int Internal { get; private set; }
                    [CsvInclude] private string _field;
                    public Kind Kind { get; set; }
                }
                public struct Value { public int X { get; set; } [CsvInclude] private int _y; }
                public sealed record Rec(int Id, string Name);
                public static partial class Outer
                {
                    [CsvSerializable(typeof(Item))]
                    [CsvSerializable(typeof(Value), TypeInfoPropertyName = "ValueInfo")]
                    [CsvSerializable(typeof(Rec))]
                    [CsvSourceGenerationOptions(Separator = ';', PropertyNamingPolicy = CsvKnownNamingPolicy.CamelCase, HasHeaderRow = false, NewLine = "\n", PropertyNameCaseInsensitive = true, IncludeFields = true, MissingColumnHandling = CsvMissingColumnHandling.Throw)]
                    internal partial class Context : CsvSerializerContext
                    {
                    }
                }
            }
            """);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity >= DiagnosticSeverity.Warning);
        Assert.Contains("ValueInfo", result.GeneratedSource, StringComparison.Ordinal);
    }

    private static void AssertSame<T>(T[] values, CsvTypeInfo<T> typeInfo)
    {
        // The generated metadata produces the same data as the reflection-based metadata
        var reflectionOptions = new CsvSerializerOptions(typeInfo.Options) { TypeInfoResolver = null };
        var expected = CsvSerializer.Serialize(values, reflectionOptions);
        var actual = CsvSerializer.Serialize(values, typeInfo);
        Assert.Equal(expected, actual);

        var expectedValues = CsvSerializer.Deserialize<T>(expected, reflectionOptions);
        var actualValues = CsvSerializer.Deserialize(actual, typeInfo);
        Assert.Equal(CsvSerializer.Serialize(expectedValues, reflectionOptions), CsvSerializer.Serialize(actualValues, typeInfo));
    }

    private static (Diagnostic[] GeneratorDiagnostics, Diagnostic[] Diagnostics, string GeneratedSource) RunGenerator([StringSyntax("c#-test")] string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var platformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var references = platformAssemblies
            .Append(typeof(CsvSerializer).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create([new CsvSerializerContextGenerator().AsSourceGenerator()], parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        var generatedSource = string.Join(Environment.NewLine, driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources).Select(generated => generated.SourceText.ToString()));
        return ([.. generatorDiagnostics], [.. outputCompilation.GetDiagnostics()], generatedSource);
    }
}

#region CsvSerializerContext_Declare
[CsvSerializable(typeof(Product))]
[CsvSerializable(typeof(Person))]
[CsvSourceGenerationOptions(Separator = ';')]
public partial class AppCsvContext : CsvSerializerContext
{
}
#endregion

[CsvSerializable(typeof(Person))]
[CsvSerializable(typeof(Product))]
[CsvSerializable(typeof(Order))]
[CsvSerializable(typeof(Measure))]
[CsvSerializable(typeof(Shape))]
[CsvSerializable(typeof(RequiredModel))]
[CsvSerializable(typeof(BooleanModel))]
[CsvSerializable(typeof(AllTypes))]
[CsvSerializable(typeof(MembersModel))]
[CsvSerializable(typeof(StructModel))]
[CsvSerializable(typeof(DerivedModel))]
[CsvSerializable(typeof(CallbackModel))]
[CsvSerializable(typeof(DefaultValuesModel))]
[CsvSerializable(typeof(ParameterOnlyModel))]
[CsvSerializable(typeof(SelectedConstructorModel))]
[CsvSerializable(typeof(ValidatingSetterModel))]
internal sealed partial class TestCsvContext : CsvSerializerContext
{
}

[CsvSerializable(typeof(FieldsModel))]
[CsvSourceGenerationOptions(IncludeFields = true)]
internal sealed partial class FieldsCsvContext : CsvSerializerContext
{
}
