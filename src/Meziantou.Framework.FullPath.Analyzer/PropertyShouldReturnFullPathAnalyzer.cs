using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PropertyShouldReturnFullPathAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        id: FullPathAnalyzerCommon.PropertyShouldReturnFullPathDiagnosticId,
        title: "Declare the property as FullPath instead of string",
        messageFormat: "Property '{0}' returns FullPath values and should be declared as FullPath instead of string",
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

            context.RegisterOperationBlockAction(context => AnalyzeOperationBlock(context, analyzerContext));
        });
    }

    private static void AnalyzeOperationBlock(OperationBlockAnalysisContext context, FullPathContext analyzerContext)
    {
        if (context.OwningSymbol is not IMethodSymbol { MethodKind: MethodKind.PropertyGet, AssociatedSymbol: IPropertySymbol propertySymbol })
            return;

        if (propertySymbol.Type.SpecialType != SpecialType.System_String)
            return;

        // Changing the type of a property that has a setter also changes what callers are allowed to assign
        if (propertySymbol.SetMethod is not null)
            return;

        if (propertySymbol.IsIndexer)
            return;

        if (!FullPathAnalyzerCommon.CanChangeDeclaredType(propertySymbol))
            return;

        var hasReturnValue = false;
        var allReturnsAreFullPath = true;
        foreach (var operationBlock in context.OperationBlocks)
        {
            analyzerContext.AnalyzeReturnOperations(operationBlock, ref hasReturnValue, ref allReturnsAreFullPath);
        }

        // Expression-bodied properties and expression-bodied getters have no return operation
        if (!hasReturnValue && context.OperationBlocks.Length == 1 && context.OperationBlocks[0] is not IBlockOperation)
        {
            hasReturnValue = true;
            allReturnsAreFullPath &= analyzerContext.IsFullPathType(context.OperationBlocks[0]);
        }

        if (!hasReturnValue || !allReturnsAreFullPath)
            return;

        var location = FullPathAnalyzerCommon.GetFirstSourceLocation(propertySymbol);
        if (location is null)
            return;

        context.ReportDiagnostic(Diagnostic.Create(Descriptor, location, propertySymbol.Name));
    }
}
