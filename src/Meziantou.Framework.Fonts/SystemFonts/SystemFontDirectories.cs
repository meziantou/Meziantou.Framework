using Microsoft.Win32;

namespace Meziantou.Framework.Fonts;

/// <summary>The directories, and the files, of the fonts installed on the machine.</summary>
internal static class SystemFontDirectories
{
    private const string WindowsFontsKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";

    /// <summary>Gets the directories and the files of the fonts of the operating system.</summary>
    /// <param name="getEnvironmentVariable">Reads an environment variable: <see cref="Environment.GetEnvironmentVariable(string)"/>, or a substitute in tests.</param>
    public static List<string> GetDefault(Func<string, string?> getEnvironmentVariable)
    {
        var paths = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            AddWindows(paths);
        }
        else if (OperatingSystem.IsMacOS())
        {
            AddMacOS(paths, GetHome(getEnvironmentVariable));
        }
        else
        {
            AddUnix(paths, getEnvironmentVariable, GetHome(getEnvironmentVariable));
        }

        return Deduplicate(paths);
    }

    /// <summary>
    /// The fonts of Windows: the fonts directory, the fonts installed for the current user (Windows 10 1809 and later),
    /// and the files the registry lists elsewhere.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void AddWindows(List<string> paths)
    {
        var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        paths.Add(fonts);

        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (localApplicationData.Length != 0)
        {
            paths.Add(Path.Combine(localApplicationData, "Microsoft", "Windows", "Fonts"));
        }

        // A value is the name of a file of the fonts directory, which is already scanned, or the full path of a file.
        AddRegistryFonts(paths, Registry.LocalMachine);
        AddRegistryFonts(paths, Registry.CurrentUser);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void AddRegistryFonts(List<string> paths, RegistryKey root)
    {
        try
        {
            using var key = root.OpenSubKey(WindowsFontsKey);
            if (key is null)
                return;

            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name) is string value && Path.IsPathFullyQualified(value))
                {
                    paths.Add(value);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // The fonts of a key that cannot be read are not found
        }
    }

    /// <summary>The <c>Library/Fonts</c> directories of macOS, and the fonts the system downloads on demand (mobile assets).</summary>
    private static void AddMacOS(List<string> paths, string? home)
    {
        if (home is not null)
        {
            paths.Add(Path.Combine(home, "Library", "Fonts"));
        }

        paths.Add("/Library/Fonts");
        paths.Add("/Network/Library/Fonts");
        paths.Add("/System/Library/Fonts");
        foreach (var assets in (ReadOnlySpan<string>)["/System/Library/AssetsV2", "/System/Library/Assets"])
        {
            try
            {
                if (Directory.Exists(assets))
                {
                    paths.AddRange(Directory.EnumerateDirectories(assets, "com_apple_MobileAsset_Font*"));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // The downloaded fonts are not found
            }
        }
    }

    /// <summary>
    /// The directories of the configuration of fontconfig, and the font directories of the XDG Base Directory
    /// Specification, which a system without fontconfig uses too.
    /// </summary>
    private static void AddUnix(List<string> paths, Func<string, string?> getEnvironmentVariable, string? home)
    {
        paths.AddRange(FontConfigFile.ReadDirectories(getEnvironmentVariable, home));

        var dataHome = GetXdgDataHome(getEnvironmentVariable, home);
        if (dataHome is not null)
        {
            paths.Add(Path.Combine(dataHome, "fonts"));
        }

        if (home is not null)
        {
            paths.Add(Path.Combine(home, ".fonts"));
        }

        var dataDirectories = getEnvironmentVariable("XDG_DATA_DIRS");
        if (string.IsNullOrEmpty(dataDirectories))
        {
            dataDirectories = "/usr/local/share/:/usr/share/";
        }

        foreach (var directory in dataDirectories.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            // The specification ignores the relative paths
            if (Path.IsPathRooted(directory))
            {
                paths.Add(Path.Combine(directory, "fonts"));
            }
        }
    }

    /// <summary>Gets <c>$XDG_DATA_HOME</c>, or its default <c>~/.local/share</c>.</summary>
    internal static string? GetXdgDataHome(Func<string, string?> getEnvironmentVariable, string? home)
        => GetXdgDirectory(getEnvironmentVariable, "XDG_DATA_HOME", home, ".local", "share");

    /// <summary>Gets <c>$XDG_CONFIG_HOME</c>, or its default <c>~/.config</c>.</summary>
    internal static string? GetXdgConfigHome(Func<string, string?> getEnvironmentVariable, string? home)
        => GetXdgDirectory(getEnvironmentVariable, "XDG_CONFIG_HOME", home, ".config", null);

    private static string? GetXdgDirectory(Func<string, string?> getEnvironmentVariable, string variable, string? home, string first, string? second)
    {
        var value = getEnvironmentVariable(variable);
        if (!string.IsNullOrEmpty(value) && Path.IsPathRooted(value))
            return value;

        if (home is null)
            return null;

        return second is null ? Path.Combine(home, first) : Path.Combine(home, first, second);
    }

    internal static string? GetHome(Func<string, string?> getEnvironmentVariable)
    {
        var home = getEnvironmentVariable("HOME");
        if (string.IsNullOrEmpty(home))
        {
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return string.IsNullOrEmpty(home) ? null : home;
    }

    private static List<string> Deduplicate(List<string> paths)
    {
        var seen = new HashSet<string>(SystemFontScanner.PathComparer);
        var result = new List<string>(paths.Count);
        foreach (var path in paths)
        {
            if (!string.IsNullOrEmpty(path) && seen.Add(Path.TrimEndingDirectorySeparator(path)))
            {
                result.Add(path);
            }
        }

        return result;
    }
}
