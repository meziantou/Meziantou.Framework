namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a reference to a type, including support for generics, arrays, and nullable annotations.</summary>
/// <example>
/// <code>
/// var stringType = new TypeReference(typeof(string));
/// var listOfInt = new TypeReference(typeof(List&lt;&gt;)).MakeGeneric(new TypeReference(typeof(int)));
/// var intArray = new TypeReference(typeof(int)).MakeArray(1);
/// </code>
/// </example>
public class TypeReference
{
    private static readonly char[] ArityOrArrayCharacters = ['`', '['];

    private TypeParameter? _typeParameter;
    private TypeDeclaration? _typeDeclaration;
    private string? _typeName;
    private List<TypeReference>? _parameters;
    private List<TypeReference>? _typeDeclarationParameters;

    private TypeReference()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TypeReference"/> class from a type declaration.</summary>
    /// <param name="typeDeclaration">The type declaration to reference.</param>
    public TypeReference(TypeDeclaration typeDeclaration)
    {
        _typeDeclaration = typeDeclaration ?? throw new ArgumentNullException(nameof(typeDeclaration));
    }

    /// <summary>Initializes a new instance of the <see cref="TypeReference"/> class from a type parameter.</summary>
    /// <param name="typeParameter">The type parameter to reference.</param>
    public TypeReference(TypeParameter typeParameter)
    {
        _typeParameter = typeParameter ?? throw new ArgumentNullException(nameof(typeParameter));
    }

    /// <summary>Initializes a new instance of the <see cref="TypeReference"/> class from a type name.</summary>
    /// <param name="typeName">The fully qualified type name.</param>
    public TypeReference(string typeName)
    {
        _typeName = typeName;
    }

    /// <summary>Initializes a new instance of the <see cref="TypeReference"/> class from a reflection type.</summary>
    /// <param name="type">The reflection type to reference.</param>
    public TypeReference(Type type)
    {
        var name = type.Name;
        var arityOrArrayIndex = name.IndexOfAny(ArityOrArrayCharacters);
        if (arityOrArrayIndex > 0)
        {
            name = name[..arityOrArrayIndex];
        }

        var declaringType = type.DeclaringType;
        if (declaringType != null)
        {
            var typeName = name;
            string? lastNamespace = null;
            while (declaringType != null)
            {
                typeName = declaringType.Name + '+' + typeName;

                lastNamespace = declaringType.Namespace;
                declaringType = declaringType.DeclaringType;
            }

            _typeName = lastNamespace + '.' + typeName;
        }
        else
        {
            var ns = type.Namespace;
            if (!string.IsNullOrEmpty(ns))
            {
                _typeName = ns + '.' + name;
            }
            else
            {
                _typeName = name;
            }
        }

        if (type.IsArray)
        {
            ArrayRank = type.GetArrayRank();
        }

        if (type.IsGenericType)
        {
            foreach (var genericType in type.GenericTypeArguments)
            {
                Parameters.Add(new TypeReference(genericType));
            }
        }
    }

    /// <summary>Gets a value indicating whether this type reference represents an array.</summary>
    public bool IsArray => ArrayRank > 0;

    /// <summary>Gets or sets the array rank (0 for non-arrays, 1 for single-dimension arrays, etc.).</summary>
    public int ArrayRank { get; set; }

    /// <summary>Gets or sets the nullable annotation for this type reference.</summary>
    public NullableAnnotation Nullable { get; set; }

    /// <summary>Gets the list of generic type parameters for this type reference.</summary>
    public IList<TypeReference> Parameters
    {
        get
        {
            if (_typeDeclaration is IParametrableType typeParameter)
            {
                if (_typeDeclarationParameters is null)
                {
                    var collection = new List<TypeReference>();
                    collection.AddRange(typeParameter.Parameters.Select(p => new TypeReference(p.Name ?? throw new InvalidOperationException("TypeReference has no name"))));
                    _typeDeclarationParameters = collection;
                }

                return _typeDeclarationParameters;
            }

            return _parameters ??= [];
        }
    }

