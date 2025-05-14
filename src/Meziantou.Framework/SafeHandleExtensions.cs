using System.Runtime.InteropServices;

namespace Meziantou.Framework;
public static class SafeHandleExtensions
{
    public static SafeHandleValue CreateHandleScope(this SafeHandle safeHandle)
    {
        return new SafeHandleValue(safeHandle);
    }
}
