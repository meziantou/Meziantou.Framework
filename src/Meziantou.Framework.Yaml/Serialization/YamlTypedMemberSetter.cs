using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>
/// Reads the value of a member with its typed converter and assigns it with a compiled setter: a value type is neither
/// boxed by the converter nor unboxed by the setter.
/// </summary>
internal abstract class YamlTypedMemberSetter
{
    /// <summary>Reads the value of the member at the position of the reader and assigns it.</summary>
    /// <remarks>
    /// Like the boxed path, an exception other than a <see cref="YamlException"/> is reported at the location of the key,
    /// and a null value is rejected for a member that does not accept it.
    /// </remarks>
    public abstract void ReadAndSet(YamlReader reader, object instance, in YamlLocation keyLocation);

    /// <summary>Compiles the setter of a member whose converter is typed for its exact type, or returns <see langword="null"/>.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "The generic argument is the type of a member of a type the reflection-based contract already keeps.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Guarded by RuntimeFeature.IsDynamicCodeSupported.")]
    public static YamlTypedMemberSetter? Create(YamlConverter converter, MemberInfo member, Type memberType, Type declaringType, string memberName, bool disallowNull)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported || member.DeclaringType is not { IsValueType: false, IsInterface: false } memberDeclaringType || memberDeclaringType.ContainsGenericParameters)
            return null;

        if (memberType.IsByRef || memberType.IsPointer || memberType.IsByRefLike || memberType.ContainsGenericParameters)
            return null;

        // The converter returns values of the type of the member
        if (!typeof(YamlConverter<>).MakeGenericType(memberType).IsInstanceOfType(converter))
            return null;

        var create = typeof(YamlTypedMemberSetter).GetMethod(nameof(CreateCore), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(memberType);
        return (YamlTypedMemberSetter?)create.Invoke(null, [converter, member, memberDeclaringType, declaringType, memberName, disallowNull]);
    }

    private static YamlTypedMemberSetter<TValue>? CreateCore<TValue>(YamlConverter<TValue> converter, MemberInfo member, Type memberDeclaringType, Type declaringType, string memberName, bool disallowNull)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            return null;

        MethodInfo? setMethod = null;
        FieldInfo? field = null;
        switch (member)
        {
            case PropertyInfo property when property.SetMethod is { } propertySetter && property.GetIndexParameters().Length == 0:
                setMethod = propertySetter;
                break;

            // A read-only field can only be set by reflection
            case FieldInfo { IsInitOnly: false, IsLiteral: false } fieldInfo:
                field = fieldInfo;
                break;

            default:
                return null;
        }

        var method = new DynamicMethod("set_" + member.Name, null, [typeof(object), typeof(TValue)], memberDeclaringType, skipVisibility: true);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, memberDeclaringType);
        il.Emit(OpCodes.Ldarg_1);
        if (setMethod is not null)
        {
            il.Emit(setMethod.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, setMethod);
        }
        else
        {
            il.Emit(OpCodes.Stfld, field!);
        }

        il.Emit(OpCodes.Ret);
        return new YamlTypedMemberSetter<TValue>(converter, method.CreateDelegate<Action<object, TValue?>>(), declaringType, memberName, disallowNull);
    }
}

internal sealed class YamlTypedMemberSetter<TValue>(YamlConverter<TValue> converter, Action<object, TValue?> setter, Type declaringType, string memberName, bool disallowNull) : YamlTypedMemberSetter
{
    public override void ReadAndSet(YamlReader reader, object instance, in YamlLocation keyLocation)
    {
        TValue? value;
        try
        {
            value = converter.Read(reader);
        }
        catch (YamlException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new YamlException(reader.SourceName, keyLocation.Start, keyLocation.End, exception.Message, exception);
        }

        if (value is null && disallowNull && reader.Options.RespectNullableAnnotations)
            throw YamlThrowHelper.ThrowNullForNonNullableMember(reader, declaringType, memberName);

        try
        {
            setter(instance, value);
        }
        catch (YamlException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new YamlException(reader.SourceName, keyLocation.Start, keyLocation.End, exception.Message, exception);
        }
    }
}
