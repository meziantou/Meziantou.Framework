using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

internal readonly record struct RawGenericContext(ImmutableArray<string> TypeParameterNames, ImmutableArray<string> MethodParameterNames)
{
    public string GetMethodParameterName(int index)
    {
        return index >= 0 && !MethodParameterNames.IsDefault && index < MethodParameterNames.Length
            ? MethodParameterNames[index]
            : "TMethod" + index.ToString(CultureInfo.InvariantCulture);
    }

    public string GetTypeParameterName(int index)
    {
        return index >= 0 && !TypeParameterNames.IsDefault && index < TypeParameterNames.Length
            ? TypeParameterNames[index]
            : "T" + index.ToString(CultureInfo.InvariantCulture);
    }
}
