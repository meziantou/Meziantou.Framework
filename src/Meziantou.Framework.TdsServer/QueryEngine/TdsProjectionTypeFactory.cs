using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace Meziantou.Framework.Tds.QueryEngine;

internal static class TdsProjectionTypeFactory
{
    private static readonly AssemblyBuilder AssemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Meziantou.Framework.Tds.QueryEngine.Projections"), AssemblyBuilderAccess.Run);
    private static readonly ModuleBuilder ModuleBuilder = AssemblyBuilder.DefineDynamicModule("Projections");
    private static readonly Lock Lock = new();
    private static readonly Dictionary<string, Type> Types = [];

    public static Type GetProjectionType(IReadOnlyList<TdsProjectionMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);

        var key = CreateKey("Projection", members);
        lock (Lock)
        {
            if (Types.TryGetValue(key, out var type))
            {
                return type;
            }

            type = CreateType("TdsProjection", members);
            Types.Add(key, type);
            return type;
        }
    }

    public static Type GetCarrierType(IReadOnlyList<TdsProjectionMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);

        var key = CreateKey("Carrier", members);
        lock (Lock)
        {
            if (Types.TryGetValue(key, out var type))
            {
                return type;
            }

            type = CreateType("TdsCarrier", members);
            Types.Add(key, type);
            return type;
        }
    }

    private static Type CreateType(string prefix, IReadOnlyList<TdsProjectionMember> members)
    {
        var typeBuilder = ModuleBuilder.DefineType(
            prefix + Types.Count.ToString(CultureInfo.InvariantCulture),
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed);

        _ = typeBuilder.DefineDefaultConstructor(MethodAttributes.Public);
        foreach (var member in members)
        {
            DefineProperty(typeBuilder, member.Name, member.Type);
        }

        return typeBuilder.CreateType();
    }

    private static void DefineProperty(TypeBuilder typeBuilder, string name, Type type)
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
