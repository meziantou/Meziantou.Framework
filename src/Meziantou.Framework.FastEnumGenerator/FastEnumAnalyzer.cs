using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.FastEnumGenerator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FastEnumAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor InvalidEnumType = new(
        id: "MFEG0001",
        title: "FastEnum target type is invalid",
        messageFormat: "The type '{0}' is not a valid enum type for FastEnum generation",
        category: "FastEnumGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UseFastEnumParse = new(
        id: "MFEG0002",
        title: "Use FastEnum Parse",
        messageFormat: "Use '{0}.Parse(...)' instead of 'Enum.Parse(...)'",
        category: "FastEnumGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UseFastEnumTryParse = new(
        id: "MFEG0003",
        title: "Use FastEnum TryParse",
        messageFormat: "Use '{0}.TryParse(...)' instead of 'Enum.TryParse(...)'",
        category: "FastEnumGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UseFastEnumGetNames = new(
        id: "MFEG0004",
        title: "Use FastEnum GetNames",
        messageFormat: "Use '{0}.GetNames(useMetadata: false)' instead of 'Enum.GetNames(...)'",
        category: "FastEnumGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UseFastEnumGetValues = new(
        id: "MFEG0005",
        title: "Use FastEnum GetValues",
        messageFormat: "Use '{0}.GetValues()' instead of 'Enum.GetValues(...)'",
        category: "FastEnumGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UseFastEnumGetName = new(
        id: "MFEG0006",
        title: "Use FastEnum GetName",
        messageFormat: "Use '{0}.GetName()' instead of 'Enum.GetName(...)'",
        category: "FastEnumGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UseFastEnumIsDefined = new(
        id: "MFEG0007",
        title: "Use FastEnum IsDefined",
        messageFormat: "Use '{0}.IsDefined(...)' instead of 'Enum.IsDefined(...)'",
        category: "FastEnumGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UseFastEnumToStringFast = new(
        id: "MFEG0008",
        title: "Use FastEnum ToStringFast",
        messageFormat: "Use 'ToStringFast()' instead of '{0}.ToString()'",
        category: "FastEnumGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [InvalidEnumType, UseFastEnumParse, UseFastEnumTryParse, UseFastEnumGetNames, UseFastEnumGetValues, UseFastEnumGetName, UseFastEnumIsDefined, UseFastEnumToStringFast];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(context =>
        {
            var fastEnumAttribute = context.Compilation.GetTypeByMetadataName(FastEnumAnalyzerCommon.FastEnumAttributeMetadataName);
            if (fastEnumAttribute is null)
                return;

            context.RegisterOperationAction(context => AnalyzeAttributeOperation(context, fastEnumAttribute), OperationKind.Attribute);

            var enumType = context.Compilation.GetSpecialType(SpecialType.System_Enum);
            var fastEnumTypes = FastEnumAnalyzerCommon.GetFastEnumTypes(context.Compilation, fastEnumAttribute);
            if (fastEnumTypes.Count == 0)
                return;

            context.RegisterOperationAction(context => AnalyzeInvocationOperation(context, enumType, fastEnumTypes), OperationKind.Invocation);
        });
    }

    private static void AnalyzeAttributeOperation(OperationAnalysisContext context, INamedTypeSymbol fastEnumAttribute)
    {
        var attributeOperation = (IAttributeOperation)context.Operation;

        if (attributeOperation.Operation is not IObjectCreationOperation attribute)
            return;

        if (!SymbolEqualityComparer.Default.Equals(attribute.Constructor?.ContainingType, fastEnumAttribute))
            return;

        AnalyzeAttribute(context, attribute);
    }

    private static void AnalyzeAttribute(OperationAnalysisContext context, IObjectCreationOperation attribute)
    {
        if (attribute.Arguments.Length != 1)
            return;

        var location = attribute.Syntax.GetLocation();
        var argument = attribute.Arguments[0].Value;

        if (argument.ConstantValue is { HasValue: true, Value: null })
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidEnumType, location, "(null)"));
            return;
        }

        if (argument is not ITypeOfOperation { TypeOperand.TypeKind: not TypeKind.Enum } typeOfOperation)
            return;

        context.ReportDiagnostic(Diagnostic.Create(InvalidEnumType, location, typeOfOperation.TypeOperand.ToDisplayString()));
    }

    private static void AnalyzeInvocationOperation(OperationAnalysisContext context, INamedTypeSymbol enumType, ImmutableHashSet<INamedTypeSymbol> fastEnumTypes)
    {
        var invocationOperation = (IInvocationOperation)context.Operation;
        if (!FastEnumAnalyzerCommon.TryGetFastEnumInvocationMatch(invocationOperation, enumType, fastEnumTypes, out var match))
            return;

        var diagnostic = Diagnostic.Create(GetDiagnosticDescriptor(match.MethodKind), invocationOperation.Syntax.GetLocation(), match.EnumType.ToDisplayString());
        context.ReportDiagnostic(diagnostic);
    }

    private static DiagnosticDescriptor GetDiagnosticDescriptor(FastEnumMethodKind methodKind)
    {
        return methodKind switch
        {
            FastEnumMethodKind.Parse => UseFastEnumParse,
            FastEnumMethodKind.TryParse => UseFastEnumTryParse,
            FastEnumMethodKind.GetNames => UseFastEnumGetNames,
            FastEnumMethodKind.GetValues => UseFastEnumGetValues,
            FastEnumMethodKind.GetName => UseFastEnumGetName,
            FastEnumMethodKind.IsDefined => UseFastEnumIsDefined,
            FastEnumMethodKind.ToString => UseFastEnumToStringFast,
            _ => InvalidEnumType,
        };
    }
}
