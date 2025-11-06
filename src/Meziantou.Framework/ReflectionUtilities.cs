using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Meziantou.Framework;

#if ReflectionUtilities_Internal
internal
#else
public
#endif
static class ReflectionUtilities
{
    public static bool IsNullableOfT(this Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return Nullable.GetUnderlyingType(type) != null;
    }

    public static bool IsFlagsEnum<T>()
    {
        return IsFlagsEnum(typeof(T));
    }

    public static bool IsFlagsEnum(this Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!type.IsEnum)
            return false;

        return type.IsDefined(typeof(FlagsAttribute), inherit: true);
    }

    [RequiresUnreferencedCode("Use reflection to find static methods")]
    public static MethodInfo? GetImplicitConversion(object? value, Type targetType)
    {
        if (value is null)
            return null;

        var valueType = value.GetType();
        var methods = valueType.GetMethods(BindingFlags.Public | BindingFlags.Static);
        foreach (var method in methods)
        {
            if (IsImplicitOperator(method, valueType, targetType))
                return method;
        }

        return null;

        static bool IsImplicitOperator(MethodInfo mi, Type sourceType, Type targetType)
        {
            if (!string.Equals(mi.Name, "op_Implicit", StringComparison.Ordinal))
                return false;

            if (!targetType.IsAssignableFrom(mi.ReturnType))
                return false;

            var p = mi.GetParameters();
            if (p.Length != 1)
                return false;

            if (!p[0].ParameterType.IsAssignableFrom(sourceType))
                return false;

            return true;
        }
    }

    /// <summary>Retrieves the source file path and the first sequence point for the specified method using portable PDB information if available.</summary>
    /// <remarks>This method requires that the target assembly has an accessible portable PDBs (embedded or next to the dll). If the method
    /// or its assembly does not meet this requirement, an exception is thrown. The returned sequence point typically
    /// corresponds to the first executable line of the method in the source file.</remarks>
    /// <param name="methodInfo">The reflection metadata for the method whose source location is to be determined.</param>
    /// <returns>A tuple containing the source file path and the first sequence point for the method, or null if the assembly location is unavailable.</returns>
    public static (string FilePath, SequencePoint SequencePoint)? GetMethodLocation(this MethodInfo methodInfo)
    {
        ArgumentNullException.ThrowIfNull(methodInfo);

        var location = methodInfo.DeclaringType?.Assembly.Location;
        if (string.IsNullOrEmpty(location))
            return null;

        using var fs = File.OpenRead(location);
        return GetMethodLocation(methodInfo, location, fs);
    }

    /// <summary>Retrieves the source file path and the first sequence point for the specified method using portable PDB information if available.</summary>
    /// <remarks>This method requires that the target assembly has an accessible portable PDBs (embedded or next to the dll). If the method
    /// or its assembly does not meet this requirement, an exception is thrown. The returned sequence point typically
    /// corresponds to the first executable line of the method in the source file.</remarks>
    /// <param name="methodInfo">The reflection metadata for the method whose source location is to be determined.</param>
    /// <returns>A tuple containing the source file path and the first sequence point for the method, or null if the assembly location is unavailable.</returns>
    public static async Task<(string FilePath, SequencePoint SequencePoint)?> GetMethodLocationAsync(this MethodInfo methodInfo)
    {
        ArgumentNullException.ThrowIfNull(methodInfo);

        var location = methodInfo.DeclaringType?.Assembly.Location;
        if (string.IsNullOrEmpty(location))
            return null;

        await using var fs = File.OpenRead(location);
        return GetMethodLocation(methodInfo, location, fs);
    }

    private static (string FilePath, SequencePoint SequencePoint)? GetMethodLocation(MethodInfo methodInfo, string location, FileStream fs)
    {
        using var reader = new PEReader(fs);

        // Get the embedded PDB reader if available
        var pdbReaderProvider = reader.ReadDebugDirectory()
            .Where(entry => entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
            .Select(entry => reader.ReadEmbeddedPortablePdbDebugDirectoryData(entry))
            .FirstOrDefault();
        try
        {
            if (pdbReaderProvider is null)
            {
                // Try to open the associated PDB file
                if (!reader.TryOpenAssociatedPortablePdb(location, File.OpenRead, out pdbReaderProvider, out _))
                {
                    pdbReaderProvider?.Dispose();
                    return null;
                }

                if (pdbReaderProvider is null)
                    return null;
            }

            var pdbReader = pdbReaderProvider.GetMetadataReader();
            var methodHandle = MetadataTokens.MethodDefinitionHandle(methodInfo.MetadataToken);
            var methodDebugInfo = pdbReader.GetMethodDebugInformation(methodHandle);
            if (!methodDebugInfo.SequencePointsBlob.IsNil)
            {
                var sequencePoints = methodDebugInfo.GetSequencePoints();
                var firstSequencePoint = sequencePoints.FirstOrDefault();
                if (firstSequencePoint.Document.IsNil == false)
                {
                    var document = pdbReader.GetDocument(firstSequencePoint.Document);
                    var filePath = pdbReader.GetString(document.Name);
                    return (filePath, firstSequencePoint);
                }
            }

            return null;
        }
        finally
        {
            pdbReaderProvider?.Dispose();
        }
    }
}
