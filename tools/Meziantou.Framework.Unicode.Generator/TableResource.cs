#pragma warning disable MA0047 // Declare types in namespaces
#pragma warning disable MA0048 // File name must match type name
using System.IO.Compression;
using Meziantou.Framework;

/// <summary>Collects the tables of a generated class into a file that the library embeds as a resource (<c>Data/*</c>).</summary>
/// <remarks>
/// The generated code reads the resources with <c>UnicodeResources</c>. A <c>.bin</c> file is read in place, from the
/// image of the assembly, and a table is a span with a constant offset and length. A <c>.br</c> file is compressed with
/// Brotli and decompressed on first use: it holds the tables that most programs never read.
/// </remarks>
internal sealed class TableResource
{
    private readonly List<byte> _data = [];

    /// <param name="fileName">The name of the file, in <c>src/Meziantou.Framework.Unicode/Data</c>: <c>.bin</c> or <c>.br</c>.</param>
    /// <param name="memberName">The name of the member of the generated class that gives access to the content.</param>
    public TableResource(string fileName, string memberName = "Data")
    {
        if (!fileName.EndsWith(".bin", StringComparison.Ordinal) && !fileName.EndsWith(".br", StringComparison.Ordinal))
            throw new ArgumentException("The file must be a .bin or a .br file: " + fileName, nameof(fileName));

        FileName = fileName;
        MemberName = memberName;
    }

    public string FileName { get; }

    public string MemberName { get; }

    public bool IsCompressed => FileName.EndsWith(".br", StringComparison.Ordinal);

    /// <summary>Gets the size of each table, for the header of the generated file.</summary>
    public List<(string Name, int Bytes)> Sizes { get; } = [];

    public int Length => _data.Count;

    /// <summary>Adds a table and gets the C# expression of a span over it.</summary>
    /// <remarks>The class that reads a table read in place should use <see cref="AddProperty"/>.</remarks>
    public string Add(ReadOnlySpan<byte> bytes)
    {
        var offset = _data.Count;
        _data.AddRange(bytes);
        if (IsCompressed)
            return string.Create(CultureInfo.InvariantCulture, $"{MemberName}.Slice({offset}, {bytes.Length})");

        var address = offset == 0 ? MemberName : string.Create(CultureInfo.InvariantCulture, $"{MemberName} + {offset}");
        return string.Create(CultureInfo.InvariantCulture, $"UnicodeResources.GetSpan({address}, {bytes.Length})");
    }

    /// <summary>Adds a table and gets the declaration of a property that returns a span over it.</summary>
    /// <param name="declaration">The modifiers, the type and the name of the property, such as <c>private static ReadOnlySpan&lt;byte&gt; Table</c>.</param>
    /// <param name="bytes">The table.</param>
    /// <param name="indentation">The indentation of the members of the class.</param>
    /// <remarks>
    /// The getter of a table read in place is marked for aggressive inlining. Without it, it is too large for the JIT
    /// compiler to always inline it (a span over the data of the source code is 16 bytes of IL, a span over a resource is
    /// 22), and a large method that reads many tables, such as the decomposition of the normalization, can run out of
    /// inlining budget and call some of them: that made NFKD 8% slower.
    /// </remarks>
    public string AddProperty(string declaration, ReadOnlySpan<byte> bytes, string indentation = "    ")
    {
        var expression = Add(bytes);
        if (IsCompressed)
            return $"{indentation}{declaration} => {expression};";

        return $$"""
            {{indentation}}{{declaration}}
            {{indentation}}{
            {{indentation}}    [MethodImpl(MethodImplOptions.AggressiveInlining)]
            {{indentation}}    get => {{expression}};
            {{indentation}}}
            """;
    }

    /// <summary>Gets the declaration of the member that gives access to the content, for the body of the generated class.</summary>
    /// <param name="indentation">The indentation of the members of the class.</param>
    public string GetDeclaration(string indentation = "    ")
    {
        if (!IsCompressed)
        {
            return string.Create(CultureInfo.InvariantCulture, $$"""
                {{indentation}}// The tables are read in place from the embedded resource Data/{{FileName}}, see UnicodeResources.
                {{indentation}}private static readonly nint {{MemberName}} = UnicodeResources.Map("{{FileName}}", {{Length}});
                """);
        }

        var field = "s_" + char.ToLowerInvariant(MemberName[0]) + MemberName[1..];
        return string.Create(CultureInfo.InvariantCulture, $$"""
            {{indentation}}// The tables are decompressed from the embedded resource Data/{{FileName}} the first time one of them is read, see UnicodeResources.
            {{indentation}}private static byte[]? {{field}};

            {{indentation}}private static ReadOnlySpan<byte> {{MemberName}} => {{field}} ?? UnicodeResources.Decompress(ref {{field}}, "{{FileName}}", {{Length}});
            """);
    }

    public byte[] ToArray() => [.. _data];

    /// <summary>Writes the file when its content changed.</summary>
    /// <returns><see langword="true"/> when the file was written.</returns>
    public async Task<bool> WriteAsync(FullPath directory)
    {
        var path = directory / FileName;
        var content = ToArray();
        var existing = await ReadAsync(path);
        if (existing is not null && existing.AsSpan().SequenceEqual(content))
            return false;

        // A compressed file is compared by its decompressed content: another version of the Brotli encoder can compress the
        // same tables to other bytes, and that is not a change.
        path.CreateParentDirectory();
        await File.WriteAllBytesAsync(path, IsCompressed ? Compress(content) : content);
        return true;
    }

    /// <summary>Reads the content of a file written by <see cref="WriteAsync"/>, decompressed.</summary>
    /// <returns>The content, or <see langword="null"/> when the file does not exist.</returns>
    public static async Task<byte[]?> ReadAsync(FullPath path)
    {
        if (!File.Exists(path))
            return null;

        var bytes = await File.ReadAllBytesAsync(path);
        if (!path.Value.EndsWith(".br", StringComparison.Ordinal))
            return bytes;

        using var input = new MemoryStream(bytes);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        await brotli.CopyToAsync(output);
        return output.ToArray();
    }

    private static byte[] Compress(byte[] content)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, new BrotliCompressionOptions { Quality = 11 }, leaveOpen: true))
        {
            brotli.Write(content);
        }

        return output.ToArray();
    }
}
