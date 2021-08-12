using Microsoft.Win32.SafeHandles;

namespace Meziantou.Framework.Win32.Natives;

internal sealed class ChangeJournalSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public ChangeJournalSafeHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle()
    {
        return Win32Methods.CloseHandle(handle);
    }
}
