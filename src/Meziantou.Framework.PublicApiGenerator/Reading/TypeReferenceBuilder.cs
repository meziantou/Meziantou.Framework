using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>
/// Converts a <see cref="RawType"/> to a <see cref="PublicApiTypeReference"/>, applying the nullable annotations
/// (<c>NullableAttribute</c> / <c>NullableContextAttribute</c>) and the tuple element names (<c>TupleElementNamesAttribute</c>).
/// </summary>
/// <remarks>
/// The nullable annotations are stored in a pre-order traversal of the type, as described in
/// https://github.com/dotnet/roslyn/blob/main/docs/features/nullable-metadata.md:
/// non-generic value types have no entry, generic value types and function pointers have an entry that is always 0,
/// <see cref="Nullable{T}"/> only contributes its type argument, and type arguments include the ones of the containing types.
/// When the attribute contains a single value, it applies to every entry.
/// </remarks>
internal struct TypeReferenceBuilder
{
    private readonly NullableMetadataInfo _nullableInfo;
    private readonly ImmutableArray<string?> _tupleElementNames;
    private int _nullableIndex;
    private int _tupleElementNameIndex;

    private TypeReferenceBuilder(NullableMetadataInfo nullableInfo, ImmutableArray<string?> tupleElementNames)
    {
        _nullableInfo = nullableInfo;
        _tupleElementNames = tupleElementNames;
    }

    public static PublicApiTypeReference Build(RawType type, NullableMetadataInfo nullableInfo, ImmutableArray<string?> tupleElementNames = default)
    {
        var builder = new TypeReferenceBuilder(nullableInfo, tupleElementNames);
        return builder.Convert(type);
    }

    public static PublicApiTypeReference Build(RawType type) => Build(type, default);

    public static PublicApiNamedTypeReference BuildNamed(RawType.Named type) => (PublicApiNamedTypeReference)Build(type, default);

    private PublicApiTypeReference Convert(RawType type)
    {
        switch (type)
        {
            case RawType.ByReference byReference:
                // A by-ref type has no entry. Top-level by-ref types are converted to a PublicApiRefKind by the callers.
                return Convert(byReference.ElementType);

            case RawType.Pointer pointer:
                return new PublicApiPointerTypeReference(Convert(pointer.ElementType));

            case RawType.Array array:
                {
                    var annotation = ReadAnnotation();
                    return new PublicApiArrayTypeReference(Convert(array.ElementType), array.Rank, array.IsSZArray, annotation);
                }

            case RawType.TypeParameter typeParameter:
                return new PublicApiTypeParameterReference(typeParameter.Name, typeParameter.Index, typeParameter.IsMethodTypeParameter, ReadAnnotation());

            case RawType.FunctionPointer functionPointer:
                {
                    _ = ReadAnnotation();
                    var signature = functionPointer.Signature;
                    var returnRefKind = GetFunctionPointerRefKind(signature.ReturnType, isReturnType: true);
                    var returnType = Convert(signature.ReturnType);
                    var parameters = ImmutableArray.CreateBuilder<PublicApiFunctionPointerParameter>(signature.ParameterTypes.Length);
                    foreach (var parameterType in signature.ParameterTypes)
                    {
                        var refKind = GetFunctionPointerRefKind(parameterType, isReturnType: false);
                        parameters.Add(new PublicApiFunctionPointerParameter(refKind, Convert(parameterType)));
                    }

                    return new PublicApiFunctionPointerTypeReference(GetCallingConvention(signature.Header.CallingConvention), returnType, returnRefKind, parameters.MoveToImmutable());
                }

            case RawType.GenericInstance genericInstance:
                return ConvertGenericInstance(genericInstance);

            case RawType.Named named:
                {
                    var annotation = named.IsValueType ? PublicApiNullableAnnotation.NotAnnotated : ReadAnnotation();
                    return CreateNamed(named, [], annotation, tupleElementNames: default);
                }

            default:
                throw new InvalidOperationException("Unexpected type: " + type.GetType().Name);
        }
    }

    private PublicApiNamedTypeReference ConvertGenericInstance(RawType.GenericInstance genericInstance)
    {
        var definition = genericInstance.Definition;
        if (definition.IsSystemType("Nullable`1") && genericInstance.TypeArguments.Length == 1)
        {
            // Nullable<T> has no entry, only its type argument has
            var underlyingType = Convert(genericInstance.TypeArguments[0]);
            return CreateNamed(definition with { IsValueType = true }, [underlyingType], PublicApiNullableAnnotation.NotAnnotated, tupleElementNames: default);
        }

        var annotation = ReadAnnotation();
        if (definition.IsValueType)
        {
            annotation = PublicApiNullableAnnotation.NotAnnotated;
        }

        var tupleElementNames = ReadTupleElementNames(definition, genericInstance.TypeArguments.Length);
        var typeArguments = ImmutableArray.CreateBuilder<PublicApiTypeReference>(genericInstance.TypeArguments.Length);
        foreach (var typeArgument in genericInstance.TypeArguments)
        {
            typeArguments.Add(Convert(typeArgument));
        }

        return CreateNamed(definition, typeArguments.MoveToImmutable(), annotation, tupleElementNames);
    }

