#nullable enable
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using Meziantou.Framework.Roslyn;

namespace Meziantou.Framework.Roslyn.Tests;

public sealed class RoslynHelperTests
{
    private static readonly CSharpParseOptions DefaultParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12);
    private static readonly CSharpCompilationOptions DefaultCompilationOptions = new(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);

    [Fact]
    public void IsNet9OrGreater_ReturnsResultFromCoreAssemblyVersion()
    {
        var compilation = CreateCompilation("""
            public class Sample;
            """);

        Assert.Equal(typeof(object).Assembly.GetName().Version!.Major >= 9, compilation.IsNet9OrGreater());
    }

    [Fact]
    public void GetBestTypeByMetadataName_ReturnsSourceTypeBeforeReferencedType()
    {
        var referenceCompilation = CreateCompilation("""
            public class Sample;
            """, assemblyName: "Reference");
        var compilation = CreateCompilation("""
            public class Sample;
            """, additionalReferences: [referenceCompilation.ToMetadataReference()]);

        var type = compilation.GetBestTypeByMetadataName("Sample");

        Assert.True(SymbolEqualityComparer.Default.Equals(compilation.GetTypeByMetadataName("Sample"), type));
    }

    [Fact]
    public void GetBestTypeByMetadataName_ReturnsNullForAmbiguousReferencedTypes()
    {
        var referenceCompilation1 = CreateCompilation("""
            public class Sample;
            """, assemblyName: "Reference1");
        var referenceCompilation2 = CreateCompilation("""
            public class Sample;
            """, assemblyName: "Reference2");
        var compilation = CreateCompilation("""
            public class Consumer;
            """, additionalReferences: [referenceCompilation1.ToMetadataReference(), referenceCompilation2.ToMetadataReference()]);

        var type = compilation.GetBestTypeByMetadataName("Sample");

        Assert.Null(type);
    }

    [Fact]
    public void GetCSharpLanguageVersion_FromOperation_ReturnsParseOptionsLanguageVersion()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M()
                {
                    var value = 1;
                }
            }
            """, parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11));
        var semanticModel = GetSemanticModel(compilation);
        var operation = GetInitializerOperation(semanticModel, "value");

        Assert.Equal(LanguageVersion.CSharp11, operation.GetCSharpLanguageVersion());
    }

    [Fact]
    public void GetCSharpLanguageVersion_FromSyntaxNode_ReturnsParseOptionsLanguageVersion()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
            }
            """, parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp10));
        var typeDeclaration = compilation.SyntaxTrees.Single().GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();

        Assert.Equal(LanguageVersion.CSharp10, typeDeclaration.GetCSharpLanguageVersion());
    }

    [Fact]
    public void GetCSharpLanguageVersion_FromSyntaxTree_ReturnsParseOptionsLanguageVersion()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
            }
            """, parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9));
        var syntaxTree = compilation.SyntaxTrees.Single();

        Assert.Equal(LanguageVersion.CSharp9, syntaxTree.GetCSharpLanguageVersion());
    }

    [Fact]
    public void GetCSharpLanguageVersion_FromCompilation_ReturnsFirstSyntaxTreeLanguageVersion()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
            }
            """, parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp8));

        Assert.Equal(LanguageVersion.CSharp8, compilation.GetCSharpLanguageVersion());
    }

    [Fact]
    public void IsCSharp8OrGreater_ReturnsTrueOnlyForCSharp8AndLater()
    {
        Assert.False(LanguageVersion.CSharp7_3.IsCSharp8OrGreater());
        Assert.True(LanguageVersion.CSharp8.IsCSharp8OrGreater());
    }

    [Fact]
    public void IsCSharp9OrGreater_ReturnsTrueOnlyForCSharp9AndLater()
    {
        Assert.False(LanguageVersion.CSharp8.IsCSharp9OrGreater());
        Assert.True(LanguageVersion.CSharp9.IsCSharp9OrGreater());
    }

    [Fact]
    public void IsCSharp10OrGreater_ReturnsTrueOnlyForCSharp10AndLater()
    {
        Assert.False(LanguageVersion.CSharp9.IsCSharp10OrGreater());
        Assert.True(LanguageVersion.CSharp10.IsCSharp10OrGreater());
    }

    [Fact]
    public void IsCSharp11OrGreater_ReturnsTrueOnlyForCSharp11AndLater()
    {
        Assert.False(LanguageVersion.CSharp10.IsCSharp11OrGreater());
        Assert.True(LanguageVersion.CSharp11.IsCSharp11OrGreater());
    }

    [Fact]
    public void IsCSharp12OrGreater_ReturnsTrueOnlyForCSharp12AndLater()
    {
        Assert.False(LanguageVersion.CSharp11.IsCSharp12OrGreater());
        Assert.True(LanguageVersion.CSharp12.IsCSharp12OrGreater());
    }

    [Fact]
    public void IsCSharp13OrGreater_ReturnsTrueOnlyForCSharp13AndLater()
    {
        Assert.False(LanguageVersion.CSharp12.IsCSharp13OrGreater());
        Assert.True(((LanguageVersion)1300).IsCSharp13OrGreater());
    }

    [Fact]
    public void IsCSharp14OrGreater_ReturnsTrueOnlyForCSharp14AndLater()
    {
        Assert.False(((LanguageVersion)1300).IsCSharp14OrGreater());
        Assert.True(((LanguageVersion)1400).IsCSharp14OrGreater());
    }

    [Fact]
    public void IsCSharp15OrGreater_ReturnsValueBasedOnAvailableRoslynConstants()
    {
#if ROSLYN_5_6_OR_GREATER
        Assert.True(LanguageVersion.Preview.IsCSharp15OrGreater());
#else
        Assert.False(LanguageVersion.Preview.IsCSharp15OrGreater());
#endif
    }

    [Fact]
    public void IsNamespace_MatchesTheFullNamespaceChain()
    {
        var compilation = CreateCompilation("""
            namespace Demo.Inner;

            public class Sample;
            """);
        var type = GetRequiredType(compilation, "Demo.Inner.Sample");

        Assert.True(type.ContainingNamespace.MatchesNamespace(["Demo", "Inner"]));
        Assert.False(type.ContainingNamespace.MatchesNamespace(["Inner"]));
    }

    [Fact]
    public void TryFindNode_ReturnsTheNodeAtTheDiagnosticLocation()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M(int value)
                {
                }
            }
            """);
        var root = compilation.SyntaxTrees.Single().GetRoot();
        var parameter = root.DescendantNodes().OfType<ParameterSyntax>().Single();
        var descriptor = CreateDescriptor();
        var diagnostic = Diagnostic.Create(descriptor, parameter.GetLocation());

        Assert.Same(parameter, diagnostic.FindNode(default));
    }

    [Fact]
    public void ReportDiagnostic_IsAvailableAsDiagnosticReporterExtensionMethod()
    {
        var reportDiagnosticMethods = typeof(ContextExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static);

        Assert.Contains(reportDiagnosticMethods, method => method.Name == "ReportDiagnostic");
    }

    [Fact]
    public async Task DiagnosticReporter_ExposesTheAnalyzerOptionsOfTheContext()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M() => System.Console.WriteLine();
            }
            """);

        var diagnostics = await compilation
            .WithAnalyzers([new AnalyzerOptionsAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "Message CodeBlockAnalysisContext",
                "Message CompilationAnalysisContext",
                "Message OperationAnalysisContext",
                "Message OperationBlockAnalysisContext",
                "Message SemanticModelAnalysisContext",
                "Message SymbolAnalysisContext",
                "Message SyntaxNodeAnalysisContext",
                "Message SyntaxTreeAnalysisContext",
            ],
            diagnostics.Select(diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)).Distinct(StringComparer.Ordinal).OrderBy(message => message, StringComparer.Ordinal));
    }

    [Fact]
    public async Task DiagnosticReporter_DoesNotFilterDiagnosticsByDefault()
    {
        using var scope = await DiagnosticFilterScope.EnterAsync(TestContext.Current.CancellationToken);

        Assert.Null(DiagnosticReporter.CanReportDiagnostic);
    }

    [Fact]
    public async Task DiagnosticReporter_CanReportDiagnosticReceivesTheDiagnosticAndTheContextData()
    {
        var compilation = CreateCompilation("""
            public class Sample;
            """);
        var symbol = GetRequiredType(compilation, "Sample");
        var descriptor = CreateDescriptor("MFTEST100");
        var options = new AnalyzerOptions([]);
        using var cancellationTokenSource = new CancellationTokenSource();
        Diagnostic? reported = null;

        // A DiagnosticAnalyzer cannot be defined in this assembly (RS1041), so the context is created directly
#pragma warning disable CS0618
        var context = new SymbolAnalysisContext(symbol, compilation, options, diagnostic => reported = diagnostic, _ => true, cancellationTokenSource.Token);
#pragma warning restore CS0618

        Diagnostic? filteredDiagnostic = null;
        AnalyzerOptions? filteredOptions = null;
        CancellationToken filteredCancellationToken = default;

        using (var scope = await DiagnosticFilterScope.EnterAsync(TestContext.Current.CancellationToken))
        {
            // The filter is global, so the diagnostics of the tests running concurrently must not be filtered
            DiagnosticReporter.CanReportDiagnostic = (diagnostic, analyzerOptions, cancellationToken) =>
            {
                if (!ReferenceEquals(diagnostic.Descriptor, descriptor))
                    return true;

                filteredDiagnostic = diagnostic;
                filteredOptions = analyzerOptions;
                filteredCancellationToken = cancellationToken;
                return false;
            };

            context.ReportDiagnostic(descriptor, symbol);
        }

        Assert.Null(reported);
        Assert.NotNull(filteredDiagnostic);
        Assert.Same(descriptor, filteredDiagnostic.Descriptor);
        Assert.Same(compilation.SyntaxTrees.Single(), filteredDiagnostic.Location.SourceTree);
        Assert.Same(options, filteredOptions);
        Assert.Equal(cancellationTokenSource.Token, filteredCancellationToken);
    }

    [Fact]
    public async Task DiagnosticReporter_CanReportDiagnosticReportsTheDiagnosticWhenItReturnsTrue()
    {
        var compilation = CreateCompilation("""
            public class Sample;
            """);
        var symbol = GetRequiredType(compilation, "Sample");
        var descriptor = CreateDescriptor("MFTEST101");
        Diagnostic? reported = null;

        // A DiagnosticAnalyzer cannot be defined in this assembly (RS1041), so the context is created directly
#pragma warning disable CS0618
        var context = new SymbolAnalysisContext(symbol, compilation, new AnalyzerOptions([]), diagnostic => reported = diagnostic, _ => true, cancellationToken: default);
#pragma warning restore CS0618

        using (var scope = await DiagnosticFilterScope.EnterAsync(TestContext.Current.CancellationToken))
        {
            DiagnosticReporter.CanReportDiagnostic = (diagnostic, analyzerOptions, cancellationToken) => true;

            context.ReportDiagnostic(descriptor, symbol);
        }

        Assert.NotNull(reported);
        Assert.Same(descriptor, reported.Descriptor);
    }

    [Fact]
    public async Task DiagnosticReporter_CanReportDiagnosticFiltersTheDiagnosticsOfAnAnalyzer()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
            }
            """);

        ImmutableArray<Diagnostic> diagnostics;
        using (var scope = await DiagnosticFilterScope.EnterAsync(TestContext.Current.CancellationToken))
        {
            // The filter is global, so the diagnostics of the tests running concurrently must not be filtered
            DiagnosticReporter.CanReportDiagnostic = (diagnostic, analyzerOptions, cancellationToken)
                => diagnostic.Id is not DiagnosticFilterAnalyzer.DiagnosticId || !diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains("dropped", StringComparison.Ordinal);

            diagnostics = await compilation
                .WithAnalyzers([new DiagnosticFilterAnalyzer()])
                .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(
            [
                "Message context-kept",
                "Message reporter-kept",
                "Message tree-kept",
            ],
            diagnostics.Select(diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)).OrderBy(message => message, StringComparer.Ordinal));
    }

    [Fact]
    public async Task DiagnosticReporter_ReportsTheDiagnosticsOfAnAdditionalFileAnalysisContext()
    {
        var compilation = CreateCompilation("""
            public class Sample;
            """);

        var diagnostics = await compilation
            .WithAnalyzers([new AdditionalFileAnalyzer()], new AnalyzerOptions([new TestAdditionalText("sample.txt", "content")]))
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            ["Message sample.txt"],
            diagnostics.Select(diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void ReportDiagnostic_DeclaresMessageArgsAsParams()
    {
        var messageArgsParameters = typeof(ContextExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == "ReportDiagnostic")
            .Select(method => method.GetParameters()[^1])
            .ToArray();

        Assert.NotEmpty(messageArgsParameters);
        Assert.All(messageArgsParameters, parameter =>
        {
            Assert.Equal("messageArgs", parameter.Name);
            Assert.True(parameter.IsDefined(typeof(ParamArrayAttribute), inherit: false), $"messageArgs is not a params parameter on {parameter.Member}");
        });
    }

    [Fact]
    public async Task ReportDiagnostic_AcceptsMessageArgsWithoutAnExplicitArray()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
            }
            """);

        var diagnostics = await compilation
            .WithAnalyzers([new MessageArgsAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "Message context-locations",
                "Message context-node",
                "Message reporter-location",
                "Message reporter-locations",
                "Message reporter-node",
                "Message reporter-properties",
                "Message reporter-token",
                "Message {0}",
            ],
            diagnostics.Select(diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)).OrderBy(message => message, StringComparer.Ordinal));
    }

    [Fact]
    public void GetActualType_FollowsLocalAssignments()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M()
                {
                    var value = 41;
                    value = 42;
                    object boxed = value;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");

        Assert.Equal(SpecialType.System_Int32, boxed.GetActualType(default)?.SpecialType);
    }

    [Fact]
    public void TryGetConstantValue_FollowsLocalAssignmentsAndMemberInitializers()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                private readonly object _field = 3;
                private object Property { get; } = "text";

                public void M()
                {
                    var value = 41;
                    value = 42;
                    object boxedLocal = value;
                    object boxedField = _field;
                    object boxedProperty = Property;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var boxedLocal = GetInitializerOperation(semanticModel, "boxedLocal");
        var boxedField = GetInitializerOperation(semanticModel, "boxedField");
        var boxedProperty = GetInitializerOperation(semanticModel, "boxedProperty");

        Assert.True(boxedLocal.TryGetConstantValue(out var localValue, default));
        Assert.Equal(42, localValue);
        Assert.True(boxedField.TryGetConstantValue(out var fieldValue, default));
        Assert.Equal(3, fieldValue);
        Assert.True(boxedProperty.TryGetConstantValue(out var propertyValue, default));
        Assert.Equal("text", propertyValue);
    }

    [Fact]
    public void GetActualType_IgnoresAssignmentsFromOtherMembers()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void Other()
                {
                    object value = "text";
                    value = 41;
                }

                public void M()
                {
                    object value = 42;
                    object boxed = value;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");

        Assert.Equal(SpecialType.System_Int32, boxed.GetActualType(default)?.SpecialType);
    }

    [Fact]
    public void TryGetConstantValue_FollowsAssignmentsInTopLevelStatements()
    {
        var compilation = CreateCompilation("""
            var value = 41;
            value = 42;
            object boxed = value;
            """, compilationOptions: DefaultCompilationOptions.WithOutputKind(OutputKind.ConsoleApplication));
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");

        Assert.True(boxed.TryGetConstantValue(out var value, default));
        Assert.Equal(42, value);
    }

    [Fact]
    public void TryGetConstantValue_ReturnsFalseWhenATopLevelStatementWritesTheLocal()
    {
        var compilation = CreateCompilation("""
            var value = 41;
            value = 42;
            value++;
            object boxed = value;
            """, compilationOptions: DefaultCompilationOptions.WithOutputKind(OutputKind.ConsoleApplication));
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");

        Assert.False(boxed.TryGetConstantValue(out var value, default));
        Assert.Null(value);
    }

    [Fact]
    public void TryGetConstantValue_FollowsChainedMemberInitializers()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                private static readonly int a = 42;
                private static readonly int b = a;
                private static readonly int c = b;

                public void M()
                {
                    object boxed = c;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");

        Assert.True(boxed.TryGetConstantValue(out var value, default));
        Assert.Equal(42, value);
    }

    [Fact]
    public void TryGetConstantValue_StopsOnMutuallyReferencingFields()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                private static readonly int a = b;
                private static readonly int b = a;

                public void M()
                {
                    object boxed = a;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");

        Assert.False(boxed.TryGetConstantValue(out var value, default));
        Assert.Null(value);
    }

    [Fact]
    public void TryGetConstantValue_StopsOnMutuallyReferencingProperties()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                private static int P { get; } = Q;
                private static int Q { get; } = P;

                public void M()
                {
                    object boxed = P;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");

        Assert.False(boxed.TryGetConstantValue(out var value, default));
        Assert.Null(value);
    }

    [Fact]
    public void GetActualType_StopsOnMutuallyReferencingFields()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                private static readonly int a = b;
                private static readonly int b = a;

                public void M()
                {
                    object boxed = a;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");

        Assert.Equal(SpecialType.System_Int32, boxed.GetActualType(default)?.SpecialType);
    }

    [Fact]
    public void TryGetConstantValue_ReturnsFalseWhenReadOnlyFieldIsWrittenThroughAnOutArgument()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                private readonly int _x = 5;

                public Sample()
                {
                    Init(out _x);
                }

                private static void Init(out int value) => value = 10;

                public void M()
                {
                    object boxed = _x;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");

        Assert.False(boxed.TryGetConstantValue(out var value, default));
        Assert.Null(value);
    }

    [Fact]
    public void TryGetConstantValue_ReturnsFalseWhenReadOnlyFieldIsIncrementedInConstructor()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                private readonly int _x = 5;

                public Sample()
                {
                    _x++;
                }

                public void M()
                {
                    object boxed = _x;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");

        Assert.False(boxed.TryGetConstantValue(out var value, default));
        Assert.Null(value);
    }

    [Fact]
    public void TryGetConstantValue_ReturnsFalseWhenReadOnlyFieldIsAliasedByARefLocal()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                private readonly int _x = 5;

                public Sample()
                {
                    ref var alias = ref _x;
                    alias = 10;
                }

                public void M()
                {
                    object boxed = _x;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");

        Assert.False(boxed.TryGetConstantValue(out var value, default));
        Assert.Null(value);
    }

    [Fact]
    public void TryGetConstantValue_ReturnsFalseWhenLocalIsWrittenThroughARefArgumentInTopLevelStatements()
    {
        var compilation = CreateCompilation("""
            var text = "a";
            Modify(ref text);
            object boxed = text;

            static void Modify(ref string value) => value = "b";
            """, compilationOptions: DefaultCompilationOptions.WithOutputKind(OutputKind.ConsoleApplication));
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");

        Assert.False(boxed.TryGetConstantValue(out var value, default));
        Assert.Null(value);
    }

    [Fact]
    public void TryGetConstantValue_FollowsLocalInitializersInTopLevelStatements()
    {
        var compilation = CreateCompilation("""
            var text = "a";
            System.Console.WriteLine("unrelated");
            object boxed = text;
            """, compilationOptions: DefaultCompilationOptions.WithOutputKind(OutputKind.ConsoleApplication));
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");

        Assert.True(boxed.TryGetConstantValue(out var value, default));
        Assert.Equal("a", value);
    }

    [Fact]
    public void TryGetConstantValue_ReturnsFalseWhenABackwardGotoCanReachTheReadAfterAWrite()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M(bool condition)
                {
                    var value = "a";
                Loop:
                    if (condition) { }
                    object boxed = value;
                    value = "b";
                    if (condition) goto Loop;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");

        Assert.False(boxed.TryGetConstantValue(out var value, default));
        Assert.Null(value);
    }

    [Fact]
    public void TryGetConstantValue_ReturnsFalseWhenAGotoCanSkipAnAssignment()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M(bool condition)
                {
                    var value = "a";
                    if (condition) goto Skip;
                    value = "b";
                Skip:
                    object boxed = value;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");

        Assert.False(boxed.TryGetConstantValue(out var value, default));
        Assert.Null(value);
    }

    [Fact]
    public void TryGetConstantValue_IsNotAffectedByAJumpInAnotherMethod()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M()
                {
                    var value = 42;
                    object boxed = value;
                }

                public void Other(bool condition)
                {
                Loop:
                    if (condition) goto Loop;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");

        Assert.True(boxed.TryGetConstantValue(out var value, default));
        Assert.Equal(42, value);
    }

    [Fact]
    public void GetActualType_ReturnsTheDeclaredTypeWhenABackwardGotoCanReachTheReadAfterAWrite()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M(bool condition)
                {
                    object value = 42;
                Loop:
                    if (condition) { }
                    object boxed = value;
                    value = "b";
                    if (condition) goto Loop;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");

        Assert.Equal(SpecialType.System_Object, boxed.GetActualType(default)?.SpecialType);
    }

    [Fact]
    public void GetActualType_WithoutDataFlowAnalysis_OnlyUnwrapsConversions()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M()
                {
                    object value = 42;
                    object boxed = value;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");

        Assert.Equal(SpecialType.System_Int32, boxed.GetActualType(useDataFlowAnalysis: true, default)?.SpecialType);
        Assert.Equal(SpecialType.System_Object, boxed.GetActualType(useDataFlowAnalysis: false, default)?.SpecialType);
    }

    [Fact]
    public void TryGetConstantValue_WithoutDataFlowAnalysis_OnlyUsesTheUnwrappedOperationValue()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M()
                {
                    var value = 42;
                    object boxed = value;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");

        Assert.True(boxed.TryGetConstantValue(useDataFlowAnalysis: true, out var flowValue, default));
        Assert.Equal(42, flowValue);
        Assert.False(boxed.TryGetConstantValue(useDataFlowAnalysis: false, out var unwrappedValue, default));
        Assert.Null(unwrappedValue);
    }

    [Fact]
    public void IsPrimaryConstructor_ReturnsTrueForPrimaryClassConstructor()
    {
        var compilation = CreateCompilation("""
            public class Customer(string name);
            """);
        var type = GetRequiredType(compilation, "Customer");
        var constructor = type.InstanceConstructors.Single(method => method.Parameters.Length == 1);

        Assert.True(constructor.IsPrimaryConstructor(default));
    }

    [Fact]
    public void IsInterfaceImplementation_ReturnsTrueForMethodsPropertiesAndEvents()
    {
        var compilation = CreateCompilation("""
            using System;

            public interface ISample
            {
                void M();
                string Property { get; }
                event EventHandler? Changed;
            }

            public class Sample : ISample
            {
                public void M() { }
                string ISample.Property => "";
                event EventHandler? ISample.Changed { add { } remove { } }
                public void Other() { }
            }
            """);
        var type = GetRequiredType(compilation, "Sample");
        var method = GetRequiredMethod(type, "M");
        var property = type.GetMembers().OfType<IPropertySymbol>().Single(symbol => symbol.ExplicitInterfaceImplementations.Length == 1);
        var @event = type.GetMembers().OfType<IEventSymbol>().Single(symbol => symbol.ExplicitInterfaceImplementations.Length == 1);
        var other = GetRequiredMethod(type, "Other");

        Assert.True(method.IsInterfaceImplementation());
        Assert.True(property.IsInterfaceImplementation());
        Assert.True(@event.IsInterfaceImplementation());
        Assert.False(other.IsInterfaceImplementation());
    }

    [Fact]
    public void GetImplementingInterfaceSymbol_ReturnsImplementedMethodPropertyAndEvent()
    {
        var compilation = CreateCompilation("""
            using System;

            public interface ISample
            {
                void M();
                string Property { get; }
                event EventHandler? Changed;
            }

            public class Sample : ISample
            {
                public void M() { }
                string ISample.Property => "";
                event EventHandler? ISample.Changed { add { } remove { } }
            }
            """);
        var type = GetRequiredType(compilation, "Sample");
        var method = GetRequiredMethod(type, "M");
        var property = type.GetMembers().OfType<IPropertySymbol>().Single(symbol => symbol.ExplicitInterfaceImplementations.Length == 1);
        var @event = type.GetMembers().OfType<IEventSymbol>().Single(symbol => symbol.ExplicitInterfaceImplementations.Length == 1);

        Assert.Equal("M", method.GetImplementedInterfaceMember()?.Name);
        Assert.Equal("Property", property.GetImplementedInterfaceMember()?.Name);
        Assert.Equal("Changed", @event.GetImplementedInterfaceMember()?.Name);
    }

    [Fact]
    public void IsOrOverrideMethod_ReturnsTrueForTheMethodAndItsOverrides()
    {
        var compilation = CreateCompilation("""
            public class Base
            {
                public virtual string M() => "";
                public void Other() { }
            }

            public class Derived : Base
            {
                public override string M() => "";
            }
            """);
        var baseType = GetRequiredType(compilation, "Base");
        var derivedType = GetRequiredType(compilation, "Derived");
        var baseMethod = GetRequiredMethod(baseType, "M");
        var derivedMethod = GetRequiredMethod(derivedType, "M");
        var other = GetRequiredMethod(baseType, "Other");

        Assert.True(derivedMethod.IsOrOverrides(baseMethod));
        Assert.True(baseMethod.IsOrOverrides(baseMethod));
        Assert.False(other.IsOrOverrides(baseMethod));
    }

    [Fact]
    public void Override_ReturnsTrueWhenMethodOverridesBaseSymbol()
    {
        var compilation = CreateCompilation("""
            public class Base
            {
                public virtual string M() => "";
                public void Other() { }
            }

            public class Derived : Base
            {
                public override string M() => "";
            }
            """);
        var baseType = GetRequiredType(compilation, "Base");
        var derivedType = GetRequiredType(compilation, "Derived");
        var baseMethod = GetRequiredMethod(baseType, "M");
        var derivedMethod = GetRequiredMethod(derivedType, "M");
        var other = GetRequiredMethod(baseType, "Other");

        Assert.True(derivedMethod.Overrides(baseMethod));
        Assert.False(other.Overrides(baseMethod));
    }

    [Fact]
    public void GetReturnTypeAttribute_ReturnsMatchingReturnAttribute()
    {
        var compilation = CreateCompilation("""
            using System;

            public class BaseAttribute : Attribute;
            public sealed class SpecialAttribute : BaseAttribute;

            public class Sample
            {
                [return: Special]
                public string M() => "";
            }
            """);
        var type = GetRequiredType(compilation, "Sample");
        var baseAttribute = GetRequiredType(compilation, "BaseAttribute");
        var method = GetRequiredMethod(type, "M");

        Assert.NotNull(method.GetReturnTypeAttribute(baseAttribute));
        Assert.Null(method.GetReturnTypeAttribute(baseAttribute, inherits: false));
    }

    [Fact]
    public void HasReturnTypeAttribute_ReturnsTrueWhenReturnAttributeMatches()
    {
        var compilation = CreateCompilation("""
            using System;

            public class BaseAttribute : Attribute;
            public sealed class SpecialAttribute : BaseAttribute;

            public class Sample
            {
                [return: Special]
                public string M() => "";
            }
            """);
        var type = GetRequiredType(compilation, "Sample");
        var baseAttribute = GetRequiredType(compilation, "BaseAttribute");
        var method = GetRequiredMethod(type, "M");

        Assert.True(method.HasReturnTypeAttribute(baseAttribute));
        Assert.False(method.HasReturnTypeAttribute(baseAttribute, inherits: false));
    }

    [Fact]
    public void HasReturnTypeAttribute_DoesNotConsiderAttributesAppliedToOverriddenMethods()
    {
        var compilation = CreateCompilation("""
            using System;

            public class BaseAttribute : Attribute;

            public class Parent
            {
                [return: Base]
                public virtual string M() => "";
            }

            public class Child : Parent
            {
                public override string M() => "";
            }
            """);
        var child = GetRequiredType(compilation, "Child");
        var baseAttribute = GetRequiredType(compilation, "BaseAttribute");
        var method = GetRequiredMethod(child, "M");

        Assert.False(method.HasReturnTypeAttribute(baseAttribute));
        Assert.Null(method.GetReturnTypeAttribute(baseAttribute));
    }

    [Fact]
    public void Ancestors_ReturnsOperationParents()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M()
                {
                    var value = 1;
                    _ = nameof(value);
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var argument = GetNameofArgumentOperation(semanticModel);

        Assert.Contains(argument.Ancestors(), operation => operation.Kind == OperationKind.NameOf);
    }

    [Fact]
    public void IsInNameofOperation_ReturnsTrueForNameofArgument()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M()
                {
                    var value = 1;
                    var other = value;
                    _ = nameof(value);
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var argument = GetNameofArgumentOperation(semanticModel);
        var other = GetInitializerOperation(semanticModel, "other");

        Assert.True(argument.IsInNameofOperation());
        Assert.False(other.IsInNameofOperation());
    }

    [Fact]
    public void UnwrapImplicitConversions_RemovesImplicitConversionsOnly()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M()
                {
                    int value = 42;
                    object boxed = value;
                    object explicitBoxed = (object)42;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");
        var explicitCast = GetInitializerOperation(semanticModel, "explicitBoxed");

        Assert.IsAssignableTo<ILocalReferenceOperation>(boxed.UnwrapImplicitConversions());
        Assert.Same(explicitCast, explicitCast.UnwrapImplicitConversions());
    }

    [Fact]
    public void UnwrapConversions_RemovesImplicitAndExplicitConversions()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M()
                {
                    object boxed = (object)42;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");

        Assert.IsAssignableTo<ILiteralOperation>(boxed.UnwrapConversions());
    }

    [Fact]
    public void UnwrapLabels_ReturnsTheLabeledOperationBody()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public object M()
                {
                label:
                    return (object)42;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var label = GetRequiredOperation<ILabeledOperation>(semanticModel);

        Assert.IsAssignableTo<IReturnOperation>(label.UnwrapLabels());
    }

    [Fact]
    public void GetContainingMethod_ReturnsNearestMethodDeclarationSymbol()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M()
                {
                    var value = 1;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var value = GetInitializerOperation(semanticModel, "value");

        Assert.Equal("M", value.GetContainingMethod(default)?.Name);
    }

    [Fact]
    public void GetAttributes_ReturnsAttributesMatchingBaseTypeWhenInheritanceIsEnabled()
    {
        var compilation = CreateCompilation("""
            using System;

            public class BaseAttribute : Attribute;
            public sealed class SpecialAttribute : BaseAttribute;

            [Special]
            public class Sample;
            """);
        var type = GetRequiredType(compilation, "Sample");
        var baseAttribute = GetRequiredType(compilation, "BaseAttribute");

        var attribute = Assert.Single(type.GetAttributes(baseAttribute));
        Assert.Equal("SpecialAttribute", attribute.AttributeClass?.Name);
    }

    [Fact]
    public void GetFirstAttribute_ReturnsFirstMatchingAttribute()
    {
        var compilation = CreateCompilation("""
            using System;

            public class BaseAttribute : Attribute;
            public sealed class SpecialAttribute : BaseAttribute;

            [Special]
            public class Sample;
            """);
        var type = GetRequiredType(compilation, "Sample");
        var baseAttribute = GetRequiredType(compilation, "BaseAttribute");

        Assert.Equal("SpecialAttribute", type.GetFirstAttribute(baseAttribute)?.AttributeClass?.Name);
        Assert.Null(type.GetFirstAttribute(baseAttribute, inherits: false));
    }

    [Fact]
    public void HasAttribute_ReturnsTrueWhenSymbolHasMatchingAttribute()
    {
        var compilation = CreateCompilation("""
            using System;

            public class BaseAttribute : Attribute;
            public sealed class SpecialAttribute : BaseAttribute;

            [Special]
            public class Sample;
            """);
        var type = GetRequiredType(compilation, "Sample");
        var baseAttribute = GetRequiredType(compilation, "BaseAttribute");
        var specialAttribute = GetRequiredType(compilation, "SpecialAttribute");

        Assert.True(type.HasAttribute(baseAttribute));
        Assert.False(type.HasAttribute(baseAttribute, inherits: false));
        Assert.True(type.HasAttribute(specialAttribute, inherits: false));
    }

    [Fact]
    public void HasAttribute_DoesNotConsiderAttributesAppliedToBaseTypes()
    {
        var compilation = CreateCompilation("""
            using System;

            public class BaseAttribute : Attribute;

            [Base]
            public class Parent;
            public class Child : Parent;
            """);
        var child = GetRequiredType(compilation, "Child");
        var baseAttribute = GetRequiredType(compilation, "BaseAttribute");

        Assert.False(child.HasAttribute(baseAttribute));
        Assert.Empty(child.GetAttributes(baseAttribute));
        Assert.Null(child.GetFirstAttribute(baseAttribute));
    }

    [Fact]
    public void IsVisibleOutsideOfAssembly_ReturnsTrueForPublicAndProtectedSymbolChains()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public class PublicNested;
                protected class ProtectedNested;
                internal class InternalNested;
                private class PrivateNested;
            }
            """);
        var type = GetRequiredType(compilation, "Sample");

        Assert.True(type.GetTypeMembers("PublicNested").Single().IsVisibleOutsideOfAssembly());
        Assert.True(type.GetTypeMembers("ProtectedNested").Single().IsVisibleOutsideOfAssembly());
        Assert.False(type.GetTypeMembers("InternalNested").Single().IsVisibleOutsideOfAssembly());
        Assert.False(type.GetTypeMembers("PrivateNested").Single().IsVisibleOutsideOfAssembly());
    }

    [Fact]
    public void IsOverrideOrInterfaceImplementation_ReturnsTrueForOverridesAndInterfaceMembers()
    {
        var compilation = CreateCompilation("""
            public interface ISample
            {
                void InterfaceMethod();
            }

            public class Base
            {
                public virtual string Property => "";
            }

            public class Sample : Base, ISample
            {
                public override string Property => "";
                public void InterfaceMethod() { }
                public void Other() { }
            }
            """);
        var type = GetRequiredType(compilation, "Sample");
        var property = GetRequiredProperty(type, "Property");
        var interfaceMethod = GetRequiredMethod(type, "InterfaceMethod");
        var other = GetRequiredMethod(type, "Other");

        Assert.True(property.IsOverrideOrInterfaceImplementation());
        Assert.True(interfaceMethod.IsOverrideOrInterfaceImplementation());
        Assert.False(other.IsOverrideOrInterfaceImplementation());
    }

    [Fact]
    public void GetSymbolType_ReturnsTheDeclaredTypeForSupportedSymbolKinds()
    {
        var compilation = CreateCompilation("""
            public class Sample<T>
            {
                public string Field = "";
                public string Property { get; } = "";
                public int M(string parameter)
                {
                    string local = parameter;
                    return local.Length;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var type = GetRequiredType(compilation, "Sample`1");
        var field = GetRequiredField(type, "Field");
        var property = GetRequiredProperty(type, "Property");
        var method = GetRequiredMethod(type, "M");
        var parameter = method.Parameters.Single();
        var local = GetRequiredLocal(semanticModel, "local");
        var typeParameter = type.TypeParameters.Single();

        Assert.Same(field.Type, field.GetSymbolType());
        Assert.Same(property.Type, property.GetSymbolType());
        Assert.Same(method.ReturnType, method.GetSymbolType());
        Assert.Same(parameter.Type, parameter.GetSymbolType());
        Assert.Same(local.Type, local.GetSymbolType());
        Assert.Same(type, type.GetSymbolType());
        Assert.Same(typeParameter, typeParameter.GetSymbolType());
    }

    [Fact]
    public void GetFirstSourceLocation_ReturnsFirstLocationDeclaredInSource()
    {
        var compilation = CreateCompilation("""
            public class Sample;
            """);
        var type = GetRequiredType(compilation, "Sample");

        var location = type.GetFirstSourceLocation();

        Assert.NotNull(location);
        Assert.True(location!.IsInSource);
        Assert.EndsWith("Tests.cs", location.SourceTree?.FilePath);
    }

    [Fact]
    public void GetAllInterfacesIncludingThis_IncludesTheInterfaceWhenSymbolIsAnInterface()
    {
        var compilation = CreateCompilation("""
            public interface ISample;
            """);
        var type = GetRequiredType(compilation, "ISample");

        Assert.Contains(type, type.GetAllInterfacesIncludingSelf(), SymbolEqualityComparer.Default);
    }

    [Fact]
    public void GetAllInterfacesIncludingThis_DoesNotIncludeTheTypeWhenSymbolIsNotAnInterface()
    {
        var compilation = CreateCompilation("""
            public interface ISample;
            public class Sample : ISample;
            """);
        var type = GetRequiredType(compilation, "Sample");
        var interfaceType = GetRequiredType(compilation, "ISample");

        var interfaces = type.GetAllInterfacesIncludingSelf();

        Assert.Contains(interfaceType, interfaces, SymbolEqualityComparer.Default);
        Assert.DoesNotContain(type, interfaces, SymbolEqualityComparer.Default);
    }

    [Fact]
    public void GetAllInterfacesIncludingThis_DoesNotDuplicateSelfWhenAlreadyPresent()
    {
        var compilation = CreateCompilation("""
            public interface IBase;
            public interface ISample : IBase;
            """);
        var type = GetRequiredType(compilation, "ISample");

        var interfaces = type.GetAllInterfacesIncludingSelf();

        Assert.Equal(1, interfaces.Count(i => SymbolEqualityComparer.Default.Equals(i, type)));
    }

    [Fact]
    public void GetAllMembers_ReturnsMembersFromBaseTypes()
    {
        var compilation = CreateCompilation("""
            public class Base
            {
                public void BaseOnly() { }
            }

            public class Sample : Base
            {
                public void DerivedOnly() { }
            }
            """);
        var type = GetRequiredType(compilation, "Sample");
        var baseType = GetRequiredType(compilation, "Base");
        var baseOnly = GetRequiredMethod(baseType, "BaseOnly");

        Assert.Contains(baseOnly, type.GetAllMembers());
    }

    [Fact]
    public void GetAllMembers_WithName_ReturnsMatchingMembersFromBaseTypes()
    {
        var compilation = CreateCompilation("""
            public class Base
            {
                public void BaseOnly() { }
            }

            public class Sample : Base;
            """);
        var type = GetRequiredType(compilation, "Sample");
        var baseType = GetRequiredType(compilation, "Base");
        var baseOnly = GetRequiredMethod(baseType, "BaseOnly");

        Assert.Contains(baseOnly, type.GetAllMembers("BaseOnly"));
    }

    [Fact]
    public void InheritsFrom_ReturnsTrueForBaseTypesAndConstrainedTypeParameters()
    {
        var compilation = CreateCompilation("""
            public class Base;
            public class Sample : Base
            {
                public T M<T>(T value) where T : Sample => value;
                public T MConstrainedToBase<T>(T value) where T : Base => value;
            }
            """);
        var baseType = GetRequiredType(compilation, "Base");
        var sampleType = GetRequiredType(compilation, "Sample");
        var typeParameter = GetRequiredMethod(sampleType, "M").TypeParameters.Single();
        var typeParameterConstrainedToBase = GetRequiredMethod(sampleType, "MConstrainedToBase").TypeParameters.Single();

        Assert.True(sampleType.InheritsFrom(baseType));
        Assert.True(typeParameter.InheritsFrom(baseType));
        Assert.True(typeParameterConstrainedToBase.InheritsFrom(baseType));
        Assert.False(baseType.InheritsFrom(baseType));
        Assert.False(baseType.InheritsFrom(sampleType));
        Assert.False(typeParameterConstrainedToBase.InheritsFrom(sampleType));
    }

    [Fact]
    public void Implements_ReturnsTrueForImplementedInterfacesAndConstrainedTypeParameters()
    {
        var compilation = CreateCompilation("""
            public interface ISample;
            public interface IDerived : ISample;
            public class Sample : ISample
            {
                public T M<T>(T value) where T : ISample => value;
                public T MConstrainedToDerived<T>(T value) where T : IDerived => value;
            }
            """);
        var interfaceType = GetRequiredType(compilation, "ISample");
        var derivedInterfaceType = GetRequiredType(compilation, "IDerived");
        var sampleType = GetRequiredType(compilation, "Sample");
        var typeParameter = GetRequiredMethod(sampleType, "M").TypeParameters.Single();
        var typeParameterConstrainedToDerived = GetRequiredMethod(sampleType, "MConstrainedToDerived").TypeParameters.Single();

        Assert.True(sampleType.Implements(interfaceType));
        Assert.True(typeParameter.Implements(interfaceType));
        Assert.True(typeParameterConstrainedToDerived.Implements(interfaceType));
        Assert.True(typeParameterConstrainedToDerived.Implements(derivedInterfaceType));
        Assert.False(interfaceType.Implements(interfaceType));
        Assert.False(typeParameter.Implements(derivedInterfaceType));
    }

    [Fact]
    public void ImplementsGenericInterface_ReturnsTrueForConstructedGenericInterfaces()
    {
        var compilation = CreateCompilation("""
            public interface ISample<T>;
            public class Sample : ISample<string>
            {
                public T M<T>(T value) where T : ISample<int> => value;
            }
            """);
        var interfaceType = GetRequiredType(compilation, "ISample`1");
        var sampleType = GetRequiredType(compilation, "Sample");
        var typeParameter = GetRequiredMethod(sampleType, "M").TypeParameters.Single();

        Assert.True(sampleType.ImplementsGenericInterface(interfaceType));
        Assert.True(typeParameter.ImplementsGenericInterface(interfaceType));
        Assert.False(interfaceType.ImplementsGenericInterface(interfaceType));
    }

    [Fact]
    public void IsOrImplements_ReturnsTrueForMatchingInterfaceOrImplementation()
    {
        var compilation = CreateCompilation("""
            public interface ISample;
            public class Sample : ISample
            {
                public T M<T>(T value) where T : ISample => value;
            }

            public class Other;
            """);
        var interfaceType = GetRequiredType(compilation, "ISample");
        var sampleType = GetRequiredType(compilation, "Sample");
        var otherType = GetRequiredType(compilation, "Other");
        var typeParameter = GetRequiredMethod(sampleType, "M").TypeParameters.Single();

        Assert.True(interfaceType.IsOrImplements(interfaceType));
        Assert.True(sampleType.IsOrImplements(interfaceType));
        Assert.True(typeParameter.IsOrImplements(interfaceType));
        Assert.False(otherType.IsOrImplements(interfaceType));
    }

    [Fact]
    public void IsOrInheritFrom_ReturnsTrueForMatchingTypeOrBaseType()
    {
        var compilation = CreateCompilation("""
            public class Base;
            public class Sample : Base
            {
                public T M<T>(T value) where T : Base => value;
            }
            """);
        var baseType = GetRequiredType(compilation, "Base");
        var sampleType = GetRequiredType(compilation, "Sample");
        var typeParameter = GetRequiredMethod(sampleType, "M").TypeParameters.Single();

        Assert.True(baseType.IsOrInheritsFrom(baseType));
        Assert.True(sampleType.IsOrInheritsFrom(baseType));
        Assert.True(typeParameter.IsOrInheritsFrom(baseType));
        Assert.False(baseType.IsOrInheritsFrom(sampleType));
        Assert.False(typeParameter.IsOrInheritsFrom(sampleType));
    }

    [Fact]
    public void IsEqualToAny_ReturnsTrueForAnyMatchingExpectedType()
    {
        var compilation = CreateCompilation("""
            public class Base;
            public class Sample;
            public interface ISample;
            """);
        var baseType = GetRequiredType(compilation, "Base");
        var sampleType = GetRequiredType(compilation, "Sample");
        var interfaceType = GetRequiredType(compilation, "ISample");
        ReadOnlySpan<ITypeSymbol?> expectedTypes = [baseType, sampleType];

        Assert.True(sampleType.IsEqualToAny(sampleType));
        Assert.True(sampleType.IsEqualToAny(baseType, sampleType));
        Assert.True(sampleType.IsEqualToAny(baseType, interfaceType, sampleType));
        Assert.True(sampleType.IsEqualToAny(expectedTypes));
        Assert.False(baseType.IsEqualToAny(interfaceType));
    }

    [Fact]
    public void IsObject_ReturnsTrueOnlyForSystemObject()
    {
        var compilation = CreateCompilation("""
            public class Sample;
            """);

        Assert.True(compilation.GetSpecialType(SpecialType.System_Object).IsObject());
        Assert.False(compilation.GetSpecialType(SpecialType.System_String).IsObject());
    }

    [Fact]
    public void IsString_ReturnsTrueOnlyForSystemString()
    {
        var compilation = CreateCompilation("""
            public class Sample;
            """);

        Assert.True(compilation.GetSpecialType(SpecialType.System_String).IsString());
        Assert.False(compilation.GetSpecialType(SpecialType.System_Object).IsString());
    }

    [Fact]
    public void IsChar_ReturnsTrueOnlyForSystemChar()
    {
        var compilation = CreateCompilation("""
            public class Sample;
            """);

        Assert.True(compilation.GetSpecialType(SpecialType.System_Char).IsChar());
        Assert.False(compilation.GetSpecialType(SpecialType.System_String).IsChar());
    }

    [Fact]
    public void IsInt32_ReturnsTrueOnlyForSystemInt32()
    {
        var compilation = CreateCompilation("""
            public class Sample;
            """);

        Assert.True(compilation.GetSpecialType(SpecialType.System_Int32).IsInt32());
        Assert.False(compilation.GetSpecialType(SpecialType.System_Int64).IsInt32());
    }

    [Fact]
    public void IsBoolean_ReturnsTrueOnlyForSystemBoolean()
    {
        var compilation = CreateCompilation("""
            public class Sample;
            """);

        Assert.True(compilation.GetSpecialType(SpecialType.System_Boolean).IsBoolean());
        Assert.False(compilation.GetSpecialType(SpecialType.System_Int32).IsBoolean());
    }

    [Fact]
    public void IsDateTime_ReturnsTrueOnlyForSystemDateTime()
    {
        var compilation = CreateCompilation("""
            public class Sample;
            """);
        var dateTime = GetRequiredType(compilation, "System.DateTime");

        Assert.True(dateTime.IsDateTime());
        Assert.False(compilation.GetSpecialType(SpecialType.System_String).IsDateTime());
    }

    [Fact]
    public void IsEnum_ReturnsTrueOnlyForEnums()
    {
        var compilation = CreateCompilation("""
            public enum Sample
            {
                Value,
            }

            public class Other;
            """);
        var enumType = GetRequiredType(compilation, "Sample");
        var otherType = GetRequiredType(compilation, "Other");

        Assert.True(enumType.IsEnum());
        Assert.False(otherType.IsEnum());
    }

    [Fact]
    public void GetEnumType_ReturnsEnumUnderlyingType()
    {
        var compilation = CreateCompilation("""
            public enum Sample
            {
                Value,
            }

            public class Other;
            """);
        var enumType = GetRequiredType(compilation, "Sample");
        var otherType = GetRequiredType(compilation, "Other");

        Assert.Equal(SpecialType.System_Int32, enumType.GetEnumUnderlyingType()?.SpecialType);
        Assert.Null(otherType.GetEnumUnderlyingType());
    }

    [Fact]
    public void IsNumberType_ReturnsTrueForNumericSpecialTypes()
    {
        var compilation = CreateCompilation("""
            public class Sample;
            """);

        Assert.True(compilation.GetSpecialType(SpecialType.System_Int32).IsNumberType());
        Assert.True(compilation.GetSpecialType(SpecialType.System_Decimal).IsNumberType());
        Assert.False(compilation.GetSpecialType(SpecialType.System_String).IsNumberType());
    }

    [Fact]
    public void IsBlittableType_ReturnsTrueForPrimitiveEnumsAndBlittableStructs()
    {
        var compilation = CreateCompilation("""
            public readonly struct Blittable
            {
                public readonly int X;
                public readonly long Y;
            }

            public struct NotBlittable
            {
                public string Text;
            }

            public enum SampleEnum
            {
                Value,
            }
            """);
        var blittable = GetRequiredType(compilation, "Blittable");
        var notBlittable = GetRequiredType(compilation, "NotBlittable");
        var sampleEnum = GetRequiredType(compilation, "SampleEnum");

        Assert.True(compilation.GetSpecialType(SpecialType.System_Int32).IsBlittableType());
        Assert.True(sampleEnum.IsBlittableType());
        Assert.True(blittable.IsBlittableType());
        Assert.False(notBlittable.IsBlittableType());
    }

    [Fact]
    public void GetUnderlyingNullableTypeOrSelf_ReturnsUnderlyingTypeForNullableValueTypes()
    {
        var compilation = CreateCompilation("""
            public class Sample;
            """);
        var nullableInt = compilation.GetSpecialType(SpecialType.System_Nullable_T).Construct(compilation.GetSpecialType(SpecialType.System_Int32));

        Assert.Equal(SpecialType.System_Int32, nullableInt.GetUnderlyingNullableTypeOrSelf().SpecialType);
        Assert.Equal(SpecialType.System_String, compilation.GetSpecialType(SpecialType.System_String).GetUnderlyingNullableTypeOrSelf().SpecialType);
    }

    [Fact]
    public void GetLineSpan_ReturnsLineSpanForNodesTokensTriviaAndNodeOrToken()
    {
        var compilation = CreateCompilation("""
            namespace Demo;
            public class Sample
            {
                // comment
                public void M(
                    int value)
                {
                }
            }
            """);
        var root = compilation.SyntaxTrees.Single().GetRoot();
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var comment = root.DescendantTrivia().Single(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia));

        Assert.True(method.GetLineSpan(default)?.Path.EndsWith("Tests.cs", StringComparison.Ordinal));
        Assert.Equal(4, method.Identifier.GetLineSpan(default)?.StartLinePosition.Line);
        Assert.Equal(3, comment.GetLineSpan(default)?.StartLinePosition.Line);
        Assert.Equal(4, ((SyntaxNodeOrToken)method).GetLineSpan(default)?.StartLinePosition.Line);
    }

    [Fact]
    public void GetLine_ReturnsStartLineForNodesTokensAndTrivia()
    {
        var compilation = CreateCompilation("""
            namespace Demo;
            public class Sample
            {
                // comment
                public void M()
                {
                }
            }
            """);
        var root = compilation.SyntaxTrees.Single().GetRoot();
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var comment = root.DescendantTrivia().Single(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia));

        Assert.Equal(4, method.GetLine(default));
        Assert.Equal(4, method.Identifier.GetLine(default));
        Assert.Equal(3, comment.GetLine(default));
    }

    [Fact]
    public void GetEndLine_ReturnsEndLineForNodesTokensAndTrivia()
    {
        var compilation = CreateCompilation("""
            namespace Demo;
            public class Sample
            {
                // comment
                public void M()
                {
                }
            }
            """);
        var root = compilation.SyntaxTrees.Single().GetRoot();
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var comment = root.DescendantTrivia().Single(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia));

        Assert.Equal(6, method.GetEndLine(default));
        Assert.Equal(4, method.Identifier.GetEndLine(default));
        Assert.Equal(3, comment.GetEndLine(default));
    }

    [Fact]
    public void SpansMultipleLines_ReturnsTrueWhenNodeOrTriviaCoversMultipleLines()
    {
        var compilation = CreateCompilation("""
            namespace Demo;
            public class Sample
            {
                // comment
                public void M()
                {
                }

                /*
                 * multi-line
                 */
            }
            """);
        var root = compilation.SyntaxTrees.Single().GetRoot();
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var singleLineComment = root.DescendantTrivia().Single(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia));
        var multiLineComment = root.DescendantTrivia().Single(trivia => trivia.IsKind(SyntaxKind.MultiLineCommentTrivia));

        Assert.True(method.SpansMultipleLines(default));
        Assert.True(multiLineComment.SpansMultipleLines(default));
        Assert.False(singleLineComment.SpansMultipleLines(default));
    }

    [Theory]
    [InlineData("Sample.g.cs")]
    [InlineData("Sample.G.CS")]
    [InlineData("Sample.designer.cs")]
    [InlineData("Sample.Designer.cs")]
    [InlineData("Sample.generated.cs")]
    [InlineData("Sample.g.i.cs")]
    [InlineData("TemporaryGeneratedFile_1234.cs")]
    [InlineData("temporarygeneratedfile_1234.vb")]
    [InlineData(".g.cs")]
    public void IsGeneratedCodeFile_ReturnsTrueForWellKnownGeneratedFileNames(string filePath)
    {
        Assert.True(GeneratedCodeExtensions.IsGeneratedCodeFile(filePath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Sample.cs")]
    [InlineData("Generated.cs")]
    [InlineData("Designer.cs")]
    [InlineData("Sample.g")]
    [InlineData("Sample.generator.cs")]
    [InlineData("Sample.g.")]
    public void IsGeneratedCodeFile_ReturnsFalseForRegularFileNames(string? filePath)
    {
        Assert.False(GeneratedCodeExtensions.IsGeneratedCodeFile(filePath));
    }

    [Theory]
    [InlineData(@"C:\project\Sample.g.cs", true)]
    [InlineData("/project/Sample.g.cs", true)]
    [InlineData(@"Generator\Namespace.Generator\Hint.g.cs", true)]
    [InlineData(@"C:\project.g\Sample.cs", false)]
    [InlineData("/project.g/Sample.cs", false)]
    public void IsGeneratedCodeFile_UsesTheFileNameWhateverTheDirectorySeparator(string filePath, bool expected)
    {
        Assert.Equal(expected, GeneratedCodeExtensions.IsGeneratedCodeFile(filePath));
    }

    [Fact]
    public void IsGeneratedCode_ReturnsTrueWhenTheFileNameIndicatesGeneratedCode()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("public class Sample;", DefaultParseOptions, path: "Sample.g.cs");

        Assert.True(syntaxTree.IsGeneratedCode(default));
    }

    [Fact]
    public void IsGeneratedCode_ReturnsFalseForRegularFile()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("public class Sample;", DefaultParseOptions, path: "Sample.cs");

        Assert.False(syntaxTree.IsGeneratedCode(default));
    }

    [Theory]
    [InlineData("// <auto-generated/>")]
    [InlineData("// <auto-generated>")]
    [InlineData("//<autogenerated/>")]
    [InlineData("/* <auto-generated/> */")]
    public void IsGeneratedCode_ReturnsTrueWhenTheFileStartsWithAnAutoGeneratedComment(string comment)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText($$"""
            {{comment}}
            namespace Demo;

            public class Sample;
            """, DefaultParseOptions, path: "Sample.cs");

        Assert.True(syntaxTree.IsGeneratedCode(default));
    }

    [Fact]
    public void IsGeneratedCode_IgnoresDocumentationComments()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("""
            /// <auto-generated/>
            public class Sample;
            """, DefaultParseOptions, path: "Sample.cs");

        Assert.False(syntaxTree.IsGeneratedCode(default));
    }

    [Fact]
    public void IsGeneratedCode_IgnoresAutoGeneratedCommentsThatAreNotAtTheBeginningOfTheFile()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("""
            namespace Demo;

            // <auto-generated/>
            public class Sample;
            """, DefaultParseOptions, path: "Sample.cs");

        Assert.False(syntaxTree.IsGeneratedCode(default));
    }

    [Fact]
    public void HasAutoGeneratedComment_ReportsTheLeadingTriviaOfTheNode()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("""
            namespace Demo;

            // <auto-generated/>
            public class Sample;
            """, DefaultParseOptions, path: "Sample.cs");
        var root = syntaxTree.GetRoot();
        var type = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();

        Assert.False(root.HasAutoGeneratedComment());
        Assert.True(type.HasAutoGeneratedComment());
    }

    [Theory]
    [InlineData("Sample.g.cs", "false", false)]
    [InlineData("Sample.cs", "true", true)]
    [InlineData("Sample.cs", "TRUE", true)]
    public void IsGeneratedCode_UsesTheGeneratedCodeOptionWhenConfigured(string filePath, string optionValue, bool expected)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("public class Sample;", DefaultParseOptions, path: filePath);
        var optionsProvider = new GeneratedCodeOptionProvider(optionValue);

        Assert.Equal(expected, syntaxTree.IsGeneratedCode(optionsProvider, default));
    }

    [Theory]
    [InlineData("Sample.g.cs", null, true)]
    [InlineData("Sample.g.cs", "invalid", true)]
    [InlineData("Sample.cs", null, false)]
    [InlineData("Sample.cs", "invalid", false)]
    public void IsGeneratedCode_FallsBackToTheHeuristicsWhenTheGeneratedCodeOptionIsNotUsable(string filePath, string? optionValue, bool expected)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("public class Sample;", DefaultParseOptions, path: filePath);
        var optionsProvider = new GeneratedCodeOptionProvider(optionValue);

        Assert.Equal(expected, syntaxTree.IsGeneratedCode(optionsProvider, default));
    }

    [Fact]
    public void IsGeneratedCode_UsesTheGeneratedCodeOptionFromAnalyzerOptions()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("public class Sample;", DefaultParseOptions, path: "Sample.cs");
        var analyzerOptions = new AnalyzerOptions([], new GeneratedCodeOptionProvider("true"));

        Assert.True(syntaxTree.IsGeneratedCode(analyzerOptions, default));
    }

    [Fact]
    public void ReportDiagnostic_MethodSymbol_ReportOnMethodName_ReportsOnTheIdentifier()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public string Method() => "";
            }
            """);
        var method = GetRequiredMethod(GetRequiredType(compilation, "Sample"), "Method");

        var diagnostic = ReportMethodDiagnostic(compilation, method, DiagnosticMethodReportOptions.ReportOnMethodName);

        Assert.Equal("Method", GetLocationText(diagnostic.Location));
    }

    [Fact]
    public void ReportDiagnostic_MethodSymbol_ReportOnReturnType_ReportsOnTheReturnType()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public string Method() => "";
            }
            """);
        var method = GetRequiredMethod(GetRequiredType(compilation, "Sample"), "Method");

        var diagnostic = ReportMethodDiagnostic(compilation, method, DiagnosticMethodReportOptions.ReportOnReturnType);

        Assert.Equal("string", GetLocationText(diagnostic.Location));
    }

    [Fact]
    public void ReportDiagnostic_MethodSymbol_ReportOnMethodName_TakesPrecedenceOverReportOnReturnType()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public string Method() => "";
            }
            """);
        var method = GetRequiredMethod(GetRequiredType(compilation, "Sample"), "Method");

        var diagnostic = ReportMethodDiagnostic(compilation, method, DiagnosticMethodReportOptions.ReportOnMethodName | DiagnosticMethodReportOptions.ReportOnReturnType);

        Assert.Equal("Method", GetLocationText(diagnostic.Location));
    }

    [Fact]
    public void ReportDiagnostic_MethodSymbol_ReportOnMethodName_ReportsOnTheDelegateIdentifier()
    {
        var compilation = CreateCompilation("""
            public delegate string Sample(int value);
            """);
        var invokeMethod = GetRequiredType(compilation, "Sample").DelegateInvokeMethod;
        Assert.NotNull(invokeMethod);

        var diagnostic = ReportMethodDiagnostic(compilation, invokeMethod, DiagnosticMethodReportOptions.ReportOnMethodName);

        Assert.Equal("Sample", GetLocationText(diagnostic.Location));
    }

    [Fact]
    public void ReportDiagnostic_MethodSymbol_ReportOnMethodName_ReportsOnTheLocalFunctionIdentifier()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M()
                {
                    string Local() => "";
                    Local();
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var localFunction = semanticModel.SyntaxTree.GetRoot().DescendantNodes().OfType<LocalFunctionStatementSyntax>().Single();
        var symbol = (IMethodSymbol?)semanticModel.GetDeclaredSymbol(localFunction);
        Assert.NotNull(symbol);

        var diagnostic = ReportMethodDiagnostic(compilation, symbol, DiagnosticMethodReportOptions.ReportOnMethodName);

        Assert.Equal("Local", GetLocationText(diagnostic.Location));
    }

    [Fact]
    public void ReportDiagnostic_MethodSymbol_None_ReportsOnTheSymbolLocations()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public string Method() => "";
            }
            """);
        var method = GetRequiredMethod(GetRequiredType(compilation, "Sample"), "Method");

        var diagnostic = ReportMethodDiagnostic(compilation, method, DiagnosticMethodReportOptions.None);

        Assert.Equal(method.Locations, [diagnostic.Location]);
    }

    [Fact]
    public void ReportDiagnostic_MethodSymbol_ReportOnMethodName_ReportsWithoutLocationForMetadataMethods()
    {
        var compilation = CreateCompilation("""
            public class Sample;
            """);
        var method = GetRequiredMethod(GetRequiredType(compilation, "System.Object"), "ToString");

        var diagnostic = ReportMethodDiagnostic(compilation, method, DiagnosticMethodReportOptions.ReportOnMethodName);

        Assert.False(diagnostic.Location.IsInSource);
    }

    private static CSharpCompilation CreateCompilation(
        string source,
        string assemblyName = "Tests",
        IReadOnlyCollection<MetadataReference>? additionalReferences = null,
        CSharpParseOptions? parseOptions = null,
        CSharpCompilationOptions? compilationOptions = null,
        int? dotnetMajorVersion = null,
        bool allowInvalidCode = false)
    {
        var references = CreateMetadataReferences(dotnetMajorVersion);
        if (additionalReferences is not null)
        {
            references.AddRange(additionalReferences);
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, parseOptions ?? DefaultParseOptions, path: assemblyName + ".cs")],
            references,
            compilationOptions ?? DefaultCompilationOptions);

        if (!allowInvalidCode)
        {
            var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            Assert.Empty(errors);
        }

        return compilation;
    }

    private static List<MetadataReference> CreateMetadataReferences(int? dotnetMajorVersion)
    {
        if (dotnetMajorVersion is not null && TryGetReferenceAssemblyDirectory(dotnetMajorVersion.Value) is { } referenceAssemblyDirectory)
        {
            return Directory
                .EnumerateFiles(referenceAssemblyDirectory, "*.dll")
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToList<MetadataReference>();
        }

        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        Assert.NotNull(trustedPlatformAssemblies);

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToList<MetadataReference>();
    }

    private static string? TryGetReferenceAssemblyDirectory(int dotnetMajorVersion)
    {
        foreach (var dotnetRoot in GetDotNetRoots())
        {
            var packRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
            if (!Directory.Exists(packRoot))
                continue;

            foreach (var versionDirectory in Directory.EnumerateDirectories(packRoot).OrderByDescending(path => path, StringComparer.Ordinal))
            {
                var version = Path.GetFileName(versionDirectory);
                if (!version.StartsWith(dotnetMajorVersion + ".", StringComparison.Ordinal))
                    continue;

                var referenceAssemblyDirectory = Path.Combine(versionDirectory, "ref", "net" + dotnetMajorVersion + ".0");
                if (Directory.Exists(referenceAssemblyDirectory))
                    return referenceAssemblyDirectory;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetDotNetRoots()
    {
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
            yield return dotnetRoot;

        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (runtimeDirectory is not null)
            yield return Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", ".."));
    }

    private static SemanticModel GetSemanticModel(Compilation compilation)
    {
        return compilation.GetSemanticModel(compilation.SyntaxTrees.Single());
    }

    private static INamedTypeSymbol GetRequiredType(Compilation compilation, string metadataName)
    {
        var type = compilation.GetTypeByMetadataName(metadataName);
        Assert.NotNull(type);

        return type;
    }

    private static IMethodSymbol GetRequiredMethod(INamedTypeSymbol type, string name)
    {
        return type.GetMembers(name).OfType<IMethodSymbol>().Single(method => method.MethodKind is MethodKind.Ordinary);
    }

    private static IPropertySymbol GetRequiredProperty(INamedTypeSymbol type, string name)
    {
        return type.GetMembers(name).OfType<IPropertySymbol>().Single();
    }

    private static IFieldSymbol GetRequiredField(INamedTypeSymbol type, string name)
    {
        return type.GetMembers(name).OfType<IFieldSymbol>().Single();
    }

    private static ILocalSymbol GetRequiredLocal(SemanticModel semanticModel, string name)
    {
        var variable = semanticModel.SyntaxTree.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>().Single(node => node.Identifier.ValueText == name);
        var symbol = semanticModel.GetDeclaredSymbol(variable);
        Assert.NotNull(symbol);

        return (ILocalSymbol)symbol;
    }

    private static IOperation GetInitializerOperation(SemanticModel semanticModel, string variableName)
    {
        var variable = semanticModel.SyntaxTree.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>().Single(node => node.Identifier.ValueText == variableName);
        var value = variable.Initializer?.Value;
        Assert.NotNull(value);
        var operation = semanticModel.GetOperation(value);
        Assert.NotNull(operation);

        return operation;
    }

    private static IOperation GetNameofArgumentOperation(SemanticModel semanticModel)
    {
        var argument = semanticModel.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression.ToString() == "nameof")
            .ArgumentList
            .Arguments
            .Single()
            .Expression;
        var operation = semanticModel.GetOperation(argument);
        Assert.NotNull(operation);

        return operation;
    }

    private static TOperation GetRequiredOperation<TOperation>(SemanticModel semanticModel)
        where TOperation : class, IOperation
    {
        var operation = semanticModel.SyntaxTree.GetRoot()
            .DescendantNodes()
            .Select(node => semanticModel.GetOperation(node))
            .OfType<TOperation>()
            .FirstOrDefault();
        Assert.NotNull(operation);

        return operation;
    }

    private static Diagnostic ReportMethodDiagnostic(Compilation compilation, IMethodSymbol symbol, DiagnosticMethodReportOptions reportOptions)
    {
        Diagnostic? reported = null;

        // A DiagnosticAnalyzer cannot be defined in this assembly (RS1041), so the context is created directly
#pragma warning disable CS0618
        var context = new SymbolAnalysisContext(symbol, compilation, new AnalyzerOptions([]), diagnostic => reported = diagnostic, _ => true, cancellationToken: default);
#pragma warning restore CS0618

        context.ReportDiagnostic(CreateDescriptor(), symbol, reportOptions);

        Assert.NotNull(reported);

        return reported;
    }

    private static string GetLocationText(Location location)
    {
        Assert.NotNull(location.SourceTree);

        return location.SourceTree.GetText().ToString(location.SourceSpan);
    }

    private static DiagnosticDescriptor CreateDescriptor()
    {
        return CreateDescriptor("MFTEST001");
    }

    private static DiagnosticDescriptor CreateDescriptor(string id)
    {
        return new DiagnosticDescriptor(id, "Title", "Message", "Category", DiagnosticSeverity.Warning, isEnabledByDefault: true);
    }

    /// <summary>
    /// <see cref="DiagnosticReporter.CanReportDiagnostic"/> is global, so the tests that set it must not run concurrently.
    /// </summary>
    private sealed class DiagnosticFilterScope : IDisposable
    {
        private static readonly SemaphoreSlim Semaphore = new(initialCount: 1, maxCount: 1);

        private DiagnosticFilterScope()
        {
        }

        public static async Task<DiagnosticFilterScope> EnterAsync(CancellationToken cancellationToken)
        {
            await Semaphore.WaitAsync(cancellationToken);

            return new DiagnosticFilterScope();
        }

        public void Dispose()
        {
            DiagnosticReporter.CanReportDiagnostic = null;
            Semaphore.Release();
        }
    }

    // RS1036/RS1038/RS1041 only apply to analyzers shipped in an analyzer package. This one only exists to exercise the extension methods.
#pragma warning disable RS1036 // A project containing analyzers or source generators should specify the property '<EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>'
#pragma warning disable RS1038 // This compiler extension should not be implemented in an assembly containing a reference to Microsoft.CodeAnalysis.Workspaces
#pragma warning disable RS1041 // This compiler extension should not be implemented in an assembly with target framework
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class DiagnosticFilterAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "MFTEST004";

        private static readonly DiagnosticDescriptor Descriptor = new(DiagnosticId, "Title", "Message {0}", "Category", DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Descriptor];

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterSyntaxNodeAction(AnalyzeClassDeclaration, SyntaxKind.ClassDeclaration);
            context.RegisterSyntaxTreeAction(AnalyzeSyntaxTree);
        }

        private static void AnalyzeClassDeclaration(SyntaxNodeAnalysisContext context)
        {
            var declaration = (ClassDeclarationSyntax)context.Node;
            DiagnosticReporter reporter = context;

            reporter.ReportDiagnostic(Descriptor, declaration, "reporter-kept");
            reporter.ReportDiagnostic(Descriptor, declaration, "reporter-dropped");
            context.ReportDiagnostic(Descriptor, declaration, "context-kept");
            context.ReportDiagnostic(Descriptor, declaration, "context-dropped");
        }

        private static void AnalyzeSyntaxTree(SyntaxTreeAnalysisContext context)
        {
            var location = context.Tree.GetRoot(context.CancellationToken).GetLocation();

            context.ReportDiagnostic(Descriptor, location, "tree-kept");
            context.ReportDiagnostic(Descriptor, location, "tree-dropped");
        }
    }
