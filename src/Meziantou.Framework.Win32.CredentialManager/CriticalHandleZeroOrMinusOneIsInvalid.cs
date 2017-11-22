using System;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32
{
    /// <summary>
    /// Original code: https://github.com/dotnet/corefx/blob/d0dc5fc099946adc1035b34a8b1f6042eddb0c75/src/Common/src/Microsoft/Win32/SafeHandles/CriticalHandleZeroOrMinusOneIsInvalid.cs
    /// </summary>
    internal abstract class CriticalHandleZeroOrMinusOneIsInvalid : CriticalHandle
    {
        protected CriticalHandleZeroOrMinusOneIsInvalid() : base(IntPtr.Zero)
        {
        }

        public override bool IsInvalid => handle == new IntPtr(0) || handle == new IntPtr(-1);
    }
}
