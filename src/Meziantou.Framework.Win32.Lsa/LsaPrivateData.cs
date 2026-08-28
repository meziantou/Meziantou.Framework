using System.ComponentModel;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security.Authentication.Identity;

namespace Meziantou.Framework.Win32;

/// <summary>
/// Provides methods to interact with Local Security Authority (LSA) private data storage on Windows.
/// LSA private data storage keeps values encrypted on disk under a DACL that allows only the creator and administrators to read them.
/// </summary>
/// <remarks>
/// <para>
/// Microsoft recommends <c>CryptProtectData</c> and <c>CryptUnprotectData</c> over the LSA private data functions unless you specifically need to manipulate LSA secrets.
/// The stored data is not absolutely protected.
/// </para>
/// <para>
/// The key name decides how far the secret reaches. A name starting with <c>L$</c> creates a local object that cannot be accessed remotely,
/// <c>G$</c> a global object, and <c>M$</c> a machine object that only the operating system can read back.
/// A name with none of those prefixes creates an object that <b>can be accessed remotely</b>, so prefer an <c>L$</c> prefix unless you need otherwise.
/// </para>
/// </remarks>
/// <example>
/// Store and retrieve a secret value:
/// <code>
/// // Store a secret value (requires administrator privileges)
/// LsaPrivateData.SetValue("L$MySecretKey", "MySecretValue");
///
/// // Retrieve the value
/// string? value = LsaPrivateData.GetValue("L$MySecretKey");
///
/// // Remove the value (requires administrator privileges)
/// LsaPrivateData.RemoveValue("L$MySecretKey");
/// </code>
/// </example>
[SupportedOSPlatform("windows5.1.2600")]
public static class LsaPrivateData
{
    /// <summary>Removes a value from LSA private data storage. Requires administrator privileges.</summary>
    /// <remarks>Removing a key that does not exist does nothing.</remarks>
    /// <param name="key">The key of the value to remove.</param>
    public static void RemoveValue(string key)
    {
        SetValue(key, value: null);
    }

    /// <summary>Stores a value in LSA private data storage. Requires administrator privileges.</summary>
    /// <param name="key">The key under which to store the value. Cannot be null or empty.</param>
    /// <param name="value">The value to store. If null, the key is removed; removing a key that does not exist does nothing.</param>
    public static unsafe void SetValue(string key, string? value)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length == 0)
            throw new ArgumentException($"{nameof(key)} must not be empty", nameof(key));

        var objectAttributes = new LSA_OBJECT_ATTRIBUTES();
        var secretName = new LSA_UNICODE_STRING();
        fixed (char* keyPtr = key)
        fixed (char* valuePtr = value)
        {
            secretName.Buffer = new PWSTR(keyPtr);
            secretName.MaximumLength = (ushort)(key.Length * 2);
            secretName.Length = (ushort)(key.Length * 2);

            LSA_UNICODE_STRING? lusSecretData = null;
            if (value is not null)
            {
                lusSecretData = new LSA_UNICODE_STRING()
                {
                    Buffer = new PWSTR(valuePtr),
                    Length = (ushort)(value.Length * 2),
                    MaximumLength = (ushort)(value.Length * 2),
                };
            }

            using var lsaPolicyHandle = GetLsaPolicy(in objectAttributes);
            var result = PInvoke.LsaStorePrivateData(lsaPolicyHandle, in secretName, lusSecretData);

            // Removing a key that does not exist is a no-op, so RemoveValue and GetValue agree on what a missing key means
            if (value is null && result == NTSTATUS.STATUS_OBJECT_NAME_NOT_FOUND)
                return;

            var winErrorCode = PInvoke.LsaNtStatusToWinError(result);
            if (winErrorCode != 0)
                throw new Win32Exception((int)winErrorCode);
        }
    }

    /// <summary>Retrieves a value from LSA private data storage.</summary>
    /// <param name="key">The key of the value to retrieve. Cannot be null or empty.</param>
    /// <returns>The value associated with the key, or null if the key does not exist.</returns>
    public static unsafe string? GetValue(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length == 0)
            throw new ArgumentException($"{nameof(key)} must not be empty", nameof(key));

        var objectAttributes = new LSA_OBJECT_ATTRIBUTES();
        var secretName = new LSA_UNICODE_STRING();
        fixed (char* keyPtr = key)
        {
            secretName.Buffer = new PWSTR(keyPtr);
            secretName.MaximumLength = (ushort)(key.Length * 2);
            secretName.Length = (ushort)(key.Length * 2);

            // Get LSA policy
            using var lsaPolicyHandle = GetLsaPolicy(in objectAttributes);
            var result = PInvoke.LsaRetrievePrivateData(lsaPolicyHandle, in secretName, out var privateData);
            if (result == NTSTATUS.STATUS_OBJECT_NAME_NOT_FOUND)
                return null;

            var winErrorCode = PInvoke.LsaNtStatusToWinError(result);
            if (winErrorCode != 0)
                throw new Win32Exception((int)winErrorCode, "LsaRetrievePrivateData failed: " + winErrorCode.ToString(CultureInfo.InvariantCulture));

            if (privateData is null)
                return null;

            try
            {
                return new string(privateData->Buffer.Value, 0, privateData->Length / 2);
            }
            finally
            {
                // Mimic SecureZeroMemory so the decrypted secret does not stay readable in the freed block. SecureZeroMemory is not an exported function, neither is RtlSecureZeroMemory
                new Span<byte>(privateData->Buffer.Value, privateData->Length).Clear();
                FreeMemory(privateData);
            }
        }
    }

    private static unsafe LsaCloseSafeHandle GetLsaPolicy(in LSA_OBJECT_ATTRIBUTES objectAttributes)
    {
        // A null SystemName means the local system
        var ntsResult = PInvoke.LsaOpenPolicy(SystemName: null, in objectAttributes, PInvoke.POLICY_GET_PRIVATE_INFORMATION, out var lsaPolicyHandle);
        var winErrorCode = PInvoke.LsaNtStatusToWinError(ntsResult);
        if (winErrorCode != 0)
            throw new Win32Exception((int)winErrorCode, "LsaOpenPolicy failed: " + winErrorCode.ToString(CultureInfo.InvariantCulture));

        return lsaPolicyHandle;
    }

    private static unsafe void FreeMemory(void* buffer)
    {
        var ntsResult = PInvoke.LsaFreeMemory(buffer);
        var winErrorCode = PInvoke.LsaNtStatusToWinError(ntsResult);
        if (winErrorCode != 0)
            throw new Win32Exception((int)winErrorCode, "LsaFreeMemory failed: " + winErrorCode.ToString(CultureInfo.InvariantCulture));
    }
}
