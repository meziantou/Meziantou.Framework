using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

public sealed class OperatingSystemSnapshot
{
    private static readonly char[] CommaSeparator = ['='];

    internal OperatingSystemSnapshot()
    {
    }

    public Architecture OSArchitecture { get; } = RuntimeInformation.OSArchitecture;
    public PlatformID Platform { get; } = Environment.OSVersion.Platform;
    public Version? Version { get; } = Environment.OSVersion.Version;
    public string? ServicePack { get; } = Environment.OSVersion.ServicePack;
    public int? WindowsUpdateBuildRevision { get; } = GetWindowsUbr();
    public string? LinuxOsVersion { get; } = GetLinuxOsVersion();
    public bool IsWindows { get; } = OperatingSystem.IsWindows();
    public bool IsAndroid { get; } = OperatingSystem.IsAndroid();
    public bool IsBrowser { get; } = OperatingSystem.IsBrowser();
    public bool IsFreeBSD { get; } = OperatingSystem.IsFreeBSD();
    public bool IsIOS { get; } = OperatingSystem.IsIOS();
    public bool IsLinux { get; } = OperatingSystem.IsLinux();
    public bool IsMacCatalyst { get; } = OperatingSystem.IsMacCatalyst();
    public bool IsTvOS { get; } = OperatingSystem.IsTvOS();
    public bool IsWatchOS { get; } = OperatingSystem.IsWatchOS();

#if NET8_0_OR_GREATER
    public bool IsWasi { get; } = OperatingSystem.IsWasi();
#endif

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
