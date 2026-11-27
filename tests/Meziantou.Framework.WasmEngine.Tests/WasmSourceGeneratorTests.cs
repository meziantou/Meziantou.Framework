using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.WasmEngine.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.WasmEngine.Tests;

public sealed partial class WasmSourceGeneratorTests
{
    private static readonly WasmRuntime Runtime = new();

    [Fact]
    public void Documentation_TypedExports()
    {
        #region WasmEngine_TypedExports
        // fib.wat is an AdditionalFiles item of the project: the generator writes the FibModule class
        using var runtime = new WasmRuntime();
        using var store = runtime.CreateStore();
        var fib = FibModule.Instantiate(store, FibModule.Compile(runtime));

        Assert.Equal(6765, fib.Fib(20));
        #endregion
    }

    #region WasmEngine_TypedImportsHost
    // A partial class: the generator adds the DefineWasmImports method
    public sealed partial class Logger
    {
        private readonly List<int> _values = [];

        public IReadOnlyList<int> Values => _values;

        // A host function of the import "env" "log" whose type is (func (param i32))
        [WasmImport("env", "log")]
        public void Log(int value) => _values.Add(value);

        // A static method: (func (param f64) (result f64))
        [WasmImport("env", "scale")]
        public static double Scale(double value) => value * 2;
    }
    #endregion

