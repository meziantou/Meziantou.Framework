using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.TaggedValues.Analyzer;

/// <summary>
/// Computes the tags of symbols and expressions for one compilation.
/// </summary>
internal sealed class TagResolver
{
    private const int MaxDepth = 32;

    private readonly Compilation _compilation;
    private readonly AnalyzerConfigOptionsProvider _optionsProvider;
    private readonly ConcurrentDictionary<ISymbol, TagInfo> _declaredTags = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<ISymbol, TagInfo> _localTags = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<SyntaxTree, bool> _conventionsEnabled = new();
    private readonly Lazy<Dictionary<(ITypeSymbol Type, string MemberName), TagInfo>> _externalTags;

    public TagResolver(Compilation compilation, AnalyzerConfigOptionsProvider optionsProvider)
    {
        _compilation = compilation;
        _optionsProvider = optionsProvider;
        _externalTags = new(LoadExternalTags);
    }

    public static bool IsValueTagAttribute(INamedTypeSymbol? type)
    {
        return type is { Name: "ValueTagAttribute", ContainingType: null, ContainingNamespace: { Name: "TaggedValues", ContainingNamespace: { Name: "Framework", ContainingNamespace: { Name: "Meziantou", ContainingNamespace.IsGlobalNamespace: true } } } };
    }

    public static bool HasValueTagAttributeType(Compilation compilation)
    {
        return !compilation.GetTypesByMetadataName("Meziantou.Framework.TaggedValues.ValueTagAttribute").IsEmpty;
    }

    public bool AreConventionsEnabled(SyntaxTree tree)
    {
        return _conventionsEnabled.GetOrAdd(tree, tree =>
            _optionsProvider.GetOptions(tree).TryGetValue(ValueTagDiagnostics.InferTagsFromNamesOption, out var value) &&
            bool.TryParse(value, out var enabled) &&
            enabled);
    }

    /// <summary>
    /// Reads a <c>[ValueTag]</c> attribute applied to a member. Returns <see cref="TagInfo.None"/> for the assembly form.
    /// </summary>
    public static TagInfo ReadMemberAttribute(AttributeData attribute)
    {
        if (!IsValueTagAttribute(attribute.AttributeClass) || attribute.ConstructorArguments.Length is not 1)
            return TagInfo.None;

        return ReadAttribute(attribute, attribute.ConstructorArguments[0]);
    }

    public static TagInfo GetExplicitTags(ImmutableArray<AttributeData> attributes)
    {
        var result = TagInfo.None;
        foreach (var attribute in attributes)
        {
            result = TagInfo.Union(result, ReadMemberAttribute(attribute));
        }

        return result;
    }

