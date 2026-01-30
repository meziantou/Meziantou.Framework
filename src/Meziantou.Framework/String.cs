namespace Meziantou.Framework;

#if PUBLIC_STRING_EXTENSIONS
public
#else
internal
#endif
#pragma warning disable CA1720 // Identifier contains type name - This is intentional for extension type
static partial class String
{
    public static bool EqualsIgnoreCase(string? a, string? b)
    {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
