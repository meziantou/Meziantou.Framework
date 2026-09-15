namespace Meziantou.Framework.SnapshotTesting;

public sealed class SnapshotType : IEquatable<SnapshotType>
{
    public static SnapshotType None { get; } = new("");
    public static SnapshotType Default { get; } = new("txt", "text/plain", "Text");
    public static SnapshotType Png { get; } = new("png", "image/png", "PNG image");
    public static SnapshotType Svg { get; } = new("svg", "image/svg+xml", "SVG image");
    public static SnapshotType Bmp { get; } = new("bmp", "image/bmp", "BMP image");
    public static SnapshotType Jpeg { get; } = new("jpeg", "image/jpeg", "JPEG image");
    public static SnapshotType Tiff { get; } = new("tiff", "image/tiff", "TIFF image");
    public static SnapshotType Webp { get; } = new("webp", "image/webp", "WebP image");
    public static SnapshotType Gif { get; } = new("gif", "image/gif", "GIF image");
    public static SnapshotType Ico { get; } = new("ico", "image/x-icon", "ICO image");

    private static readonly Dictionary<string, SnapshotType> Cache = new(StringComparer.OrdinalIgnoreCase)
    {
        ["txt"] = Default,
        ["png"] = Png,
        ["svg"] = Svg,
        ["bmp"] = Bmp,
        ["jpeg"] = Jpeg,
        ["jpg"] = Jpeg,
        ["tiff"] = Tiff,
        ["tif"] = Tiff,
        ["webp"] = Webp,
        ["gif"] = Gif,
        ["ico"] = Ico,
    };

    // Formats whose content is text. They are compared without regard to line endings or a leading UTF-8 byte
    // order mark, and scrubbers are always applied to them.
    private static readonly HashSet<string> KnownTextTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "txt", "text", "log", "svg",
        "json", "jsonc", "json5", "jsonl", "ndjson", "geojson", "har",
        "yaml", "yml", "toml", "ini", "cfg", "conf", "config", "env", "properties", "editorconfig",
        "xml", "xsd", "xsl", "xslt", "xaml", "axaml", "resx", "props", "targets", "csproj", "vbproj", "fsproj", "sln", "slnx", "nuspec", "manifest", "plist",
        "html", "htm", "xhtml", "cshtml", "vbhtml", "razor", "css", "scss", "sass", "less",
        "md", "markdown", "mdx", "rst", "adoc", "tex",
        "csv", "tsv",
        "cs", "csx", "vb", "fs", "fsi", "fsx", "il", "c", "h", "cpp", "hpp", "cc", "java", "kt", "kts", "go", "rs", "swift", "py", "rb", "php", "pl", "lua",
        "js", "mjs", "cjs", "jsx", "ts", "mts", "cts", "tsx",
        "sql", "graphql", "gql", "proto", "http",
        "sh", "bash", "zsh", "ps1", "psm1", "psd1", "bat", "cmd",
        "diff", "patch", "ics", "vcf", "srt", "vtt",
    };

    // Formats whose content is binary. Scrubbers are never applied to them, even when the bytes happen to be
    // valid UTF-8.
    private static readonly HashSet<string> KnownBinaryTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "dat",
        "png", "bmp", "jpeg", "jpg", "tiff", "tif", "webp", "gif", "ico", "avif", "heic", "heif",
        "pdf", "zip", "gz", "tgz", "7z", "tar", "br", "zst", "nupkg", "snupkg",
        "docx", "xlsx", "pptx", "odt", "ods", "odp",
        "dll", "exe", "pdb", "so", "dylib", "wasm",
        "woff", "woff2", "ttf", "otf", "eot",
        "mp3", "mp4", "wav", "ogg", "webm", "flac", "avi", "mov",
    };

    private SnapshotType(string type, string? mimeType = null, string? displayName = null)
    {
        Type = type;
        MimeType = mimeType;
        DisplayName = displayName;
        FileExtension = string.IsNullOrEmpty(type) ? "" : "." + type;
    }

    public string Type { get; }
    public string? MimeType { get; }
    public string? DisplayName { get; }
    public string FileExtension { get; }

    public static SnapshotType Create(string type, string? mimeType, string? displayName)
        => new(type, mimeType, displayName);

    public static SnapshotType Create(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return Default;

        var nameSpan = name.AsSpan();
        if (nameSpan.StartsWith('.'))
        {
            nameSpan = nameSpan[1..];
            name = null;
        }

        if (Cache.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(nameSpan, out var snapshotType))
            return snapshotType;

        return new SnapshotType(name ?? nameSpan.ToString());
    }

    /// <summary>Indicates whether a snapshot stored with this extension is text, such as <c>txt</c>, <c>json</c> or <c>cs</c>.</summary>
    internal static bool IsKnownTextType(string? extension) => ContainsExtension(KnownTextTypes, extension);

    /// <summary>Indicates whether a snapshot stored with this extension is binary, such as <c>png</c> or <c>bin</c>.</summary>
    internal static bool IsKnownBinaryType(string? extension) => ContainsExtension(KnownBinaryTypes, extension);

    private static bool ContainsExtension(HashSet<string> extensions, string? extension)
    {
        if (string.IsNullOrEmpty(extension))
            return false;

        var span = extension.AsSpan();
        if (span[0] == '.')
        {
            span = span[1..];
        }

        return extensions.GetAlternateLookup<ReadOnlySpan<char>>().Contains(span);
    }

    // A type names a file extension, and "JSON" and "json" name the same format: a comparer registered for one
    // must apply to a snapshot requested as the other.
    public bool Equals([NotNullWhen(true)] SnapshotType? other) => other is not null && StringComparer.OrdinalIgnoreCase.Equals(Type, other.Type);
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Type);
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is SnapshotType snapshotType && Equals(snapshotType);

    public static bool operator ==(SnapshotType? left, SnapshotType? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(SnapshotType? left, SnapshotType? right) => !(left == right);

    public static implicit operator SnapshotType(string? name) => Create(name);
}
