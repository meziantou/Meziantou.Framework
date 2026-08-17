using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConstantAssertionAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        id: RuleIdentifiers.ConstantAssertionDiagnosticId,
        title: "Assertion always produces the same result",
        messageFormat: "{0}",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Descriptor];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(context =>
        {
            var assertType = context.Compilation.GetTypeByMetadataName(AssertionsAnalyzerHelpers.AssertMetadataName);
            if (assertType is null)
                return;

            context.RegisterOperationAction(context => Analyze(context, assertType), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol assertType)
    {
        var invocationOperation = (IInvocationOperation)context.Operation;
        if (invocationOperation.TargetMethod is not { IsStatic: true } targetMethod ||
            !SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, assertType))
        {
            return;
        }

        if (TryGetConstantConditionMessage(invocationOperation, targetMethod.Name, out var conditionMessage))
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptor, invocationOperation.Syntax.GetLocation(), conditionMessage));
            return;
        }

        if (TryGetSelfComparisonMessage(invocationOperation, targetMethod.Name, out var selfComparisonMessage))
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptor, invocationOperation.Syntax.GetLocation(), selfComparisonMessage));
        }
    }

    private static bool TryGetConstantConditionMessage(IInvocationOperation invocationOperation, string methodName, out string message)
    {
        if (methodName is not ("True" or "False"))
        {
            message = null!;
            return false;
        }

        var conditionArgument = invocationOperation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == "condition");
        if (conditionArgument is null ||
            AssertionsAnalyzerHelpers.UnwrapImplicitConversion(conditionArgument.Value).ConstantValue is not { HasValue: true, Value: bool conditionValue })
        {
            message = null!;
            return false;
        }

        // Assert.True(false) and Assert.False(true) are reported by MFAS0051, which suggests Assert.Fail
        var alwaysSucceeds = methodName == "True" == conditionValue;
        if (!alwaysSucceeds)
        {
            message = null!;
            return false;
        }

        message = $"Assert.{methodName} is called with the constant '{(conditionValue ? "true" : "false")}' and always succeeds";
        return true;
    }

    private static bool TryGetSelfComparisonMessage(IInvocationOperation invocationOperation, string methodName, out string message)
    {
        // Equal, NotEqual and the unordered variants compare through EqualityComparer<T>.Default and therefore run
        // the type's own Equals, so comparing a value with itself is a reflexivity assertion rather than a constant.
        // Same and NotSame compare references and Equivalent compares structurally, neither of which runs user code.
        if (methodName is not ("Same" or "NotSame" or "Equivalent" or "NotEquivalent"))
        {
            message = null!;
            return false;
        }

        IArgumentOperation? expectedArgument = null;
        IArgumentOperation? actualArgument = null;
        foreach (var argument in invocationOperation.Arguments)
        {
            switch (argument.Parameter?.Name)
            {
                case "expected":
                    expectedArgument = argument;
                    break;
                case "actual":
                    actualArgument = argument;
                    break;
            }
        }

        if (expectedArgument is null || actualArgument is null)
        {
            message = null!;
            return false;
        }

        var expectedOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(expectedArgument.Value);
        var actualOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(actualArgument.Value);
        if (!IsSideEffectFreeReference(expectedOperation) ||
            !IsSideEffectFreeReference(actualOperation) ||
            !SyntaxFactory.AreEquivalent(expectedOperation.Syntax, actualOperation.Syntax))
        {
            message = null!;
            return false;
        }

        var alwaysSucceeds = !methodName.StartsWith("Not", StringComparison.Ordinal);
        message = $"Assert.{methodName} compares a value with itself and always {(alwaysSucceeds ? "succeeds" : "fails")}";
        return true;
    }

    /// <summary>
    /// Only simple references are considered, so that comparing two calls with side effects is never reported.
    /// A field access is side-effect free only when the instance it is read from is itself side-effect free,
    /// otherwise <c>Next().Value</c> would be treated as a comparison of a value with itself.
    /// </summary>
    private static bool IsSideEffectFreeReference(IOperation operation) => operation switch
    {
        ILocalReferenceOperation or IParameterReferenceOperation or IInstanceReferenceOperation => true,
        IFieldReferenceOperation fieldReference => fieldReference.Instance is null || IsSideEffectFreeReference(fieldReference.Instance),
        _ => false,
    };
}
