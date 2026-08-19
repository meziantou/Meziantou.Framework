using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.Yaml.SourceGeneration;

/// <summary>
/// Collects the <c>[UnsafeAccessor]</c> stubs the generated context needs to reach members and constructors
/// that are not accessible from it, and emits them into the generated source.
/// </summary>
/// <remarks>
/// Stubs for members of a generic type cannot use a constructed type such as <c>Foo&lt;int&gt;</c>: the runtime
/// rejects them with <c>MissingMethodException</c> or <c>InvalidProgramException</c>. They must live in a generic
/// class that mirrors the declaring type's type parameters together with their constraints, and that class is shared
/// by every instantiation.
/// </remarks>
internal sealed class UnsafeAccessorRegistry
{
    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol _contextSymbol;
    private readonly Dictionary<string, string> _namesByKey = new(StringComparer.Ordinal);
    private readonly List<string> _declarations = [];
    private readonly Dictionary<string, GenericAccessorClass> _genericClassesByKey = new(StringComparer.Ordinal);

    public UnsafeAccessorRegistry(Compilation compilation, INamedTypeSymbol contextSymbol)
    {
        _compilation = compilation;
        _contextSymbol = contextSymbol;
    }

    public bool HasAccessors => _declarations.Count != 0 || _genericClassesByKey.Count != 0;

    /// <summary>
    /// Indicates whether the generated context can reference <paramref name="symbol"/> directly.
    /// </summary>
    public bool IsAccessible(ISymbol symbol) => _compilation.IsSymbolAccessibleWithin(symbol, _contextSymbol);

    /// <summary>
    /// Returns an expression reading <paramref name="property"/> from <paramref name="receiver"/>.
    /// </summary>
    public string GetPropertyReadExpression(IPropertySymbol property, IMethodSymbol getMethod, string receiver)
    {
        var context = AccessorContext.Create(this, getMethod);
        var returnType = context.TypeName(property, static p => p.Type);
        var accessorName = GetOrAddMethodAccessor(context, returnType, parameters: []);
        return context.Qualify(accessorName) + "(" + GetReceiverArguments(getMethod, receiver, additionalArguments: null) + ")";
    }

    /// <summary>
    /// Returns a statement expression assigning <paramref name="value"/> to <paramref name="property"/> on <paramref name="receiver"/>.
    /// </summary>
    public string GetPropertyWriteExpression(IPropertySymbol property, IMethodSymbol setMethod, string receiver, string value)
    {
        var context = AccessorContext.Create(this, setMethod);
        var parameterType = context.TypeName(property, static p => p.Type);
        var accessorName = GetOrAddMethodAccessor(context, "void", [parameterType]);
        return context.Qualify(accessorName) + "(" + GetReceiverArguments(setMethod, receiver, value) + ")";
    }

    /// <summary>
    /// Returns a <c>ref</c> expression to <paramref name="field"/> on <paramref name="receiver"/>. The expression can be
    /// used both as a value and as an assignment target.
    /// </summary>
    public string GetFieldExpression(IFieldSymbol field, string receiver)
    {
        var context = AccessorContext.Create(this, field);
        var key = context.Key + ":field:" + field.MetadataName;
        if (!_namesByKey.TryGetValue(key, out var name))
        {
            name = CreateName(context, field.MetadataName);
            var builder = new StringBuilder();
            AppendAttribute(builder, context, "Field", field.MetadataName);
            builder.Append(context.Indent).Append(context.Modifiers).Append(" extern ref ")
                .Append(context.TypeName(field, static f => f.Type))
                .Append(' ').Append(name).Append('(');
            AppendReceiverParameter(builder, context, field);
            builder.AppendLine(");");
            _namesByKey.Add(key, name);
            context.AddDeclaration(this, builder.ToString());
        }

        return context.Qualify(name) + "(" + GetReceiverArguments(field, receiver, additionalArguments: null) + ")";
    }

    /// <summary>
    /// Returns an expression creating an instance using <paramref name="constructor"/> with the provided arguments.
    /// </summary>
    public string GetConstructorExpression(IMethodSymbol constructor, string arguments)
    {
        var context = AccessorContext.Create(this, constructor);
        var key = context.Key + ":ctor:" + constructor.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (!_namesByKey.TryGetValue(key, out var name))
        {
            name = CreateName(context, "Create" + constructor.ContainingType.Name);
            var parameters = constructor.OriginalDefinition.Parameters;
            var builder = new StringBuilder();
            AppendAttribute(builder, context, "Constructor", name: null);
            builder.Append(context.Indent).Append(context.Modifiers).Append(" extern ").Append(context.ReceiverTypeName)
                .Append(' ').Append(name).Append('(');
            for (var i = 0; i < parameters.Length; i++)
            {
                if (i != 0)
                {
                    builder.Append(", ");
                }

                builder.Append(parameters[i].Type.ToDisplayString(YamlSerializerContextGenerator.FullyQualifiedNullableFormat))
                    .Append(" arg").Append(i.ToString(CultureInfo.InvariantCulture));
            }

            builder.AppendLine(");");
            _namesByKey.Add(key, name);
            context.AddDeclaration(this, builder.ToString());
        }

        return context.Qualify(name) + "(" + arguments + ")";
    }

