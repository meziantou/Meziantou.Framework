using System.Reflection;
using System.Reflection.PortableExecutable;

namespace Meziantou.Framework;

public static class AssemblyUtilities
{
    /// <summary>Gets the informational version of an assembly.</summary>
    /// <param name="assembly">The assembly. May not be null.</param>
    /// <returns>The version represented as a string. May not be null.</returns>
    public static string? GetInformationalVersion(this Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var attr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (attr is not null)
        {
            return attr.InformationalVersion;
        }

        return null;
    }

    /// <summary>Gets the linker timestamp of a specified assembly.</summary>
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
        catch (NotSupportedException)
        {
            // Dynamic assemblies have no location
        }

        return null;
    }

    /// <summary>Gets the linker timestamp of a specified assembly.</summary>
    /// <param name="filePath">The assembly file path.</param>
    /// <returns>A valid date time, or <see langword="null"/> if the file has no meaningful timestamp or could not be read.</returns>
    /// <remarks>
    /// Deterministic builds, which the .NET SDK produces by default, store a hash of the content in the
    /// COFF <c>TimeDateStamp</c> field instead of a date. Those are reported as <see langword="null"/>
    /// rather than as the arbitrary date the hash decodes to.
    /// </remarks>
    public static DateTime? GetLinkerTimestampUtc(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        try
        {
            if (!File.Exists(filePath))
                return null;

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var peReader = new PEReader(stream);

            // A deterministic build advertises itself with a Reproducible debug directory entry
            foreach (var entry in peReader.ReadDebugDirectory())
            {
                if (entry.Type == DebugDirectoryEntryType.Reproducible)
                    return null;
            }

            var timestamp = unchecked((uint)peReader.PEHeaders.CoffHeader.TimeDateStamp);
            return DateTime.UnixEpoch.AddSeconds(timestamp);
        }
        catch (BadImageFormatException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Gets a manifest resource stream, throwing an exception if not found.</summary>
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

    /// <summary>Gets a manifest resource stream scoped to the specified type's namespace, throwing an exception if not found.</summary>
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