    /// <summary>
    /// Returns the tags written on the symbol itself: the attributes of a field, a property or a parameter, or the return value attributes of a method.
    /// </summary>
    public static TagInfo GetOwnExplicitTags(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol method => GetExplicitTags(method.GetReturnTypeAttributes()),
            IFieldSymbol or IPropertySymbol or IParameterSymbol => GetExplicitTags(symbol.GetAttributes()),
            _ => TagInfo.None,
        };
    }

    /// <summary>
    /// Returns the tags of a field, a property, a parameter, or the return value of a method, ignoring where it is accessed from.
    /// </summary>
    public TagInfo GetDeclaredTags(ISymbol symbol)
    {
        symbol = symbol.OriginalDefinition;
        return _declaredTags.GetOrAdd(symbol, symbol =>
        {
            // The implicit 'value' parameter of a setter has the tags of its property
            if (symbol is IParameterSymbol { ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.PropertySet, AssociatedSymbol: IPropertySymbol property } setter } parameter &&
                parameter.Ordinal == setter.Parameters.Length - 1)
            {
                return GetDeclaredTags(property);
            }

            var tags = GetExplicitAndInheritedTags(symbol);
            if (!tags.IsEmpty)
                return tags;

            if (symbol is IPropertySymbol recordProperty)
            {
                tags = GetRecordPrimaryConstructorParameterTags(recordProperty);
                if (!tags.IsEmpty)
                    return tags;
            }

            return GetConventionTags(symbol);
        });
    }

    /// <summary>
    /// Returns the tags of a field or a property accessed through a receiver of type <paramref name="receiverType"/>,
    /// or the declared tags of any other symbol.
    /// </summary>
    public TagInfo GetMemberTags(ISymbol member, ITypeSymbol? receiverType)
    {
        if (member is not (IFieldSymbol or IPropertySymbol))
            return GetDeclaredTags(member);

        var external = GetExternalTags(member, receiverType);
        if (!external.IsEmpty)
            return external;

        var declared = GetDeclaredTags(member);
        if (declared.IsEmpty || declared.IsExplicit || GetConventionName(member) is not "Id")
            return declared;

        // An inherited 'Id' is also an id of every type between the receiver and the declaring type
        var result = declared;
        for (var type = receiverType as INamedTypeSymbol; type is not null; type = type.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, member.ContainingType?.OriginalDefinition))
                break;

            if (IsConventionType(type))
            {
                result = TagInfo.Union(result, TagInfo.Create([type.Name + "Id"], isExplicit: false));
            }
        }

        return result;
    }

    public static string? GetConventionName(ISymbol symbol)
    {
        switch (symbol)
        {
            case IFieldSymbol field:
                // s_orderId and _orderId both read as OrderId
                var name = field.Name.StartsWith("s_", StringComparison.Ordinal) ? field.Name.Substring(2) : field.Name;
                name = name.TrimStart('_');
                if (name.Length is 0)
                    return null;

                return char.ToUpperInvariant(name[0]) + name.Substring(1);

            case IPropertySymbol { IsIndexer: false } property:
                return property.Name;

            case IParameterSymbol parameter:
                return parameter.Name.Length is 0 ? null : char.ToUpperInvariant(parameter.Name[0]) + parameter.Name.Substring(1);

            default:
                return null;
        }
    }

    /// <summary>
    /// Returns the tag the naming convention infers for the symbol, when conventions are enabled where it is declared.
    /// </summary>
    public TagInfo GetConventionTags(ISymbol symbol)
    {
        if (symbol.IsImplicitlyDeclared && !IsRecordPrimaryConstructorProperty(symbol))
            return TagInfo.None;

        if (symbol.DeclaringSyntaxReferences.IsEmpty || !AreConventionsEnabled(symbol.DeclaringSyntaxReferences[0].SyntaxTree))
            return TagInfo.None;

        var (type, containingType) = symbol switch
        {
            IFieldSymbol field => (field.Type, field.ContainingType),
            IPropertySymbol property => (property.Type, property.ContainingType),
            IParameterSymbol parameter => (parameter.Type, parameter.ContainingType),
            _ => default,
        };

        if (type is null || containingType is null || containingType.IsAnonymousType || IsCollectionType(type))
            return TagInfo.None;

        var name = GetConventionName(symbol);
        if (name is null)
            return TagInfo.None;

        if (name is "Id")
        {
            if (symbol is IParameterSymbol)
                return TagInfo.None;

            return TagInfo.Create([containingType.Name + "Id"], isExplicit: false);
        }

        if (name.Length > 2 && name.EndsWith("Id", StringComparison.Ordinal))
            return TagInfo.Create([name], isExplicit: false);

        return TagInfo.None;
    }

    private bool IsConventionType(INamedTypeSymbol type)
    {
        return !type.DeclaringSyntaxReferences.IsEmpty && AreConventionsEnabled(type.DeclaringSyntaxReferences[0].SyntaxTree);
    }

    private static bool IsRecordPrimaryConstructorProperty(ISymbol symbol)
    {
        return symbol is IPropertySymbol { ContainingType.IsRecord: true } property &&
            property.DeclaringSyntaxReferences.Length > 0 &&
            property.DeclaringSyntaxReferences[0].GetSyntax() is ParameterSyntax;
    }

    private static TagInfo ReadAttribute(AttributeData attribute, TypedConstant tagsArgument)
    {
        var tags = new List<string>();
        if (tagsArgument.Kind is TypedConstantKind.Array && !tagsArgument.IsNull)
        {
            foreach (var value in tagsArgument.Values)
            {
                if (value.Value is string tag)
                {
                    tags.Add(tag);
                }
            }
        }

        string? key = null;
        string? tagValue = null;
        foreach (var namedArgument in attribute.NamedArguments)
        {
            switch (namedArgument.Key)
            {
                case "Key":
                    key = namedArgument.Value.Value as string;
                    break;
                case "Value":
                    tagValue = namedArgument.Value.Value as string;
                    break;
            }
        }

        return TagInfo.Create(tags, key is null ? [] : [key], tagValue is null ? [] : [tagValue], isExplicit: true);
    }

    public static TagInfo GetExplicitAndInheritedTags(ISymbol symbol)
    {
        var tags = GetOwnExplicitTags(symbol);
        if (!tags.IsEmpty)
            return tags;

        foreach (var baseSymbol in GetOverriddenOrImplementedSymbols(symbol))
        {
            tags = TagInfo.Union(tags, GetExplicitAndInheritedTags(baseSymbol));
        }

        return tags;
    }

    /// <summary>
    /// Returns the members a field, property, method or parameter overrides or implements.
    /// </summary>
    public static IEnumerable<ISymbol> GetOverriddenOrImplementedSymbols(ISymbol symbol)
    {
        switch (symbol)
        {
            case IPropertySymbol property:
                if (property.OverriddenProperty is not null)
                    return [property.OverriddenProperty];

                return GetImplementedInterfaceMembers(property, property.ExplicitInterfaceImplementations);

            case IMethodSymbol method:
                if (method.OverriddenMethod is not null)
                    return [method.OverriddenMethod];

                return GetImplementedInterfaceMembers(method, method.ExplicitInterfaceImplementations);

            case IParameterSymbol { ContainingSymbol: IMethodSymbol containingMethod } parameter:
                return GetOverriddenOrImplementedSymbols(containingMethod)
                    .OfType<IMethodSymbol>()
                    .Where(baseMethod => parameter.Ordinal < baseMethod.Parameters.Length)
                    .Select(baseMethod => (ISymbol)baseMethod.Parameters[parameter.Ordinal])
                    .ToArray();

            default:
                return [];
        }
    }

    private static ISymbol[] GetImplementedInterfaceMembers<TSymbol>(TSymbol symbol, ImmutableArray<TSymbol> explicitImplementations)
        where TSymbol : class, ISymbol
    {
        if (!explicitImplementations.IsEmpty)
            return explicitImplementations.Cast<ISymbol>().ToArray();

        var containingType = symbol.ContainingType;
        if (containingType is null || symbol.IsStatic)
            return [];

        List<ISymbol>? result = null;
        foreach (var @interface in containingType.AllInterfaces)
        {
            foreach (var member in @interface.GetMembers(symbol.Name))
            {
                if (member is TSymbol && SymbolEqualityComparer.Default.Equals(containingType.FindImplementationForInterfaceMember(member), symbol))
                {
                    result ??= [];
                    result.Add(member);
                }
            }
        }

        return result?.ToArray() ?? [];
    }

    private static TagInfo GetRecordPrimaryConstructorParameterTags(IPropertySymbol property)
    {
        if (!property.ContainingType.IsRecord)
            return TagInfo.None;

        foreach (var constructor in property.ContainingType.InstanceConstructors)
        {
            foreach (var parameter in constructor.Parameters)
            {
                if (parameter.Name == property.Name && SymbolEqualityComparer.Default.Equals(parameter.Type, property.Type))
                {
                    var tags = GetExplicitTags(parameter.GetAttributes());
                    if (!tags.IsEmpty)
                        return tags;
                }
            }
        }

        return TagInfo.None;
    }

    private Dictionary<(ITypeSymbol Type, string MemberName), TagInfo> LoadExternalTags()
    {
        var result = new Dictionary<(ITypeSymbol Type, string MemberName), TagInfo>(ExternalTagKeyComparer.Instance);
        AddAssembly(_compilation.Assembly);
        foreach (var assembly in _compilation.SourceModule.ReferencedAssemblySymbols)
        {
            AddAssembly(assembly);
        }

        return result;

        void AddAssembly(IAssemblySymbol assembly)
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                if (TryReadExternalAttribute(attribute, out var type, out var memberName, out var tags) && !tags.IsEmpty)
                {
                    var key = (type.OriginalDefinition, memberName);
                    result[key] = result.TryGetValue(key, out var existing) ? TagInfo.Union(existing, tags) : tags;
                }
            }
        }
    }

    public static bool TryReadExternalAttribute(AttributeData attribute, out ITypeSymbol type, out string memberName, out TagInfo tags)
    {
        type = null!;
        memberName = null!;
        tags = TagInfo.None;
        if (!IsValueTagAttribute(attribute.AttributeClass) || attribute.ConstructorArguments.Length is not 3)
            return false;

        if (attribute.ConstructorArguments[0].Value is not ITypeSymbol typeValue || attribute.ConstructorArguments[1].Value is not string memberNameValue)
            return false;

        type = typeValue;
        memberName = memberNameValue;
        tags = ReadAttribute(attribute, attribute.ConstructorArguments[2]);
        return true;
    }

    private TagInfo GetExternalTags(ISymbol member, ITypeSymbol? receiverType)
    {
        var externalTags = _externalTags.Value;
        if (externalTags.Count is 0)
            return TagInfo.None;

        var result = TagInfo.None;
        var startType = receiverType as INamedTypeSymbol ?? member.ContainingType;
        for (var type = startType; type is not null; type = type.BaseType)
        {
            if (externalTags.TryGetValue((type.OriginalDefinition, member.Name), out var tags))
            {
                result = TagInfo.Union(result, tags);
            }
        }

        if (startType is not null)
        {
            foreach (var @interface in startType.AllInterfaces)
            {
                if (externalTags.TryGetValue((@interface.OriginalDefinition, member.Name), out var tags))
                {
                    result = TagInfo.Union(result, tags);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the tags of a local variable: its <c>/* ValueTag=... */</c> comment, or the tags inferred from its initializer.
    /// </summary>
    public TagInfo GetLocalTags(ILocalSymbol local, SemanticModel? semanticModel, int depth = 0)
    {
        if (_localTags.TryGetValue(local, out var cached))
            return cached;

        var tags = GetLocalCommentTags(local);
        if (tags.IsEmpty && semanticModel is not null && depth < MaxDepth)
        {
            tags = InferLocalTags(local, semanticModel, depth);
        }

        _localTags.TryAdd(local, tags);
        return tags;
    }

    public static TagInfo GetLocalCommentTags(ILocalSymbol local)
    {
        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            foreach (var trivia in GetLocalCommentTrivia(reference.GetSyntax()))
            {
                if (ValueTagComment.Parse(trivia.ToString(), out var tags) is ValueTagCommentKind.Valid)
                    return tags;
            }
        }

        // Comments in the type arguments of the declared type, e.g. Dictionary</* ValueTag=OrderId */ Guid, Guid> map
        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            var typeSyntax = GetLocalDeclarationType(reference.GetSyntax());
            if (typeSyntax is not null)
            {
                var tags = TypeArgumentComments.GetTags(typeSyntax, local.Type);
                if (!tags.IsEmpty)
                    return tags;
            }
        }

        return TagInfo.None;
    }

    private static TypeSyntax? GetLocalDeclarationType(SyntaxNode declaration)
    {
        return declaration switch
        {
            VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax variableDeclaration } => variableDeclaration.Type,
            ForEachStatementSyntax forEach => forEach.Type,
            SingleVariableDesignationSyntax { Parent: DeclarationExpressionSyntax declarationExpression } => declarationExpression.Type,
            SingleVariableDesignationSyntax { Parent: DeclarationPatternSyntax declarationPattern } => declarationPattern.Type,
            _ => null,
        };
    }

    /// <summary>
    /// Returns the comments that can tag the local declared by <paramref name="declaration"/>, the most specific first.
    /// </summary>
    /// <remarks>
    /// <c>/* ... */</c> comments are accepted anywhere between the start of the declaration and the name of the variable.
    /// <c>// ...</c> comments are only accepted on the lines before a statement that declares variables.
    /// </remarks>
    public static IEnumerable<SyntaxTrivia> GetLocalCommentTrivia(SyntaxNode declaration)
    {
        switch (declaration)
        {
            case VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax or ForStatementSyntax or UsingStatementSyntax or FixedStatementSyntax } variableDeclaration } declarator:
                var statement = variableDeclaration.Parent!;
                var typeArgumentTrivia = TypeArgumentComments.GetAllTrivia(variableDeclaration.Type);
                return GetTokenTrivia(declarator.Identifier)
                    .Concat(statement.DescendantTokens().TakeWhile(token => token.SpanStart < variableDeclaration.Variables[0].SpanStart).SelectMany(GetTokenTrivia).Where(trivia => !typeArgumentTrivia.Contains(trivia)))
                    .Concat(GetLineComments(statement.GetFirstToken()));

            case ForEachStatementSyntax forEach:
                var forEachTypeArgumentTrivia = TypeArgumentComments.GetAllTrivia(forEach.Type);
                return GetTokenTrivia(forEach.Identifier)
                    .Concat(forEach.Type.DescendantTokens().SelectMany(GetTokenTrivia).Where(trivia => !forEachTypeArgumentTrivia.Contains(trivia)))
                    .Concat(forEach.OpenParenToken.TrailingTrivia.Where(trivia => trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)))
                    .Concat(GetLineComments(forEach.GetFirstToken()));

            case SingleVariableDesignationSyntax designation:
                var result = GetTokenTrivia(designation.Identifier);
                var type = designation.Parent switch
                {
                    DeclarationExpressionSyntax declarationExpression => declarationExpression.Type,
                    DeclarationPatternSyntax declarationPattern => declarationPattern.Type,
                    _ => null,
                };

                if (type is null)
                    return result;

                var designationTypeArgumentTrivia = TypeArgumentComments.GetAllTrivia(type);
                return result.Concat(type.DescendantTokens().SelectMany(GetTokenTrivia).Where(trivia => !designationTypeArgumentTrivia.Contains(trivia)));

            default:
                return [];
        }

        static IEnumerable<SyntaxTrivia> GetTokenTrivia(SyntaxToken token)
        {
            return token.LeadingTrivia.Concat(token.TrailingTrivia).Where(trivia => trivia.IsKind(SyntaxKind.MultiLineCommentTrivia));
        }

        static IEnumerable<SyntaxTrivia> GetLineComments(SyntaxToken firstToken)
        {
            // The closest comment first
            return firstToken.LeadingTrivia.Reverse().Where(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia));
        }
    }

    private TagInfo InferLocalTags(ILocalSymbol local, SemanticModel semanticModel, int depth)
    {
        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            if (reference.SyntaxTree != semanticModel.SyntaxTree)
                continue;

            var syntax = reference.GetSyntax();
            switch (syntax)
            {
                case VariableDeclaratorSyntax declarator when semanticModel.GetOperation(declarator) is IVariableDeclaratorOperation declaratorOperation:
                    return GetTag(declaratorOperation.GetVariableInitializer()?.Value, depth + 1);

                case ForEachStatementSyntax forEach when semanticModel.GetOperation(forEach) is IForEachLoopOperation forEachOperation:
                    return GetTag(forEachOperation.Collection, depth + 1);

                case SingleVariableDesignationSyntax { Parent: DeclarationExpressionSyntax declarationExpression } when semanticModel.GetOperation(declarationExpression) is { Parent: IArgumentOperation argument }:
                    return GetExpectedArgumentTags(argument, depth + 1);

                case SingleVariableDesignationSyntax { Parent: (DeclarationPatternSyntax or VarPatternSyntax) and { } patternSyntax }:
                    for (var operation = semanticModel.GetOperation(patternSyntax); operation is not null; operation = operation.Parent)
                    {
                        switch (operation)
                        {
                            case IIsPatternOperation isPattern:
                                return GetTag(isPattern.Value, depth + 1);
                            case ISwitchExpressionOperation switchExpression:
                                return GetTag(switchExpression.Value, depth + 1);
                            case ISwitchOperation switchStatement:
                                return GetTag(switchStatement.Value, depth + 1);
                        }
                    }

                    return TagInfo.None;
            }
        }

        return TagInfo.None;
    }

    public TagInfo GetTag(IOperation? operation)
    {
        return GetTag(operation, depth: 0);
    }

    private TagInfo GetTag(IOperation? operation, int depth)
    {
        if (operation is null || depth > MaxDepth)
            return TagInfo.None;

        switch (operation)
        {
            case IConversionOperation conversion:
                if (conversion.Conversion.IsUserDefined)
                    return TagInfo.None;

                // (Guid)(object)value is the explicit way to drop the tag
                if (!conversion.IsImplicit && (IsObjectOrDynamic(conversion.Type) || IsObjectOrDynamic(conversion.Operand.Type)))
                    return TagInfo.None;

                return GetTag(conversion.Operand, depth + 1);

            case IParenthesizedOperation parenthesized:
                return GetTag(parenthesized.Operand, depth + 1);

            case IAwaitOperation awaitOperation:
                return GetTag(awaitOperation.Operation, depth + 1);

            case ISimpleAssignmentOperation assignment:
                return GetTag(assignment.Value, depth + 1);

            case IFieldReferenceOperation fieldReference:
                return GetMemberTags(fieldReference.Field, fieldReference.Instance?.Type);

            case IPropertyReferenceOperation propertyReference:
                var propertyTags = GetAnonymousTypePropertyTags(propertyReference, depth);
                if (!propertyTags.IsEmpty)
                    return propertyTags;

                propertyTags = GetMemberTags(propertyReference.Property, propertyReference.Instance?.Type);
                if (!propertyTags.IsEmpty)
                    return propertyTags;

                return GetBoundTag(propertyReference.Property.OriginalDefinition.Type, propertyReference.Property, propertyReference.Instance, propertyReference.Arguments, depth);

            case IParameterReferenceOperation parameterReference:
                var parameterTags = GetDeclaredTags(parameterReference.Parameter);
                if (!parameterTags.IsEmpty)
                    return parameterTags;

                return GetLambdaParameterTags(parameterReference, depth);

            case ILocalReferenceOperation localReference:
                return GetLocalTags(localReference.Local, localReference.SemanticModel, depth);

            case IInvocationOperation invocation:
                var returnTags = GetDeclaredTags(invocation.TargetMethod);
                if (!returnTags.IsEmpty)
                    return returnTags;

                return GetBoundTag(invocation.TargetMethod.OriginalDefinition.ReturnType, invocation.TargetMethod, invocation.Instance, invocation.Arguments, depth);

            case IObjectCreationOperation { Constructor: not null } objectCreation:
                var creationTags = GetObjectCreationTypeArgumentTags(objectCreation);
                if (!creationTags.IsEmpty)
                    return creationTags;

                return GetBoundTag(objectCreation.Constructor.OriginalDefinition.ContainingType, objectCreation.Constructor, instance: null, objectCreation.Arguments, depth);

            // The receiver of the Add calls of a collection initializer, and of the assignments of an object initializer
            case IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ImplicitReceiver } implicitReceiver:
                for (var parent = implicitReceiver.Parent; parent is not null; parent = parent.Parent)
                {
                    switch (parent)
                    {
                        // new Order { Lines = { line } } adds to Lines
                        case IMemberInitializerOperation memberInitializer when implicitReceiver.Parent != memberInitializer.InitializedMember:
                            return GetTag(memberInitializer.InitializedMember, depth + 1);

                        case IObjectCreationOperation creation:
                            return GetObjectCreationTypeArgumentTags(creation);
                    }
                }

                return TagInfo.None;

            case IArrayElementReferenceOperation arrayElementReference:
                return GetTag(arrayElementReference.ArrayReference, depth + 1);

            case IConditionalOperation { WhenFalse: not null } conditional when conditional.Syntax is ConditionalExpressionSyntax:
                return Combine([conditional.WhenTrue, conditional.WhenFalse], depth);

            case ICoalesceOperation coalesce:
                return Combine([coalesce.Value, coalesce.WhenNull], depth);

            case ICoalesceAssignmentOperation coalesceAssignment:
                return Combine([coalesceAssignment.Target, coalesceAssignment.Value], depth);

            case ISwitchExpressionOperation switchExpression:
                return Combine(switchExpression.Arms.Select(arm => arm.Value), depth);

            case IArrayCreationOperation { Initializer: not null } arrayCreation:
                return GetTag(arrayCreation.Initializer, depth + 1);

            case IArrayInitializerOperation arrayInitializer:
                return Combine(arrayInitializer.ElementValues, depth);

            case ICollectionExpressionOperation collectionExpression:
                return Combine(collectionExpression.Elements, depth);

            case ISpreadOperation spread:
                return GetTag(spread.Operand, depth + 1);

            case IConditionalAccessOperation conditionalAccess:
                return GetTag(conditionalAccess.WhenNotNull, depth + 1);

            case IConditionalAccessInstanceOperation conditionalAccessInstance:
                IOperation child = conditionalAccessInstance;
                for (var parent = child.Parent; parent is not null; parent = parent.Parent)
                {
                    if (parent is IConditionalAccessOperation access && access.WhenNotNull == child)
                        return GetTag(access.Operation, depth + 1);

                    child = parent;
                }

                return TagInfo.None;

            default:
                return TagInfo.None;
        }
    }

    /// <summary>
    /// Returns the tags of the combined values, or <see cref="TagInfo.None"/> when two of them are incompatible.
    /// </summary>
    private TagInfo Combine(IEnumerable<IOperation> operations, int depth)
    {
        var tags = new List<TagInfo>();
        foreach (var operation in operations)
        {
            var tag = GetTag(operation, depth + 1);
            if (tag.IsEmpty)
                continue;

            foreach (var existing in tags)
            {
                if (!TagInfo.AreCompatible(existing, tag))
                    return TagInfo.None;
            }

            tags.Add(tag);
        }

        return tags.Aggregate(TagInfo.None, TagInfo.Union);
    }

    /// <summary>
    /// Returns the first pair of combined values whose tags are incompatible.
    /// </summary>
    public bool TryFindIncompatibleValues(IEnumerable<IOperation> operations, out IOperation first, out TagInfo firstTags, out IOperation second, out TagInfo secondTags)
    {
        var tagged = new List<(IOperation Operation, TagInfo Tags)>();
        foreach (var operation in operations)
        {
            var tag = GetTag(operation);
            if (tag.IsEmpty)
                continue;

            foreach (var existing in tagged)
            {
                if (!TagInfo.AreCompatible(existing.Tags, tag))
                {
                    first = existing.Operation;
                    firstTags = existing.Tags;
                    second = operation;
                    secondTags = tag;
                    return true;
                }
            }

            tagged.Add((operation, tag));
        }

        first = null!;
        firstTags = TagInfo.None;
        second = null!;
        secondTags = TagInfo.None;
        return false;
    }

    private static TagInfo GetObjectCreationTypeArgumentTags(IObjectCreationOperation objectCreation)
    {
        return objectCreation.Syntax is ObjectCreationExpressionSyntax objectCreationSyntax
            ? TypeArgumentComments.GetTags(objectCreationSyntax.Type, objectCreation.Type)
            : TagInfo.None;
    }

    private TagInfo GetAnonymousTypePropertyTags(IPropertyReferenceOperation propertyReference, int depth)
    {
        var property = propertyReference.Property;
        if (!property.ContainingType.IsAnonymousType || propertyReference.SemanticModel is not { } semanticModel)
            return TagInfo.None;

        // Anonymous types are shared by every creation with the same shape, so every initializer in this file must agree
        var initializers = new List<IOperation>();
        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            if (reference.SyntaxTree != semanticModel.SyntaxTree)
                continue;

            if (reference.GetSyntax() is AnonymousObjectMemberDeclaratorSyntax declarator && semanticModel.GetOperation(declarator.Expression) is { } initializer)
            {
                initializers.Add(initializer);
            }
        }

        return Combine(initializers, depth);
    }

    private TagInfo GetLambdaParameterTags(IParameterReferenceOperation parameterReference, int depth)
    {
        var parameter = parameterReference.Parameter;
        if (parameter.ContainingSymbol is not IMethodSymbol { MethodKind: MethodKind.AnonymousFunction } lambdaSymbol)
            return TagInfo.None;

        for (var operation = parameterReference.Parent; operation is not null; operation = operation.Parent)
        {
            if (operation is IAnonymousFunctionOperation anonymousFunction && SymbolEqualityComparer.Default.Equals(anonymousFunction.Symbol, lambdaSymbol))
                return GetAnonymousFunctionParameterTags(anonymousFunction, parameter.Ordinal, depth);
        }

        return TagInfo.None;
    }

    private TagInfo GetAnonymousFunctionParameterTags(IAnonymousFunctionOperation anonymousFunction, int ordinal, int depth)
    {
        var current = anonymousFunction.Parent;
        while (current is IDelegateCreationOperation or IConversionOperation)
        {
            current = current.Parent;
        }

        if (current is not IArgumentOperation { Parameter: not null } argument || !TryGetArgumentOwner(argument, out var member, out var instance, out var arguments))
            return TagInfo.None;

        var delegateType = GetDelegateType(argument.Parameter.OriginalDefinition.Type);
        if (delegateType?.DelegateInvokeMethod is not { } invokeMethod || ordinal >= invokeMethod.Parameters.Length)
            return TagInfo.None;

        var parameterType = invokeMethod.Parameters[ordinal].Type;
        if (!ContainsTypeParameter(parameterType))
            return TagInfo.None;

        var typeParameters = BindTypeParameters(member, instance, arguments, arguments.IndexOf(argument), depth);
        return ComputeTag(parameterType, typeParameters);
    }

    private TagInfo GetAnonymousFunctionReturnTags(IAnonymousFunctionOperation anonymousFunction, int depth)
    {
        var explicitTags = GetOwnExplicitTags(anonymousFunction.Symbol);
        if (!explicitTags.IsEmpty)
            return explicitTags;

        var returnedValues = new List<IOperation>();
        CollectReturnedValues(anonymousFunction.Body, returnedValues);
        return Combine(returnedValues, depth);

        static void CollectReturnedValues(IOperation operation, List<IOperation> returnedValues)
        {
            foreach (var child in operation.ChildOperations)
            {
                switch (child)
                {
                    case IAnonymousFunctionOperation or ILocalFunctionOperation:
                        continue;

                    case IReturnOperation { ReturnedValue: not null } returnOperation:
                        returnedValues.Add(returnOperation.ReturnedValue);
                        break;
                }

                CollectReturnedValues(child, returnedValues);
            }
        }
    }

    /// <summary>
    /// Returns the tags expected for an argument: the tags of its parameter, or the tags bound to the generic type of the parameter
    /// by the receiver and the previous arguments.
    /// </summary>
    public TagInfo GetExpectedArgumentTags(IArgumentOperation argument)
    {
        return GetExpectedArgumentTags(argument, depth: 0);
    }

    private TagInfo GetExpectedArgumentTags(IArgumentOperation argument, int depth)
    {
        if (argument.Parameter is null)
            return TagInfo.None;

        var tags = GetDeclaredTags(argument.Parameter);
        if (!tags.IsEmpty)
            return tags;

        var parameterType = argument.Parameter.OriginalDefinition.Type;
        if (!ContainsTypeParameter(parameterType) || !TryGetArgumentOwner(argument, out var member, out var instance, out var arguments))
            return TagInfo.None;

        var typeParameters = BindTypeParameters(member, instance, arguments, arguments.IndexOf(argument), depth);
        return ComputeTag(parameterType, typeParameters);
    }

    public static bool TryGetArgumentOwner(IArgumentOperation argument, out ISymbol member, out IOperation? instance, out ImmutableArray<IArgumentOperation> arguments)
    {
        switch (argument.Parent)
        {
            case IInvocationOperation invocation:
                (member, instance, arguments) = (invocation.TargetMethod, invocation.Instance, invocation.Arguments);
                return true;

            case IObjectCreationOperation { Constructor: not null } objectCreation:
                (member, instance, arguments) = (objectCreation.Constructor, null, objectCreation.Arguments);
                return true;

            case IPropertyReferenceOperation propertyReference:
                (member, instance, arguments) = (propertyReference.Property, propertyReference.Instance, propertyReference.Arguments);
                return true;

            default:
                (member, instance, arguments) = (null!, null, []);
                return false;
        }
    }

    private TagInfo GetBoundTag(ITypeSymbol resultType, ISymbol member, IOperation? instance, ImmutableArray<IArgumentOperation> arguments, int depth)
    {
        if (!ContainsTypeParameter(resultType))
            return TagInfo.None;

        var typeParameters = BindTypeParameters(member, instance, arguments, arguments.Length, depth);
        return ComputeTag(resultType, typeParameters);
    }

    /// <summary>
    /// Binds the type parameters of <paramref name="member"/> and of its containing types to the tags of the receiver and of the first
    /// <paramref name="argumentCount"/> arguments. The first binding of a type parameter wins.
    /// </summary>
    private Dictionary<ITypeParameterSymbol, TagInfo>? BindTypeParameters(ISymbol member, IOperation? instance, ImmutableArray<IArgumentOperation> arguments, int argumentCount, int depth)
    {
        if (depth > MaxDepth)
            return null;

        Dictionary<ITypeParameterSymbol, TagInfo>? typeParameters = null;
        var definition = member.OriginalDefinition;
        if (instance is not null && !member.IsStatic && definition.ContainingType is { } containingType && ContainsTypeParameter(containingType))
        {
            Bind(containingType, GetTag(instance, depth + 1), ref typeParameters);
        }

        // new List</* ValueTag=OrderId */ Guid>(values) expects values tagged OrderId
        if (arguments.Length > 0 && arguments[0].Parent is IObjectCreationOperation objectCreation && definition.ContainingType is { } createdType)
        {
            Bind(createdType, GetObjectCreationTypeArgumentTags(objectCreation), ref typeParameters);
        }

        for (var i = 0; i < argumentCount && i < arguments.Length; i++)
        {
            var argument = arguments[i];
            var parameter = argument.Parameter?.OriginalDefinition;
            if (parameter is null || !ContainsTypeParameter(parameter.Type))
                continue;

            var value = argument.Value;
            while (value is IConversionOperation { IsImplicit: true } or IDelegateCreationOperation)
            {
                value = value is IConversionOperation conversion ? conversion.Operand : ((IDelegateCreationOperation)value).Target;
            }

            switch (value)
            {
                case IAnonymousFunctionOperation anonymousFunction:
                    if (GetDelegateType(parameter.Type)?.DelegateInvokeMethod is { } invokeMethod)
                    {
                        Bind(invokeMethod.ReturnType, GetAnonymousFunctionReturnTags(anonymousFunction, depth + 1), ref typeParameters);
                    }

                    break;

                case IMethodReferenceOperation methodReference:
                    if (GetDelegateType(parameter.Type)?.DelegateInvokeMethod is { } methodReferenceInvokeMethod)
                    {
                        Bind(methodReferenceInvokeMethod.ReturnType, GetDeclaredTags(methodReference.Method), ref typeParameters);
                    }

                    break;

                default:
                    Bind(parameter.Type, GetTag(argument.Value, depth + 1), ref typeParameters);
                    break;
            }
        }

        return typeParameters;
    }

    private static void Bind(ITypeSymbol type, TagInfo tags, ref Dictionary<ITypeParameterSymbol, TagInfo>? typeParameters)
    {
        if (tags.IsEmpty)
            return;

        switch (type)
        {
            case ITypeParameterSymbol typeParameter:
                typeParameters ??= new(SymbolEqualityComparer.Default);
                if (!typeParameters.ContainsKey(typeParameter))
                {
                    typeParameters.Add(typeParameter, tags);
                }

                break;

            case IArrayTypeSymbol arrayType:
                Bind(arrayType.ElementType, tags, ref typeParameters);
                break;

            case INamedTypeSymbol namedType when IsKeyValuePair(namedType):
                Bind(namedType.TypeArguments[0], tags.GetKey(), ref typeParameters);
                Bind(namedType.TypeArguments[1], tags.GetValue(), ref typeParameters);
                break;

            case INamedTypeSymbol namedType when TryGetWrappedType(namedType, out var wrappedType):
                Bind(wrappedType, tags, ref typeParameters);
                break;
        }
    }

    private static TagInfo ComputeTag(ITypeSymbol type, Dictionary<ITypeParameterSymbol, TagInfo>? typeParameters)
    {
        if (typeParameters is null)
            return TagInfo.None;

        switch (type)
        {
            case ITypeParameterSymbol typeParameter:
                return typeParameters.TryGetValue(typeParameter, out var tags) ? tags : TagInfo.None;

            case IArrayTypeSymbol arrayType:
                return ComputeTag(arrayType.ElementType, typeParameters);

            case INamedTypeSymbol namedType when IsKeyValuePair(namedType):
                return TagInfo.KeyValue(ComputeTag(namedType.TypeArguments[0], typeParameters), ComputeTag(namedType.TypeArguments[1], typeParameters));

            case INamedTypeSymbol namedType when TryGetWrappedType(namedType, out var wrappedType):
                return ComputeTag(wrappedType, typeParameters);

            default:
                return TagInfo.None;
        }
    }

    private static bool ContainsTypeParameter(ITypeSymbol? type)
    {
        switch (type)
        {
            case ITypeParameterSymbol:
                return true;

            case IArrayTypeSymbol arrayType:
                return ContainsTypeParameter(arrayType.ElementType);

            case INamedTypeSymbol namedType:
                foreach (var typeArgument in namedType.TypeArguments)
                {
                    if (ContainsTypeParameter(typeArgument))
                        return true;
                }

                return ContainsTypeParameter(namedType.ContainingType);

            default:
                return false;
        }
    }

    private static INamedTypeSymbol? GetDelegateType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Delegate } delegateType)
            return delegateType;

        // Queryable methods take Expression<Func<...>>
        if (type is INamedTypeSymbol { Name: "Expression", TypeArguments.Length: 1 } expressionType && IsInNamespace(expressionType, "System", "Linq", "Expressions"))
            return expressionType.TypeArguments[0] as INamedTypeSymbol;

        return null;
    }

    public static bool IsKeyValuePair(INamedTypeSymbol type)
    {
        return type is { Name: "KeyValuePair", TypeArguments.Length: 2 } && IsInNamespace(type, "System", "Collections", "Generic");
    }

    /// <summary>
    /// Returns the type of the value a tag describes for a wrapper type: the element of a collection, or the value of a
    /// <c>Nullable&lt;T&gt;</c>, a <c>Task&lt;T&gt;</c>, a <c>ValueTask&lt;T&gt;</c>, a <c>Lazy&lt;T&gt;</c>, a span, or a memory.
    /// </summary>
    public static bool TryGetWrappedType(INamedTypeSymbol type, out ITypeSymbol wrappedType)
    {
        wrappedType = null!;
        if (type.SpecialType is SpecialType.System_String)
            return false;

        if (type.TypeArguments.Length is 1)
        {
            var isWrapper = type.OriginalDefinition.SpecialType is SpecialType.System_Nullable_T or SpecialType.System_Collections_Generic_IEnumerable_T or SpecialType.System_Collections_Generic_IEnumerator_T ||
                (type.Name is "Task" or "ValueTask" && IsInNamespace(type, "System", "Threading", "Tasks")) ||
                (type.Name is "Lazy" or "Span" or "ReadOnlySpan" or "Memory" or "ReadOnlyMemory" && IsInNamespace(type, "System")) ||
                (type.Name is "IAsyncEnumerable" or "IAsyncEnumerator" && IsInNamespace(type, "System", "Collections", "Generic"));

            if (isWrapper)
            {
                wrappedType = type.TypeArguments[0];
                return true;
            }
        }

        ITypeSymbol? elementType = null;
        foreach (var @interface in type.AllInterfaces)
        {
            if (@interface.OriginalDefinition.SpecialType is SpecialType.System_Collections_Generic_IEnumerable_T ||
                (@interface is { Name: "IAsyncEnumerable", TypeArguments.Length: 1 } && IsInNamespace(@interface, "System", "Collections", "Generic")))
            {
                if (elementType is not null && !SymbolEqualityComparer.Default.Equals(elementType, @interface.TypeArguments[0]))
                    return false;

                elementType = @interface.TypeArguments[0];
            }
        }

        if (elementType is null)
            return false;

        wrappedType = elementType;
        return true;
    }

    public static bool IsCollectionType(ITypeSymbol type)
    {
        return type is IArrayTypeSymbol ||
            (type is INamedTypeSymbol namedType && namedType.OriginalDefinition.SpecialType is not SpecialType.System_Nullable_T && TryGetWrappedType(namedType, out _) &&
             !(namedType.Name is "Task" or "ValueTask" or "Lazy"));
    }

    /// <summary>
    /// Returns whether the tags of a value of this type describe a dictionary, whose keys and values are tagged separately.
    /// </summary>
    public static bool IsKeyValueShaped(ITypeSymbol type)
    {
        for (var depth = 0; depth < 8; depth++)
        {
            switch (type)
            {
                case IArrayTypeSymbol arrayType:
                    type = arrayType.ElementType;
                    break;

                case INamedTypeSymbol namedType when IsKeyValuePair(namedType):
                    return true;

                case INamedTypeSymbol namedType when TryGetWrappedType(namedType, out var wrappedType):
                    type = wrappedType;
                    break;

                default:
                    return false;
            }
        }

        return false;
    }

    private static bool IsInNamespace(ISymbol symbol, params string[] namespaceParts)
    {
        var ns = symbol.ContainingNamespace;
        for (var i = namespaceParts.Length - 1; i >= 0; i--)
        {
            if (ns is null || ns.Name != namespaceParts[i])
                return false;

            ns = ns.ContainingNamespace;
        }

        return ns is { IsGlobalNamespace: true };
    }

    private static bool IsObjectOrDynamic(ITypeSymbol? type)
    {
        return type is { SpecialType: SpecialType.System_Object } or { TypeKind: TypeKind.Dynamic };
    }

    private sealed class ExternalTagKeyComparer : IEqualityComparer<(ITypeSymbol Type, string MemberName)>
    {
        public static readonly ExternalTagKeyComparer Instance = new();

        public bool Equals((ITypeSymbol Type, string MemberName) x, (ITypeSymbol Type, string MemberName) y)
        {
            return SymbolEqualityComparer.Default.Equals(x.Type, y.Type) && string.Equals(x.MemberName, y.MemberName, StringComparison.Ordinal);
        }

        public int GetHashCode((ITypeSymbol Type, string MemberName) obj)
        {
            return (SymbolEqualityComparer.Default.GetHashCode(obj.Type) * 397) ^ StringComparer.Ordinal.GetHashCode(obj.MemberName);
        }
    }
}