    public void Emit(StringBuilder builder)
    {
        if (!HasAccessors)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("    // Accessors for members and constructors that are not accessible from this context.");
        foreach (var declaration in _declarations)
        {
            builder.Append(declaration);
        }

        foreach (var genericClass in _genericClassesByKey.Values)
        {
            builder.Append("    private static class ").Append(genericClass.Name).Append(genericClass.TypeParameterList)
                .AppendLine(genericClass.ConstraintClauses);
            builder.AppendLine("    {");
            foreach (var declaration in genericClass.Declarations)
            {
                builder.Append(declaration);
            }

            builder.AppendLine("    }");
        }
    }

    private string GetOrAddMethodAccessor(AccessorContext context, string returnType, string[] parameters)
    {
        var method = (IMethodSymbol)context.Member;
        var key = context.Key + ":method:" + method.MetadataName;
        if (_namesByKey.TryGetValue(key, out var name))
        {
            return name;
        }

        name = CreateName(context, method.MetadataName);
        var builder = new StringBuilder();
        AppendAttribute(builder, context, "Method", method.MetadataName);
        builder.Append(context.Indent).Append(context.Modifiers).Append(" extern ").Append(returnType).Append(' ').Append(name).Append('(');
        var hasReceiver = AppendReceiverParameter(builder, context, method);
        for (var i = 0; i < parameters.Length; i++)
        {
            if (i != 0 || hasReceiver)
            {
                builder.Append(", ");
            }

            builder.Append(parameters[i]).Append(" arg").Append(i.ToString(CultureInfo.InvariantCulture));
        }

        builder.AppendLine(");");
        _namesByKey.Add(key, name);
        context.AddDeclaration(this, builder.ToString());
        return name;
    }

    private static void AppendAttribute(StringBuilder builder, AccessorContext context, string kind, string? name)
    {
        builder.Append(context.Indent).Append("[global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.")
            .Append(kind);
        if (name is not null)
        {
            builder.Append(", Name = ").Append(YamlSerializerContextGenerator.ToLiteral(name));
        }

        builder.AppendLine(")]");
    }

    private static bool AppendReceiverParameter(StringBuilder builder, AccessorContext context, ISymbol member)
    {
        if (member.IsStatic)
        {
            return false;
        }

        if (member.ContainingType.IsValueType)
        {
            builder.Append("ref ");
        }

        builder.Append(context.ReceiverTypeName).Append(" obj");
        return true;
    }

    private static string GetReceiverArguments(ISymbol member, string receiver, string? additionalArguments)
    {
        if (member.IsStatic)
        {
            return additionalArguments ?? string.Empty;
        }

        var self = member.ContainingType.IsValueType ? "ref " + receiver : receiver;
        return additionalArguments is null ? self : self + ", " + additionalArguments;
    }