    private ImmutableArray<string?> ReadTupleElementNames(RawType.Named definition, int typeArgumentCount)
    {
        if (_tupleElementNames.IsDefaultOrEmpty || !IsValueTupleDefinition(definition))
            return default;

        // ValueTuple`8 stores the 8th and following elements in its TRest type argument, which is a tuple itself
        var count = Math.Min(typeArgumentCount, 7);
        var builder = ImmutableArray.CreateBuilder<string?>(count);
        var hasName = false;
        for (var i = 0; i < count; i++)
        {
            var name = _tupleElementNameIndex < _tupleElementNames.Length ? _tupleElementNames[_tupleElementNameIndex] : null;
            _tupleElementNameIndex++;
            hasName |= name is not null;
            builder.Add(name);
        }

        return hasName ? builder.MoveToImmutable() : default;
    }

    private static bool IsValueTupleDefinition(RawType.Named definition)
    {
        return definition.ContainingType is null &&
               definition.Namespace is "System" &&
               definition.MetadataName.StartsWith("ValueTuple`", StringComparison.Ordinal);
    }

    private static PublicApiNamedTypeReference CreateNamed(RawType.Named definition, ImmutableArray<PublicApiTypeReference> typeArguments, PublicApiNullableAnnotation annotation, ImmutableArray<string?> tupleElementNames)
    {
        // Metadata stores the type arguments of all the nesting levels in a single list.
        // They are distributed using the arity of each level, from the outermost type.
        var chain = new List<RawType.Named>();
        for (var current = definition; current is not null; current = current.ContainingType)
        {
            chain.Add(current);
        }

        chain.Reverse();
        var arities = chain.Select(static type => MetadataNameHelper.GetGenericArity(type.MetadataName)).ToArray();
        if (arities.Sum() != typeArguments.Length)
        {
            Array.Clear(arities);
            arities[^1] = typeArguments.Length;
        }

        PublicApiNamedTypeReference? containingType = null;
        var offset = 0;
        for (var i = 0; i < chain.Count; i++)
        {
            var level = chain[i];
            var levelTypeArguments = typeArguments.Slice(offset, arities[i]);
            offset += arities[i];

            var isInnermost = i == chain.Count - 1;
            containingType = new PublicApiNamedTypeReference(
                level.Namespace,
                level.MetadataName,
                containingType,
                levelTypeArguments,
                level.AssemblyName,
                level.IsValueType,
                isInnermost ? annotation : PublicApiNullableAnnotation.NotAnnotated,
                isInnermost ? tupleElementNames : default,
                isPrimitive: level.IsPrimitive,
                isFromSerializedName: level.IsFromSerializedName);
        }

        return containingType!;
    }

    private PublicApiNullableAnnotation ReadAnnotation()
    {
        var flags = _nullableInfo.Flags;
        byte value;
        if (flags.IsDefaultOrEmpty)
        {
            value = _nullableInfo.ContextFlag;
        }
        else if (flags.Length == 1)
        {
            value = flags[0];
        }
        else if (_nullableIndex < flags.Length)
        {
            value = flags[_nullableIndex];
        }
        else
        {
            value = _nullableInfo.ContextFlag;
        }

        _nullableIndex++;
        return value switch
        {
            1 => PublicApiNullableAnnotation.NotAnnotated,
            2 => PublicApiNullableAnnotation.Annotated,
            _ => PublicApiNullableAnnotation.Oblivious,
        };
    }

    private static PublicApiRefKind GetFunctionPointerRefKind(RawType type, bool isReturnType)
    {
        if (type is not RawType.ByReference byReference)
            return PublicApiRefKind.None;

        if (byReference.HasOutModifier || byReference.ElementType.HasOutModifier)
            return PublicApiRefKind.Out;

        if (byReference.HasRequiresLocationModifier || byReference.ElementType.HasRequiresLocationModifier)
            return PublicApiRefKind.RefReadOnly;

        if (byReference.HasInModifier || byReference.ElementType.HasInModifier || byReference.HasIsReadOnlyModifier || byReference.ElementType.HasIsReadOnlyModifier)
            return isReturnType ? PublicApiRefKind.RefReadOnly : PublicApiRefKind.In;

        return PublicApiRefKind.Ref;
    }

    private static PublicApiCallingConvention GetCallingConvention(SignatureCallingConvention callingConvention)
    {
        return callingConvention switch
        {
            SignatureCallingConvention.CDecl => PublicApiCallingConvention.CDecl,
            SignatureCallingConvention.StdCall => PublicApiCallingConvention.StdCall,
            SignatureCallingConvention.ThisCall => PublicApiCallingConvention.ThisCall,
            SignatureCallingConvention.FastCall => PublicApiCallingConvention.FastCall,
            SignatureCallingConvention.VarArgs => PublicApiCallingConvention.VarArgs,
            SignatureCallingConvention.Unmanaged => PublicApiCallingConvention.Unmanaged,
            _ => PublicApiCallingConvention.Managed,
        };
    }
}
