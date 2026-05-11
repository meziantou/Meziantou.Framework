using System.Collections.Immutable;
using System.Linq;
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

            if (!TryCreateReplacementInvocation(invocationOperation, invocationSyntax, out _))
                continue;

            var title = diagnostic.Id switch
            {
                UseFastEnumParseDiagnosticId => "Use FastEnum Parse",
                UseFastEnumTryParseDiagnosticId => "Use FastEnum TryParse",
                UseFastEnumGetNamesDiagnosticId => "Use FastEnum GetNames",
                UseFastEnumGetValuesDiagnosticId => "Use FastEnum GetValues",
                UseFastEnumGetNameDiagnosticId => "Use FastEnum GetName",
                UseFastEnumIsDefinedDiagnosticId => "Use FastEnum IsDefined",
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

        if (!TryCreateReplacementInvocation(invocationOperation, invocationSyntax, out var replacement))
            return document;

        replacement = replacement.WithTriviaFrom(invocationSyntax).WithAdditionalAnnotations(Formatter.Annotation);
        var newRoot = root.ReplaceNode(invocationSyntax, replacement);
        return document.WithSyntaxRoot(newRoot);
    }

    private static bool TryCreateReplacementInvocation(IInvocationOperation invocationOperation, InvocationExpressionSyntax invocationSyntax, out InvocationExpressionSyntax replacement)
    {
        if (invocationOperation.SemanticModel is null)
        {
            replacement = null!;
            return false;
        }

        var compilation = invocationOperation.SemanticModel.Compilation;
        var fastEnumAttribute = compilation.GetTypeByMetadataName(FastEnumAnalyzerCommon.FastEnumAttributeMetadataName);
        if (fastEnumAttribute is null)
        {
            replacement = null!;
            return false;
        }

        var enumType = compilation.GetSpecialType(SpecialType.System_Enum);
        var fastEnumTypes = FastEnumAnalyzerCommon.GetFastEnumTypes(compilation, fastEnumAttribute);
        if (!FastEnumAnalyzerCommon.TryGetFastEnumInvocationMatch(invocationOperation, enumType, fastEnumTypes, out var match))
        {
            replacement = null!;
            return false;
        }

        return match.MethodKind switch
        {
            FastEnumMethodKind.Parse => TryCreateParseReplacement(invocationOperation, invocationSyntax, match.EnumType, out replacement),
            FastEnumMethodKind.TryParse => TryCreateTryParseReplacement(invocationOperation, invocationSyntax, match.EnumType, out replacement),
            FastEnumMethodKind.GetNames => TryCreateGetNamesReplacement(invocationOperation, invocationSyntax, match.EnumType, out replacement),
            FastEnumMethodKind.GetValues => TryCreateGetValuesReplacement(invocationOperation, invocationSyntax, match.EnumType, out replacement),
            FastEnumMethodKind.GetName => TryCreateGetNameReplacement(invocationOperation, invocationSyntax, match.EnumType, out replacement),
            FastEnumMethodKind.IsDefined => TryCreateIsDefinedReplacement(invocationOperation, invocationSyntax, match.EnumType, out replacement),
            FastEnumMethodKind.ToString => TryCreateToStringReplacement(invocationSyntax, out replacement),
            _ => TryReturnNoReplacement(out replacement),
        };
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

    private static bool TryCreateGetNamesReplacement(IInvocationOperation invocationOperation, InvocationExpressionSyntax invocationSyntax, INamedTypeSymbol enumType, out InvocationExpressionSyntax replacement)
    {
        if (!TryGetArgumentsWithoutTypeOf(invocationOperation, invocationSyntax, out var arguments, out var operationArguments) || arguments.Count != 0 || operationArguments.Length != 0)
            return TryReturnNoReplacement(out replacement);

        arguments = arguments.Add(CreateNamedBooleanArgument("useMetadata", value: false));
        replacement = CreateStaticInvocation(enumType, nameof(Enum.GetNames), arguments);
        return true;
    }

    private static bool TryCreateGetValuesReplacement(IInvocationOperation invocationOperation, InvocationExpressionSyntax invocationSyntax, INamedTypeSymbol enumType, out InvocationExpressionSyntax replacement)
    {
        if (!TryGetArgumentsWithoutTypeOf(invocationOperation, invocationSyntax, out var arguments, out var operationArguments) || arguments.Count != 0 || operationArguments.Length != 0)
            return TryReturnNoReplacement(out replacement);

        replacement = CreateStaticInvocation(enumType, nameof(Enum.GetValues), arguments);
        return true;
    }

    private static bool TryCreateGetNameReplacement(IInvocationOperation invocationOperation, InvocationExpressionSyntax invocationSyntax, INamedTypeSymbol enumType, out InvocationExpressionSyntax replacement)
    {
        if (!TryGetArgumentsWithoutTypeOf(invocationOperation, invocationSyntax, out var arguments, out var operationArguments) || arguments.Count != 1 || operationArguments.Length != 1)
            return TryReturnNoReplacement(out replacement);

        var valueExpression = GetValueExpression(arguments[0].Expression, operationArguments[0].Value, enumType);
        replacement = InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                valueExpression,
                IdentifierName("GetName")));
        return true;
    }

    private static bool TryCreateIsDefinedReplacement(IInvocationOperation invocationOperation, InvocationExpressionSyntax invocationSyntax, INamedTypeSymbol enumType, out InvocationExpressionSyntax replacement)
    {
        if (!TryGetArgumentsWithoutTypeOf(invocationOperation, invocationSyntax, out var arguments, out var operationArguments) || arguments.Count != 1 || operationArguments.Length != 1)
            return TryReturnNoReplacement(out replacement);

        var valueExpression = GetValueExpression(arguments[0].Expression, operationArguments[0].Value, enumType);
        replacement = CreateStaticInvocation(enumType, nameof(Enum.IsDefined), [Argument(valueExpression)]);
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

    private static ExpressionSyntax GetValueExpression(ExpressionSyntax expression, IOperation valueOperation, INamedTypeSymbol enumType)
    {
        if (SymbolEqualityComparer.Default.Equals(valueOperation.Type, enumType))
            return expression;

        var enumTypeSyntax = ParseTypeName(enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        return ParenthesizedExpression(CastExpression(enumTypeSyntax, ParenthesizedExpression(expression)));
    }

    private static bool TryReturnNoReplacement(out InvocationExpressionSyntax replacement)
    {
        replacement = null!;
        return false;
    }
}
