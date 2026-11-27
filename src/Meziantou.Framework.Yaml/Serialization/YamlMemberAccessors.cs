using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>
/// Compiles the accessors of the members and the constructors of the reflection-based contracts with Reflection.Emit,
/// like System.Text.Json does: a call to a compiled delegate costs a fraction of <see cref="PropertyInfo.SetValue(object, object)"/>.
/// </summary>
/// <remarks>
/// The methods return <see langword="null"/> when the runtime does not support dynamic code, such as NativeAOT, or for a
/// member they do not handle: a member of a struct, which a delegate would update on a copy, or a read-only field. The
/// caller then uses reflection.
/// </remarks>
internal static class YamlMemberAccessors
{
    public static Func<object, object?>? CreateGetter(MemberInfo member)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported || member.DeclaringType is not { IsValueType: false } declaringType || declaringType.ContainsGenericParameters)
            return null;

        Type memberType;
        MethodInfo? getMethod = null;
        FieldInfo? field = null;
        switch (member)
        {
            case PropertyInfo property when property.GetMethod is { } propertyGetter && property.GetIndexParameters().Length == 0:
                getMethod = propertyGetter;
                memberType = property.PropertyType;
                break;

            case FieldInfo fieldInfo:
                field = fieldInfo;
                memberType = fieldInfo.FieldType;
                break;

            default:
                return null;
        }

        if (memberType.IsByRef || memberType.IsPointer || memberType.IsByRefLike)
            return null;

        var method = new DynamicMethod("get_" + member.Name, typeof(object), [typeof(object)], declaringType, skipVisibility: true);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, declaringType);
        if (getMethod is not null)
        {
            il.Emit(getMethod.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, getMethod);
        }
        else
        {
            il.Emit(OpCodes.Ldfld, field!);
        }

        if (memberType.IsValueType)
        {
            il.Emit(OpCodes.Box, memberType);
        }

        il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Func<object, object?>>();
    }

    public static Action<object, object?>? CreateSetter(MemberInfo member)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported || member.DeclaringType is not { IsValueType: false } declaringType || declaringType.ContainsGenericParameters)
            return null;

        Type memberType;
        MethodInfo? setMethod = null;
        FieldInfo? field = null;
        switch (member)
        {
            case PropertyInfo property when property.SetMethod is { } propertySetter && property.GetIndexParameters().Length == 0:
                setMethod = propertySetter;
                memberType = property.PropertyType;
                break;

            // A read-only field can only be set by reflection
            case FieldInfo { IsInitOnly: false, IsLiteral: false } fieldInfo:
                field = fieldInfo;
                memberType = fieldInfo.FieldType;
                break;

            default:
                return null;
        }

        if (memberType.IsByRef || memberType.IsPointer || memberType.IsByRefLike)
            return null;

        var method = new DynamicMethod("set_" + member.Name, null, [typeof(object), typeof(object)], declaringType, skipVisibility: true);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, declaringType);
        EmitConvertArgument(il, 1, memberType);
        if (setMethod is not null)
        {
            il.Emit(setMethod.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, setMethod);
        }
        else
        {
            il.Emit(OpCodes.Stfld, field!);
        }

        il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Action<object, object?>>();
    }

    /// <summary>Compiles a call to a parameterless constructor of a class.</summary>
    public static Func<object>? CreateConstructor(ConstructorInfo constructor)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported || constructor.DeclaringType is not { IsValueType: false, IsAbstract: false } declaringType || declaringType.ContainsGenericParameters || constructor.GetParameters().Length != 0)
            return null;

        var method = new DynamicMethod("new_" + declaringType.Name, typeof(object), Type.EmptyTypes, declaringType, skipVisibility: true);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Newobj, constructor);
        il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Func<object>>();
    }

    // Like reflection, a null value of a value type is its default value
    private static void EmitConvertArgument(ILGenerator il, short argument, Type type)
    {
        il.Emit(OpCodes.Ldarg, argument);
        if (!type.IsValueType)
        {
            il.Emit(OpCodes.Castclass, type);
            return;
        }

        if (Nullable.GetUnderlyingType(type) is not null)
        {
            il.Emit(OpCodes.Unbox_Any, type);
            return;
        }

        var notNull = il.DefineLabel();
        var end = il.DefineLabel();
        il.Emit(OpCodes.Brtrue_S, notNull);
        var defaultValue = il.DeclareLocal(type);
        il.Emit(OpCodes.Ldloca_S, defaultValue);
        il.Emit(OpCodes.Initobj, type);
        il.Emit(OpCodes.Ldloc, defaultValue);
        il.Emit(OpCodes.Br_S, end);
        il.MarkLabel(notNull);
        il.Emit(OpCodes.Ldarg, argument);
        il.Emit(OpCodes.Unbox_Any, type);
        il.MarkLabel(end);
    }
}
