using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>
/// Gets the value of a member with a compiled getter and writes it with its typed converter: a value type is neither
/// boxed by the getter nor unboxed by the converter.
/// </summary>
internal abstract class YamlTypedMemberWriter
{
    /// <summary>Writes the key and the value of the member of <paramref name="instance"/>, unless its ignore condition skips the value.</summary>
    /// <remarks>
    /// Like the boxed path, a null value is rejected for a member that does not accept it before the ignore condition is
    /// evaluated.
    /// </remarks>
    public abstract void Write(YamlWriter writer, object instance);

    /// <summary>Compiles the getter of a member whose converter is typed for its exact type, or returns <see langword="null"/>.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "The generic argument is the type of a member of a type the reflection-based contract already keeps.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Guarded by RuntimeFeature.IsDynamicCodeSupported.")]
    public static YamlTypedMemberWriter? Create(YamlConverter converter, MemberInfo member, Type memberType, in YamlMemberWriteInfo info)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported || member.DeclaringType is not { IsValueType: false, IsInterface: false } memberDeclaringType || memberDeclaringType.ContainsGenericParameters)
            return null;

        if (memberType.IsByRef || memberType.IsPointer || memberType.IsByRefLike || memberType.ContainsGenericParameters)
            return null;

        // The converter writes values of the type of the member
        if (!typeof(YamlConverter<>).MakeGenericType(memberType).IsInstanceOfType(converter))
            return null;

        var create = typeof(YamlTypedMemberWriter).GetMethod(nameof(CreateCore), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(memberType);
        return (YamlTypedMemberWriter?)create.Invoke(null, [converter, member, memberDeclaringType, info]);
    }

    private static YamlTypedMemberWriter<TValue>? CreateCore<TValue>(YamlConverter<TValue> converter, MemberInfo member, Type memberDeclaringType, YamlMemberWriteInfo info)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            return null;

        MethodInfo? getMethod = null;
        FieldInfo? field = null;
        switch (member)
        {
            case PropertyInfo property when property.GetMethod is { } propertyGetter && property.GetIndexParameters().Length == 0:
                getMethod = propertyGetter;
                break;

            case FieldInfo fieldInfo:
                field = fieldInfo;
                break;

            default:
                return null;
        }

        var method = new DynamicMethod("get_" + member.Name, typeof(TValue), [typeof(object)], memberDeclaringType, skipVisibility: true);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, memberDeclaringType);
        if (getMethod is not null)
        {
            il.Emit(getMethod.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, getMethod);
        }
        else
        {
            il.Emit(OpCodes.Ldfld, field!);
        }

        il.Emit(OpCodes.Ret);
        return new YamlTypedMemberWriter<TValue>(converter, method.CreateDelegate<Func<object, TValue>>(), info);
    }
}

internal sealed class YamlTypedMemberWriter<TValue>(YamlConverter<TValue> converter, Func<object, TValue> getter, YamlMemberWriteInfo info) : YamlTypedMemberWriter
{
    public override void Write(YamlWriter writer, object instance)
    {
        var value = getter(instance);
        var options = writer.Options;
        if (value is null && info.DisallowNull && options.RespectNullableAnnotations)
            throw YamlThrowHelper.ThrowNullForNonNullableMember(info.DeclaringType, info.Name);

        switch (info.IgnoreCondition ?? options.DefaultIgnoreCondition)
        {
            case YamlIgnoreCondition.Always or YamlIgnoreCondition.WhenWriting:
            case YamlIgnoreCondition.WhenWritingNull when value is null:
                return;

            // The value is compared with the default value of its type with object.Equals, like the boxed path does
            case YamlIgnoreCondition.WhenWritingDefault when value is null || (typeof(TValue).IsValueType && ((object)value).Equals(default(TValue))):
                return;
        }

        writer.WritePropertyName(info.Name);
        using var styleScope = writer.PushBlockSequenceItemStyle(info.BlockSequenceMappingStyle, info.BlockSequenceSequenceStyle);
        using var stringStyleScope = writer.PushStringStyle(info.StringStyle);
        converter.Write(writer, value);
    }
}