#pragma warning restore RS1041
#pragma warning restore RS1038
#pragma warning restore RS1036

    // RS1036/RS1038/RS1041 only apply to analyzers shipped in an analyzer package. This one only exists to exercise the extension methods.
#pragma warning disable RS1036 // A project containing analyzers or source generators should specify the property '<EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>'
#pragma warning disable RS1038 // This compiler extension should not be implemented in an assembly containing a reference to Microsoft.CodeAnalysis.Workspaces
#pragma warning disable RS1041 // This compiler extension should not be implemented in an assembly with target framework
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class AdditionalFileAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor Descriptor = new("MFTEST005", "Title", "Message {0}", "Category", DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Descriptor];

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterAdditionalFileAction(AnalyzeAdditionalFile);
        }

        private static void AnalyzeAdditionalFile(AdditionalFileAnalysisContext context)
        {
            var location = Location.Create(context.AdditionalFile.Path, new TextSpan(0, 0), new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 0)));

            context.ReportDiagnostic(Descriptor, location, Path.GetFileName(context.AdditionalFile.Path));
        }
    }
#pragma warning restore RS1041
#pragma warning restore RS1038
#pragma warning restore RS1036

    private sealed class TestAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }

    // RS1036/RS1038/RS1041 only apply to analyzers shipped in an analyzer package. This one only exists to exercise the extension methods.
