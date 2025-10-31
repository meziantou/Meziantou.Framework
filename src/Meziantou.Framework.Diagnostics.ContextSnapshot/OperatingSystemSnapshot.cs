using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of operating system information at a specific point in time.</summary>
public sealed class OperatingSystemSnapshot
{
    private static readonly char[] CommaSeparator = ['='];

    internal OperatingSystemSnapshot()
    {
    }

    /// <summary>Gets the processor architecture of the operating system.</summary>
    public Architecture OSArchitecture { get; } = RuntimeInformation.OSArchitecture;
    /// <summary>Gets the platform identifier for the operating system.</summary>
    public PlatformID Platform { get; } = Environment.OSVersion.Platform;
    /// <summary>Gets the version of the operating system.</summary>
    public Version? Version { get; } = Environment.OSVersion.Version;
    /// <summary>Gets the service pack version of the operating system.</summary>
    public string? ServicePack { get; } = Environment.OSVersion.ServicePack;
    /// <summary>Gets the Windows update build revision (UBR) number.</summary>
    public int? WindowsUpdateBuildRevision { get; } = GetWindowsUbr();
    /// <summary>Gets the Linux operating system version string.</summary>
    public string? LinuxOsVersion { get; } = GetLinuxOsVersion();
    /// <summary>Gets a value indicating whether the operating system is Windows.</summary>
    public bool IsWindows { get; } = OperatingSystem.IsWindows();
    /// <summary>Gets a value indicating whether the operating system is Android.</summary>
    public bool IsAndroid { get; } = OperatingSystem.IsAndroid();
    /// <summary>Gets a value indicating whether the operating system is a browser.</summary>
    public bool IsBrowser { get; } = OperatingSystem.IsBrowser();
    /// <summary>Gets a value indicating whether the operating system is FreeBSD.</summary>
    public bool IsFreeBSD { get; } = OperatingSystem.IsFreeBSD();
    /// <summary>Gets a value indicating whether the operating system is iOS.</summary>
    public bool IsIOS { get; } = OperatingSystem.IsIOS();
    /// <summary>Gets a value indicating whether the operating system is Linux.</summary>
    public bool IsLinux { get; } = OperatingSystem.IsLinux();
    /// <summary>Gets a value indicating whether the operating system is Mac Catalyst.</summary>
    public bool IsMacCatalyst { get; } = OperatingSystem.IsMacCatalyst();
    /// <summary>Gets a value indicating whether the operating system is tvOS.</summary>
    public bool IsTvOS { get; } = OperatingSystem.IsTvOS();
    /// <summary>Gets a value indicating whether the operating system is watchOS.</summary>
    public bool IsWatchOS { get; } = OperatingSystem.IsWatchOS();
    /// <summary>Gets a value indicating whether the operating system is WASI.</summary>
    public bool IsWasi { get; } = OperatingSystem.IsWasi();

    private static int? GetWindowsUbr()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
                using var ndpKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");

                if (ndpKey is null)
                    return null;

                return Convert.ToInt32(ndpKey.GetValue("UBR"), CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return null;
            }
        }

        return null;
    }

    private static string? GetLinuxOsVersion()
    {
        if (!OperatingSystem.IsLinux())
            return null;

        try
        {
            return GetNameByOsRelease(File.ReadAllLines("/etc/os-release"));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? GetNameByOsRelease(string[] lines)
    {
        try
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in lines)
            {
                var parts = line.Split(CommaSeparator, 2);

                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    // remove quotes if value is quoted
                    if (value.Length >= 2 &&
                        ((value.StartsWith('"') && value.EndsWith('"')) ||
                         (value.StartsWith('\'') && value.EndsWith('\''))))
                    {
                        value = value[1..^1];
                    }

                    values[key] = value;
                }
            }

            var id = values.GetValueOrDefault("ID");
            var name = values.GetValueOrDefault("NAME");
            var version = values.GetValueOrDefault("VERSION");

            string[] idsWithExtendedVersion = ["ubuntu", "linuxmint", "solus", "kali"];
            if (idsWithExtendedVersion.Contains(id, StringComparer.Ordinal) && !string.IsNullOrEmpty(version) && !string.IsNullOrEmpty(name))
                return name + " " + version;

            var prettyName = values.GetValueOrDefault("PRETTY_NAME");
            if (prettyName is not null)
                return prettyName;

            if (name is not null && version is not null)
                return name + " " + version;

            if (name is not null)
                return name;

            if (id is not null)
                return id;

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
