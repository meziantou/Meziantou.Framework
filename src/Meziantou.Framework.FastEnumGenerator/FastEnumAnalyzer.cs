using System.Collections.Immutable;
using Meziantou.Framework.Roslyn;
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
        messageFormat: "Use the generated 'GetName()' extension method on the '{0}' value instead of 'Enum.GetName(...)'",
        category: "FastEnumGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UseFastEnumIsDefined = new(
        id: "MFEG0007",
        title: "Use FastEnum IsDefinedFast",
        messageFormat: "Use '{0}.IsDefinedFast(...)' instead of 'Enum.IsDefined(...)'",
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

    internal static readonly DiagnosticDescriptor EmptyEnumType = new(
        id: "MFEG0009",
        title: "FastEnum target enum has no members",
        messageFormat: "No code is generated for '{0}' because the enum has no members",
        category: "FastEnumGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [InvalidEnumType, UseFastEnumParse, UseFastEnumTryParse, UseFastEnumGetNames, UseFastEnumGetValues, UseFastEnumGetName, UseFastEnumIsDefined, UseFastEnumToStringFast, EmptyEnumType];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Validating the attribute arguments only needs the assembly's attribute list, so it does not
        // require materializing an IOperation for every attribute application in the compilation.
        context.RegisterCompilationAction(AnalyzeAssemblyAttributes);

        context.RegisterCompilationStartAction(context =>
        {
            var fastEnumAttribute = context.Compilation.GetTypeByMetadataName(FastEnumAnalyzerCommon.FastEnumAttributeMetadataName);
            if (fastEnumAttribute is null)
                return;

            var fastEnumTypes = FastEnumAnalyzerCommon.GetFastEnumTypes(context.Compilation, fastEnumAttribute);
            if (fastEnumTypes.Count == 0)
                return;

            var enumType = context.Compilation.GetSpecialType(SpecialType.System_Enum);
            var supportsExtensionMembers = FastEnumAnalyzerCommon.SupportsExtensionMembers(context.Compilation);
            context.RegisterOperationAction(context => AnalyzeInvocationOperation(context, enumType, fastEnumTypes, supportsExtensionMembers), OperationKind.Invocation);
        });
    }

    private static void AnalyzeAssemblyAttributes(CompilationAnalysisContext context)
    {
        var fastEnumAttribute = context.Compilation.GetTypeByMetadataName(FastEnumAnalyzerCommon.FastEnumAttributeMetadataName);
        if (fastEnumAttribute is null)
            return;

        foreach (var attribute in context.Compilation.Assembly.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, fastEnumAttribute))
                continue;

            if (attribute.ConstructorArguments.Length != 1)
                continue;

            var syntaxReference = attribute.ApplicationSyntaxReference;
            if (syntaxReference is null)
                continue;

            var argument = attribute.ConstructorArguments[0];
            switch (argument.Value)
            {
                case null:
                    context.ReportDiagnostic(InvalidEnumType, syntaxReference, "(null)");
                    break;

                case INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType when !FastEnumAnalyzerCommon.HasEnumMembers(enumType):
                    context.ReportDiagnostic(EmptyEnumType, syntaxReference, enumType.ToDisplayString());
                    break;

                case INamedTypeSymbol { TypeKind: TypeKind.Enum }:
                    break;

                case ITypeSymbol type:
                    context.ReportDiagnostic(InvalidEnumType, syntaxReference, type.ToDisplayString());
                    break;
            }
        }
    }

    private static void AnalyzeInvocationOperation(OperationAnalysisContext context, INamedTypeSymbol enumType, ImmutableHashSet<INamedTypeSymbol> fastEnumTypes, bool supportsExtensionMembers)
    {
        var invocationOperation = (IInvocationOperation)context.Operation;
        if (!FastEnumAnalyzerCommon.TryGetFastEnumInvocationMatch(invocationOperation, enumType, fastEnumTypes, supportsExtensionMembers, out var match))
            return;

        context.ReportDiagnostic(GetDiagnosticDescriptor(match.MethodKind), invocationOperation, match.EnumType.ToDisplayString());
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
