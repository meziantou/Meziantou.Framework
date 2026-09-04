using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Operations;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Meziantou.Framework.FastEnumGenerator;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(FastEnumCodeFixProvider))]
public sealed class FastEnumCodeFixProvider : CodeFixProvider
{
    private const string UseFastEnumParseDiagnosticId = "MFEG0002";
    private const string UseFastEnumTryParseDiagnosticId = "MFEG0003";
    private const string UseFastEnumGetNamesDiagnosticId = "MFEG0004";
    private const string UseFastEnumGetValuesDiagnosticId = "MFEG0005";
    private const string UseFastEnumGetNameDiagnosticId = "MFEG0006";
    private const string UseFastEnumIsDefinedDiagnosticId = "MFEG0007";
    private const string UseFastEnumToStringFastDiagnosticId = "MFEG0008";

    private static readonly ImmutableArray<string> SupportedDiagnosticIds = [UseFastEnumParseDiagnosticId, UseFastEnumTryParseDiagnosticId, UseFastEnumGetNamesDiagnosticId, UseFastEnumGetValuesDiagnosticId, UseFastEnumGetNameDiagnosticId, UseFastEnumIsDefinedDiagnosticId, UseFastEnumToStringFastDiagnosticId];

    public override ImmutableArray<string> FixableDiagnosticIds => SupportedDiagnosticIds;

    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var invocationSyntax = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true).FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (invocationSyntax is null || semanticModel.GetOperation(invocationSyntax, context.CancellationToken) is not IInvocationOperation invocationOperation)
                continue;

            if (!TryCreateReplacementInvocation(invocationOperation, invocationSyntax, out _, out _))
                continue;

            var title = diagnostic.Id switch
            {
                UseFastEnumParseDiagnosticId => "Use FastEnum Parse",
                UseFastEnumTryParseDiagnosticId => "Use FastEnum TryParse",
                UseFastEnumGetNamesDiagnosticId => "Use FastEnum GetNames",
                UseFastEnumGetValuesDiagnosticId => "Use FastEnum GetValues",
                UseFastEnumGetNameDiagnosticId => "Use FastEnum GetName",
                UseFastEnumIsDefinedDiagnosticId => "Use FastEnum IsDefinedFast",
                UseFastEnumToStringFastDiagnosticId => "Use FastEnum ToStringFast",
                _ => "Use FastEnum API",
            };

            context.RegisterCodeFix(
                CodeAction.Create(
                    title,
                    cancellationToken => ApplyFixAsync(context.Document, invocationSyntax, cancellationToken),
                    equivalenceKey: diagnostic.Id),
                diagnostic);
        }
    }

    private static async Task<Document> ApplyFixAsync(Document document, InvocationExpressionSyntax invocationSyntax, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null || semanticModel.GetOperation(invocationSyntax, cancellationToken) is not IInvocationOperation invocationOperation)
            return document;

        if (!TryCreateReplacementInvocation(invocationOperation, invocationSyntax, out var replacement, out var requiredNamespace))
            return document;

        replacement = replacement.WithTriviaFrom(invocationSyntax).WithAdditionalAnnotations(Formatter.Annotation);
        var newRoot = root.ReplaceNode(invocationSyntax, replacement);

        // The generated members live in the enum's namespace, or in ExtensionMethodNamespace when set.
        // Without the import the rewritten call does not resolve.
        if (requiredNamespace is not null)
        {
            newRoot = AddUsingDirectiveIfMissing(newRoot, invocationSyntax, requiredNamespace);
        }

        return document.WithSyntaxRoot(newRoot);
    }

    private static SyntaxNode AddUsingDirectiveIfMissing(SyntaxNode root, SyntaxNode originalNode, string namespaceName)
    {
        if (root is not CompilationUnitSyntax compilationUnit)
            return root;

        if (IsNamespaceInScope(compilationUnit, originalNode, namespaceName))
            return root;

        var usingDirective = UsingDirective(ParseName(namespaceName)).WithAdditionalAnnotations(Formatter.Annotation);
        return compilationUnit.AddUsings(usingDirective);
    }

    private static bool IsNamespaceInScope(CompilationUnitSyntax compilationUnit, SyntaxNode originalNode, string namespaceName)
    {
        foreach (var usingDirective in compilationUnit.Usings)
        {
            if (usingDirective is { Alias: null, StaticKeyword.RawKind: 0, Name: { } name } && string.Equals(name.ToString(), namespaceName, StringComparison.Ordinal))
                return true;
        }

        // A call site inside the target namespace (or a nested one) already sees the generated class.
        for (var node = originalNode; node is not null; node = node.Parent)
        {
            var declaredName = node switch
            {
                NamespaceDeclarationSyntax namespaceDeclaration => namespaceDeclaration.Name.ToString(),
                FileScopedNamespaceDeclarationSyntax fileScopedNamespace => fileScopedNamespace.Name.ToString(),
                _ => null,
            };

            if (declaredName is null)
                continue;

            if (string.Equals(declaredName, namespaceName, StringComparison.Ordinal))
                return true;

            foreach (var usingDirective in GetUsings(node))
            {
                if (usingDirective is { Alias: null, StaticKeyword.RawKind: 0, Name: { } name } && string.Equals(name.ToString(), namespaceName, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    private static SyntaxList<UsingDirectiveSyntax> GetUsings(SyntaxNode node)
    {
        return node switch
        {
            NamespaceDeclarationSyntax namespaceDeclaration => namespaceDeclaration.Usings,
            FileScopedNamespaceDeclarationSyntax fileScopedNamespace => fileScopedNamespace.Usings,
            _ => default,
        };
    }

    private static bool TryCreateReplacementInvocation(IInvocationOperation invocationOperation, InvocationExpressionSyntax invocationSyntax, out InvocationExpressionSyntax replacement, out string? requiredNamespace)
    {
        requiredNamespace = null;
        if (invocationOperation.SemanticModel is null)
            return TryReturnNoReplacement(out replacement);

        var compilation = invocationOperation.SemanticModel.Compilation;
        var fastEnumAttribute = compilation.GetTypeByMetadataName(FastEnumAnalyzerCommon.FastEnumAttributeMetadataName);
        if (fastEnumAttribute is null)
            return TryReturnNoReplacement(out replacement);

        var enumType = compilation.GetSpecialType(SpecialType.System_Enum);
        var fastEnumTypes = FastEnumAnalyzerCommon.GetFastEnumTypes(compilation, fastEnumAttribute);
        var supportsExtensionMembers = FastEnumAnalyzerCommon.SupportsExtensionMembers(compilation);
        if (!FastEnumAnalyzerCommon.TryGetFastEnumInvocationMatch(invocationOperation, enumType, fastEnumTypes, supportsExtensionMembers, out var match))
            return TryReturnNoReplacement(out replacement);

        var created = match.MethodKind switch
        {
            FastEnumMethodKind.Parse => TryCreateParseReplacement(invocationOperation, invocationSyntax, match.EnumType, out replacement),
            FastEnumMethodKind.TryParse => TryCreateTryParseReplacement(invocationOperation, invocationSyntax, match.EnumType, out replacement),
            FastEnumMethodKind.GetNames => TryCreateGetNamesReplacement(invocationOperation, invocationSyntax, match.EnumType, compilation, out replacement),
            FastEnumMethodKind.GetValues => TryCreateGetValuesReplacement(invocationOperation, invocationSyntax, match.EnumType, compilation, out replacement),
            FastEnumMethodKind.GetName => TryCreateGetNameReplacement(invocationOperation, invocationSyntax, match.EnumType, out replacement),
            FastEnumMethodKind.IsDefined => TryCreateIsDefinedReplacement(invocationOperation, invocationSyntax, match.EnumType, out replacement),
            FastEnumMethodKind.ToString => TryCreateToStringReplacement(invocationSyntax, out replacement),
            _ => TryReturnNoReplacement(out replacement),
        };

        if (created)
        {
            requiredNamespace = FastEnumAnalyzerCommon.GetGeneratedNamespace(compilation, fastEnumAttribute, match.EnumType);
        }

        return created;
    }

    private static bool TryCreateParseReplacement(IInvocationOperation invocationOperation, InvocationExpressionSyntax invocationSyntax, INamedTypeSymbol enumType, out InvocationExpressionSyntax replacement)
    {
        if (!TryGetArgumentsWithoutTypeOf(invocationOperation, invocationSyntax, out var arguments, out _))
            return TryReturnNoReplacement(out replacement);

        if (arguments.Count == 1)
        {
            arguments = arguments.Add(CreateNamedBooleanArgument("ignoreCase", value: false));
        }
        else if (arguments.Count != 2)
        {
            return TryReturnNoReplacement(out replacement);
        }

        replacement = CreateStaticInvocation(enumType, nameof(Enum.Parse), arguments);
        return true;
    }

    private static bool TryCreateTryParseReplacement(IInvocationOperation invocationOperation, InvocationExpressionSyntax invocationSyntax, INamedTypeSymbol enumType, out InvocationExpressionSyntax replacement)
    {
        if (!TryGetArgumentsWithoutTypeOf(invocationOperation, invocationSyntax, out var arguments, out var operationArguments))
            return TryReturnNoReplacement(out replacement);

        if (operationArguments.Length is < 2 or > 3)
            return TryReturnNoReplacement(out replacement);

        if (operationArguments[^1].Parameter?.RefKind != RefKind.Out)
            return TryReturnNoReplacement(out replacement);

        if (!CanUseOutArgument(operationArguments[^1].Value, enumType))
            return TryReturnNoReplacement(out replacement);

        if (arguments.Count == 2)
        {
            arguments = arguments.Insert(1, CreateNamedBooleanArgument("ignoreCase", value: false));
        }
        else if (arguments.Count != 3)
        {
            return TryReturnNoReplacement(out replacement);
        }

        replacement = CreateStaticInvocation(enumType, nameof(Enum.TryParse), arguments);
        return true;
    }

    private static bool TryCreateGetNamesReplacement(IInvocationOperation invocationOperation, InvocationExpressionSyntax invocationSyntax, INamedTypeSymbol enumType, Compilation compilation, out InvocationExpressionSyntax replacement)
    {
        if (!TryGetArgumentsWithoutTypeOf(invocationOperation, invocationSyntax, out var arguments, out var operationArguments) || arguments.Count != 0 || operationArguments.Length != 0)
            return TryReturnNoReplacement(out replacement);

        // The generated member returns ReadOnlySpan<string> rather than string[].
        if (!CanUseSpanResult(invocationOperation, compilation.GetSpecialType(SpecialType.System_String), compilation))
            return TryReturnNoReplacement(out replacement);

        arguments = arguments.Add(CreateNamedBooleanArgument("useMetadata", value: false));
        replacement = CreateStaticInvocation(enumType, nameof(Enum.GetNames), arguments);
        return true;
    }

    private static bool TryCreateGetValuesReplacement(IInvocationOperation invocationOperation, InvocationExpressionSyntax invocationSyntax, INamedTypeSymbol enumType, Compilation compilation, out InvocationExpressionSyntax replacement)
    {
        if (!TryGetArgumentsWithoutTypeOf(invocationOperation, invocationSyntax, out var arguments, out var operationArguments) || arguments.Count != 0 || operationArguments.Length != 0)
            return TryReturnNoReplacement(out replacement);

        // The generated member returns ReadOnlySpan<TEnum> rather than TEnum[] or Array.
        if (!CanUseSpanResult(invocationOperation, enumType, compilation))
            return TryReturnNoReplacement(out replacement);

        replacement = CreateStaticInvocation(enumType, nameof(Enum.GetValues), arguments);
        return true;
    }

    /// <summary>
    /// Determines whether replacing the invocation with one returning <c>ReadOnlySpan&lt;T&gt;</c> still
    /// compiles. A span cannot be assigned to an array, enumerated with LINQ, or captured in a lambda,
    /// so anything not provably compatible is left alone.
    /// </summary>
    private static bool CanUseSpanResult(IInvocationOperation invocationOperation, ITypeSymbol elementType, Compilation compilation)
    {
        var spanTypeDefinition = compilation.GetTypeByMetadataName("System.ReadOnlySpan`1");
        if (spanTypeDefinition is null)
            return false;

        var spanType = spanTypeDefinition.Construct(elementType);
        var parent = invocationOperation.Parent;

        // foreach binds against the span's own pattern-based enumerator, so the conversion the compiler
        // inserted for the original array is irrelevant here.
        if (parent is IForEachLoopOperation || parent is IConversionOperation { Parent: IForEachLoopOperation })
            return true;

        // The compiler inserts a conversion around the original array-returning call.
        if (parent is IConversionOperation { IsImplicit: true } conversion)
        {
            if (conversion.Type is null || conversion.Type.TypeKind == TypeKind.Error)
                return false;

            if (!IsImplicitlyConvertible(spanType, conversion.Type, compilation))
                return false;

            parent = conversion.Parent;
        }

        return parent switch
        {
            null or IExpressionStatementOperation => true,

            // `_ = ...` accepts any type.
            ISimpleAssignmentOperation { Target: IDiscardOperation } => true,
            ISimpleAssignmentOperation { Target.Type: { } targetType } => IsImplicitlyConvertible(spanType, targetType, compilation),

            IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator } =>
                IsVarDeclaration(declarator) || IsImplicitlyConvertible(spanType, declarator.Symbol.Type, compilation),

            IArgumentOperation { Parameter.Type: { } parameterType } => IsImplicitlyConvertible(spanType, parameterType, compilation),

            // Member access such as `.Length` is fine when the span declares the same member; anything
            // else (LINQ, ToArray on an array, ...) is not guaranteed to exist on a span.
            IPropertyReferenceOperation propertyReference => !spanType.GetMembers(propertyReference.Property.Name).IsEmpty,
            IArrayElementReferenceOperation => true,

            _ => false,
        };
    }

    private static bool IsVarDeclaration(IVariableDeclaratorOperation declarator)
    {
        return declarator.Syntax.Parent is VariableDeclarationSyntax { Type.IsVar: true };
    }

    private static bool IsImplicitlyConvertible(ITypeSymbol source, ITypeSymbol destination, Compilation compilation)
    {
        if (destination.TypeKind == TypeKind.Error)
            return false;

        var conversion = compilation.ClassifyCommonConversion(source, destination);
        return conversion is { Exists: true, IsImplicit: true };
    }

    private static bool TryCreateGetNameReplacement(IInvocationOperation invocationOperation, InvocationExpressionSyntax invocationSyntax, INamedTypeSymbol enumType, out InvocationExpressionSyntax replacement)
    {
        if (!TryGetArgumentsWithoutTypeOf(invocationOperation, invocationSyntax, out var arguments, out var operationArguments) || arguments.Count != 1 || operationArguments.Length != 1)
            return TryReturnNoReplacement(out replacement);

        if (!TryGetValueExpression(arguments[0].Expression, operationArguments[0].Value, enumType, out var valueExpression))
            return TryReturnNoReplacement(out replacement);

        replacement = InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                ParenthesizeIfNeeded(valueExpression),
                IdentifierName("GetName")));
        return true;
    }

    private static bool TryCreateIsDefinedReplacement(IInvocationOperation invocationOperation, InvocationExpressionSyntax invocationSyntax, INamedTypeSymbol enumType, out InvocationExpressionSyntax replacement)
    {
        if (!TryGetArgumentsWithoutTypeOf(invocationOperation, invocationSyntax, out var arguments, out var operationArguments) || arguments.Count != 1 || operationArguments.Length != 1)
            return TryReturnNoReplacement(out replacement);

        // Enum.IsDefined(Type, object) also accepts a member name or a boxed underlying value; neither can
        // be cast to the enum type, so only rewrite when the argument already is the enum.
        if (!TryGetValueExpression(arguments[0].Expression, operationArguments[0].Value, enumType, out var valueExpression))
            return TryReturnNoReplacement(out replacement);

        replacement = CreateStaticInvocation(enumType, "IsDefinedFast", [Argument(valueExpression)]);
        return true;
    }

    private static bool TryCreateToStringReplacement(InvocationExpressionSyntax invocationSyntax, out InvocationExpressionSyntax replacement)
    {
        if (invocationSyntax is not { ArgumentList.Arguments.Count: 0, Expression: MemberAccessExpressionSyntax memberAccessExpression })
            return TryReturnNoReplacement(out replacement);

        replacement = invocationSyntax.WithExpression(memberAccessExpression.WithName(IdentifierName("ToStringFast")));
        return true;
    }

    private static bool TryGetArgumentsWithoutTypeOf(IInvocationOperation invocationOperation, InvocationExpressionSyntax invocationSyntax, out SeparatedSyntaxList<ArgumentSyntax> syntaxArguments, out ImmutableArray<IArgumentOperation> operationArguments)
    {
        syntaxArguments = invocationSyntax.ArgumentList.Arguments;
        operationArguments = invocationOperation.Arguments;
        if (FastEnumAnalyzerCommon.HasTypeOfFirstArgument(invocationOperation))
        {
            if (syntaxArguments.Count == 0 || operationArguments.Length == 0)
                return false;

            syntaxArguments = syntaxArguments.RemoveAt(0);
            operationArguments = [.. operationArguments.Skip(1)];
        }

        return true;
    }

    private static bool CanUseOutArgument(IOperation argumentValue, INamedTypeSymbol enumType)
    {
        if (argumentValue is IDiscardOperation)
            return true;

        if (argumentValue is IDeclarationExpressionOperation declarationExpressionOperation)
        {
            if (declarationExpressionOperation.Type is null)
                return true;

            return SymbolEqualityComparer.Default.Equals(declarationExpressionOperation.Type, enumType);
        }

        return SymbolEqualityComparer.Default.Equals(argumentValue.Type, enumType);
    }

    private static InvocationExpressionSyntax CreateStaticInvocation(INamedTypeSymbol enumType, string methodName, SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        var enumTypeSyntax = ParseName(enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        return InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                enumTypeSyntax,
                IdentifierName(methodName)),
            ArgumentList(arguments));
    }

    private static ArgumentSyntax CreateNamedBooleanArgument(string name, bool value)
    {
        return Argument(LiteralExpression(value ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression))
            .WithNameColon(NameColon(IdentifierName(name)));
    }

    private static bool TryGetValueExpression(ExpressionSyntax expression, IOperation valueOperation, INamedTypeSymbol enumType, out ExpressionSyntax valueExpression)
    {
        // Look through the boxing conversion the compiler inserts for the `object` parameter.
        var operation = valueOperation;
        while (operation is IConversionOperation { IsImplicit: true } conversion)
        {
            operation = conversion.Operand;
        }

        if (!SymbolEqualityComparer.Default.Equals(operation.Type, enumType))
        {
            valueExpression = null!;
            return false;
        }

        valueExpression = expression;
        return true;
    }

    /// <summary>
    /// <c>Enum.GetName(a | b)</c> must not become <c>a | b.GetName()</c>, which binds as <c>a | (b.GetName())</c>.
    /// </summary>
    private static ExpressionSyntax ParenthesizeIfNeeded(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax or QualifiedNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax
                or ParenthesizedExpressionSyntax or ElementAccessExpressionSyntax or LiteralExpressionSyntax
                or ThisExpressionSyntax or BaseExpressionSyntax => expression,
            _ => ParenthesizedExpression(expression),
        };
    }

    private static bool TryReturnNoReplacement(out InvocationExpressionSyntax replacement)
    {
        replacement = null!;
        return false;
    }
}
