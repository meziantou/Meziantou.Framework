using System.Reflection;

namespace Meziantou.Framework;

public static class AssemblyUtilities
{
    /// <summary>
    /// Gets the informational version of an assembly.
    /// </summary>
    /// <param name="assembly">The assembly. May not be null.</param>
    /// <returns>The version represented as a string. May not be null.</returns>
    public static string? GetInformationalVersion(this Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var attr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (attr != null)
        {
            return attr.InformationalVersion;
        }

        return null;
    }

    /// <summary>
    /// Gets the linker timestamp of a specified assembly.
    /// </summary>
    /// <param name="assembly">The assembly. May not be null.</param>
    /// <returns>A valid date time or null if an error occurred.</returns>
    public static DateTime? GetLinkerTimestampUtc(this Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        try
        {
            var location = assembly.Location;
            if (!string.IsNullOrEmpty(location))
            {
                return GetLinkerTimestampUtc(location);
            }
        }
        catch
        {
        }

        return null;
    }

    /// <summary>
    /// Gets the linker timestamp of a specified assembly.
    /// </summary>
    /// <param name="filePath">The assembly file path.</param>
    /// <returns>
    /// A valid date time or null if an error occurred.
    /// </returns>
    public static DateTime? GetLinkerTimestampUtc(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        try
        {
            if (!File.Exists(filePath))
                return null;

            const int PeHeaderOffset = 60;
            const int LinkerTimestampOffset = 8;
            var bytes = new byte[2048];

            using (var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                file.TryReadAll(bytes, 0, bytes.Length);
            }

            var headerPos = BitConverter.ToInt32(bytes, PeHeaderOffset);
            var secondsSince1970 = BitConverter.ToInt32(bytes, headerPos + LinkerTimestampOffset);
            return DateTime.UnixEpoch.AddSeconds(secondsSince1970);
        }
        catch
        {
            return null;
        }
    }

    public static Stream GetRequiredManifestResourceStream(this Assembly assembly, string name)
    {
        var stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            var names = assembly.GetManifestResourceNames();
            throw new ArgumentException($"Resource '{name}' not found. Available resource names: {string.Join(", ", names)}", nameof(name));
        }

        return stream;
    }

    public static Stream GetRequiredManifestResourceStream(this Assembly assembly, Type type, string name)
    {
        var stream = assembly.GetManifestResourceStream(type, name);
        if (stream is null)
        {
            var names = assembly.GetManifestResourceNames();
            throw new ArgumentException($"Resource '{name}' not found. Available resource names: {string.Join(", ", names)}", nameof(name));
        }

        return stream;
    }
}
