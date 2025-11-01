using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for <see cref="SafeHandle"/>.
/// </summary>
/// <example>
/// <code>
/// using var scope = safeHandle.CreateHandleScope();
/// nint handle = scope.Value;
/// // Use handle safely
/// </code>
/// </example>
public static class SafeHandleExtensions
{
    /// <summary>Creates a scope that safely increments and decrements the reference count of a safe handle.</summary>
    public static SafeHandleValue CreateHandleScope(this SafeHandle safeHandle)
    {
        return new SafeHandleValue(safeHandle);
    }
}
