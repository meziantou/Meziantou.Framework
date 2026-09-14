using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AssertionReturnValueCodeFixProvider))]
public sealed class AssertionReturnValueCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [RuleIdentifiers.UseAssertionReturnValueDiagnosticId];

    // Each fix declares a local, so fixes in the same document must be applied one after the other to get unique names
    public override FixAllProvider GetFixAllProvider() => FixAllProvider.Create(FixAllAsync);

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true).FirstAncestorOrSelf<InvocationExpressionSyntax>() is not { Parent: ExpressionStatementSyntax } invocationExpression)
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Use the value returned by the assertion",
                    createChangedDocument: ct => ApplyFixAsync(context.Document, invocationExpression, ct),
                    equivalenceKey: GetType().FullName),
                diagnostic);
        }
    }

    private static async Task<Document?> FixAllAsync(FixAllContext context, Document document, ImmutableArray<Diagnostic> diagnostics)
    {
        var root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        var annotations = new List<SyntaxAnnotation>();
        var nodes = new Dictionary<SyntaxNode, SyntaxAnnotation>();
        foreach (var diagnostic in diagnostics.OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start))
        {
            if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true).FirstAncestorOrSelf<InvocationExpressionSyntax>() is not { } invocationExpression)
                continue;

            if (nodes.ContainsKey(invocationExpression))
                continue;

            var annotation = new SyntaxAnnotation();
            nodes.Add(invocationExpression, annotation);
            annotations.Add(annotation);
        }

        document = document.WithSyntaxRoot(root.ReplaceNodes(nodes.Keys, (original, rewritten) => rewritten.WithAdditionalAnnotations(nodes[original])));
        foreach (var annotation in annotations)
        {
            root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root?.GetAnnotatedNodes(annotation).OfType<InvocationExpressionSyntax>().FirstOrDefault() is not { } invocationExpression)
                continue;

            document = await ApplyFixAsync(document, invocationExpression, context.CancellationToken).ConfigureAwait(false);
        }

        return document;
    }

    private static async Task<Document> ApplyFixAsync(Document document, InvocationExpressionSyntax invocationExpression, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
            return document;

        var assertType = semanticModel.Compilation.GetTypeByMetadataName(AssertionsAnalyzerHelpers.AssertMetadataName);
        if (assertType is null)
            return document;

        if (!AssertionCodeFixHelpers.TryGetInvocationOperation(semanticModel, invocationExpression, cancellationToken, out var invocationOperation))
            return document;

        if (!AssertionReturnValueAnalyzerCommon.TryGetMatch(invocationOperation, assertType, out var match))
            return document;

        if (match.AssertionStatement.Syntax is not ExpressionStatementSyntax statement)
            return document;

        var name = GetUniqueName(semanticModel, statement, GetBaseName(invocationOperation, match.Kind), cancellationToken);
        var nodesToReplace = new List<SyntaxNode> { statement };
        foreach (var rederivation in match.Rederivations)
        {
            // Replace the parentheses around the re-derivation too, such as in ((T)value).Property
            var node = rederivation.Syntax;
            while (node.Parent is ParenthesizedExpressionSyntax parenthesizedExpression)
            {
                node = parenthesizedExpression;
            }

            nodesToReplace.Add(node);
        }

        var newRoot = root.ReplaceNodes(nodesToReplace, (original, rewritten) =>
        {
            if (original == statement)
                return CreateDeclaration(statement, name);

            return SyntaxFactory.IdentifierName(name).WithTriviaFrom(original);
        });

        return document.WithSyntaxRoot(newRoot);
    }

    private static LocalDeclarationStatementSyntax CreateDeclaration(ExpressionStatementSyntax statement, string name)
    {
        var declarator = SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(SyntaxFactory.TriviaList(), name, SyntaxFactory.TriviaList(SyntaxFactory.Space)))
            .WithInitializer(SyntaxFactory.EqualsValueClause(
                SyntaxFactory.Token(SyntaxFactory.TriviaList(), SyntaxKind.EqualsToken, SyntaxFactory.TriviaList(SyntaxFactory.Space)),
                statement.Expression.WithoutTrivia()));

        var type = SyntaxFactory.IdentifierName(SyntaxFactory.Identifier(SyntaxFactory.TriviaList(), "var", SyntaxFactory.TriviaList(SyntaxFactory.Space)));

        return SyntaxFactory.LocalDeclarationStatement(SyntaxFactory.VariableDeclaration(type, [declarator]))
            .WithSemicolonToken(statement.SemicolonToken)
            .WithLeadingTrivia(statement.GetLeadingTrivia())
            .WithAdditionalAnnotations(Formatter.Annotation);
    }

    private static string GetBaseName(IInvocationOperation invocationOperation, AssertionReturnValueAnalyzerCommon.AssertionKind kind)
    {
        return kind switch
        {
            AssertionReturnValueAnalyzerCommon.AssertionKind.Single => "item",
            AssertionReturnValueAnalyzerCommon.AssertionKind.Type => GetTypeBasedName(invocationOperation.TargetMethod.TypeArguments[0]) ?? "value",
            _ => "value",
        };

        static string? GetTypeBasedName(ITypeSymbol type)
        {
            // Types with a keyword alias, such as int or string, do not give a meaningful name
            if (type is not INamedTypeSymbol namedType || namedType.Name.Length == 0 || HasKeywordAlias(namedType))
                return null;

            var name = namedType.Name;

            // IDisposable => disposable
            if (namedType.TypeKind is TypeKind.Interface && name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]))
            {
                name = name.Substring(1);
            }

            name = char.ToLowerInvariant(name[0]) + name.Substring(1);
            if (!SyntaxFacts.IsValidIdentifier(name) || SyntaxFacts.GetKeywordKind(name) is not SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(name) is not SyntaxKind.None)
                return null;

            return name;
        }

        static bool HasKeywordAlias(INamedTypeSymbol type)
        {
            return type.SpecialType is SpecialType.System_Object or SpecialType.System_Boolean or SpecialType.System_Char or
                SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16 or
                SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or
                SpecialType.System_Decimal or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_String or
                SpecialType.System_IntPtr or SpecialType.System_UIntPtr;
        }
    }

    private static string GetUniqueName(SemanticModel semanticModel, StatementSyntax statement, string baseName, CancellationToken cancellationToken)
    {
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var symbol in semanticModel.LookupSymbols(statement.SpanStart))
        {
            usedNames.Add(symbol.Name);
        }

        // Locals declared later in the member, including in nested scopes, would conflict with the new local
        SyntaxNode member = statement.FirstAncestorOrSelf<MemberDeclarationSyntax>() is { } memberDeclaration and not GlobalStatementSyntax
            ? memberDeclaration
            : statement.SyntaxTree.GetRoot(cancellationToken);
        foreach (var token in member.DescendantTokens())
        {
            if (token.IsKind(SyntaxKind.IdentifierToken))
            {
                usedNames.Add(token.ValueText);
            }
        }

        var name = baseName;
        for (var i = 2; usedNames.Contains(name); i++)
        {
            name = baseName + i.ToString(CultureInfo.InvariantCulture);
        }

        return name;
    }
}
