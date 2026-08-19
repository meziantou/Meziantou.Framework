using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.Yaml.SourceGeneration;

/// <summary>
/// Collects the <c>[UnsafeAccessor]</c> stubs the generated context needs to reach members and constructors
/// that are not accessible from it, and emits them into the generated source.
/// </summary>
internal sealed class UnsafeAccessorRegistry
{
    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol _contextSymbol;
    private readonly Dictionary<string, string> _namesByKey = new(StringComparer.Ordinal);
    private readonly List<string> _declarations = [];

    public UnsafeAccessorRegistry(Compilation compilation, INamedTypeSymbol contextSymbol)
    {
        _compilation = compilation;
        _contextSymbol = contextSymbol;
    }

    public bool HasAccessors => _declarations.Count != 0;

    /// <summary>
    /// Indicates whether the generated context can reference <paramref name="symbol"/> directly.
    /// </summary>
    public bool IsAccessible(ISymbol symbol) => _compilation.IsSymbolAccessibleWithin(symbol, _contextSymbol);

    /// <summary>
    /// Returns an expression reading <paramref name="property"/> from <paramref name="receiver"/>.
    /// </summary>
    public string GetPropertyReadExpression(IPropertySymbol property, IMethodSymbol getMethod, string receiver)
    {
        var accessorName = GetOrAddMethodAccessor(getMethod, property.Type.ToDisplayString(YamlSerializerContextGenerator.FullyQualifiedNullableFormat), parameters: []);
        return accessorName + "(" + GetReceiverArguments(getMethod, receiver, additionalArguments: null) + ")";
    }

    /// <summary>
    /// Returns a statement expression assigning <paramref name="value"/> to <paramref name="property"/> on <paramref name="receiver"/>.
    /// </summary>
    public string GetPropertyWriteExpression(IPropertySymbol property, IMethodSymbol setMethod, string receiver, string value)
    {
        var parameterType = property.Type.ToDisplayString(YamlSerializerContextGenerator.FullyQualifiedNullableFormat);
        var accessorName = GetOrAddMethodAccessor(setMethod, "void", [parameterType]);
        return accessorName + "(" + GetReceiverArguments(setMethod, receiver, value) + ")";
    }

    /// <summary>
    /// Returns a <c>ref</c> expression to <paramref name="field"/> on <paramref name="receiver"/>. The expression can be
    /// used both as a value and as an assignment target.
    /// </summary>
    public string GetFieldExpression(IFieldSymbol field, string receiver)
    {
        var key = "field:" + field.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ":" + field.MetadataName;
        if (!_namesByKey.TryGetValue(key, out var name))
        {
            name = CreateName(field.MetadataName);
            var builder = new StringBuilder();
            AppendAttribute(builder, "Field", field.MetadataName);
            builder.Append("    private static extern ref ").Append(field.Type.ToDisplayString(YamlSerializerContextGenerator.FullyQualifiedNullableFormat))
                .Append(' ').Append(name).Append('(');
            AppendReceiverParameter(builder, field);
            builder.AppendLine(");");
            _namesByKey.Add(key, name);
            _declarations.Add(builder.ToString());
        }

        return name + "(" + GetReceiverArguments(field, receiver, additionalArguments: null) + ")";
    }

    /// <summary>
    /// Returns an expression creating an instance using <paramref name="constructor"/> with the provided arguments.
    /// </summary>
    public string GetConstructorExpression(IMethodSymbol constructor, string arguments)
    {
        var key = "ctor:" + constructor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (!_namesByKey.TryGetValue(key, out var name))
        {
            name = CreateName("Create" + constructor.ContainingType.Name);
            var builder = new StringBuilder();
            AppendAttribute(builder, "Constructor", name: null);
            builder.Append("    private static extern ").Append(constructor.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .Append(' ').Append(name).Append('(');
            for (var i = 0; i < constructor.Parameters.Length; i++)
            {
                if (i != 0)
                {
                    builder.Append(", ");
                }

                builder.Append(constructor.Parameters[i].Type.ToDisplayString(YamlSerializerContextGenerator.FullyQualifiedNullableFormat))
                    .Append(" arg").Append(i.ToString(CultureInfo.InvariantCulture));
            }

            builder.AppendLine(");");
            _namesByKey.Add(key, name);
            _declarations.Add(builder.ToString());
        }

        return name + "(" + arguments + ")";
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
    }

    private string GetOrAddMethodAccessor(IMethodSymbol method, string returnType, string[] parameters)
    {
        var key = "method:" + method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ":" + method.MetadataName;
        if (_namesByKey.TryGetValue(key, out var name))
        {
            return name;
        }

        name = CreateName(method.MetadataName);
        var builder = new StringBuilder();
        AppendAttribute(builder, "Method", method.MetadataName);
        builder.Append("    private static extern ").Append(returnType).Append(' ').Append(name).Append('(');
        var hasReceiver = AppendReceiverParameter(builder, method);
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
        _declarations.Add(builder.ToString());
        return name;
    }

    private static void AppendAttribute(StringBuilder builder, string kind, string? name)
    {
        builder.Append("    [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.")
            .Append(kind);
        if (name is not null)
        {
            builder.Append(", Name = ").Append(YamlSerializerContextGenerator.ToLiteral(name));
        }

        builder.AppendLine(")]");
    }

    private static bool AppendReceiverParameter(StringBuilder builder, ISymbol member)
    {
        if (member.IsStatic)
        {
            return false;
        }

        var containingType = member.ContainingType;
        if (containingType.IsValueType)
        {
            builder.Append("ref ");
        }

        builder.Append(containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).Append(" obj");
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

    private string CreateName(string hint)
    {
        var builder = new StringBuilder("__unsafeAccessor");
        builder.Append(_declarations.Count.ToString(CultureInfo.InvariantCulture)).Append('_');
        foreach (var c in hint)
        {
            builder.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        return builder.ToString();
    }
}
