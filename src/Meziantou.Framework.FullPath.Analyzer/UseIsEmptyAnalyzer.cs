using System.Collections.Immutable;
using Meziantou.Framework.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

/// <summary>
/// Reports emptiness checks written against the string representation of a <c>FullPath</c> or against
/// <c>FullPath.Empty</c>.
/// </summary>
/// <remarks>
/// <c>IsEmpty</c> carries a <see cref="MemberNotNullWhenAttribute"/>, which the alternatives do not.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseIsEmptyAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        id: FullPathAnalyzerCommon.UseIsEmptyDiagnosticId,
        title: "Use FullPath.IsEmpty",
        messageFormat: "Use FullPath.IsEmpty to check whether a path is empty",
        category: "FullPath",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

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

            context.RegisterOperationAction(context => AnalyzeInvocation(context, analyzerContext), OperationKind.Invocation);
            context.RegisterOperationAction(context => AnalyzeBinaryOperation(context, analyzerContext), OperationKind.Binary);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, FullPathContext analyzerContext)
    {
        var invocationOperation = (IInvocationOperation)context.Operation;
        if (invocationOperation.TargetMethod is not { IsStatic: true, Name: "IsNullOrEmpty" or "IsNullOrWhiteSpace" } targetMethod)
            return;

        if (targetMethod.ContainingType?.SpecialType != SpecialType.System_String)
            return;

        if (invocationOperation.Arguments.Length != 1 || !analyzerContext.IsFullPathType(invocationOperation.Arguments[0].Value))
            return;

        context.ReportDiagnostic(Descriptor, invocationOperation);
    }

    private static void AnalyzeBinaryOperation(OperationAnalysisContext context, FullPathContext analyzerContext)
    {
        var binaryOperation = (IBinaryOperation)context.Operation;
        if (binaryOperation.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals))
            return;

        if (IsComparisonWithEmpty(binaryOperation.LeftOperand, binaryOperation.RightOperand, analyzerContext) ||
            IsComparisonWithEmpty(binaryOperation.RightOperand, binaryOperation.LeftOperand, analyzerContext))
        {
            context.ReportDiagnostic(Descriptor, binaryOperation);
        }
    }

    private static bool IsComparisonWithEmpty(IOperation operand, IOperation otherOperand, FullPathContext analyzerContext)
    {
        // fullPath == FullPath.Empty
        if (operand is IPropertyReferenceOperation { Property: { IsStatic: true, Name: "Empty" } property } &&
            analyzerContext.IsFullPathType(property.ContainingType) &&
            analyzerContext.IsFullPathType(otherOperand))
        {
            return true;
        }

        // fullPath.Value.Length == 0
        return operand is ILiteralOperation { ConstantValue: { HasValue: true, Value: 0 } } &&
            otherOperand is IPropertyReferenceOperation { Property.Name: "Length", Instance: { } instance } &&
            analyzerContext.IsFullPathType(instance);
    }
}
