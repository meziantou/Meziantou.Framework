using System.Collections.Immutable;
using Meziantou.Framework.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

/// <summary>
/// Reports comparisons performed on the string representation of a <c>FullPath</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>fullPath == "value"</c> binds to <c>string.operator ==</c>, because there is no implicit conversion from
/// <see cref="string"/> to <c>FullPath</c> that would make <c>FullPath.operator ==</c> applicable. The comparison is
/// therefore ordinal, whereas <c>FullPathComparer.Default</c> is case-insensitive on Windows. <c>string.Equals</c> is
/// ordinal as well.
/// </para>
/// <para>
/// <c>string.Compare(string, string)</c> and <c>string.CompareTo(string)</c> use the current culture, so the ordering
/// of paths depends on the culture of the machine. The message states which kind of comparison is performed.
/// </para>
/// <para>
/// An explicit <see cref="StringComparison"/> is left alone: the developer asked for a string comparison.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CompareFullPathAsStringAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        id: FullPathAnalyzerCommon.CompareFullPathAsStringDiagnosticId,
        title: "Compare FullPath values instead of their string representation",
        messageFormat: "This compares the string representation of a FullPath, which is {0}, instead of using FullPathComparer",
        category: "FullPath",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private const string OrdinalComparison = "ordinal";
    private const string CultureSensitiveComparison = "culture-sensitive";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Descriptor];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(context =>
        {
            var analyzerContext = new FullPathContext(context.Compilation);
            if (!analyzerContext.IsValid)
                return;

            context.RegisterOperationAction(context => AnalyzeBinaryOperation(context, analyzerContext), OperationKind.Binary);
            context.RegisterOperationAction(context => AnalyzeInvocation(context, analyzerContext), OperationKind.Invocation);
        });
    }

    private static void AnalyzeBinaryOperation(OperationAnalysisContext context, FullPathContext analyzerContext)
    {
        var binaryOperation = (IBinaryOperation)context.Operation;
        if (binaryOperation.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals))
            return;

        // FullPath == FullPath binds to FullPath.operator ==, and its operands are not strings
        if (!IsString(binaryOperation.LeftOperand) || !IsString(binaryOperation.RightOperand))
            return;

        if (!analyzerContext.IsFullPathType(binaryOperation.LeftOperand) && !analyzerContext.IsFullPathType(binaryOperation.RightOperand))
            return;

        context.ReportDiagnostic(Descriptor, binaryOperation, OrdinalComparison);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, FullPathContext analyzerContext)
    {
        var invocationOperation = (IInvocationOperation)context.Operation;
        var targetMethod = invocationOperation.TargetMethod;
        if (targetMethod.ContainingType?.SpecialType != SpecialType.System_String)
            return;

        if (targetMethod.Name is not ("Equals" or "Compare" or "CompareTo"))
            return;

        // An explicit StringComparison means the developer asked for a string comparison
        foreach (var parameter in targetMethod.Parameters)
        {
            if (parameter.Type.Name is "StringComparison")
                return;
        }

        var comparesFullPath = invocationOperation.Instance is not null && analyzerContext.IsFullPathType(invocationOperation.Instance);
        foreach (var argument in invocationOperation.Arguments)
        {
            if (!IsString(argument.Value))
                return;

            comparesFullPath |= analyzerContext.IsFullPathType(argument.Value);
        }

        if (!comparesFullPath)
            return;

        // string.Equals is ordinal, whereas string.Compare(string, string) and string.CompareTo(string) use the current culture
        var comparisonKind = targetMethod.Name is "Equals" ? OrdinalComparison : CultureSensitiveComparison;
        context.ReportDiagnostic(Descriptor, invocationOperation, comparisonKind);
    }

    private static bool IsString(IOperation operation)
    {
        return operation.Type?.SpecialType == SpecialType.System_String;
    }
}
