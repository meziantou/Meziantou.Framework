using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MethodShouldReturnFullPathAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        id: FullPathAnalyzerCommon.MethodShouldReturnFullPathDiagnosticId,
        title: "Method should return FullPath",
        messageFormat: "Method '{0}' returns FullPath values and should return FullPath instead of string",
        category: "FullPath",
        defaultSeverity: DiagnosticSeverity.Warning,
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

            context.RegisterOperationBlockStartAction(context => Analyze(context, analyzerContext));
        });
    }

    private static void Analyze(OperationBlockStartAnalysisContext context, FullPathContext analyzerContext)
    {
        if (context.OwningSymbol is not IMethodSymbol methodSymbol)
            return;

        if (methodSymbol.ReturnType.SpecialType != SpecialType.System_String)
            return;

        var hasReturnValue = false;
        var allReturnsAreFullPath = true;
        context.RegisterOperationAction(context =>
        {
            var returnOperation = (IReturnOperation)context.Operation;
            if (returnOperation.ReturnedValue is null)
                return;

            Volatile.Write(ref hasReturnValue, true);
            if (!analyzerContext.IsFullPathType(returnOperation.ReturnedValue))
            {
                Volatile.Write(ref allReturnsAreFullPath, false);
            }
        }, OperationKind.Return);

        context.RegisterOperationBlockEndAction(context =>
        {
            if (!Volatile.Read(ref hasReturnValue) || !Volatile.Read(ref allReturnsAreFullPath))
                return;

            Location? location = null;
            foreach (var candidateLocation in methodSymbol.Locations)
            {
                if (!candidateLocation.IsInSource)
                    continue;

                location = candidateLocation;
                break;
            }

            if (location is null)
                return;

            context.ReportDiagnostic(Diagnostic.Create(Descriptor, location, methodSymbol.Name));
        });
    }
}
