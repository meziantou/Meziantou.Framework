using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AssertionMethodSelectionAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor UseNullDescriptor = new(
        id: RuleIdentifiers.UseNullAssertionDiagnosticId,
        title: "Use Assert.Null for null comparisons",
        messageFormat: "Use Assert.Null instead of Assert.True(value == null)",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseNotNullDescriptor = new(
        id: RuleIdentifiers.UseNotNullAssertionDiagnosticId,
        title: "Use Assert.NotNull for null comparisons",
        messageFormat: "Use Assert.NotNull instead of Assert.True(value != null)",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NullWithValueTypeDescriptor = new(
        id: RuleIdentifiers.NullWithValueTypeDiagnosticId,
        title: "Do not use Assert.Null with value types",
        messageFormat: "Use Assert.Equal(default({0}), value) instead of Assert.Null for value types",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NotNullWithValueTypeDescriptor = new(
        id: RuleIdentifiers.NotNullWithValueTypeDiagnosticId,
        title: "Do not use Assert.NotNull with value types",
        messageFormat: "Use Assert.NotEqual(default({0}), value) instead of Assert.NotNull for value types",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SameWithValueTypeDescriptor = new(
        id: RuleIdentifiers.SameWithValueTypeDiagnosticId,
        title: "Do not use Assert.Same with value types",
        messageFormat: "Use Assert.Equal instead of Assert.Same for value types",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NotSameWithValueTypeDescriptor = new(
        id: RuleIdentifiers.NotSameWithValueTypeDiagnosticId,
        title: "Do not use Assert.NotSame with value types",
        messageFormat: "Use Assert.NotEqual instead of Assert.NotSame for value types",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseIsAssignableToDescriptor = new(
        id: RuleIdentifiers.UseIsAssignableToDiagnosticId,
        title: "Use Assert.IsAssignableTo for type pattern checks",
        messageFormat: "Use Assert.IsAssignableTo<T>(value) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseIsNotAssignableToDescriptor = new(
        id: RuleIdentifiers.UseIsNotAssignableToDiagnosticId,
        title: "Use Assert.IsNotAssignableTo for type pattern checks",
        messageFormat: "Use Assert.IsNotAssignableTo<T>(value) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseMatchesDescriptor = new(
        id: RuleIdentifiers.UseMatchesDiagnosticId,
        title: "Use Assert.Matches instead of Assert.True(Regex.IsMatch(...))",
        messageFormat: "Use Assert.Matches(pattern, actual) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseDoesNotMatchDescriptor = new(
        id: RuleIdentifiers.UseDoesNotMatchDiagnosticId,
        title: "Use Assert.DoesNotMatch instead of Assert.False(Regex.IsMatch(...))",
        messageFormat: "Use Assert.DoesNotMatch(pattern, actual) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseStringContainsDescriptor = new(
        id: RuleIdentifiers.UseStringContainsDiagnosticId,
        title: "Use Assert.Contains instead of Assert.True(string.Contains(...))",
        messageFormat: "Use Assert.Contains(expected, actual) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseStringDoesNotContainDescriptor = new(
        id: RuleIdentifiers.UseStringDoesNotContainDiagnosticId,
        title: "Use Assert.DoesNotContain instead of Assert.False(string.Contains(...))",
        messageFormat: "Use Assert.DoesNotContain(expected, actual) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseStringStartsWithDescriptor = new(
        id: RuleIdentifiers.UseStringStartsWithDiagnosticId,
        title: "Use Assert.StartsWith instead of Assert.True(string.StartsWith(...))",
        messageFormat: "Use Assert.StartsWith(expected, actual) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseStringDoesNotStartWithDescriptor = new(
        id: RuleIdentifiers.UseStringDoesNotStartWithDiagnosticId,
        title: "Use Assert.DoesNotStartWith instead of Assert.False(string.StartsWith(...))",
        messageFormat: "Use Assert.DoesNotStartWith(expected, actual) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseStringEndsWithDescriptor = new(
        id: RuleIdentifiers.UseStringEndsWithDiagnosticId,
        title: "Use Assert.EndsWith instead of Assert.True(string.EndsWith(...))",
        messageFormat: "Use Assert.EndsWith(expected, actual) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseStringDoesNotEndWithDescriptor = new(
        id: RuleIdentifiers.UseStringDoesNotEndWithDiagnosticId,
        title: "Use Assert.DoesNotEndWith instead of Assert.False(string.EndsWith(...))",
        messageFormat: "Use Assert.DoesNotEndWith(expected, actual) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseCollectionContainsDescriptor = new(
        id: RuleIdentifiers.UseCollectionContainsDiagnosticId,
        title: "Use Assert.Contains instead of Assert.True(collection.Contains(...))",
        messageFormat: "Use Assert.Contains(expected, actual) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseCollectionDoesNotContainDescriptor = new(
        id: RuleIdentifiers.UseCollectionDoesNotContainDiagnosticId,
        title: "Use Assert.DoesNotContain instead of Assert.False(collection.Contains(...))",
        messageFormat: "Use Assert.DoesNotContain(expected, actual) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseCollectionAnyContainsDescriptor = new(
        id: RuleIdentifiers.UseCollectionAnyContainsDiagnosticId,
        title: "Use Assert.Contains instead of Assert.True(collection.Any(...))",
        messageFormat: "Use Assert.Contains(actual, predicate) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseCollectionAnyDoesNotContainDescriptor = new(
        id: RuleIdentifiers.UseCollectionAnyDoesNotContainDiagnosticId,
        title: "Use Assert.DoesNotContain instead of Assert.False(collection.Any(...))",
        messageFormat: "Use Assert.DoesNotContain(actual, predicate) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseCollectionAllDescriptor = new(
        id: RuleIdentifiers.UseCollectionAllDiagnosticId,
        title: "Use Assert.All instead of Assert.True(collection.All(...))",
        messageFormat: "Use Assert.All(actual, predicate) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseCollectionDoesNotAllDescriptor = new(
        id: RuleIdentifiers.UseCollectionDoesNotAllDiagnosticId,
        title: "Use Assert.DoesNotAll instead of Assert.False(collection.All(...))",
        messageFormat: "Use Assert.DoesNotAll(actual, predicate) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly ImmutableArray<DiagnosticDescriptor> TrueFalseConditionDescriptors =
    [
        UseMatchesDescriptor, UseDoesNotMatchDescriptor,
        UseStringContainsDescriptor, UseStringDoesNotContainDescriptor,
        UseStringStartsWithDescriptor, UseStringDoesNotStartWithDescriptor,
        UseStringEndsWithDescriptor, UseStringDoesNotEndWithDescriptor,
        UseCollectionContainsDescriptor, UseCollectionDoesNotContainDescriptor,
        UseCollectionAnyContainsDescriptor, UseCollectionAnyDoesNotContainDescriptor,
        UseCollectionAllDescriptor, UseCollectionDoesNotAllDescriptor,
    ];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
    [
        UseNullDescriptor, UseNotNullDescriptor,
        NullWithValueTypeDescriptor, NotNullWithValueTypeDescriptor,
        SameWithValueTypeDescriptor, NotSameWithValueTypeDescriptor,
        UseIsAssignableToDescriptor, UseIsNotAssignableToDescriptor,
        .. TrueFalseConditionDescriptors,
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(context =>
        {
            var assertType = context.Compilation.GetTypeByMetadataName(AssertionsAnalyzerHelpers.AssertMetadataName);
            if (assertType is null)
                return;

            TrueFalseConditionMethodSelectionAnalyzerCommon.TryCreateSymbols(context.Compilation, out var symbols);
            context.RegisterOperationAction(context => Analyze(context, assertType, symbols), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol assertType, TrueFalseConditionMethodSelectionAnalyzerCommon.Symbols? symbols)
    {
        var invocationOperation = (IInvocationOperation)context.Operation;
        if (AssertionMethodSelectionAnalyzerCommon.TryGetNullCheckMatch(invocationOperation, assertType, out var nullCheckMatch))
        {
            var descriptor = nullCheckMatch.AssertionMethodName == "Null" ? UseNullDescriptor : UseNotNullDescriptor;
            context.ReportDiagnostic(Diagnostic.Create(descriptor, nullCheckMatch.ActualOperation.Syntax.GetLocation()));
            return;
        }

        if (AssertionMethodSelectionAnalyzerCommon.TryGetNullNotNullValueTypeMatch(invocationOperation, assertType, out var nullNotNullValueTypeMatch))
        {
            var descriptor = nullNotNullValueTypeMatch.AssertionMethodName == "Equal" ? NullWithValueTypeDescriptor : NotNullWithValueTypeDescriptor;
            context.ReportDiagnostic(Diagnostic.Create(descriptor, nullNotNullValueTypeMatch.ActualOperation.Syntax.GetLocation(), nullNotNullValueTypeMatch.ValueType.ToDisplayString()));
            return;
        }

        if (AssertionMethodSelectionAnalyzerCommon.TryGetAssignableTypeCheckMatch(invocationOperation, assertType, out var assignableTypeCheckMatch))
        {
            var descriptor = assignableTypeCheckMatch.AssertionMethodName == "IsAssignableTo" ? UseIsAssignableToDescriptor : UseIsNotAssignableToDescriptor;
            context.ReportDiagnostic(Diagnostic.Create(descriptor, assignableTypeCheckMatch.ActualOperation.Syntax.GetLocation()));
            return;
        }

        if (AssertionMethodSelectionAnalyzerCommon.TryGetSameNotSameValueTypeMatch(invocationOperation, assertType, out var sameNotSameValueTypeMatch))
        {
            var descriptor = sameNotSameValueTypeMatch.AssertionMethodName == "Equal" ? SameWithValueTypeDescriptor : NotSameWithValueTypeDescriptor;
            context.ReportDiagnostic(Diagnostic.Create(descriptor, invocationOperation.Syntax.GetLocation()));
            return;
        }

        if (symbols is not null &&
            TrueFalseConditionMethodSelectionAnalyzerCommon.TryGetMatch(invocationOperation, assertType, symbols.Value, out var trueFalseMatch))
        {
            var diagnosticId = TrueFalseConditionMethodSelectionAnalyzerCommon.GetDiagnosticId(trueFalseMatch);
            var descriptor = GetDescriptorById(diagnosticId);
            context.ReportDiagnostic(Diagnostic.Create(descriptor, trueFalseMatch.InnerInvocation.Syntax.GetLocation()));
        }
    }

    private static DiagnosticDescriptor GetDescriptorById(string id)
    {
        return id switch
        {
            RuleIdentifiers.UseMatchesDiagnosticId => UseMatchesDescriptor,
            RuleIdentifiers.UseDoesNotMatchDiagnosticId => UseDoesNotMatchDescriptor,
            RuleIdentifiers.UseStringContainsDiagnosticId => UseStringContainsDescriptor,
            RuleIdentifiers.UseStringDoesNotContainDiagnosticId => UseStringDoesNotContainDescriptor,
            RuleIdentifiers.UseStringStartsWithDiagnosticId => UseStringStartsWithDescriptor,
            RuleIdentifiers.UseStringDoesNotStartWithDiagnosticId => UseStringDoesNotStartWithDescriptor,
            RuleIdentifiers.UseStringEndsWithDiagnosticId => UseStringEndsWithDescriptor,
            RuleIdentifiers.UseStringDoesNotEndWithDiagnosticId => UseStringDoesNotEndWithDescriptor,
            RuleIdentifiers.UseCollectionContainsDiagnosticId => UseCollectionContainsDescriptor,
            RuleIdentifiers.UseCollectionDoesNotContainDiagnosticId => UseCollectionDoesNotContainDescriptor,
            RuleIdentifiers.UseCollectionAnyContainsDiagnosticId => UseCollectionAnyContainsDescriptor,
            RuleIdentifiers.UseCollectionAnyDoesNotContainDiagnosticId => UseCollectionAnyDoesNotContainDescriptor,
            RuleIdentifiers.UseCollectionAllDiagnosticId => UseCollectionAllDescriptor,
            RuleIdentifiers.UseCollectionDoesNotAllDiagnosticId => UseCollectionDoesNotAllDescriptor,
            _ => throw new System.InvalidOperationException($"Unknown diagnostic id: {id}"),
        };
    }
}
