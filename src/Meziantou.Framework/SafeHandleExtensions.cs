using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for <see cref="SafeHandle"/>.
/// </summary>
public static class SafeHandleExtensions
{
    /// <summary>Creates a scope that safely increments and decrements the reference count of a safe handle.</summary>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Tests/SafeHandleExtensionsTests.cs" ref:region="SafeHandle_CreateHandleScope" ref:dedent="true">
    /// using var safeHandle = File.OpenHandle(Path.GetTempFileName(), FileMode.Open, FileAccess.Read, FileShare.None, FileOptions.DeleteOnClose);
    ///
    /// // The SafeHandle cannot be released while the scope is alive
    /// using var scope = safeHandle.CreateHandleScope();
    /// nint handle = scope.Value;
    /// Assert.NotEqual(0, handle); // Use handle safely, for instance in a P/Invoke call
    /// </code>
    /// </example>
    public static SafeHandleValue CreateHandleScope(this SafeHandle safeHandle)
    {
        return new SafeHandleValue(safeHandle);
    }
}
