using System.Collections.Immutable;
using Meziantou.Framework.JavaScriptEngine.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Meziantou.Framework.JavaScriptEngine.Tests;

public sealed class JsExportGeneratorTests
{
    [Theory]
    [InlineData("MFJS001", "[JsExport] public class A { }")]
    [InlineData("MFJS001", "public class Outer { [JsExport] public partial class A { } }")]
    [InlineData("MFJS002", "[JsExport] public partial class A<T> { }")]
    [InlineData("MFJS002", "public partial class Outer<T> { [JsExport] public partial class A { } }")]
    [InlineData("MFJS002", "[JsExport] file partial class A { }")]
    [InlineData("MFJS003", "[JsExport] public partial class A { public void M(System.Guid id) { } }")]
    [InlineData("MFJS003", "[JsExport] public partial class A { public System.Guid M() => default; }")]
    [InlineData("MFJS003", "[JsExport] public partial class A { public void M(ref int value) { } }")]
    [InlineData("MFJS003", "[JsExport] public partial class A { public void M(out int value) { value = 0; } }")]
    [InlineData("MFJS003", "[JsExport] public partial class A { public void M(params int[] values) { } }")]
    [InlineData("MFJS003", "[JsExport] public partial class A { public void M<T>() { } }")]
    [InlineData("MFJS003", "[JsExport] public partial class A { public System.Guid P { get; set; } }")]
    [InlineData("MFJS003", "[JsExport] public partial class A { public int[] Items { get; set; } = []; }")]
    [InlineData("MFJS003", "[JsExport] public partial class A { public System.Collections.Generic.Dictionary<int, int> M() => new(); }")]
    [InlineData("MFJS003", "[JsExport] public partial class A { public System.Collections.Generic.IReadOnlyDictionary<string, int> M() => new System.Collections.Generic.Dictionary<string, int>(); }")]
    [InlineData("MFJS003", "[JsExport] public partial class A { public System.Threading.Tasks.Task<System.Threading.Tasks.Task> M() => null!; }")]
    [InlineData("MFJS003", "[JsExport] public partial class A { public void M(System.DayOfWeek day) { } }")]
    [InlineData("MFJS003", "[JsExport] public partial class A { public required int X { get; init; } }")]
    [InlineData("MFJS004", "[JsExport] public partial class A { public void M(int a) { } public void M(string a) { } }")]
    [InlineData("MFJS004", "[JsExport] public partial class A { [JsName(\"x\")] public int A1 => 1; public int X => 2; }")]
    [InlineData("MFJS004", "[JsExport] public partial class A { public static int Prototype => 1; }")]
    [InlineData("MFJS004", "[JsExport] public partial class A { public int Constructor => 1; }")]
    [InlineData("MFJS005", "[JsExport] public partial class A { public A() { } public A(int a) { } }")]
    [InlineData("MFJS005", "[JsExport] public partial class A { [JsConstructor] public A() { } [JsConstructor] public A(int a) { } }")]
    [InlineData("MFJS006", "[JsExport] public partial class A { [JsName(\"\")] public int X => 1; }")]
    [InlineData("MFJS006", "[JsExport(Name = \" \")] public partial class A { }")]
    public void Generator_UnsupportedDeclaration_ReportsADiagnostic(string id, string declarations)
    {
        var result = RunGenerator("#nullable enable\nusing Meziantou.Framework.JavaScriptEngine;\n" + declarations);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics);
        Assert.Equal(id, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.True(diagnostic.Location.IsInSource, diagnostic.ToString());
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generator_SupportedDeclarations_CompileWithoutWarnings()
    {
        var source = """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Text.Json.Nodes;
            using System.Threading.Tasks;
            using Meziantou.Framework.JavaScriptEngine;

            [JsExport]
            public partial class InGlobalNamespace
            {
                public int @class() => 1;

                public string @event { get; set; } = "";

                [Obsolete("obsolete")]
                public void Old() { }

                // Not exposed: object members without [JsName], unsupported members marked [JsIgnore], non-public members
                public override string ToString() => "";

                [JsIgnore]
                public void Ignored(Guid id) { }

                internal void Internal(Guid id) { }

                public event EventHandler? Changed;

                public int this[int index] => index;

                public static InGlobalNamespace operator +(InGlobalNamespace a, InGlobalNamespace b) => a;

                public int WriteOnly { set { } }

                public int PrivateSetter { get; private set; }

                protected void Protected(Guid id) => Changed?.Invoke(this, EventArgs.Empty);
            }

            namespace Sample.@event
            {
                public partial class @struct
                {
                    [JsExport]
                    public partial class @class
                    {
                        public int @int() => 1;
                    }
                }

                [JsExport]
                public static partial class @static
                {
                    public static int Value => 1;
                }
            }

            namespace Sample.HintNames
            {
                // Their files have distinct names
                [JsExport]
                public partial class @checked { }

                [JsExport]
                public partial class _checked { }

                [JsExport]
                public partial class Casing { }

                [JsExport]
                public partial class casing { }
            }

            namespace Sample.Bindings
            {
                public partial struct Container
                {
                    [JsExport(Name = "Item")]
                    internal sealed partial class ContainerItem
                    {
                        public ContainerItem(JsRealm realm, string name, int count = 2) { }
                    }
                }

                public partial record class RecordContainer
                {
                    [JsExport]
                    public partial record class Person(string Name, int Age);
                }

                [JsExport]
                public abstract partial class Base
                {
                    public abstract int Value { get; }

                    public virtual string Name() => "";

                    public static string Shared(JsRealm realm) => "";
                }

                [JsExport]
                public sealed partial class Derived : Base
                {
                    [JsConstructor]
                    public Derived() { }

                    public Derived(int a) { }

                    [JsIgnore]
                    public Derived(Guid id) { }

                    public override int Value => 1;

                    public override string Name() => "derived";
                }

                [JsExport]
                public static partial class Tools
                {
                    public const double Pi = 3.14;

                    public static readonly string Text = "";

                    public static int Counter;

                    public static Task DelayAsync() => Task.CompletedTask;

                    public static ValueTask DelayValueAsync() => ValueTask.CompletedTask;

                    public static Task<Derived> CreateAsync() => Task.FromResult(new Derived());

                    public static ValueTask<IEnumerable<int?>> ValuesAsync() => default;

                    public static Task<JsValue> RawAsync() => Task.FromResult(JsValue.Undefined);

                    public static JsArray? Array(JsObject? value, JsValue raw, object? any) => null;

                    public static JsonNode? Json() => null;

                    public static IDictionary<string, object?> Dictionary() => new Dictionary<string, object?>();

                    public static SortedDictionary<string, List<Base>> Nested() => new();

                    public static Half Half() => default;

                    public static string Defaults(
                        int a = -1, long b = long.MinValue, ulong c = ulong.MaxValue, uint d = uint.MaxValue, short e = short.MinValue,
                        ushort f = 1, byte g = 255, sbyte h = -128, double i = double.PositiveInfinity, double j = double.NegativeInfinity,
                        double k = 1e300, float l = float.NaN, float m = float.NegativeInfinity, decimal n = -79228162514264337593543950335m,
                        bool o = false, string p = "a\"b\n", string? q = null, int? r = null, int? s = 3, DateTime t = default,
                        DateTimeOffset? u = null, Derived? v = null, object? w = null, JsValue x = default, double y = 0.1, float z = float.PositiveInfinity)
                        => "";
                }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.OutputDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.HasCount(12, result.GeneratedSources);
        var tools = Assert.Single(result.GeneratedSources, source => source.Contains("class @Tools", StringComparison.Ordinal));
        Assert.Contains("static partial class @Tools", tools, StringComparison.Ordinal);
        Assert.DoesNotContain("IJsExportable", tools, StringComparison.Ordinal);
        var derived = Assert.Single(result.GeneratedSources, source => source.Contains("partial class @Derived", StringComparison.Ordinal));
        Assert.Contains("public static new global::Meziantou.Framework.JavaScriptEngine.JsClass JsClass", derived, StringComparison.Ordinal);
        Assert.DoesNotContain("\"value\"", derived, StringComparison.Ordinal);
        Assert.DoesNotContain("\"name\"", derived, StringComparison.Ordinal);
        var global = Assert.Single(result.GeneratedSources, source => source.Contains("partial class @InGlobalNamespace", StringComparison.Ordinal));
        Assert.DoesNotContain("namespace", global, StringComparison.Ordinal);
        var keywords = Assert.Single(result.GeneratedSources, source => source.Contains("partial class @class", StringComparison.Ordinal));
        Assert.Contains("namespace Sample.@event", keywords, StringComparison.Ordinal);
        Assert.Contains("partial class @struct", keywords, StringComparison.Ordinal);
        Assert.Contains("\"class\"", global, StringComparison.Ordinal);
        Assert.Contains("\"old\"", global, StringComparison.Ordinal);
        Assert.Contains("\"privateSetter\", (thisValue, arguments) => realm.FromDotNet(GetThis<global::InGlobalNamespace>(thisValue).@PrivateSetter), null)", global, StringComparison.Ordinal);
        foreach (var name in (string[])["\"toString\"", "\"ignored\"", "\"internal\"", "\"changed\"", "\"writeOnly\"", "\"protected\""])
        {
            Assert.DoesNotContain(name, global, StringComparison.Ordinal);
        }
    }

#if NET11_0_OR_GREATER
    [Fact]
    public void Generator_Ieee754Types_AreConverted()
    {
        const string Source = """
            using Meziantou.Framework.JavaScriptEngine;

            [JsExport]
            public sealed partial class Numbers
            {
                public System.Numerics.Decimal64 Value { get; set; }

                public static System.Numerics.Decimal32 Convert(System.Numerics.Decimal128 a, System.Numerics.BFloat16? b, System.Half c) => default;
            }
            """;

        var result = RunGenerator(Source);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.OutputDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var numbers = Assert.Single(result.GeneratedSources);
        Assert.Contains("\"convert\"", numbers, StringComparison.Ordinal);
        Assert.Contains("\"value\"", numbers, StringComparison.Ordinal);
    }
#endif

    [Fact]
    public void Generator_UnrelatedEdit_IsCached()
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = CreateCompilation("using Meziantou.Framework.JavaScriptEngine; [JsExport] public sealed partial class A { public int Value { get; set; } }", parseOptions);
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new JsExportGenerator().AsSourceGenerator()], parseOptions: parseOptions, driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);

        driver = driver.RunGenerators(compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("public sealed class Other { }", parseOptions)));

        var result = Assert.Single(driver.GetRunResult().Results);
        Assert.Single(result.GeneratedSources);
        var outputs = result.TrackedSteps["JsExportOutputs"].SelectMany(step => step.Outputs);
        Assert.All(outputs, output => Assert.True(output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged, output.Reason.ToString()));
    }

    private static GeneratorTestResult RunGenerator(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = CreateCompilation(source, parseOptions);
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new JsExportGenerator().AsSourceGenerator()], parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        var inputTrees = compilation.SyntaxTrees.ToHashSet();
        var generatedSources = driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources).Select(generated => generated.SourceText.ToString()).ToImmutableArray();
        var outputDiagnostics = outputCompilation.GetDiagnostics();
        var generatedCodeDiagnostics = outputDiagnostics
            .Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning && diagnostic.Location.SourceTree is { } tree && !inputTrees.Contains(tree))
            .ToImmutableArray();
        if (!generatedCodeDiagnostics.IsEmpty)
        {
            Assert.Fail("Generated code produced diagnostics:" + Environment.NewLine + string.Join(Environment.NewLine, generatedCodeDiagnostics) + Environment.NewLine + string.Join(Environment.NewLine, generatedSources));
        }

        return new GeneratorTestResult(generatorDiagnostics, outputDiagnostics, generatedSources);
    }

    private static CSharpCompilation CreateCompilation(string source, CSharpParseOptions parseOptions)
    {
        var references = new List<MetadataReference>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string assemblies)
        {
            paths.UnionWith(assemblies.Split(Path.PathSeparator));
        }

        paths.Add(typeof(JsEngine).Assembly.Location);
        foreach (var path in paths)
        {
            if (path.Length > 0 && File.Exists(path))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        return CSharpCompilation.Create(
            assemblyName: "Meziantou.Framework.JavaScriptEngine.SourceGeneration.Tests.Input",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    private readonly record struct GeneratorTestResult(ImmutableArray<Diagnostic> GeneratorDiagnostics, ImmutableArray<Diagnostic> OutputDiagnostics, ImmutableArray<string> GeneratedSources);
}