#pragma warning disable RS1036 // A project containing analyzers or source generators should specify the property '<EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>'
#pragma warning disable RS1038 // This compiler extension should not be implemented in an assembly containing a reference to Microsoft.CodeAnalysis.Workspaces
#pragma warning disable RS1041 // This compiler extension should not be implemented in an assembly with target framework
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class MessageArgsAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor Descriptor = new("MFTEST002", "Title", "Message {0}", "Category", DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Descriptor];

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterSyntaxNodeAction(AnalyzeClassDeclaration, SyntaxKind.ClassDeclaration);
        }

        private static void AnalyzeClassDeclaration(SyntaxNodeAnalysisContext context)
        {
            var declaration = (ClassDeclarationSyntax)context.Node;
            var location = declaration.GetLocation();
            DiagnosticReporter reporter = context;

            reporter.ReportDiagnostic(Descriptor, declaration, "reporter-node");
            reporter.ReportDiagnostic(Descriptor, declaration.Identifier, "reporter-token");
            reporter.ReportDiagnostic(Descriptor, location, "reporter-location");
            reporter.ReportDiagnostic(Descriptor, (IEnumerable<Location>)[location], "reporter-locations");
            reporter.ReportDiagnostic(Descriptor, ImmutableDictionary<string, string?>.Empty, declaration, "reporter-properties");
            reporter.ReportDiagnostic(Descriptor, location);
            context.ReportDiagnostic(Descriptor, declaration, "context-node");
            context.ReportDiagnostic(Descriptor, (IEnumerable<Location>)[location], "context-locations");
        }
    }
