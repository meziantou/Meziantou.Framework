namespace Meziantou.Framework.HumanReadable.Utils;
internal static class TypeUtils
{
    internal static IEnumerable<Type> GetAllInterfaces(this Type type)
    {
        if (type.IsInterface)
            yield return type;

        foreach (var iface in type.GetInterfaces())
        {
            yield return iface;
        }
    }
}
