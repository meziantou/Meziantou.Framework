using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>Reads the tables of the generated files out of the embedded resources of the assembly (<c>Data/*</c>).</summary>
/// <remarks>
/// <para>
/// A <c>.bin</c> resource is read in place: the runtime maps the resources with the image of the assembly, so a table is
/// a span over memory that is already there, and nothing is copied, parsed or allocated. A generated class keeps the
/// address in a <see langword="static"/> <see langword="readonly"/> field, which the optimizing JIT compiler reads as a
/// constant, and the length of every table is a constant of the generated code, so a lookup compiles to the same code
/// as a lookup in a table of the source code.
/// </para>
/// <para>
/// A <c>.deflate</c> resource is compressed with Deflate. It holds tables that most programs never read (the names of
/// the characters, their decomposition mappings...): the resource is decompressed the first time one of its tables is
/// read, and the result is kept for the lifetime of the program.
/// </para>
/// <para>
/// Deflate is the only compression of <c>System.IO.Compression</c> that Blazor WebAssembly supports: Brotli and
/// Zstandard throw a <see cref="PlatformNotSupportedException"/> in the browser (.NET 11), whatever the build options.
/// </para>
/// </remarks>
internal static class UnicodeResources
{
    private const string ResourcePrefix = "Meziantou.Framework.Unicode.";

    /// <summary>Gets the address of a resource that is read in place.</summary>
    /// <param name="name">The name of the file of the resource, such as <c>UnicodeTables.bin</c>.</param>
    /// <param name="length">The length the generated code expects.</param>
    /// <returns>The address of the first byte of the resource, valid for the lifetime of the program.</returns>
    public static nint Map(string name, int length)
    {
        using var stream = OpenResource(name, length);
        unsafe
        {
            if (stream is UnmanagedMemoryStream unmanagedStream)
                return (nint)unmanagedStream.PositionPointer;

            // The runtime returns the resources of a loaded assembly as memory it owns. In case another one does not, the
            // content is copied into memory that is never freed, as the image of an assembly is never unloaded.
            var buffer = (byte*)NativeMemory.Alloc((nuint)Math.Max(length, 1));
            stream.ReadExactly(new Span<byte>(buffer, length));
            return (nint)buffer;
        }
    }

    /// <summary>Gets a span over a table of a resource that is read in place.</summary>
    /// <param name="address">The address of the table, from <see cref="Map(string, int)"/>.</param>
    /// <param name="length">The length of the table.</param>
    /// <returns>A span over the table.</returns>
    /// <remarks>
    /// Only the generated code calls this method, with the address that <see cref="Map(string, int)"/> returned for a
    /// resource, plus the offset of a table, and the length of the table: the span is within the resource, whose length
    /// <see cref="Map(string, int)"/> checked.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<byte> GetSpan(nint address, int length)
    {
        unsafe
        {
            return new ReadOnlySpan<byte>((void*)address, length);
        }
    }

    /// <summary>Decompresses a Deflate resource, once.</summary>
    /// <param name="location">The field that caches the content.</param>
    /// <param name="name">The name of the file of the resource, such as <c>UnicodeCharacterInfos.Names.deflate</c>.</param>
    /// <param name="length">The length of the decompressed content.</param>
    /// <returns>The decompressed content.</returns>
    /// <exception cref="InvalidDataException">The resource does not decompress to <paramref name="length"/> bytes.</exception>
    public static byte[] Decompress(ref byte[]? location, string name, int length)
    {
        var data = new byte[length];
        using (var stream = OpenResource(name, expectedLength: -1))
        using (var deflate = new DeflateStream(stream, CompressionMode.Decompress))
        {
            try
            {
                deflate.ReadExactly(data);
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidDataException($"The resource '{name}' is invalid", ex);
            }

            if (deflate.ReadByte() >= 0)
                throw new InvalidDataException($"The resource '{name}' is invalid");
        }

        // Two threads may decompress the resource at once; they get the same content, and one of them is kept.
        return Interlocked.CompareExchange(ref location, data, comparand: null) ?? data;
    }

    private static Stream OpenResource(string name, int expectedLength)
    {
        var stream = typeof(UnicodeResources).Assembly.GetManifestResourceStream(ResourcePrefix + name) ?? throw new InvalidOperationException($"The resource '{name}' is missing");
        var length = stream.Length;
        if (expectedLength >= 0 && length != expectedLength)
        {
            stream.Dispose();
            throw new InvalidDataException($"The resource '{name}' has {length} bytes instead of {expectedLength}");
        }

        return stream;
    }
}