    /// <summary>Gets the fully qualified name of the referenced type.</summary>
    public string? TypeName
    {
        get
        {
            if (_typeParameter is not null)
                return _typeParameter.Name;

            if (_typeDeclaration is not null)
            {
                var result = _typeDeclaration.Name;
                var type = _typeDeclaration;
                var parentType = type.AnscestorOfType<TypeDeclaration>();
                while (parentType is not null)
                {
                    result = parentType.Name + '+' + result;
                    parentType = parentType.AnscestorOfType<TypeDeclaration>();
                }

                var ns = _typeDeclaration.Namespace;
                if (ns is not null)
                {
                    result = ns + '.' + result;
                }

                return result;
            }

            return _typeName;
        }
    }

    internal string ClrFullTypeNameWithoutArray
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(TypeName);
            if (Parameters.Any())
            {
                sb.Append('<');
                var first = true;
                foreach (var parameter in Parameters)
                {
                    if (!first)
                    {
                        sb.Append(", ");
                    }

                    sb.Append(parameter.ClrFullTypeName);

                    first = false;
                }

                sb.Append('>');
            }

            return sb.ToString();
        }
    }

    /// <summary>Gets the full CLR type name including generic parameters and array notation.</summary>
    public string ClrFullTypeName
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(TypeName);
            if (Parameters.Any())
            {
                sb.Append('<');
                var first = true;
                foreach (var parameter in Parameters)
                {
                    if (!first)
                    {
                        sb.Append(", ");
                    }

                    sb.Append(parameter.ClrFullTypeName);

                    first = false;
                }

                sb.Append('>');
            }

            if (IsArray)
            {
                sb.Append('[');
                for (var i = 1; i < ArrayRank; i++)
                {
                    sb.Append(',');
                }

                sb.Append(']');
            }

            return sb.ToString();
        }
    }

    /// <summary>Creates a shallow copy of this type reference.</summary>
    /// <returns>A new <see cref="TypeReference"/> instance with the same values.</returns>
    public TypeReference Clone()
    {
        var clone = new TypeReference
        {
            _typeName = _typeName,
            _typeDeclaration = _typeDeclaration,
            _typeParameter = _typeParameter,
            ArrayRank = ArrayRank,
            Nullable = Nullable,
        };

        if (_parameters is not null)
        {
            clone._parameters = [.. _parameters];
        }

        return clone;
    }

    /// <summary>Creates a generic type reference with the specified type arguments.</summary>
    /// <param name="typeArguments">The type arguments for the generic type.</param>
    /// <returns>A new generic <see cref="TypeReference"/> instance.</returns>
    public TypeReference MakeGeneric(params TypeReference[] typeArguments)
    {
        var type = Clone();
        type.Parameters.Clear();
        foreach (var arg in typeArguments)
        {
            type.Parameters.Add(arg);
        }

        return type;
    }

    /// <summary>Creates an array type reference with the specified rank.</summary>
    /// <param name="rank">The array rank (1 for single-dimension, 2 for two-dimensional, etc.).</param>
    /// <returns>A new array <see cref="TypeReference"/> instance.</returns>
    public TypeReference MakeArray(int rank)
    {
        var type = Clone();
        type.ArrayRank = rank;
        return type;
    }

    /// <summary>Creates a nullable type reference with the specified nullable annotation.</summary>
    /// <param name="value">The nullable annotation to apply.</param>
    /// <returns>A new nullable <see cref="TypeReference"/> instance.</returns>
    public TypeReference MakeNullable(NullableAnnotation value = NullableAnnotation.Nullable)
    {
        var type = Clone();
        type.Nullable = value;
        return type;
    }

    public static implicit operator TypeReference(TypeDeclaration typeDeclaration) => new(typeDeclaration);

    public static implicit operator TypeReference(Type type) => new(type);

    public static implicit operator TypeReference(TypeParameter type) => new(type);
}
