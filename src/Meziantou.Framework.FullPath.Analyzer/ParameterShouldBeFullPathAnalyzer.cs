using System.Collections.Concurrent;
using System.Collections.Immutable;
using Meziantou.Framework.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

/// <summary>
/// Reports parameters that are declared as <see cref="string"/> but only ever receive <c>FullPath</c> arguments.
/// </summary>
/// <remarks>
/// Only methods that are not visible outside of the assembly are considered, because every call site must be visible
/// in the compilation for the conclusion to hold.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ParameterShouldBeFullPathAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        id: FullPathAnalyzerCommon.ParameterShouldBeFullPathDiagnosticId,
        title: "Declare the parameter as FullPath instead of string",
        messageFormat: "Parameter '{0}' only receives FullPath values and should be declared as FullPath instead of string",
        category: "FullPath",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

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

            var candidateCache = new ConcurrentDictionary<ISymbol, bool>(SymbolEqualityComparer.Default);
            var arguments = new ConcurrentBag<ArgumentUsage>();
            var excludedMethods = new ConcurrentBag<IMethodSymbol>();

            context.RegisterOperationAction(context => AnalyzeInvocation(context, analyzerContext, candidateCache, arguments), OperationKind.Invocation);
            context.RegisterOperationAction(context => excludedMethods.Add(((IMethodReferenceOperation)context.Operation).Method.OriginalDefinition), OperationKind.MethodReference);
            context.RegisterCompilationEndAction(context => Report(context, arguments, excludedMethods));
        });
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        FullPathContext analyzerContext,
        ConcurrentDictionary<ISymbol, bool> candidateCache,
        ConcurrentBag<ArgumentUsage> arguments)
    {
        var invocationOperation = (IInvocationOperation)context.Operation;
        var targetMethod = invocationOperation.TargetMethod.OriginalDefinition;
        if (!candidateCache.GetOrAdd(targetMethod, static (_, method) => IsCandidate(method), targetMethod))
            return;

        foreach (var argument in invocationOperation.Arguments)
        {
            if (argument.Parameter is not { } parameter || !IsCandidate(parameter))
                continue;

            arguments.Add(new ArgumentUsage(targetMethod, parameter.Ordinal, analyzerContext.IsFullPathType(argument.Value)));
        }
    }

    private static void Report(CompilationAnalysisContext context, ConcurrentBag<ArgumentUsage> arguments, ConcurrentBag<IMethodSymbol> excludedMethods)
    {
        var excluded = new HashSet<ISymbol>(excludedMethods, SymbolEqualityComparer.Default);

        // For each parameter: null when never seen, true while every argument was a FullPath, false otherwise
        var states = new Dictionary<ISymbol, bool?[]>(SymbolEqualityComparer.Default);
        foreach (var argument in arguments)
        {
            if (excluded.Contains(argument.Method))
                continue;

            if (!states.TryGetValue(argument.Method, out var parameterStates))
            {
                parameterStates = new bool?[argument.Method.Parameters.Length];
                states.Add(argument.Method, parameterStates);
            }

            parameterStates[argument.Ordinal] = argument.IsFullPath && parameterStates[argument.Ordinal] is not false;
        }

        foreach (var state in states)
        {
            var method = (IMethodSymbol)state.Key;
            var parameterStates = state.Value;
            for (var i = 0; i < parameterStates.Length; i++)
            {
                if (parameterStates[i] is not true)
                    continue;

                var parameter = method.Parameters[i];
                var location = parameter.GetFirstSourceLocation();
                if (location is null)
                    continue;

                context.ReportDiagnostic(Descriptor, location, parameter.Name);
            }
        }
    }

    private static bool IsCandidate(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.MethodKind is not (MethodKind.Ordinary or MethodKind.LocalFunction))
            return false;

        // Every call site must be visible in the compilation
        if (methodSymbol.IsVisibleOutsideOfAssembly())
            return false;

        if (!methodSymbol.ExplicitInterfaceImplementations.IsEmpty)
            return false;

        if (!methodSymbol.CanChangeDeclaredType())
            return false;

        return methodSymbol.GetFirstSourceLocation() is not null;
    }

    private static bool IsCandidate(IParameterSymbol parameterSymbol)
    {
        if (parameterSymbol.Type.SpecialType != SpecialType.System_String)
            return false;

        if (parameterSymbol.RefKind is not RefKind.None)
            return false;

        // FullPath is a struct and cannot represent null
        if (parameterSymbol.NullableAnnotation is NullableAnnotation.Annotated)
            return false;

        // A params array is bound positionally and an optional parameter has a string default value
        return !parameterSymbol.IsParams && !parameterSymbol.IsOptional;
    }

    private readonly record struct ArgumentUsage(IMethodSymbol Method, int Ordinal, bool IsFullPath);
}
