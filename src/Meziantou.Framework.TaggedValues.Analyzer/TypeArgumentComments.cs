using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meziantou.Framework.TaggedValues.Analyzer;

/// <summary>
/// Reads the <c>/* ValueTag=... */</c> comments written in the type arguments of a generic type,
/// e.g. <c>new Dictionary&lt;/* ValueTag=OrderId */ Guid, /* ValueTag=ProjectId */ Guid&gt;()</c>.
/// </summary>
/// <remarks>
/// The type argument of a collection or of a wrapper (<c>Nullable&lt;T&gt;</c>, <c>Task&lt;T&gt;</c>, ...) tags its elements or its value.
/// The type arguments of a dictionary or of a <c>KeyValuePair&lt;TKey, TValue&gt;</c> tag its keys and its values.
/// </remarks>
internal static class TypeArgumentComments
{
    /// <summary>
    /// Returns the comments of the type argument at <paramref name="index"/>: the comments after the <c>&lt;</c> or the <c>,</c> that precedes it,
    /// and the comments around it.
    /// </summary>
    public static IEnumerable<SyntaxTrivia> GetTrivia(TypeArgumentListSyntax typeArgumentList, int index)
    {
        var separator = index is 0 ? typeArgumentList.LessThanToken : typeArgumentList.Arguments.GetSeparator(index - 1);
        var argument = typeArgumentList.Arguments[index];
        return separator.TrailingTrivia
            .Concat(argument.GetFirstToken().LeadingTrivia)
            .Concat(argument.GetLastToken().TrailingTrivia)
            .Where(trivia => trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            .Distinct();
    }

    /// <summary>
    /// Returns every comment that belongs to a type argument of <paramref name="type"/>, including nested type arguments.
    /// </summary>
    public static HashSet<SyntaxTrivia> GetAllTrivia(SyntaxNode? type)
    {
        var result = new HashSet<SyntaxTrivia>();
        if (type is null)
            return result;

        foreach (var typeArgumentList in type.DescendantNodesAndSelf().OfType<TypeArgumentListSyntax>())
        {
            for (var i = 0; i < typeArgumentList.Arguments.Count; i++)
            {
                result.UnionWith(GetTrivia(typeArgumentList, i));
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the tags declared by the comments in the type arguments of <paramref name="typeSyntax"/>.
    /// </summary>
    public static TagInfo GetTags(TypeSyntax typeSyntax, ITypeSymbol? type, KnownTypes knownTypes)
    {
        while (true)
        {
            switch (typeSyntax)
            {
                case NullableTypeSyntax nullableType:
                    typeSyntax = nullableType.ElementType;
                    if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableSymbol)
                    {
                        type = nullableSymbol.TypeArguments[0];
                    }

                    continue;

                case QualifiedNameSyntax qualifiedName:
                    typeSyntax = qualifiedName.Right;
                    continue;

                case AliasQualifiedNameSyntax aliasQualifiedName:
                    typeSyntax = aliasQualifiedName.Name;
                    continue;
            }

            break;
        }

        if (typeSyntax is not GenericNameSyntax genericName || type is not INamedTypeSymbol namedType || genericName.TypeArgumentList.Arguments.Count != namedType.TypeArguments.Length)
            return TagInfo.None;

        if (IsElementShaped(namedType, knownTypes))
            return GetArgumentTags(genericName.TypeArgumentList, 0, namedType.TypeArguments[0], knownTypes);

        if (IsKeyValueShaped(namedType, knownTypes))
            return TagInfo.KeyValue(GetArgumentTags(genericName.TypeArgumentList, 0, namedType.TypeArguments[0], knownTypes), GetArgumentTags(genericName.TypeArgumentList, 1, namedType.TypeArguments[1], knownTypes));

        return TagInfo.None;
    }

    private static TagInfo GetArgumentTags(TypeArgumentListSyntax typeArgumentList, int index, ITypeSymbol typeArgument, KnownTypes knownTypes)
    {
        foreach (var trivia in GetTrivia(typeArgumentList, index))
        {
            if (ValueTagComment.Parse(trivia.ToString(), out var tags) is ValueTagCommentKind.Valid && !tags.HasKeyOrValue)
                return tags;
        }

        return GetTags(typeArgumentList.Arguments[index], typeArgument, knownTypes);
    }

    /// <summary>
    /// Returns whether the only type argument of the type describes its elements or its value.
    /// </summary>
    public static bool IsElementShaped(INamedTypeSymbol type, KnownTypes knownTypes)
    {
        var definition = type.OriginalDefinition;
        return definition.TypeArguments.Length is 1 &&
            knownTypes.TryGetWrappedType(definition, out var wrappedType) &&
            SymbolEqualityComparer.Default.Equals(wrappedType, definition.TypeArguments[0]);
    }

    /// <summary>
    /// Returns whether the two type arguments of the type describe its keys and its values.
    /// </summary>
    public static bool IsKeyValueShaped(INamedTypeSymbol type, KnownTypes knownTypes)
    {
        var definition = type.OriginalDefinition;
        if (definition.TypeArguments.Length is not 2)
            return false;

        if (knownTypes.IsKeyValuePair(definition))
            return true;

        return knownTypes.TryGetWrappedType(definition, out var wrappedType) &&
            wrappedType is INamedTypeSymbol keyValuePair &&
            knownTypes.IsKeyValuePair(keyValuePair) &&
            SymbolEqualityComparer.Default.Equals(keyValuePair.TypeArguments[0], definition.TypeArguments[0]) &&
            SymbolEqualityComparer.Default.Equals(keyValuePair.TypeArguments[1], definition.TypeArguments[1]);
    }

    /// <summary>
    /// Validates a comment that belongs to a type argument.
    /// </summary>
    /// <returns><see langword="false"/> when the comment does not belong to a type argument.</returns>
    public static bool TryValidate(SyntaxTrivia trivia, TagInfo tags, SemanticModel semanticModel, KnownTypes knownTypes, CancellationToken cancellationToken, out string? errorMessage)
    {
        errorMessage = null;
        GenericNameSyntax? genericName = null;
        for (var node = trivia.Token.Parent; node is not null && genericName is null; node = node.Parent)
        {
            if (node is StatementSyntax or MemberDeclarationSyntax)
                return false;

            if (node is TypeArgumentListSyntax { Parent: GenericNameSyntax parent } typeArgumentList)
            {
                for (var i = 0; i < typeArgumentList.Arguments.Count; i++)
                {
                    if (GetTrivia(typeArgumentList, i).Contains(trivia))
                    {
                        genericName = parent;
                        break;
                    }
                }
            }
        }

        if (genericName is null)
            return false;

        if (tags.HasKeyOrValue)
        {
            errorMessage = "Key and Value cannot be used in a type argument, whose position already selects the key or the value; use /* ValueTag=Tag */ instead";
            return true;
        }

        while (true)
        {
            var type = semanticModel.GetSymbolInfo(genericName, cancellationToken).Symbol as INamedTypeSymbol ?? semanticModel.GetTypeInfo(genericName, cancellationToken).Type as INamedTypeSymbol;
            if (type is null || !(IsElementShaped(type, knownTypes) || IsKeyValueShaped(type, knownTypes)))
            {
                errorMessage = "The type arguments of '" + (type?.ToDisplayString() ?? genericName.ToString()) + "' cannot be tagged; only the type arguments of collections, dictionaries, and wrappers such as Nullable<T>, Task<T>, or Lazy<T> can be tagged";
                return true;
            }

            SyntaxNode typeNode = genericName;
            while (typeNode.Parent is QualifiedNameSyntax or AliasQualifiedNameSyntax or NullableTypeSyntax)
            {
                typeNode = typeNode.Parent;
            }

            if (typeNode.Parent is TypeArgumentListSyntax { Parent: GenericNameSyntax outerGenericName })
            {
                genericName = outerGenericName;
                continue;
            }

            var isSupported = typeNode.Parent switch
            {
                ObjectCreationExpressionSyntax objectCreation => objectCreation.Type == typeNode,
                VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax or ForStatementSyntax or UsingStatementSyntax or FixedStatementSyntax } variableDeclaration => variableDeclaration.Type == typeNode,
                ForEachStatementSyntax forEach => forEach.Type == typeNode,
                DeclarationExpressionSyntax declarationExpression => declarationExpression.Type == typeNode,
                DeclarationPatternSyntax declarationPattern => declarationPattern.Type == typeNode,
                _ => false,
            };

            if (!isSupported)
            {
                errorMessage = "A value tag comment in a type argument only applies to the type of a local variable or of a new expression; use the [ValueTag] attribute on fields, properties, parameters, and return values";
            }

            return true;
        }
    }
}
