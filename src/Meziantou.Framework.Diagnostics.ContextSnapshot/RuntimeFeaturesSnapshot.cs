#if NET6_0
#pragma warning disable CA2252 // This API requires opting into preview features
#endif

using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

internal sealed class RuntimeFeaturesSnapshot
{
    public bool IsDynamicCodeCompiled { get; } = RuntimeFeature.IsDynamicCodeCompiled;
    public bool IsDynamicCodeSupported { get; } = RuntimeFeature.IsDynamicCodeSupported;
    public bool PortablePdb { get; } = RuntimeFeature.IsSupported(RuntimeFeature.PortablePdb);
    public bool DefaultImplementationsOfInterfaces { get; } = RuntimeFeature.IsSupported(RuntimeFeature.DefaultImplementationsOfInterfaces);
    public bool UnmanagedSignatureCallingConvention { get; } = RuntimeFeature.IsSupported(RuntimeFeature.UnmanagedSignatureCallingConvention);
    public bool CovariantReturnsOfClasses { get; } = RuntimeFeature.IsSupported(RuntimeFeature.CovariantReturnsOfClasses);

    public bool VirtualStaticsInInterfaces { get; } = RuntimeFeature.IsSupported(RuntimeFeature.VirtualStaticsInInterfaces);
}
