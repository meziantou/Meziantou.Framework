using System.Reflection;

namespace Meziantou.Framework.Geo.Maps.Data;

/// <summary>Reads the layers a map package embeds, named after their file, such as <c>borders.fgb</c>.</summary>
internal static class EmbeddedMapLayer
{
    /// <summary>Reads a FlatGeobuf layer in place: the memory of the resource is the memory of the loaded assembly.</summary>
    public static GeoFeatureCollection Read(Assembly assembly, string name, GeoBounds? bounds)
    {
        var options = new FlatGeobufReaderOptions { Bounds = bounds };
        using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("The resource '" + name + "' is missing.");
        if (stream is UnmanagedMemoryStream unmanagedStream)
        {
            nint address;
            unsafe
            {
                address = (nint)unmanagedStream.PositionPointer;
            }

            using var memory = new UnmanagedMemoryManager(address, checked((int)unmanagedStream.Length));
            return FlatGeobuf.Read(memory.Memory, options);
        }

        return FlatGeobuf.Read(stream, options);
    }
}