    [Fact]
    public void Documentation_TypedImports()
    {
        #region WasmEngine_TypedImports
        using var runtime = new WasmRuntime();
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (import "env" "log" (func $log (param i32)))
              (import "env" "scale" (func $scale (param f64) (result f64)))
              (func (export "run") (result f64)
                (call $log (i32.const 1))
                (call $scale (f64.const 21))))
            """));

        using var store = runtime.CreateStore();
        var logger = new Logger();

        // DefineWasmImports is generated: it creates a host function per method marked with [WasmImport]
        var instance = store.Instantiate(module, logger.DefineWasmImports(store));
        var result = instance.GetFunction("run")!.Call();

        Assert.Equal(42.0, result[0].F64);
        Assert.Equal([1], logger.Values);
        #endregion
    }

    #region WasmEngine_TypedImportsCallerHost
    public static partial class Printer
    {
        // A first parameter of type WasmCaller receives the caller: the type of the import is (func (param i32 i32) (result i32))
        [WasmImport("env", "print")]
        public static int Print(WasmCaller caller, int address, int length)
        {
            var memory = caller.GetMemory("memory")!;
            Printed.Add(System.Text.Encoding.UTF8.GetString(memory.GetSpan().Slice(address, length)));
            return length;
        }

        public static IList<string> Printed { get; } = new List<string>();
    }
    #endregion

    [Fact]
    public void Documentation_TypedImportsCaller()
    {
        #region WasmEngine_TypedImportsCaller
        using var runtime = new WasmRuntime();
        var module = runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (import "env" "print" (func $print (param i32 i32) (result i32)))
              (memory (export "memory") 1)
              (data (i32.const 0) "Hello")
              (func (export "run") (result i32)
                (call $print (i32.const 0) (i32.const 5))))
            """));

        using var store = runtime.CreateStore();
        var instance = store.Instantiate(module, Printer.DefineWasmImports(store));

        Assert.Equal(5, instance.GetFunction("run")!.Call()[0].I32);
        Assert.Equal(["Hello"], Printer.Printed);
        #endregion
    }

    public sealed partial class CalculatorHost
    {
        private readonly List<int> _logged = [];

        public IReadOnlyList<int> Logged => _logged;

        [WasmImport("env", "log")]
        public void Log(int value) => _logged.Add(value);

        [WasmImport("env", "scale")]
        public static double Scale(double value) => value * 3;
    }

    public static partial class HostTypes
    {
        [WasmImport("env", "unsigned")]
        public static (ulong, uint) AddOne(uint value, ulong other) => (other + 1, value + 1);

        [WasmImport("env", "box")]
        public static object Box(int value) => "value " + value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        [WasmImport("env", "length")]
        [WasmImport("env", "length2")]
        public static int Length(object? value) => value is string text ? text.Length : -1;

        [WasmImport("env", "call")]
        public static WasmFunction? Identity(WasmFunction? function) => function;

        [WasmImport("env", "f32")]
        public static float Half(float value) => value / 2;

        [WasmImport("env", "i64")]
        public static long Negate(long value) => -value;
    }

    private static CalculatorModule CreateCalculator(CalculatorHost? host = null)
    {
        var store = Runtime.CreateStore();
        host ??= new CalculatorHost();
        return CalculatorModule.Instantiate(store, CalculatorModule.Compile(Runtime), host.DefineWasmImports(store));
    }

    [Fact]
    public void TypedExports_Numbers()
    {
        var calculator = CreateCalculator();

        Assert.Equal(5, calculator.Add(2, 3));
        Assert.Equal(3, calculator.Add2(2));
        Assert.Equal(long.MaxValue, calculator.AddI64(long.MaxValue - 1, 1));
        Assert.Equal(7.5f, calculator.MulF32(2.5f, 3f));
        Assert.Equal(9.0, calculator.ScaleTwice(1.0));
        Assert.Equal(7, calculator.Class(7));
        Assert.Equal(42, calculator.Instance2());
    }

    [Fact]
    public void TypedExports_ParameterNames()
    {
        var calculator = CreateCalculator();

        Assert.Equal(5, calculator.Add(left: 2, right: 3));
        Assert.Equal(7, calculator.Class(@int: 7));
        Assert.Equal(1.0, calculator.ScaleTwice(value: 1.0 / 9.0), 1e-12);
    }

    [Fact]
    public void TypedExports_MultipleResults()
    {
        var calculator = CreateCalculator();

        Assert.Equal((10L, 3), calculator.Swap(3, 10));
        Assert.Equal((1, 2, 3), calculator.Three());
    }

    [Fact]
    public void TypedExports_References()
    {
        var calculator = CreateCalculator();
        var value = new object();

        Assert.Same(value, calculator.Identity(value));
        Assert.Null(calculator.Identity(null));
        Assert.Equal(1, calculator.IsNull(null));
        Assert.Equal(0, calculator.IsNull("text"));

        var add = calculator.GetAdd();
        Assert.Equal(9, add.Call(WasmValue.FromI32(4), WasmValue.FromI32(5))[0].I32);
    }

    [Fact]
    public void TypedExports_Vector()
    {
        var calculator = CreateCalculator();

        var vector = calculator.Splat(1);
        Assert.Equal(WasmValueKind.V128, vector.Kind);
        Assert.Equal(0x0000_0001_0000_0001L, vector.V128Low);
        Assert.Equal(0x0000_0001_0000_0001L, vector.V128High);
    }

    [Fact]
    public void TypedExports_OtherExports()
    {
        var calculator = CreateCalculator();

        calculator.Increment();
        calculator.Increment();

        Assert.Equal(2, calculator.Counter.Get().I32);
        Assert.Equal(65536UL, calculator.Memory.Size * WasmMemoryType.PageSize);
        Assert.Equal(1UL, calculator.Table.Size);
        Assert.HasCount(1, calculator.Error.Type.Parameters);
        Assert.Same(calculator.Instance.GetMemory("memory"), calculator.Memory);
    }

    [Fact]
    public void TypedExports_CallTheImports()
    {
        var host = new CalculatorHost();
        var calculator = CreateCalculator(host);

        calculator.LogTwice(4);

        Assert.Equal([4, 4], host.Logged);
    }

    [Fact]
    public void TypedExports_BinaryModule()
    {
        using var store = Runtime.CreateStore();
        var fib = FibBinaryModule.Instantiate(store, FibBinaryModule.Compile(Runtime));

        // The binary format has no parameter names
        Assert.Equal(55, fib.Fib(arg0: 10));
    }

    [Fact]
    public void TypedExports_NumericCallsDoNotAllocate()
    {
        var calculator = CreateCalculator();

        // The functions are compiled to .NET code after some calls: the compilation allocates
        for (var i = 0; i < 10_000; i++)
        {
            calculator.Add(i, i);
            calculator.Swap(i, i);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            calculator.Add(i, i);
            calculator.Swap(i, i);
        }

        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void TypedExports_MissingExport_Throws()
    {
        using var store = Runtime.CreateStore();
        var instance = store.Instantiate(Runtime.Compile(WasmRuntime.WatToWasm("(module)")));

        var exception = Assert.Throws<WasmLinkException>(() => new FibModule(instance));
        Assert.Equal("The instance has no exported function 'fib'", exception.Message);
    }

    [Fact]
    public void TypedExports_ExportOfAnotherType_Throws()
    {
        using var store = Runtime.CreateStore();
        var instance = store.Instantiate(Runtime.Compile(WasmRuntime.WatToWasm("""(module (func (export "fib") (param i64) (result i32) (i32.const 0)))""")));

        var exception = Assert.Throws<WasmLinkException>(() => new FibModule(instance));
        Assert.Equal("The exported function 'fib' does not have the type of the module of the generated class", exception.Message);
    }

    [Fact]
    public void TypedImports_Conversions()
    {
        using var store = Runtime.CreateStore();
        var module = Runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (import "env" "unsigned" (func $unsigned (param i32 i64) (result i64 i32)))
              (import "env" "box" (func $box (param i32) (result (ref extern))))
              (import "env" "length" (func $length (param externref) (result i32)))
              (import "env" "length2" (func $length2 (param externref) (result i32)))
              (import "env" "call" (func $call (param funcref) (result funcref)))
              (import "env" "f32" (func $f32 (param f32) (result f32)))
              (import "env" "i64" (func $i64 (param i64) (result i64)))
              (func (export "unsigned") (result i64 i32) (call $unsigned (i32.const -1) (i64.const 41)))
              (func (export "box_length") (result i32) (call $length (call $box (i32.const 12))))
              (func (export "null_length") (result i32) (call $length2 (ref.null extern)))
              (func (export "call") (param funcref) (result funcref) (call $call (local.get 0)))
              (func (export "f32") (result f32) (call $f32 (f32.const 3)))
              (func (export "i64") (result i64) (call $i64 (i64.const 5))))
            """));

        var instance = store.Instantiate(module, HostTypes.DefineWasmImports(store));

        var unsigned = instance.GetFunction("unsigned")!.Call();
        Assert.Equal(42L, unsigned[0].I64);
        Assert.Equal(0, unsigned[1].I32);
        Assert.Equal("value 12".Length, instance.GetFunction("box_length")!.Call()[0].I32);
        Assert.Equal(-1, instance.GetFunction("null_length")!.Call()[0].I32);
        var function = instance.GetFunction("f32")!;
        Assert.Same(function, instance.GetFunction("call")!.Call(WasmValue.FromReference(function))[0].Reference);
        Assert.Equal(1.5f, function.Call()[0].F32);
        Assert.Equal(-5L, instance.GetFunction("i64")!.Call()[0].I64);
    }

    [Fact]
    public void TypedImports_DefineIntoExistingImports()
    {
        using var store = Runtime.CreateStore();
        var imports = new WasmImports();

        var result = HostTypes.DefineWasmImports(store, imports);

        Assert.Same(imports, result);
        Assert.NotNull(imports.Get("env", "length2"));
        var box = (WasmFunction)imports.Get("env", "box")!;
        Assert.Equal(new WasmValueType(WasmValueKind.Ref, WasmHeapKind.Extern, Nullable: false), Assert.Single(box.Type.Results));
    }

    [Theory]
    [InlineData("MFWASM001", "public class A { [WasmImport(\"env\", \"f\")] public static void F() { } }")]
    [InlineData("MFWASM001", "public class Outer { public partial class A { [WasmImport(\"env\", \"f\")] public static void F() { } } }")]
    [InlineData("MFWASM002", "public partial class A<T> { [WasmImport(\"env\", \"f\")] public static void F() { } }")]
    [InlineData("MFWASM002", "file partial class A { [WasmImport(\"env\", \"f\")] public static void F() { } }")]
    [InlineData("MFWASM002", "public partial interface A { [WasmImport(\"env\", \"f\")] public static void F() { } }")]
    [InlineData("MFWASM003", "public partial class A { [WasmImport(\"env\", \"f\")] public static void F(string value) { } }")]
    [InlineData("MFWASM003", "public partial class A { [WasmImport(\"env\", \"f\")] public static string F() => \"\"; }")]
    [InlineData("MFWASM003", "public partial class A { [WasmImport(\"env\", \"f\")] public static (int, string) F() => default; }")]
    [InlineData("MFWASM003", "public partial class A { [WasmImport(\"env\", \"f\")] public static void F(ref int value) { } }")]
    [InlineData("MFWASM003", "public partial class A { [WasmImport(\"env\", \"f\")] public static void F<T>() { } }")]
    [InlineData("MFWASM003", "public partial class A { [WasmImport(\"env\", \"f\")] public static void F(WasmValue value) { } }")]
    [InlineData("MFWASM003", "public partial struct A { [WasmImport(\"env\", \"f\")] public void F() { } }")]
    [InlineData("MFWASM003", "public abstract partial class A { [WasmImport(\"env\", \"f\")] public abstract void F(); }")]
    [InlineData("MFWASM003", "public partial class A { [WasmImport(\"env\", \"f\")] public static void F(int value, WasmCaller caller) { } }")]
    [InlineData("MFWASM003", "public partial class A { [WasmImport(\"env\", \"f\")] public static void F(WasmCaller caller, WasmCaller other) { } }")]
    [InlineData("MFWASM003", "public partial class A { [WasmImport(\"env\", \"f\")] public static void F(in WasmCaller caller) { } }")]
    [InlineData("MFWASM003", "public partial class A { [WasmImport(\"env\", \"f\")] public static WasmCaller F() => default; }")]
    [InlineData("MFWASM004", "public partial class A { [WasmImport(\"env\", \"f\")] public static void F() { } [WasmImport(\"env\", \"f\")] public static void G() { } }")]
    public void Generator_UnsupportedImport_ReportsADiagnostic(string id, string declarations)
    {
        var result = RunGenerator("using Meziantou.Framework.WasmEngine;\n" + declarations);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(id, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.True(diagnostic.Location.IsInSource, diagnostic.ToString());
        Assert.Empty(result.Sources);
    }

    [Fact]
    public void Generator_Imports_CompileWithoutWarnings()
    {
        var result = RunGenerator("""
            using Meziantou.Framework.WasmEngine;

            namespace App
            {
                public partial class Outer
                {
                    internal partial record Host
                    {
                        [WasmImport("env", "@class")]
                        public int @class(int @int, long value, float single, double number, uint unsigned, ulong big) => @int;

                        [WasmImport("env", "objects")]
                        public static (object, object?, WasmFunction, WasmFunction?) Objects(object a, object? b, WasmFunction c, WasmFunction? d) => (a, b, c, d);

                        [System.Obsolete("obsolete")]
                        [WasmImport("env", "\"quoted\"\n")]
                        private static void Old() { }

                        [WasmImport("env", "caller")]
                        public int WithCaller(WasmCaller caller) => caller.Instance is null ? 0 : 1;

                        [WasmImport("env", "caller2")]
                        public static (int, object?) WithCallerAndValues(WasmCaller caller, int value, object? reference) => (value, reference);
                    }
                }

                public partial struct Values
                {
                    [WasmImport("env", "f")]
                    public static void F() { }
                }
            }

            public static partial class InGlobalNamespace
            {
                [WasmImport("", "")]
                public static void F() { }
            }
            """);

        Assert.Empty(result.Diagnostics);
        Assert.HasCount(3, result.Sources);
    }

    [Fact]
    public void Generator_TextModule_CompilesWithoutWarnings()
    {
        var result = RunGenerator(
            "",
            [("/project/all-types.wat", """
                (module
                  (type $s (struct (field i32)))
                  (type $a (array i8))
                  (rec (type $f (func (param (ref $f)))))
                  (func (export "numbers") (param $class i32) (param $value i64) (param f32 f64) (result f64 f32 i64 i32) unreachable)
                  (func (export "refs") (param externref (ref extern) funcref (ref func) (ref null $f) (ref $s) (ref null $a) anyref eqref i31ref structref arrayref exnref nullref nullexternref nullfuncref nullexnref) unreachable)
                  (func (export "results") (result externref (ref extern) funcref (ref func) anyref v128) unreachable)
                  (func (export "one_extern") (result externref) unreachable)
                  (func (export "") (export "1st") (export "a b") (export "ToString") (export "AllTypesModule") (export "<summary>&") unreachable)
                  (memory (export "memory") 1)
                  (table (export "table") 1 funcref)
                  (global (export "global") i32 (i32.const 0))
                  (tag (export "tag")))
                """, null)]);

        Assert.Empty(result.Diagnostics);
        var source = Assert.Single(result.Sources);
        Assert.Contains("namespace App.Root", source);
        Assert.Contains("internal sealed partial class AllTypesModule", source);
        Assert.Contains("public (double, float, long, int) Numbers(int @class, long value, float arg2, double arg3)", source);
        Assert.Contains("public (object?, object, global::Meziantou.Framework.WasmEngine.WasmFunction?, global::Meziantou.Framework.WasmEngine.WasmFunction, global::Meziantou.Framework.WasmEngine.WasmValue, global::Meziantou.Framework.WasmEngine.WasmValue) Results()", source);
        Assert.Contains("public void Export()", source);
        Assert.Contains("public void _1st()", source);
        Assert.Contains("public void AB()", source);
        Assert.Contains("public void ToString2()", source);
        Assert.Contains("public void AllTypesModule2()", source);
        Assert.Contains("public void Summary()", source);
        Assert.Contains("public global::Meziantou.Framework.WasmEngine.WasmMemory Memory { get; }", source);
        Assert.Contains("public global::Meziantou.Framework.WasmEngine.WasmTag Tag { get; }", source);
    }

    [Fact]
    public void Generator_Metadata()
    {
        var result = RunGenerator(
            "",
            [("/project/fib.wat", """(module (func (export "fib") (param i32) (result i32) (local.get 0)))""", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_metadata.AdditionalFiles.WasmClassName"] = "Fibonacci",
                ["build_metadata.AdditionalFiles.WasmNamespace"] = "Other.Space",
                ["build_metadata.AdditionalFiles.WasmVisibility"] = "public",
                ["build_metadata.AdditionalFiles.WasmEmbed"] = "false",
            })]);

        Assert.Empty(result.Diagnostics);
        var source = Assert.Single(result.Sources);
        Assert.Contains("namespace Other.Space", source);
        Assert.Contains("public sealed partial class Fibonacci", source);
        Assert.DoesNotContain("Compile(", source);
    }

    [Fact]
    public void Generator_BinaryModule()
    {
        using var directory = TemporaryDirectory.Create();
        var path = directory.CreateEmptyFile("math.wasm").Value;
        File.WriteAllBytes(path, WasmRuntime.WatToWasm("""(module (func (export "f") (param i32 i64) (result f32) unreachable) (memory (export "m") 1))"""));

        var result = RunGenerator("", [(path, "", null)]);

        Assert.Empty(result.Diagnostics);
        var source = Assert.Single(result.Sources);
        Assert.Contains("internal sealed partial class MathModule", source);
        Assert.Contains("public float F(int arg0, long arg1)", source);
        Assert.Contains("private static global::System.ReadOnlySpan<byte> __ModuleBytes => [0x00, 0x61, 0x73, 0x6D,", source);
    }

    [Theory]
    [InlineData("(module (func (export \"f\") (param i33)))", 1, 35)]
    [InlineData("(module\n  (export \"f\" (func $missing)))", 2, 21)]
    [InlineData("(module (func (export \"f\")) (func (export \"f\")))", 1, 35)]
    [InlineData("(module", 1, 1)]
    public void Generator_InvalidTextModule_ReportsADiagnostic(string text, int line, int column)
    {
        var result = RunGenerator("", [("/project/invalid.wat", text, null)]);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("MFWASM005", diagnostic.Id);
        var position = diagnostic.Location.GetLineSpan();
        Assert.Equal("/project/invalid.wat", position.Path);
        Assert.Equal(line, position.StartLinePosition.Line + 1);
        Assert.Equal(column, position.StartLinePosition.Character + 1);
        Assert.Empty(result.Sources);
    }

    [Fact]
    public void Generator_InvalidBinaryModule_ReportsADiagnostic()
    {
        using var directory = TemporaryDirectory.Create();
        var path = directory.CreateEmptyFile("invalid.wasm").Value;
        File.WriteAllBytes(path, [0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00, 0x07, 0x05, 0x01, 0x01, 0x66, 0x00, 0x00]);

        var result = RunGenerator("", [(path, "", null)]);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("MFWASM005", diagnostic.Id);
        Assert.Contains("unknown function 0", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Generator_MissingBinaryModule_ReportsADiagnostic()
    {
        using var directory = TemporaryDirectory.Create();

        var result = RunGenerator("", [((directory.FullPath / "missing.wasm").Value, "", null)]);

        Assert.Equal("MFWASM006", Assert.Single(result.Diagnostics).Id);
    }

    [Fact]
    public void Generator_DuplicateClassName_ReportsADiagnostic()
    {
        var result = RunGenerator("", [("/a/fib.wat", "(module)", null), ("/b/fib.wat", "(module)", null)]);

        Assert.Equal("MFWASM007", Assert.Single(result.Diagnostics).Id);
        Assert.Single(result.Sources);
    }

    [Theory]
    [InlineData("build_metadata.AdditionalFiles.WasmClassName", "1a")]
    [InlineData("build_metadata.AdditionalFiles.WasmClassName", "class")]
    [InlineData("build_metadata.AdditionalFiles.WasmNamespace", "A..B")]
    public void Generator_InvalidName_ReportsADiagnostic(string key, string value)
    {
        var result = RunGenerator("", [("/project/fib.wat", "(module)", new Dictionary<string, string>(StringComparer.Ordinal) { [key] = value })]);

        Assert.Equal("MFWASM008", Assert.Single(result.Diagnostics).Id);
        Assert.Empty(result.Sources);
    }

    private static GeneratorResult RunGenerator(string source, (string Path, string Text, Dictionary<string, string>? Options)[]? files = null)
    {
        files ??= [];
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = CreateCompilation(source, parseOptions);
        var fileOptions = files.ToDictionary(file => file.Path, file => file.Options ?? [], StringComparer.Ordinal);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new WasmModuleGenerator().AsSourceGenerator(), new WasmImportGenerator().AsSourceGenerator()],
            additionalTexts: [.. files.Select(file => (AdditionalText)new TestAdditionalText(file.Path, file.Text))],
            parseOptions: parseOptions,
            optionsProvider: new TestOptionsProvider(new Dictionary<string, string>(StringComparer.Ordinal) { ["build_property.RootNamespace"] = "App.Root" }, fileOptions));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        var inputTrees = compilation.SyntaxTrees.ToHashSet();
        var sources = driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources).Select(generated => generated.SourceText.ToString()).ToImmutableArray();
        var generatedCodeDiagnostics = outputCompilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning && diagnostic.Location.SourceTree is { } tree && !inputTrees.Contains(tree))
            .ToImmutableArray();
        if (!generatedCodeDiagnostics.IsEmpty)
        {
            Assert.Fail("Generated code produced diagnostics:" + Environment.NewLine + string.Join(Environment.NewLine, generatedCodeDiagnostics) + Environment.NewLine + string.Join(Environment.NewLine, sources));
        }

        return new GeneratorResult(generatorDiagnostics, sources);
    }

    private static CSharpCompilation CreateCompilation(string source, CSharpParseOptions parseOptions)
    {
        var references = new List<MetadataReference>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string assemblies)
        {
            paths.UnionWith(assemblies.Split(Path.PathSeparator));
        }

        paths.Add(typeof(WasmRuntime).Assembly.Location);
        foreach (var path in paths)
        {
            if (path.Length > 0 && File.Exists(path))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        return CSharpCompilation.Create(
            assemblyName: "Meziantou.Framework.WasmEngine.SourceGeneration.Tests.Input",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    private sealed record GeneratorResult(ImmutableArray<Diagnostic> Diagnostics, ImmutableArray<string> Sources);

    private sealed class TestAdditionalText(string path, string content) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(content);
    }

    private sealed class TestOptionsProvider(Dictionary<string, string> globalOptions, Dictionary<string, Dictionary<string, string>> fileOptions) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions => new TestOptions(globalOptions);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new TestOptions([]);

        // The options of a file include the global options, as with an .editorconfig file
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            var options = new Dictionary<string, string>(globalOptions, StringComparer.Ordinal);
            if (fileOptions.TryGetValue(textFile.Path, out var values))
            {
                foreach (var (key, value) in values)
                {
                    options[key] = value;
                }
            }

            return new TestOptions(options);
        }
    }

    private sealed class TestOptions(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => values.TryGetValue(key, out value);
    }
}