#pragma warning restore RS1041
#pragma warning restore RS1038
#pragma warning restore RS1036

    // RS1036/RS1038/RS1041 only apply to analyzers shipped in an analyzer package. This one only exists to exercise the extension methods.
#pragma warning disable RS1036 // A project containing analyzers or source generators should specify the property '<EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>'
#pragma warning disable RS1038 // This compiler extension should not be implemented in an assembly containing a reference to Microsoft.CodeAnalysis.Workspaces
#pragma warning disable RS1041 // This compiler extension should not be implemented in an assembly with target framework
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class AnalyzerOptionsAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor Descriptor = new("MFTEST003", "Title", "Message {0}", "Category", DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Descriptor];

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterSyntaxNodeAction(syntaxNodeContext => ReportWhenOptionsMatch(syntaxNodeContext, syntaxNodeContext.Options, syntaxNodeContext.Node.GetLocation(), nameof(SyntaxNodeAnalysisContext)), SyntaxKind.ClassDeclaration);
            context.RegisterSymbolAction(symbolContext => ReportWhenOptionsMatch(symbolContext, symbolContext.Options, symbolContext.Symbol.Locations[0], nameof(SymbolAnalysisContext)), SymbolKind.NamedType);
            context.RegisterOperationAction(operationContext => ReportWhenOptionsMatch(operationContext, operationContext.Options, operationContext.Operation.Syntax.GetLocation(), nameof(OperationAnalysisContext)), OperationKind.Invocation);
            context.RegisterOperationBlockAction(operationBlockContext => ReportWhenOptionsMatch(operationBlockContext, operationBlockContext.Options, operationBlockContext.OperationBlocks[0].Syntax.GetLocation(), nameof(OperationBlockAnalysisContext)));
            context.RegisterCompilationAction(compilationContext => ReportWhenOptionsMatch(compilationContext, compilationContext.Options, location: null, nameof(CompilationAnalysisContext)));
            context.RegisterSemanticModelAction(semanticModelContext => ReportWhenOptionsMatch(semanticModelContext, semanticModelContext.Options, semanticModelContext.SemanticModel.SyntaxTree.GetRoot(semanticModelContext.CancellationToken).GetLocation(), nameof(SemanticModelAnalysisContext)));
            context.RegisterSyntaxTreeAction(syntaxTreeContext => ReportWhenOptionsMatch(syntaxTreeContext, syntaxTreeContext.Options, syntaxTreeContext.Tree.GetRoot(syntaxTreeContext.CancellationToken).GetLocation(), nameof(SyntaxTreeAnalysisContext)));
            context.RegisterCodeBlockAction(codeBlockContext => ReportWhenOptionsMatch(codeBlockContext, codeBlockContext.Options, codeBlockContext.CodeBlock.GetLocation(), nameof(CodeBlockAnalysisContext)));
        }

        private static void ReportWhenOptionsMatch(DiagnosticReporter reporter, AnalyzerOptions options, Location? location, string contextName)
        {
            if (ReferenceEquals(reporter.Options, options))
            {
                reporter.ReportDiagnostic(Diagnostic.Create(Descriptor, location, contextName));
            }
        }
    }
