using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

namespace Meziantou.Framework.Tds.QueryEngine;

[RequiresUnreferencedCode(TdsQueryEngine.RequiresUnreferencedCodeMessage)]
[RequiresDynamicCode(TdsQueryEngine.RequiresDynamicCodeMessage)]
internal static class TdsProjectionTypeFactory
{
    /// <summary>
    /// Column aliases come from client SQL, so the number of distinct projection shapes is client-controlled.
    /// Emitted types are held alive only while they are among the most recently emitted ones, which bounds what
    /// a client can pin in the process; anything older is reclaimed once no query references it anymore.
    /// </summary>
    private const int MaxRetainedTypes = 1024;

    /// <summary>
    /// A collectible assembly is reclaimed only once every type it holds became unreachable, so types are packed
    /// into assemblies of a bounded size: one assembly per type would multiply the loader overhead, and a single
    /// assembly for all of them could never be reclaimed.
    /// </summary>
    private const int TypesPerAssembly = 64;

    private static readonly Lock CreateLock = new();

    /// <summary>
    /// The references are weak so that dropping a type from <see cref="RetainedTypes"/> makes it collectable,
    /// while a shape that is still in use anywhere keeps resolving to the same type. Translation compares
    /// element types by identity -- UNION requires both sides to project the same type -- so a live shape must
    /// never be emitted twice.
    /// </summary>
    private static readonly ConcurrentDictionary<string, WeakReference<Type>> Types = new(StringComparer.Ordinal);

    /// <summary>Keeps the most recently emitted types alive so that repeated queries do not re-emit them.</summary>
    private static readonly Queue<Type> RetainedTypes = new();

    /// <summary>The assemblies whose access checks the current emitted assembly ignores.</summary>
    private static readonly HashSet<string> IgnoredAccessCheckAssemblies = new(StringComparer.Ordinal);

    private static AssemblyBuilder? s_assemblyBuilder;
    private static ModuleBuilder? s_moduleBuilder;
    private static ConstructorInfo? s_ignoresAccessChecksToConstructor;
    private static int s_moduleTypeCount;
    private static int s_nextAssemblyId;
    private static int s_nextTypeId;
    private static int s_sweepThreshold = MaxRetainedTypes * 2;

    public static Type GetProjectionType(IReadOnlyList<TdsProjectionMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        ValidateMemberNames(members);

        return GetOrCreateType(CreateKey("Projection", members), "TdsProjection", members);
    }

    public static Type GetCarrierType(IReadOnlyList<TdsProjectionMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        ValidateMemberNames(members);

        return GetOrCreateType(CreateKey("Carrier", members), "TdsCarrier", members);
    }

    private static void ValidateMemberNames(IReadOnlyList<TdsProjectionMember> members)
    {
        // The names are column and table aliases from client SQL, such as SELECT Id '' or SELECT Id AS [], and
        // TypeBuilder.DefineProperty rejects an empty name or one that starts with a null character. Metadata
        // names are null-terminated, so a name with a null character further in would be truncated and the
        // property could no longer be found by its name.
        foreach (var member in members)
        {
            if (member.Name is "" || member.Name[0] is '\0')
            {
                throw new TdsQueryEngineException("An object or column name is missing or empty.");
            }

            if (member.Name.Contains('\0', StringComparison.Ordinal))
            {
                throw new TdsQueryEngineException("An object or column name contains a null character.");
            }
        }
    }

    private static Type GetOrCreateType(string key, string prefix, IReadOnlyList<TdsProjectionMember> members)
    {
        // Cache hits take no lock, so concurrent queries over known shapes do not serialize on type creation.
        if (TryGetLiveType(key, out var type))
        {
            return type;
        }

        lock (CreateLock)
        {
            if (TryGetLiveType(key, out type))
            {
                return type;
            }

            type = CreateType(prefix, members);
            Types[key] = new WeakReference<Type>(type);

            RetainedTypes.Enqueue(type);
            if (RetainedTypes.Count > MaxRetainedTypes)
            {
                _ = RetainedTypes.Dequeue();
            }

            // A reclaimed type leaves its key behind, and keys embed client-provided aliases, so sweep them once
            // the map grew well past what is retained. The next sweep only runs after the map doubled again, so
            // sweeping stays amortized when the collector has not run yet and there is nothing to remove.
            if (Types.Count >= s_sweepThreshold)
            {
                RemoveReclaimedTypes();
                s_sweepThreshold = Math.Max(MaxRetainedTypes * 2, Types.Count * 2);
            }

            return type;
        }
    }

