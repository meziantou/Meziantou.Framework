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
            // The memory of the resource lives as long as the assembly, which is never unloaded while its types are used
            ReadOnlySpan<byte> data;
            unsafe
            {
                data = new ReadOnlySpan<byte>(unmanagedStream.PositionPointer, checked((int)unmanagedStream.Length));
            }

            var result = FlatGeobuf.Read(data, options);

            // The span does not keep the stream nor the assembly alive: they own the memory it reads
            GC.KeepAlive(unmanagedStream);
            GC.KeepAlive(assembly);
            return result;
        }

        return FlatGeobuf.Read(stream, options);
    }
}
