using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

namespace Meziantou.Framework.Tds.QueryEngine;

/// <summary>The emitted projection types, keyed by shape and capped at a maximum number of distinct shapes.</summary>
internal sealed class TdsProjectionTypeCache
{
    private static readonly AssemblyBuilder AssemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Meziantou.Framework.Tds.QueryEngine.Projections"), AssemblyBuilderAccess.Run);
    private static readonly ModuleBuilder ModuleBuilder = AssemblyBuilder.DefineDynamicModule("Projections");

    // Every cache emits into the same module, so the name suffix has to be unique across all of them.
    private static int s_emittedTypeCount;

    private readonly int _maxCachedTypes;
    private readonly Lock _createLock = new();
    private readonly ConcurrentDictionary<string, Type> _types = new(StringComparer.Ordinal);
    private int _createdTypeCount;

    public TdsProjectionTypeCache(int maxCachedTypes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCachedTypes);

        _maxCachedTypes = maxCachedTypes;
    }

    public Type GetProjectionType(IReadOnlyList<TdsProjectionMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);

        return GetOrCreateType(CreateKey("Projection", members), "TdsProjection", members);
    }

    public Type GetCarrierType(IReadOnlyList<TdsProjectionMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);

        return GetOrCreateType(CreateKey("Carrier", members), "TdsCarrier", members);
    }

    private Type GetOrCreateType(string key, string prefix, IReadOnlyList<TdsProjectionMember> members)
    {
        // Cache hits take no lock, so concurrent queries over known shapes do not serialize on type creation.
        if (_types.TryGetValue(key, out var type))
        {
            return type;
        }

        lock (_createLock)
        {
            if (_types.TryGetValue(key, out type))
            {
                return type;
            }

            if (_createdTypeCount >= _maxCachedTypes)
            {
                throw new TdsQueryEngineException($"The server reached its limit of {_maxCachedTypes} distinct query projection shapes.");
            }

            type = CreateType(prefix, Interlocked.Increment(ref s_emittedTypeCount), members);
            _createdTypeCount++;
            _types[key] = type;
            return type;
        }
    }

    private static Type CreateType(string prefix, int index, IReadOnlyList<TdsProjectionMember> members)
    {
        var typeBuilder = ModuleBuilder.DefineType(
            prefix + index.ToString(CultureInfo.InvariantCulture),
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed);

        _ = typeBuilder.DefineDefaultConstructor(MethodAttributes.Public);
        var fields = new List<FieldBuilder>(members.Count);
        foreach (var member in members)
        {
            fields.Add(DefineProperty(typeBuilder, member.Name, member.Type));
        }

        DefineEqualsMethod(typeBuilder, fields);
        DefineGetHashCodeMethod(typeBuilder, fields);

        return typeBuilder.CreateType();
    }

    private static FieldBuilder DefineProperty(TypeBuilder typeBuilder, string name, Type type)
    {
        var fieldBuilder = typeBuilder.DefineField("_" + name, type, FieldAttributes.Private);
        var propertyBuilder = typeBuilder.DefineProperty(name, PropertyAttributes.HasDefault, type, null);

        var getMethod = typeBuilder.DefineMethod(
            "get_" + name,
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            type,
            Type.EmptyTypes);
        var getIl = getMethod.GetILGenerator();
        getIl.Emit(OpCodes.Ldarg_0);
        getIl.Emit(OpCodes.Ldfld, fieldBuilder);
        getIl.Emit(OpCodes.Ret);

        var setMethod = typeBuilder.DefineMethod(
            "set_" + name,
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            null,
            [type]);
        var setIl = setMethod.GetILGenerator();
        setIl.Emit(OpCodes.Ldarg_0);
        setIl.Emit(OpCodes.Ldarg_1);
        setIl.Emit(OpCodes.Stfld, fieldBuilder);
        setIl.Emit(OpCodes.Ret);

        propertyBuilder.SetGetMethod(getMethod);
        propertyBuilder.SetSetMethod(setMethod);
        return fieldBuilder;
    }

    private static void DefineEqualsMethod(TypeBuilder typeBuilder, IReadOnlyList<FieldBuilder> fields)
    {
        var equalsMethod = typeBuilder.DefineMethod(
            nameof(Equals),
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            typeof(bool),
            [typeof(object)]);
        var il = equalsMethod.GetILGenerator();
        var other = il.DeclareLocal(typeBuilder);
        var hasOtherLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeBuilder);
        il.Emit(OpCodes.Stloc, other);
        il.Emit(OpCodes.Ldloc, other);
        il.Emit(OpCodes.Brtrue_S, hasOtherLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(hasOtherLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        foreach (var field in fields)
        {
            var comparerType = typeof(EqualityComparer<>).MakeGenericType(field.FieldType);
            var defaultGetter = comparerType.GetProperty(nameof(EqualityComparer<object>.Default), BindingFlags.Public | BindingFlags.Static)!.GetMethod!;
            var equalsMethodInfo = comparerType.GetMethod(nameof(EqualityComparer<object>.Equals), [field.FieldType, field.FieldType])!;

            il.Emit(OpCodes.Call, defaultGetter);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ldloc, other);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Callvirt, equalsMethodInfo);
            il.Emit(OpCodes.And);
        }

        il.Emit(OpCodes.Ret);
        typeBuilder.DefineMethodOverride(equalsMethod, typeof(object).GetMethod(nameof(Equals), [typeof(object)])!);
    }

    private static void DefineGetHashCodeMethod(TypeBuilder typeBuilder, IReadOnlyList<FieldBuilder> fields)
    {
        var getHashCodeMethod = typeBuilder.DefineMethod(
            nameof(GetHashCode),
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            typeof(int),
            Type.EmptyTypes);
        var il = getHashCodeMethod.GetILGenerator();

        il.Emit(OpCodes.Ldc_I4_S, 17);
        foreach (var field in fields)
        {
            var comparerType = typeof(EqualityComparer<>).MakeGenericType(field.FieldType);
            var defaultGetter = comparerType.GetProperty(nameof(EqualityComparer<object>.Default), BindingFlags.Public | BindingFlags.Static)!.GetMethod!;
            var getHashCodeMethodInfo = comparerType.GetMethod(nameof(EqualityComparer<object>.GetHashCode), [field.FieldType])!;

            il.Emit(OpCodes.Ldc_I4_S, 31);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Call, defaultGetter);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Callvirt, getHashCodeMethodInfo);
            il.Emit(OpCodes.Add);
        }

        il.Emit(OpCodes.Ret);
        typeBuilder.DefineMethodOverride(getHashCodeMethod, typeof(object).GetMethod(nameof(GetHashCode), Type.EmptyTypes)!);
    }

    private static string CreateKey(string prefix, IReadOnlyList<TdsProjectionMember> members)
    {
        var builder = new StringBuilder(prefix);
        foreach (var member in members)
        {
            _ = builder.Append('|').Append(member.Name).Append(':').Append(member.Type.AssemblyQualifiedName);
        }

        return builder.ToString();
    }
}