    private static bool TryGetLiveType(string key, [NotNullWhen(true)] out Type? type)
    {
        if (Types.TryGetValue(key, out var reference) && reference.TryGetTarget(out type))
        {
            return true;
        }

        type = null;
        return false;
    }

    private static void RemoveReclaimedTypes()
    {
        foreach (var (key, reference) in Types)
        {
            if (!reference.TryGetTarget(out _))
            {
                // Compare the reference too: the entry may have been replaced by a freshly emitted type.
                _ = Types.TryRemove(new KeyValuePair<string, WeakReference<Type>>(key, reference));
            }
        }
    }

    private static Type CreateType(string prefix, IReadOnlyList<TdsProjectionMember> members)
    {
        var typeName = prefix + s_nextTypeId.ToString(CultureInfo.InvariantCulture);
        s_nextTypeId++;

        var moduleBuilder = GetModuleBuilder();
        foreach (var member in members)
        {
            EnsureTypeIsAccessible(member.Type);
        }

        var typeBuilder = moduleBuilder.DefineType(
            typeName,
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

    private static ModuleBuilder GetModuleBuilder()
    {
        if (s_moduleBuilder is null || s_moduleTypeCount >= TypesPerAssembly)
        {
            // Each assembly needs its own name: a type that references types from other assemblies records one
            // assembly reference per name, so all the assemblies sharing a name would resolve to the same one
            // and loading the type would fail with a TypeLoadException.
            var assemblyName = "Meziantou.Framework.Tds.QueryEngine.Projections" + s_nextAssemblyId.ToString(CultureInfo.InvariantCulture);
            s_nextAssemblyId++;
            s_assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName(assemblyName),
                AssemblyBuilderAccess.RunAndCollect);
            s_moduleBuilder = s_assemblyBuilder.DefineDynamicModule("Projections");
            s_moduleTypeCount = 0;
            s_ignoresAccessChecksToConstructor = null;
            IgnoredAccessCheckAssemblies.Clear();
        }

        s_moduleTypeCount++;
        return s_moduleBuilder;
    }

    /// <summary>
    /// Columns can have non-public types, such as the result of xml.query or a property of an internal enum type
    /// on a row type of the application. The emitted Equals and GetHashCode methods instantiate
    /// <see cref="EqualityComparer{T}"/> over the column types, which fails the access checks for such types unless
    /// the emitted assembly declares that it ignores the access checks to the assembly that defines them.
    /// </summary>
    private static void EnsureTypeIsAccessible(Type type)
    {
        if (type.IsVisible)
        {
            return;
        }

        if (type.HasElementType)
        {
            EnsureTypeIsAccessible(type.GetElementType()!);
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                EnsureTypeIsAccessible(argument);
            }
        }

        var assemblyName = type.Assembly.GetName().Name;
        if (assemblyName is null || !IgnoredAccessCheckAssemblies.Add(assemblyName))
        {
            return;
        }

        s_ignoresAccessChecksToConstructor ??= DefineIgnoresAccessChecksToAttribute(s_moduleBuilder!);
        s_assemblyBuilder!.SetCustomAttribute(new CustomAttributeBuilder(s_ignoresAccessChecksToConstructor, [assemblyName]));
    }

    /// <summary>
    /// The runtime recognizes the attribute by its name, and it is not part of the public API surface, so each
    /// emitted assembly declares its own copy.
    /// </summary>
    private static ConstructorInfo DefineIgnoresAccessChecksToAttribute(ModuleBuilder moduleBuilder)
    {
        var attributeBuilder = moduleBuilder.DefineType(
            "System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed,
            typeof(Attribute));

        var attributeUsageConstructor = typeof(AttributeUsageAttribute).GetConstructor([typeof(AttributeTargets)])!;
        var allowMultipleProperty = typeof(AttributeUsageAttribute).GetProperty(nameof(AttributeUsageAttribute.AllowMultiple))!;
        attributeBuilder.SetCustomAttribute(new CustomAttributeBuilder(attributeUsageConstructor, [AttributeTargets.Assembly], [allowMultipleProperty], [true]));

        var constructorBuilder = attributeBuilder.DefineConstructor(
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.HasThis,
            [typeof(string)]);
        var il = constructorBuilder.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(Attribute).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        return attributeBuilder.CreateType().GetConstructor([typeof(string)])!;
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
