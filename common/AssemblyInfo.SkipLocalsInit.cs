[module: System.Runtime.CompilerServices.SkipLocalsInit]

#if NET5_0_OR_GREATER
#elif NET461 || NET472 || NETSTANDARD2_0 || NETSTANDARD2_1 || NETCOREAPP3_1
#pragma warning disable MA0048
namespace System.Runtime.CompilerServices;

[AttributeUsage(AttributeTargets.Module
    | AttributeTargets.Class
    | AttributeTargets.Struct
    | AttributeTargets.Interface
    | AttributeTargets.Constructor
    | AttributeTargets.Method
    | AttributeTargets.Property
    | AttributeTargets.Event, Inherited = false)]
internal sealed class SkipLocalsInitAttribute : Attribute
{
    public SkipLocalsInitAttribute()
    {
    }
}
#else
#error Platform not supported
#endif
