using System.Collections.Concurrent;
using System.Reflection;

namespace Meziantou.Framework.HumanReadable.Utils;

internal sealed class FSharpUtils
{
    public static FSharpUtils? Get(Type type)
    {
        try
        {
            // Try not to load the F# dll if not needed
            var attributes = type.GetCustomAttributes(inherit: true);
            foreach (var attribute in attributes)
            {
                var attributeType = attribute.GetType();
                if (attributeType.FullName == "Microsoft.FSharp.Core.CompilationMappingAttribute")
                {
                    var asm = attributeType.Assembly;
                    var info = Get(asm);
                    if (!info.SupportDiscriminatedUnions)
                        return null;

                    return info;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static readonly ConcurrentDictionary<Assembly, FSharpUtils> Instances = new();

    public bool SupportDiscriminatedUnions => IsUnionMethod != null && GetUnionCasesMethod != null && UnionCaseInfo_NameProperty != null && UnionCaseInfo_TagProperty != null && UnionCaseInfo_GetFieldsMethod != null;

    public Type? FsharpType { get; }
    public Type FsharpOptionType { get; }
    public Type FsharpValueOptionType { get; }
    public MethodInfo? IsUnionMethod { get; }
    public MethodInfo? GetUnionCasesMethod { get; }
    public Type? UnionCaseInfoType { get; }
    public PropertyInfo? UnionCaseInfo_NameProperty { get; }
    public PropertyInfo? UnionCaseInfo_TagProperty { get; }
    public MethodInfo? UnionCaseInfo_GetFieldsMethod { get; }

    public FSharpUtils(Assembly assembly)
    {
        FsharpType = assembly.GetType("Microsoft.FSharp.Reflection.FSharpType", throwOnError: false);
        FsharpOptionType = assembly.GetType("Microsoft.FSharp.Core.FSharpOption`1", throwOnError: false)!;
        FsharpValueOptionType = assembly.GetType("Microsoft.FSharp.Core.FSharpValueOption`1", throwOnError: false)!;
        IsUnionMethod = FsharpType?.GetMethod("IsUnion", BindingFlags.Public | BindingFlags.Static);
        GetUnionCasesMethod = FsharpType?.GetMethod("GetUnionCases", BindingFlags.Public | BindingFlags.Static);
        UnionCaseInfoType = assembly.GetType("Microsoft.FSharp.Reflection.UnionCaseInfo", throwOnError: false);
        UnionCaseInfo_NameProperty = UnionCaseInfoType?.GetProperty("Name");
        UnionCaseInfo_TagProperty = UnionCaseInfoType?.GetProperty("Tag");
        UnionCaseInfo_GetFieldsMethod = UnionCaseInfoType?.GetMethod("GetFields");

    }

    public static FSharpUtils Get(Assembly assembly)
    {
        return Instances.GetOrAdd(assembly, assembly => new FSharpUtils(assembly));
    }

    public bool IsUnionType(Type type)
    {
        try
        {
            if (IsUnionMethod != null)
                return IsUnionMethod.Invoke(obj: null, [type, null]) is true;
        }
        catch
        {
        }

        return false;
    }
    
    public UnionCaseInfo[]? GetUnionCases(Type type)
    {
        try
        {
            if (GetUnionCasesMethod != null)
            {
                var array = (object[])GetUnionCasesMethod.Invoke(obj: null, [type, null])!;
                return array.Select(o => new UnionCaseInfo(this, o)).ToArray();
            }

        }
        catch
        {
        }

        return null;
    }

    public UnionCaseInfo? GetUnionCase(Type type, object value)
    {
        try
        {
            var unionCases = GetUnionCases(type);
            if(unionCases is null)
                return null;

            var tag = value.GetType().GetProperty("Tag")?.GetValue(value);
            if (tag is null)
                return null;

            foreach (var unionCase in unionCases)
            {
                if (unionCase.Tag == (int)tag)
                {
                    return unionCase;
                }
            }
        }
        catch
        {
        }

        return null;
    }
}