    private string CreateName(AccessorContext context, string hint)
    {
        var builder = new StringBuilder("__unsafeAccessor");
        builder.Append((context.GenericClass?.Declarations.Count ?? _declarations.Count).ToString(CultureInfo.InvariantCulture)).Append('_');
        foreach (var c in hint)
        {
            builder.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        return builder.ToString();
    }

    private GenericAccessorClass GetOrAddGenericClass(INamedTypeSymbol definition)
    {
        var key = definition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (_genericClassesByKey.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var typeParameters = GetTypeParametersInScope(definition);
        var typeParameterList = new StringBuilder("<");
        var constraints = new StringBuilder();
        for (var i = 0; i < typeParameters.Count; i++)
        {
            if (i != 0)
            {
                typeParameterList.Append(", ");
            }

            typeParameterList.Append(typeParameters[i].Name);
            AppendConstraintClause(constraints, typeParameters[i]);
        }

        typeParameterList.Append('>');

        var created = new GenericAccessorClass(
            "__UnsafeAccessors" + _genericClassesByKey.Count.ToString(CultureInfo.InvariantCulture),
            typeParameterList.ToString(),
            constraints.ToString());
        _genericClassesByKey.Add(key, created);
        return created;
    }

    /// <summary>
    /// Appends the <c>where</c> clause for <paramref name="typeParameter"/>. Omitting the constraints makes the
    /// generated code fail to compile with CS0314.
    /// </summary>
    private static void AppendConstraintClause(StringBuilder builder, ITypeParameterSymbol typeParameter)
    {
        var constraints = new List<string>();
        if (typeParameter.HasUnmanagedTypeConstraint)
        {
            constraints.Add("unmanaged");
        }
        else if (typeParameter.HasValueTypeConstraint)
        {
            constraints.Add("struct");
        }
        else if (typeParameter.HasReferenceTypeConstraint)
        {
            constraints.Add(typeParameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
        }
        else if (typeParameter.HasNotNullConstraint)
        {
            constraints.Add("notnull");
        }

        foreach (var constraintType in typeParameter.ConstraintTypes)
        {
            constraints.Add(constraintType.ToDisplayString(YamlSerializerContextGenerator.FullyQualifiedNullableFormat));
        }

        // 'struct' and 'unmanaged' already imply a parameterless constructor and cannot be combined with 'new()'.
        if (typeParameter.HasConstructorConstraint && !typeParameter.HasValueTypeConstraint && !typeParameter.HasUnmanagedTypeConstraint)
        {
            constraints.Add("new()");
        }

        if (constraints.Count == 0)
        {
            return;
        }

        builder.Append(" where ").Append(typeParameter.Name).Append(" : ").Append(string.Join(", ", constraints));
    }

    /// <summary>
    /// Gets every type parameter visible inside <paramref name="definition"/>, outermost type first, so the accessor
    /// class can be generic over all of them.
    /// </summary>
    private static List<ITypeParameterSymbol> GetTypeParametersInScope(INamedTypeSymbol definition)
    {
        var nesting = new Stack<INamedTypeSymbol>();
        for (var current = definition; current is not null; current = current.ContainingType)
        {
            nesting.Push(current);
        }

        var typeParameters = new List<ITypeParameterSymbol>();
        while (nesting.Count != 0)
        {
            typeParameters.AddRange(nesting.Pop().TypeParameters);
        }

        return typeParameters;
    }

    /// <summary>
    /// Gets the type arguments matching <see cref="GetTypeParametersInScope"/> for a constructed type.
    /// </summary>
    private static List<ITypeSymbol> GetTypeArgumentsInScope(INamedTypeSymbol type)
    {
        var nesting = new Stack<INamedTypeSymbol>();
        for (var current = type; current is not null; current = current.ContainingType)
        {
            nesting.Push(current);
        }

        var typeArguments = new List<ITypeSymbol>();
        while (nesting.Count != 0)
        {
            typeArguments.AddRange(nesting.Pop().TypeArguments);
        }

        return typeArguments;
    }

    private sealed class GenericAccessorClass
    {
        public GenericAccessorClass(string name, string typeParameterList, string constraintClauses)
        {
            Name = name;
            TypeParameterList = typeParameterList;
            ConstraintClauses = constraintClauses;
        }

        public string Name { get; }
        public string TypeParameterList { get; }
        public string ConstraintClauses { get; }
        public List<string> Declarations { get; } = [];
    }

    /// <summary>
    /// Describes where a stub is emitted and how its signature must be written: directly in the context for a
    /// non-generic declaring type, or in a shared generic class written against the type definition.
    /// </summary>
    private sealed class AccessorContext
    {
        private AccessorContext(ISymbol member, INamedTypeSymbol? definition, string receiverTypeName, string key)
        {
            Member = member;
            Definition = definition;
            ReceiverTypeName = receiverTypeName;
            Key = key;
        }

        public ISymbol Member { get; }
        public INamedTypeSymbol? Definition { get; }
        public string ReceiverTypeName { get; }
        public string Key { get; }
        public GenericAccessorClass? GenericClass { get; private init; }
        private string? TypeArgumentList { get; init; }

        public bool IsGeneric => Definition is not null;
        public string Indent => IsGeneric ? "        " : "    ";
        public string Modifiers => IsGeneric ? "public static" : "private static";

        public static AccessorContext Create(UnsafeAccessorRegistry registry, ISymbol member)
        {
            var containingType = member.ContainingType;
            if (GetTypeParametersInScope(containingType.OriginalDefinition).Count == 0)
            {
                var typeName = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return new AccessorContext(member, definition: null, typeName, typeName);
            }

            // Write the signature against the type definition so the stub is shared by every instantiation.
            var definition = containingType.OriginalDefinition;
            var definitionName = definition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return new AccessorContext(member.OriginalDefinition, definition, definitionName, definitionName)
            {
                GenericClass = registry.GetOrAddGenericClass(definition),
                TypeArgumentList = "<" + string.Join(", ", GetTypeArgumentsInScope(containingType)
                    .Select(static argument => argument.ToDisplayString(YamlSerializerContextGenerator.FullyQualifiedNullableFormat))) + ">",
            };
        }

        /// <summary>
        /// Gets the display name of a member type, taken from the type definition when the stub is generic.
        /// </summary>
        public string TypeName<T>(T symbol, Func<T, ITypeSymbol> selector)
            where T : ISymbol
        {
            var source = IsGeneric ? (T)symbol.OriginalDefinition : symbol;
            return selector(source).ToDisplayString(YamlSerializerContextGenerator.FullyQualifiedNullableFormat);
        }

        public void AddDeclaration(UnsafeAccessorRegistry registry, string declaration)
        {
            if (GenericClass is null)
            {
                registry._declarations.Add(declaration);
                return;
            }

            GenericClass.Declarations.Add(declaration);
        }

        public string Qualify(string accessorName)
            => GenericClass is null ? accessorName : GenericClass.Name + TypeArgumentList + "." + accessorName;
    }
}
