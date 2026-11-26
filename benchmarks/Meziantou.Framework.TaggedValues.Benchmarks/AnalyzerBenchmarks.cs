using System.Collections.Immutable;
using System.Runtime.Loader;
using BenchmarkDotNet.Attributes;
using Meziantou.Framework.TaggedValues.Analyzer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Meziantou.Framework.TaggedValues.Benchmarks;

/// <summary>
/// Runs the analyzer on a generated application of 40 files. The time includes the binding of the compilation, which the
/// <c>None</c> analyzer measures alone: it registers the same actions as the analyzer, and does nothing.
/// </summary>
/// <remarks>
/// Set the <c>TAGGEDVALUES_BASELINE_ANALYZER</c> environment variable to the path of another build of
/// <c>Meziantou.Framework.TaggedValues.Analyzer.dll</c> to compare it with the current build in the same run (<c>Baseline</c>).
/// </remarks>
[MemoryDiagnoser]
public class AnalyzerBenchmarks
{
    private const string BaselineEnvironmentVariable = "TAGGEDVALUES_BASELINE_ANALYZER";

    private CSharpCompilation _compilation = null!;
    private ImmutableArray<DiagnosticAnalyzer> _analyzers;
    private AnalyzerOptions _options = null!;

    public enum Workload
    {
        /// <summary>Code that has no annotation, the most common code in a project that references the package.</summary>
        Untagged,

        /// <summary>Annotated code.</summary>
        Tagged,

        /// <summary>Annotated code with the strict mode and the naming conventions enabled.</summary>
        TaggedStrict,
    }

    [Params(Workload.Untagged, Workload.Tagged, Workload.TaggedStrict)]
    public Workload Code { get; set; }

    [ParamsSource(nameof(AnalyzerNames))]
    public string Analyzer { get; set; } = "";

    public static IEnumerable<string> AnalyzerNames()
    {
        yield return "None";
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(BaselineEnvironmentVariable)))
            yield return "Baseline";

        yield return "Current";
    }

    [GlobalSetup]
    public void Setup()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var trees = BenchmarkSources.Create(fileCount: 40, tagged: Code is not Workload.Untagged)
            .Select((source, index) => CSharpSyntaxTree.ParseText(source, parseOptions, path: "File" + index + ".cs"))
            .ToArray();

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(path) is "netstandard.dll" or "mscorlib.dll")
            .Select(path => MetadataReference.CreateFromFile(path));

        _compilation = CSharpCompilation.Create("Benchmark", trees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var errors = _compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error).ToArray();
        if (errors.Length > 0)
            throw new InvalidOperationException("The generated code does not compile: " + string.Join(Environment.NewLine, errors.Take(10)));

        var options = Code is Workload.TaggedStrict
            ? new Dictionary<string, string>(StringComparer.Ordinal) { ["taggedvalues.strict"] = "true", ["taggedvalues.infer_tags_from_names"] = "true" }
            : [];
        _options = new AnalyzerOptions([], new OptionsProvider(options));
        _analyzers = [CreateAnalyzer(Analyzer)];
    }

    [Benchmark]
    public int Analyze()
    {
        var compilationWithAnalyzers = _compilation.WithAnalyzers(_analyzers, new CompilationWithAnalyzersOptions(_options, onAnalyzerException: null, concurrentAnalysis: false, logAnalyzerExecutionTime: false));
        return compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None).GetAwaiter().GetResult().Length;
    }

    private static DiagnosticAnalyzer CreateAnalyzer(string name)
    {
        switch (name)
        {
            case "None":
                return new EmptyAnalyzer();

            case "Current":
                return new ValueTagAnalyzer();

            case "Baseline":
                // Microsoft.CodeAnalysis is not in the folder of the baseline, so the load context resolves it from the default context
                var path = Path.GetFullPath(Environment.GetEnvironmentVariable(BaselineEnvironmentVariable)!);
                var assembly = new AssemblyLoadContext("Baseline").LoadFromAssemblyPath(path);
                return (DiagnosticAnalyzer)Activator.CreateInstance(assembly.GetType(typeof(ValueTagAnalyzer).FullName!, throwOnError: true)!)!;

            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, message: null);
        }
    }

    /// <summary>
    /// Registers the actions of <see cref="ValueTagAnalyzer"/>, so the compiler creates the same operations and symbols, and does nothing.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class EmptyAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor Rule = new("EMPTY", "Empty", "Empty", "Empty", DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(context =>
            {
                context.RegisterOperationAction(_ => { }, OperationKind.Binary, OperationKind.TupleBinary, OperationKind.Invocation, OperationKind.Argument, OperationKind.SimpleAssignment, OperationKind.CoalesceAssignment,
                    OperationKind.DeconstructionAssignment, OperationKind.Loop, OperationKind.VariableDeclarator, OperationKind.DeclarationPattern, OperationKind.RecursivePattern, OperationKind.ListPattern,
                    OperationKind.FieldInitializer, OperationKind.PropertyInitializer, OperationKind.Return, OperationKind.YieldReturn, OperationKind.CompoundAssignment, OperationKind.Unary,
                    OperationKind.Increment, OperationKind.Decrement, OperationKind.Conversion, OperationKind.Conditional, OperationKind.Coalesce, OperationKind.SwitchExpression,
                    OperationKind.ArrayInitializer, OperationKind.CollectionExpression, OperationKind.ObjectOrCollectionInitializer, OperationKind.Attribute);
                context.RegisterSemanticModelAction(_ => { });
                context.RegisterOperationBlockAction(_ => { });
                context.RegisterSymbolStartAction(context => context.RegisterOperationBlockAction(_ => { }), SymbolKind.NamedType);
                context.RegisterSymbolAction(_ => { }, SymbolKind.Method, SymbolKind.Property, SymbolKind.Field, SymbolKind.NamedType);
                context.RegisterCompilationEndAction(_ => { });
            });
        }
    }

    private sealed class OptionsProvider(Dictionary<string, string> values) : AnalyzerConfigOptionsProvider
    {
        private readonly Options _options = new(values);

        public override AnalyzerConfigOptions GlobalOptions => _options;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _options;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _options;
    }

    private sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            if (values.TryGetValue(key, out var result))
            {
                value = result;
                return true;
            }

            value = "";
            return false;
        }
    }
}
