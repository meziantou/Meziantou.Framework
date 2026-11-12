using System.Text;

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

        Guid currentPlan;
        try
        {
            currentPlan = *guid;
        }
        finally
        {
            // PowerGetActiveScheme allocates the returned GUID with LocalAlloc; the caller must free it.
            Windows.Win32.PInvoke.LocalFree((Windows.Win32.Foundation.HLOCAL)guid);
        }

        uint buffSize = 0;
        result = Windows.Win32.PInvoke.PowerReadFriendlyName(RootPowerKey: null, currentPlan, SubGroupOfPowerSettingsGuid: null, PowerSettingGuid: null, Buffer: null, ref buffSize);
        if (result is Windows.Win32.Foundation.WIN32_ERROR.ERROR_MORE_DATA or Windows.Win32.Foundation.WIN32_ERROR.ERROR_SUCCESS)
        {
            var data = new byte[buffSize];
            result = Windows.Win32.PInvoke.PowerReadFriendlyName(RootPowerKey: null, currentPlan, SubGroupOfPowerSettingsGuid: null, PowerSettingGuid: null, data, ref buffSize);
            if (result != Windows.Win32.Foundation.WIN32_ERROR.ERROR_SUCCESS)
                return null;

            // The name is NUL-terminated when the buffer has room for it: never read past the bytes that were written
            var name = Encoding.Unicode.GetString(data, 0, (int)Math.Min(buffSize, (uint)data.Length));
            var terminatorIndex = name.IndexOf('\0', StringComparison.Ordinal);
            if (terminatorIndex >= 0)
            {
                name = name[..terminatorIndex];
            }

            return new PowerManagementSnapshot(currentPlan, name);
        }

        return new PowerManagementSnapshot(currentPlan, friendlyName: null);
    }
}
