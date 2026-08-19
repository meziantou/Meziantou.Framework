using System.Collections.Immutable;
using Meziantou.Framework.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class VariableShouldBeFullPathAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        id: FullPathAnalyzerCommon.VariableShouldBeFullPathDiagnosticId,
        title: "Declare the variable as FullPath instead of string",
        messageFormat: "Variable '{0}' only holds FullPath values and should be declared as FullPath instead of string",
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
        var candidates = new Dictionary<ISymbol, VariableState>(SymbolEqualityComparer.Default);
        foreach (var operationBlock in context.OperationBlocks)
        {
            Visit(operationBlock, analyzerContext, candidates);
        }

        foreach (var candidate in candidates)
        {
            var state = candidate.Value;
            if (!state.HasValue || !state.AllValuesAreFullPath || state.Location is null)
                continue;

            context.ReportDiagnostic(Descriptor, state.Location, candidate.Key.Name);
        }
    }

    private static void Visit(IOperation operation, FullPathContext analyzerContext, Dictionary<ISymbol, VariableState> candidates)
    {
        switch (operation)
        {
            case IVariableDeclaratorOperation variableDeclaratorOperation:
                AnalyzeDeclarator(variableDeclaratorOperation, analyzerContext, candidates);
                break;

            case ISimpleAssignmentOperation { Target: ILocalReferenceOperation localReferenceOperation } assignmentOperation:
                if (candidates.TryGetValue(localReferenceOperation.Local, out var assignedState))
                {
                    assignedState.HasValue = true;
                    assignedState.AllValuesAreFullPath &= analyzerContext.IsFullPathType(assignmentOperation.Value);
                }

                break;

            // Any other kind of write means the variable is used as a string
            case ICompoundAssignmentOperation { Target: ILocalReferenceOperation compoundTarget }:
                Disqualify(candidates, compoundTarget.Local);
                break;

            case IArgumentOperation { Parameter.RefKind: not RefKind.None, Value: ILocalReferenceOperation argumentReference }:
                Disqualify(candidates, argumentReference.Local);
                break;
        }

        foreach (var childOperation in operation.ChildOperations)
        {
            Visit(childOperation, analyzerContext, candidates);
        }
    }

    private static void AnalyzeDeclarator(IVariableDeclaratorOperation operation, FullPathContext analyzerContext, Dictionary<ISymbol, VariableState> candidates)
    {
        var local = operation.Symbol;
        if (local.Type.SpecialType != SpecialType.System_String)
            return;

        if (local.IsRef)
            return;

        // 'var' declarations are left alone: the developer asked for whatever the initializer produces
        if (operation.Syntax is not VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Type.IsVar: false } })
            return;

        var state = new VariableState { Location = local.GetFirstSourceLocation() };
        var initializer = operation.Initializer ?? (operation.Parent as IVariableDeclarationOperation)?.Initializer;
        if (initializer is not null)
        {
            state.HasValue = true;
            state.AllValuesAreFullPath = analyzerContext.IsFullPathType(initializer.Value);
        }

        candidates[local] = state;
    }

    private static void Disqualify(Dictionary<ISymbol, VariableState> candidates, ILocalSymbol local)
    {
        if (candidates.TryGetValue(local, out var state))
        {
            state.AllValuesAreFullPath = false;
        }
    }

    private sealed class VariableState
    {
        public bool HasValue { get; set; }
        public bool AllValuesAreFullPath { get; set; } = true;
        public Location? Location { get; set; }
    }
}
