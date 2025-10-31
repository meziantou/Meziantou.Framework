using System.Runtime.InteropServices;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of the active power management scheme (Windows only).</summary>
public sealed class PowerManagementSnapshot
{
    private PowerManagementSnapshot(Guid id, string? friendlyName)
    {
        Id = id;
        FriendlyName = friendlyName;
    }

    public Guid Id { get; }
    public string? FriendlyName { get; }

    internal static unsafe PowerManagementSnapshot? Get()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 0, 6000))
            return null;

        var result = Windows.Win32.PInvoke.PowerGetActiveScheme(UserRootPowerKey: null, out var guid);
        if (result != Windows.Win32.Foundation.WIN32_ERROR.ERROR_SUCCESS || guid is null)
            return null;

        var currentPlan = *guid;

        uint buffSize = 0;
        result = Windows.Win32.PInvoke.PowerReadFriendlyName(RootPowerKey: null, currentPlan, SubGroupOfPowerSettingsGuid: null, PowerSettingGuid: null, Buffer: null, ref buffSize);
        if (result is Windows.Win32.Foundation.WIN32_ERROR.ERROR_MORE_DATA or Windows.Win32.Foundation.WIN32_ERROR.ERROR_SUCCESS)
        {
            var data = new byte[buffSize];
            result = Windows.Win32.PInvoke.PowerReadFriendlyName(RootPowerKey: null, currentPlan, SubGroupOfPowerSettingsGuid: null, PowerSettingGuid: null, data, ref buffSize);
            if (result != Windows.Win32.Foundation.WIN32_ERROR.ERROR_SUCCESS)
                return null;

            fixed (byte* ptr = data)
            {
                var name = Marshal.PtrToStringUni((nint)ptr);
                return new PowerManagementSnapshot(currentPlan, name);
            }
        }

        return new PowerManagementSnapshot(currentPlan, friendlyName: null);
    }
}