#pragma warning restore RS1041
#pragma warning restore RS1038
#pragma warning restore RS1036

    private sealed class GeneratedCodeOptionProvider(string? generatedCode) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(generatedCode: null);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Options(generatedCode);

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Options(generatedCode: null);

        private sealed class Options(string? generatedCode) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            {
                if (generatedCode is not null && key is "generated_code")
                {
                    value = generatedCode;
                    return true;
                }

                value = null;
                return false;
            }
        }
    }

    [Theory]
    [InlineData("value", "(value)")]
    [InlineData("a.b", "(a.b)")]
    [InlineData("M()", "(M())")]
    [InlineData("a[0]", "(a[0])")]
    [InlineData("(a)", "((a))")]
    [InlineData("this", "(this)")]
    [InlineData("\"literal\"", "(\"literal\")")]
    [InlineData("int", "(int)")]
    [InlineData("a ? b : c", "(a ? b : c)")]
    [InlineData("a ?? b", "(a ?? b)")]
    [InlineData("(string)a", "((string)a)")]
    [InlineData("a + b", "(a + b)")]
    [InlineData("a as string", "(a as string)")]
    [InlineData("await a", "(await a)")]
    [InlineData("-a", "(-a)")]
    public void Parenthesize_WrapsTheExpression(string expression, string expected)
    {
        var actual = SyntaxFactory.ParseExpression(expression).Parenthesize();

        Assert.Equal(expected, actual.ToFullString());
    }

    [Fact]
    public void Parenthesize_KeepsTriviaInsideOfTheParentheses()
    {
        var expression = SyntaxFactory.ParseExpression("  a  +  b // comment\n");

        Assert.Equal("(  a  +  b // comment\n)", expression.Parenthesize().ToFullString());
    }

    [Fact]
    public void Parenthesize_SyntaxNode_WrapsAnExpression()
    {
        SyntaxNode node = SyntaxFactory.ParseExpression("a + b");

        var actual = node.Parenthesize();

        Assert.IsType<ParenthesizedExpressionSyntax>(actual);
        Assert.Equal("(a + b)", actual.ToFullString());
    }

    [Fact]
    public void Parenthesize_SyntaxNode_ReturnsANonExpressionNodeAsIs()
    {
        SyntaxNode node = SyntaxFactory.ParseStatement("a + b;");

        Assert.Same(node, node.Parenthesize());
    }

#if ROSLYN_WORKSPACES
    [Fact]
    public void Parenthesize_AnnotatesTheParenthesesForTheSimplifier()
    {
        var parenthesized = SyntaxFactory.ParseExpression("a + b").Parenthesize();

        Assert.True(parenthesized.HasAnnotation(Microsoft.CodeAnalysis.Simplification.Simplifier.Annotation));
    }
#endif
}
