using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The last branch of the generic kernels that dispatch on their type arguments: the type tests are JIT-time constants, so
/// the call is removed from the instantiations a kernel handles, and an instantiation it does not handle throws instead
/// of reinterpreting a value of another type.
/// </summary>
internal static class UnsupportedType
{
    /// <summary>Throws an <see cref="UnreachableException"/> for a type argument a kernel does not handle.</summary>
    /// <typeparam name="T">The type argument.</typeparam>
    /// <typeparam name="TResult">The return type of the kernel, so the call can end an expression.</typeparam>
    /// <returns>Never returns.</returns>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static TResult Throw<T, TResult>() => throw Create<T>();

    /// <summary>Creates the exception of a type argument a kernel does not handle, for a <see langword="throw"/> statement.</summary>
    /// <typeparam name="T">The type argument.</typeparam>
    /// <returns>The exception.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static UnreachableException Create<T>() => new($"The type {typeof(T).Name} is not handled by this kernel.");
}
