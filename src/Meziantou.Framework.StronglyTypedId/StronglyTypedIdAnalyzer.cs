using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Meziantou.Framework.StronglyTypedId;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StronglyTypedIdAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor UnsupportedType = new(
        id: "MFSTID0001",
        title: "Not supported type",
        messageFormat: "The type '{0}' is not supported",
        category: "StronglyTypedId",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(UnsupportedType);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(InitializeCore);
    }

    private static void InitializeCore(CompilationStartAnalysisContext context)
    {
        var nonGenericAttribute = context.Compilation.GetTypeByMetadataName("Meziantou.Framework.Annotations.StronglyTypedIdAttribute");
        var genericAttribute = context.Compilation.GetTypeByMetadataName("Meziantou.Framework.Annotations.StronglyTypedIdAttribute`1");
        if (nonGenericAttribute is null && genericAttribute is null)
            return;

        context.RegisterSymbolAction(context => AnalyzeSymbol(context, nonGenericAttribute, genericAttribute), SymbolKind.NamedType);
    }

    private static void AnalyzeSymbol(SymbolAnalysisContext context, INamedTypeSymbol? nonGenericAttribute, INamedTypeSymbol? genericAttribute)
    {
        var symbol = (INamedTypeSymbol)context.Symbol;
        foreach (var attribute in symbol.GetAttributes())
        {
            var typeSymbol = TryGetIdTypeSymbol(attribute, nonGenericAttribute, genericAttribute);
            if (typeSymbol is null)
                continue;

            if (StronglyTypedIdSourceGenerator.GetIdType(context.Compilation, typeSymbol) is not StronglyTypedIdSourceGenerator.IdType.Unknown)
                continue;

            var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? symbol.Locations.FirstOrDefault();
            if (location is null)
                continue;

            context.ReportDiagnostic(Diagnostic.Create(UnsupportedType, location, typeSymbol.ToDisplayString()));
        }
    }

    private static ITypeSymbol? TryGetIdTypeSymbol(AttributeData attribute, INamedTypeSymbol? nonGenericAttribute, INamedTypeSymbol? genericAttribute)
    {
        if (genericAttribute is not null &&
            attribute.AttributeClass is { Arity: 1 } genericClass &&
            SymbolEqualityComparer.Default.Equals(genericClass.OriginalDefinition, genericAttribute) &&
            genericClass.TypeArguments.Length == 1)
        {
            return genericClass.TypeArguments[0];
        }

        if (nonGenericAttribute is null ||
            !SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, nonGenericAttribute) ||
            attribute.ConstructorArguments.Length != 6 ||
            attribute.ConstructorArguments[0].Value is not ITypeSymbol type)
        {
            return null;
        }

        return type;
    }
}
