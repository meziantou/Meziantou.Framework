namespace Meziantou.Framework.PublicApiGenerator;

internal static class CSharpTypeFormatter
{
    public static string FormatFunctionPointer(bool isUnmanaged, IReadOnlyList<string> parameterTypes, string returnType)
    {
        // The specific unmanaged calling conventions are not emitted: they are modopts that
        // Type.GetFunctionPointerCallingConventions returns empty for, so the reflection reader
        // cannot see them and the two readers would disagree.
        var sb = new StringBuilder("delegate*");
        if (isUnmanaged)
        {
            sb.Append(" unmanaged");
        }

        sb.Append('<');
        foreach (var parameterType in parameterTypes)
        {
            sb.Append(parameterType);
            sb.Append(", ");
        }

        sb.Append(returnType);
        sb.Append('>');
        return sb.ToString();
    }

    public static string NormalizeWellKnownTypeName(string typeFullName)
    {
        // decimal has no metadata primitive type code, so it reaches the readers as an ordinary type reference
        return string.Equals(typeFullName, "System.Decimal", StringComparison.Ordinal) ? "decimal" : typeFullName;
    }
}
