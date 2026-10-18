using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Meziantou.Framework.WebPlatform.Analyzers;

/// <summary>
/// Ensures every public member of a type mapping a WebIDL definition is annotated with [IdlMember] or [NotInIdl],
/// so the public surface can be traced back to the specifications.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IdlAttributeAnalyzer : DiagnosticAnalyzer
{
    private const string AttributeNamespace = "Meziantou.Framework.WebPlatform";

    internal static readonly DiagnosticDescriptor MissingIdlMember = new(
        id: "HTMLIDL001",
        title: "Public member of a WebIDL type must be annotated",
        messageFormat: "Public member '{0}' of WebIDL type '{1}' must be annotated with [IdlMember] or [NotInIdl]",
        category: "Specification",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor InvalidSpecUrl = new(
        id: "HTMLIDL002",
        title: "Specification URL must be an absolute https URL with a fragment",
        messageFormat: "'{0}' is not a valid specification URL (expected an absolute https URL with a fragment)",
        category: "Specification",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor IdlMemberOutsideIdlType = new(
        id: "HTMLIDL003",
        title: "[IdlMember] must be declared in a WebIDL type",
        messageFormat: "Member '{0}' is annotated with [IdlMember] but its type '{1}' is not annotated with [IdlInterface]",
        category: "Specification",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ConflictingAttributes = new(
        id: "HTMLIDL004",
        title: "[IdlMember] and [NotInIdl] are mutually exclusive",
        messageFormat: "Member '{0}' cannot be annotated with both [IdlMember] and [NotInIdl]",
        category: "Specification",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(MissingIdlMember, InvalidSpecUrl, IdlMemberOutsideIdlType, ConflictingAttributes);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.RegisterCompilationStartAction(compilationContext =>
        {
            var idlInterface = compilationContext.Compilation.GetTypeByMetadataName(AttributeNamespace + ".IdlInterfaceAttribute");
            var idlMember = compilationContext.Compilation.GetTypeByMetadataName(AttributeNamespace + ".IdlMemberAttribute");
            var notInIdl = compilationContext.Compilation.GetTypeByMetadataName(AttributeNamespace + ".NotInIdlAttribute");
            if (idlInterface is null || idlMember is null || notInIdl is null)
                return;

            compilationContext.RegisterSymbolAction(symbolContext => AnalyzeType(symbolContext, idlInterface), SymbolKind.NamedType);
            compilationContext.RegisterSymbolAction(
                symbolContext => AnalyzeMember(symbolContext, idlInterface, idlMember, notInIdl),
                SymbolKind.Method, SymbolKind.Property, SymbolKind.Field, SymbolKind.Event);
        });
    }

    private static void AnalyzeType(SymbolAnalysisContext context, INamedTypeSymbol idlInterface)
    {
        foreach (var attribute in context.Symbol.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, idlInterface))
            {
                ValidateSpecUrl(context, attribute);
            }
        }
    }

    private static void AnalyzeMember(SymbolAnalysisContext context, INamedTypeSymbol idlInterface, INamedTypeSymbol idlMember, INamedTypeSymbol notInIdl)
    {
        var member = context.Symbol;
        var containingType = member.ContainingType;
        if (containingType is null)
            return;

        var hasIdlMember = false;
        var hasNotInIdl = false;
        foreach (var attribute in member.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, idlMember))
            {
                hasIdlMember = true;
                ValidateSpecUrl(context, attribute);
            }
            else if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, notInIdl))
            {
                hasNotInIdl = true;
            }
        }

        if (hasIdlMember && hasNotInIdl)
        {
            Report(context, ConflictingAttributes, member.Name);
        }

        var isIdlType = HasAttribute(containingType, idlInterface);
        if (!isIdlType)
        {
            if (hasIdlMember)
            {
                Report(context, IdlMemberOutsideIdlType, member.Name, containingType.Name);
            }

            return;
        }

        if (hasIdlMember || hasNotInIdl || !MustBeAnnotated(member))
            return;

        Report(context, MissingIdlMember, member.Name, containingType.Name);
    }

    private static bool MustBeAnnotated(ISymbol member)
    {
        if (member.IsImplicitlyDeclared || member.IsOverride)
            return false;

        if (member.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal))
            return false;

        if (!IsExternallyVisible(member.ContainingType))
            return false;

        // Accessors are covered by their property or event. Operators and conversions are .NET-only plumbing (e.g. union types).
        if (member is IMethodSymbol method && method.MethodKind is not (MethodKind.Ordinary or MethodKind.Constructor))
            return false;

        return true;
    }

    private static bool IsExternallyVisible(INamedTypeSymbol? type)
    {
        while (type is not null)
        {
            if (type.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal))
                return false;

            type = type.ContainingType;
        }

        return true;
    }

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attributeType)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
                return true;
        }

        return false;
    }

    private static void ValidateSpecUrl(SymbolAnalysisContext context, AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length < 2)
            return;

        var url = attribute.ConstructorArguments[1].Value as string;
        if (url is null ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "https", StringComparison.Ordinal) ||
            uri.Fragment.Length <= 1)
        {
            var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? context.Symbol.Locations[0];
            context.ReportDiagnostic(Diagnostic.Create(InvalidSpecUrl, location, url ?? "<null>"));
        }
    }

    private static void Report(SymbolAnalysisContext context, DiagnosticDescriptor descriptor, params object[] args)
    {
        foreach (var location in context.Symbol.Locations)
        {
            if (location.IsInSource)
            {
                context.ReportDiagnostic(Diagnostic.Create(descriptor, location, args));
                return;
            }
        }
    }
}
