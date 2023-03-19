using System.Collections.Concurrent;
using System.Reflection;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class DiscriminatedUnionConverter : HumanReadableConverter
{
    public override bool CanConvert(Type type)
    {
        var utils = GetFsharpUtils(type);
        return utils?.IsUnionType(type) is true;
    }

    public override void WriteValue(HumanReadableTextWriter writer, object? value, HumanReadableSerializerOptions options)
    {
        var type = value.GetType();
        var info = GetFsharpUtils(type)!;
        var unionCase = info.GetUnionCase(type, value)!;

        writer.StartObject();
        writer.WritePropertyName("Tag");
        writer.WriteValue(unionCase.Name);

        foreach (var field in unionCase.GetFields())
        {
            writer.WritePropertyName(field.Name);

            var propertyValue = field.GetValue(value);
            var converter = options.GetConverter(propertyValue?.GetType() ?? field.PropertyType);
            converter.WriteValue(writer, propertyValue, options);
        }

        writer.EndObject();
    }

    private static FSharpUtils? GetFsharpUtils(Type type)
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
                    var info = FSharpUtils.Get(asm);
                    if (!info.IsValid)
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

    private sealed class UnionCaseInfo
    {
        private readonly FSharpUtils _utils;
        private readonly object _inner;

        public UnionCaseInfo(FSharpUtils utils, object inner)
        {
            _utils = utils;
            _inner = inner;
        }

        public string Name
        {
            get
            {
                try
                {
                    return _utils.UnionCaseInfo_NameProperty!.GetValue(_inner, index: null) as string;
                }
                catch
                {
                    return null;
                }
            }
        }

        public int Tag
        {
            get
            {
                try
                {
                    if (_utils.UnionCaseInfo_TagProperty!.GetValue(_inner, index: null) is int i)
                        return i;
                }
                catch
                {
                }

                return -1;
            }
        }

        public PropertyInfo[] GetFields()
        {
            return (PropertyInfo[])_utils.UnionCaseInfo_GetFieldsMethod.Invoke(_inner, parameters: null);
        }
    }

    private sealed class FSharpUtils
    {
        private static readonly ConcurrentDictionary<Assembly, FSharpUtils> Instances = new();

        public bool IsValid => IsUnionMethod != null && GetUnionCasesMethod != null && UnionCaseInfo_NameProperty != null && UnionCaseInfo_TagProperty != null && UnionCaseInfo_GetFieldsMethod != null;

        public Type? FsharpType { get; }
        public MethodInfo? IsUnionMethod { get; }
        public MethodInfo? GetUnionCasesMethod { get; }
        public Type? UnionCaseInfoType { get; }
        public PropertyInfo? UnionCaseInfo_NameProperty { get; }
        public PropertyInfo? UnionCaseInfo_TagProperty { get; }
        public MethodInfo? UnionCaseInfo_GetFieldsMethod { get; }

        public FSharpUtils(Assembly assembly)
        {
            FsharpType = assembly.GetType("Microsoft.FSharp.Reflection.FSharpType", throwOnError: false);
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
                    return IsUnionMethod.Invoke(obj: null, new object[] { type, null }) is true;

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
                    var array = (object[])GetUnionCasesMethod.Invoke(obj: null, new object[] { type, null });
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
                var tag = (int)value.GetType().GetProperty("Tag")?.GetValue(value);
                foreach (var unionCase in unionCases)
                {
                    if (unionCase.Tag == tag)
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
}
