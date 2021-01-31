#if NET5_0
#elif NET461 || NET472 || NETSTANDARD2_0 || NETSTANDARD2_1 || NETCOREAPP3_1
#pragma warning disable MA0048
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#else
#error Platform not supported
#endif
