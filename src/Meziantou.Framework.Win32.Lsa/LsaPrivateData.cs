using System.ComponentModel;
using System.Globalization;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.WindowsProgramming;

namespace Meziantou.Framework.Win32;

[SupportedOSPlatform("windows5.1.2600")]
public static class LsaPrivateData
{
    public static void RemoveValue(string key)
    {
        SetValue(key, value: null);
    }

    public static unsafe void SetValue(string key, string? value)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key));

        if (key.Length == 0)
            throw new ArgumentException($"{nameof(key)} must not be empty", nameof(key));

        var objectAttributes = new OBJECT_ATTRIBUTES();
        var localsystem = new UNICODE_STRING();
        var secretName = new UNICODE_STRING();
        fixed (char* keyPtr = key)
        fixed (char* valuePtr = value)
        {
            secretName.Buffer = new PWSTR(keyPtr);
            secretName.MaximumLength = (ushort)(key.Length * 2);
            secretName.Length = (ushort)(key.Length * 2);

            UNICODE_STRING? lusSecretData = null;
            if (value != null)
            {
                lusSecretData = new UNICODE_STRING()
                {
                    Buffer = new PWSTR(valuePtr),
                    Length = (ushort)(value.Length * 2),
                    MaximumLength = (ushort)(value.Length * 2),
                };
            }

            var lsaPolicyHandle = GetLsaPolicy(in objectAttributes, ref localsystem);

            var result = PInvoke.LsaStorePrivateData(lsaPolicyHandle, in secretName, lusSecretData);
            ReleaseLsaPolicy(lsaPolicyHandle);

            var winErrorCode = PInvoke.LsaNtStatusToWinError(result);
            if (winErrorCode != 0)
                throw new Win32Exception((int)winErrorCode);
        }
    }

    public static unsafe string? GetValue(string key)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key));

        if (key.Length == 0)
            throw new ArgumentException($"{nameof(key)} must not be empty", nameof(key));

        var objectAttributes = new OBJECT_ATTRIBUTES();
        var localsystem = new UNICODE_STRING();
        var secretName = new UNICODE_STRING();
        fixed (char* keyPtr = key)
        {
            secretName.Buffer = new PWSTR(keyPtr);
            secretName.MaximumLength = (ushort)(key.Length * 2);
            secretName.Length = (ushort)(key.Length * 2);

            // Get LSA policy
            var lsaPolicyHandle = GetLsaPolicy(in objectAttributes, ref localsystem);

            var result = PInvoke.LsaRetrievePrivateData(lsaPolicyHandle, in secretName, out var privateData);
            ReleaseLsaPolicy(lsaPolicyHandle);

            if (result == NTSTATUS.STATUS_OBJECT_NAME_NOT_FOUND)
                return null;

            var winErrorCode = PInvoke.LsaNtStatusToWinError(result);
            if (winErrorCode != 0)
                throw new Win32Exception((int)winErrorCode, "LsaRetrievePrivateData failed: " + winErrorCode.ToString(CultureInfo.InvariantCulture));

            if (privateData == null)
                return null;

            var value = new string(privateData->Buffer.Value, 0, privateData->Length / 2);
            FreeMemory(privateData);

            return value;
        }
    }

    private static unsafe void* GetLsaPolicy(in OBJECT_ATTRIBUTES objectAttributes, ref UNICODE_STRING localsystem)
    {
        var ntsResult = PInvoke.LsaOpenPolicy(localsystem, in objectAttributes, PInvoke.POLICY_GET_PRIVATE_INFORMATION, out var lsaPolicyHandle);
        var winErrorCode = PInvoke.LsaNtStatusToWinError(ntsResult);
        if (winErrorCode != 0)
            throw new Win32Exception((int)winErrorCode, "LsaOpenPolicy failed: " + winErrorCode.ToString(CultureInfo.InvariantCulture));

        return lsaPolicyHandle;
    }

    private static unsafe void ReleaseLsaPolicy(void* lsaPolicyHandle)
    {
        var ntsResult = PInvoke.LsaClose(lsaPolicyHandle);
        var winErrorCode = PInvoke.LsaNtStatusToWinError(ntsResult);
        if (winErrorCode != 0)
            throw new Win32Exception((int)winErrorCode, "LsaClose failed: " + winErrorCode.ToString(CultureInfo.InvariantCulture));
    }

    private static unsafe void FreeMemory(void* buffer)
    {
        var ntsResult = PInvoke.LsaFreeMemory(buffer);
        var winErrorCode = PInvoke.LsaNtStatusToWinError(ntsResult);
        if (winErrorCode != 0)
            throw new Win32Exception((int)winErrorCode, "LsaFreeMemory failed: " + winErrorCode.ToString(CultureInfo.InvariantCulture));
    }
}
