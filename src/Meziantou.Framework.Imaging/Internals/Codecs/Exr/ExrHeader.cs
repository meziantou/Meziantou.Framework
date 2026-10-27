using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The header of a single-part OpenEXR file: the attributes the library reads, validated, and the geometry of its chunks
/// (OpenEXR File Layout, "Component Three: Header" and "Component Four: Offset Tables").
/// </summary>
/// <remarks>
/// The attributes are given one by one (<see cref="ReadAttribute"/>) as the structure parser reaches them; <see cref="Complete"/>
/// then checks the required attributes and every rule that relates them. Every rejection of a file the library does not
/// read is made here, so that an identification reports it too.
/// </remarks>
internal sealed class ExrHeader
{
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);
    private ExrChannel[]? _channels;
    private ExrCompressionMethod? _compression;
    private ExrBox? _dataWindow;
    private ExrBox? _displayWindow;
    private ExrLineOrder? _lineOrder;
    private bool _hasTiles;
    private int? _chunkCount;
    private string? _type;

    public ExrHeader(int versionField)
    {
        VersionField = versionField;
    }

    /// <summary>Gets the version field (the version number in the low byte, the flags above).</summary>
    public int VersionField { get; }

    /// <summary>Gets a value indicating whether the image is stored as tiles (bit 9 of the version field).</summary>
    public bool IsTiled => (VersionField & ExrFormat.SingleTileFlag) != 0;

    /// <summary>Gets the longest attribute, type or channel name the file may have.</summary>
    public int MaxNameLength => (VersionField & ExrFormat.LongNamesFlag) != 0 ? ExrFormat.LongNameMaxLength : ExrFormat.ShortNameMaxLength;

    /// <summary>Gets the channels, sorted by name (the order of their samples in a chunk).</summary>
    public IReadOnlyList<ExrChannel> Channels => _channels ?? [];

    /// <summary>Gets the role of each channel of <see cref="Channels"/>.</summary>
    public ExrChannelRole[] Roles { get; private set; } = [];

    public ExrCompressionMethod Compression => _compression ?? ExrCompressionMethod.None;

    public ExrBox DataWindow => _dataWindow ?? default;

    public ExrBox DisplayWindow => _displayWindow ?? default;

    public ExrLineOrder LineOrder => _lineOrder ?? ExrLineOrder.IncreasingY;

    /// <summary>Gets the width of a tile.</summary>
    public int TileWidth { get; private set; }

    /// <summary>Gets the height of a tile.</summary>
    public int TileHeight { get; private set; }

    public ExrLevelMode LevelMode { get; private set; }

    /// <summary>Gets a value indicating whether the sizes of the levels are rounded up (<c>ROUND_UP</c>).</summary>
    public bool RoundsLevelsUp { get; private set; }

    /// <summary>Gets the <c>chromaticities</c> attribute (red, green, blue and white x and y), or <see langword="null"/>.</summary>
    public float[]? Chromaticities { get; private set; }

    /// <summary>Gets a value indicating whether the image is a luminance image (a <c>Y</c> channel) rather than RGB.</summary>
    public bool IsLuminance { get; private set; }

    /// <summary>Gets a value indicating whether the image has an alpha channel.</summary>
    public bool HasAlpha { get; private set; }

    /// <summary>Gets a value indicating whether every channel of the image holds <see cref="ExrPixelType.Half"/> samples.</summary>
    public bool IsHalfOnly { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the image is decoded to <see cref="PixelFormat.RgbaHalf"/>: every channel holds HALF
    /// samples and there is no alpha channel (the colors divided by alpha need the precision of a float to be multiplied
    /// back to the same halves).
    /// </summary>
    public bool UsesHalfPixels => IsHalfOnly && !HasAlpha;

    /// <summary>Gets the number of scan lines per chunk of a scan-line image.</summary>
    public int LinesPerChunk { get; private set; }

    /// <summary>Gets the number of chunks of the image: the number of entries of the offset table.</summary>
    public long ChunkCount { get; private set; }

    /// <summary>Gets the number of chunks of the full-resolution level (all of them for a scan-line image).</summary>
    public long FirstLevelChunkCount { get; private set; }

    /// <summary>Gets the number of levels along x of a tiled image.</summary>
    public int XLevelCount { get; private set; }

    /// <summary>Gets the number of levels along y of a tiled image.</summary>
    public int YLevelCount { get; private set; }

    /// <summary>Gets the size of the uncompressed data of the largest chunk.</summary>
    public long MaxChunkDataLength { get; private set; }

    /// <summary>Reads one attribute of the header.</summary>
    /// <param name="name">The attribute name.</param>
    /// <param name="type">The attribute type name.</param>
    /// <param name="value">The value, or an empty span when <see cref="IsRead"/> is <see langword="false"/> for the name.</param>
    public void ReadAttribute(string name, string type, ReadOnlySpan<byte> value)
    {
        if (!_names.Add(name))
            throw ExrFormat.Invalid($"the header has two attributes named '{name}'.");

        switch (name)
        {
            case "channels":
                ExpectType(name, type, "chlist");
                _channels = ReadChannels(value);
                break;

            case "compression":
                ExpectType(name, type, "compression");
                ExpectSize(name, value, 1);
                if (value[0] > (byte)ExrCompressionMethod.Zstd)
                    throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the compression {value[0]} is not defined."));

                _compression = (ExrCompressionMethod)value[0];
                break;

            case "dataWindow":
                ExpectType(name, type, "box2i");
                _dataWindow = ReadBox(name, value);
                break;

            case "displayWindow":
                ExpectType(name, type, "box2i");
                _displayWindow = ReadBox(name, value);
                break;

            case "lineOrder":
                ExpectType(name, type, "lineOrder");
                ExpectSize(name, value, 1);
                if (value[0] > (byte)ExrLineOrder.RandomY)
                    throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the line order {value[0]} is not defined."));

                _lineOrder = (ExrLineOrder)value[0];
                break;

            case "tiles":
                ExpectType(name, type, "tiledesc");
                ReadTiles(value);
                break;

            case "chromaticities":
                ExpectType(name, type, "chromaticities");
                ExpectSize(name, value, 32);
                var chromaticities = new float[8];
                for (var i = 0; i < chromaticities.Length; i++)
                {
                    chromaticities[i] = BinaryPrimitives.ReadSingleLittleEndian(value[(i * 4)..]);
                }

                Chromaticities = chromaticities;
                break;

            case "type":
                ExpectType(name, type, "string");
                _type = Encoding.Latin1.GetString(value);
                break;

            case "chunkCount":
                ExpectType(name, type, "int");
                ExpectSize(name, value, 4);
                _chunkCount = BinaryPrimitives.ReadInt32LittleEndian(value);
                break;
        }
    }

    /// <summary>Determines whether the value of an attribute is read (the structure parser skips the other values).</summary>
    public static bool IsRead(string name) => name is "channels" or "compression" or "dataWindow" or "displayWindow" or "lineOrder" or "tiles" or "chromaticities" or "type" or "chunkCount";

    /// <summary>Checks the header once every attribute is read and computes the geometry of the chunks.</summary>
    /// <param name="limits">The resource limits of the operation.</param>
    public void Complete(ImageResourceLimits limits)
    {
        if (_type is not null)
        {
            switch (_type)
            {
                case "scanlineimage" when !IsTiled:
                case "tiledimage" when IsTiled:
                    break;
                case "deepscanline" or "deeptile":
                    throw ExrFormat.Unsupported("The OpenEXR image holds deep data, which is not supported.", "Deep data");
                case "scanlineimage" or "tiledimage":
                    throw ExrFormat.Invalid($"the type attribute '{_type}' contradicts the tile flag of the version field.");
                default:
                    throw ExrFormat.Invalid($"the type attribute '{_type}' is not defined.");
            }
        }

        if (_channels is null)
            throw MissingAttribute("channels");

        if (_compression is null)
            throw MissingAttribute("compression");

        if (_dataWindow is null)
            throw MissingAttribute("dataWindow");

        if (_displayWindow is null)
            throw MissingAttribute("displayWindow");

        if (_lineOrder is null)
            throw MissingAttribute("lineOrder");

        if (IsTiled && !_hasTiles)
            throw MissingAttribute("tiles");

        var data = _dataWindow.Value;
        var display = _displayWindow.Value;
        if (data.Width <= 0 || data.Height <= 0)
            throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the data window ({data.MinX}, {data.MinY}) - ({data.MaxX}, {data.MaxY}) is empty."));

        if (display.Width <= 0 || display.Height <= 0)
            throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the display window ({display.MinX}, {display.MinY}) - ({display.MaxX}, {display.MaxY}) is empty."));

        if (_channels.Length == 0)
            throw ExrFormat.Invalid("the channel list is empty.");

        foreach (var channel in _channels)
        {
            if (IsTiled && (channel.XSampling != 1 || channel.YSampling != 1))
                throw ExrFormat.Invalid($"the channel '{channel.Name}' of a tiled image is sub-sampled.");

            if (MathMod(data.MinX, channel.XSampling) != 0 || MathMod(data.MinY, channel.YSampling) != 0 || data.Width % channel.XSampling != 0 || data.Height % channel.YSampling != 0)
                throw ExrFormat.Invalid($"the data window is not a whole number of samples of the channel '{channel.Name}'.");
        }

        if (!ExrFormat.IsDecodable(Compression))
            throw ExrFormat.Unsupported($"The OpenEXR image is compressed with {ExrFormat.GetName(Compression)}, which is not supported.", "Compression: " + ExrFormat.GetName(Compression));

        ResolveRoles();
        if (!display.Contains(data))
            throw ExrFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The OpenEXR image has pixels outside its display window: the data window ({data.MinX}, {data.MinY}) - ({data.MaxX}, {data.MaxY}) is not inside the display window ({display.MinX}, {display.MinY}) - ({display.MaxX}, {display.MaxY}), and they would be lost."), "Pixels outside the display window");

        if (display.Width > int.MaxValue)
            throw new ImageResourceLimitException(ImageResourceLimitKind.Width, limits.MaxWidth, display.Width);

        if (display.Height > int.MaxValue)
            throw new ImageResourceLimitException(ImageResourceLimitKind.Height, limits.MaxHeight, display.Height);

        limits.EnsureCanvasWithinLimits(display.Width, display.Height);
        ComputeChunks();
        if (_chunkCount is { } declared && declared != ChunkCount)
            throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the chunkCount attribute is {declared}, but the image has {ChunkCount} chunks."));
    }

    /// <summary>Gets the size of a level of a tiled image along one axis.</summary>
    public int GetLevelSize(int level, bool horizontal)
    {
        var size = horizontal ? DataWindow.Width : DataWindow.Height;
        var result = RoundsLevelsUp ? (size + (1L << level) - 1) >> level : size >> level;
        return (int)Math.Max(result, 1);
    }

    /// <summary>Gets the number of tiles of a level along one axis.</summary>
    public int GetTileCount(int level, bool horizontal)
    {
        var tile = horizontal ? TileWidth : TileHeight;
        return (int)(((long)GetLevelSize(level, horizontal) + tile - 1) / tile);
    }

    /// <summary>Gets the index in the offset table of the first tile of a level.</summary>
    public long GetFirstTileIndex(int levelX, int levelY)
    {
        long index = 0;
        switch (LevelMode)
        {
            case ExrLevelMode.OneLevel:
                return 0;

            case ExrLevelMode.MipmapLevels:
                for (var level = 0; level < levelX; level++)
                {
                    index += (long)GetTileCount(level, horizontal: true) * GetTileCount(level, horizontal: false);
                }

                return index;

            default:
                for (var ly = 0; ly <= levelY; ly++)
                {
                    for (var lx = 0; lx < XLevelCount; lx++)
                    {
                        if (ly == levelY && lx == levelX)
                            return index;

                        index += (long)GetTileCount(lx, horizontal: true) * GetTileCount(ly, horizontal: false);
                    }
                }

                return index;
        }
    }

    /// <summary>Gets the number of samples of a channel along y in the lines <paramref name="y0"/> to <paramref name="y0"/> + <paramref name="lines"/> - 1.</summary>
    public static int GetSampleLineCount(ExrChannel channel, int y0, int lines)
    {
        // Lines y with y mod ySampling = 0 (mathematical modulo)
        var first = y0 + MathMod(-(long)y0, channel.YSampling);
        var last = (long)y0 + lines - 1;
        return first > last ? 0 : (int)((last - first) / channel.YSampling) + 1;
    }

    /// <summary>Gets the size of the uncompressed data of a chunk.</summary>
    public long GetChunkDataLength(int width, int y0, int lines)
    {
        long length = 0;
        foreach (var channel in Channels)
        {
            length += (long)channel.BytesPerSample * (width / channel.XSampling) * GetSampleLineCount(channel, y0, lines);
        }

        return length;
    }

    private static long MathMod(long value, long modulus)
    {
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private void ResolveRoles()
    {
        var roles = new ExrChannelRole[_channels!.Length];
        var seen = 0;
        var isHalfOnly = true;
        for (var i = 0; i < roles.Length; i++)
        {
            var channel = _channels[i];
            var role = channel.Name switch
            {
                "R" => ExrChannelRole.Red,
                "G" => ExrChannelRole.Green,
                "B" => ExrChannelRole.Blue,
                "A" => ExrChannelRole.Alpha,
                "Y" => ExrChannelRole.Luminance,
                _ => throw ExrFormat.Unsupported($"The OpenEXR image has the channel '{channel.Name}', which is not one of R, G, B, A and Y: it would be lost.", "Extra channels"),
            };

            if (channel.XSampling != 1 || channel.YSampling != 1)
                throw ExrFormat.Unsupported($"The OpenEXR channel '{channel.Name}' is sub-sampled, which is not supported.", "Sub-sampled channels");

            roles[i] = role;
            seen |= 1 << (int)role;
            isHalfOnly &= channel.PixelType == ExrPixelType.Half;
        }

        const int Rgb = (1 << (int)ExrChannelRole.Red) | (1 << (int)ExrChannelRole.Green) | (1 << (int)ExrChannelRole.Blue);
        const int Luminance = 1 << (int)ExrChannelRole.Luminance;
        if ((seen & Luminance) != 0 && (seen & Rgb) != 0)
            throw ExrFormat.Unsupported("The OpenEXR image has both RGB channels and a luminance channel (Y): one of them would be lost.", "Extra channels");

        if ((seen & (Rgb | Luminance)) == 0)
            throw ExrFormat.Unsupported("The OpenEXR image has no color channel (R, G, B or Y).", "Alpha-only image");

        Roles = roles;
        IsLuminance = (seen & Luminance) != 0;
        HasAlpha = (seen & (1 << (int)ExrChannelRole.Alpha)) != 0;
        IsHalfOnly = isHalfOnly;
    }

    private void ComputeChunks()
    {
        var data = DataWindow;
        var width = (int)Math.Min(data.Width, int.MaxValue);
        if (!IsTiled)
        {
            LinesPerChunk = ExrFormat.GetLinesPerChunk(Compression);
            ChunkCount = (data.Height + LinesPerChunk - 1) / LinesPerChunk;
            FirstLevelChunkCount = ChunkCount;
            var lines = (int)Math.Min(LinesPerChunk, data.Height);

            // The largest chunk: every channel has at most ceil(lines / ySampling) lines in it
            long max = 0;
            foreach (var channel in Channels)
            {
                max += (long)channel.BytesPerSample * (width / channel.XSampling) * ((lines + channel.YSampling - 1) / channel.YSampling);
            }

            MaxChunkDataLength = max;
            return;
        }

        XLevelCount = 1;
        YLevelCount = 1;
        if (LevelMode == ExrLevelMode.MipmapLevels)
        {
            XLevelCount = YLevelCount = GetLevelCount(Math.Max(data.Width, data.Height));
        }
        else if (LevelMode == ExrLevelMode.RipmapLevels)
        {
            XLevelCount = GetLevelCount(data.Width);
            YLevelCount = GetLevelCount(data.Height);
        }

        long count = 0;
        if (LevelMode == ExrLevelMode.RipmapLevels)
        {
            for (var ly = 0; ly < YLevelCount; ly++)
            {
                for (var lx = 0; lx < XLevelCount; lx++)
                {
                    count += (long)GetTileCount(lx, horizontal: true) * GetTileCount(ly, horizontal: false);
                }
            }
        }
        else
        {
            for (var level = 0; level < XLevelCount; level++)
            {
                count += (long)GetTileCount(level, horizontal: true) * GetTileCount(level, horizontal: false);
            }
        }

        ChunkCount = count;
        FirstLevelChunkCount = (long)GetTileCount(0, horizontal: true) * GetTileCount(0, horizontal: false);
        long bytesPerPixel = 0;
        foreach (var channel in Channels)
        {
            bytesPerPixel += channel.BytesPerSample;
        }

        MaxChunkDataLength = Math.Min(TileWidth, data.Width) * Math.Min(TileHeight, data.Height) * bytesPerPixel;
    }

    private int GetLevelCount(long size)
    {
        // floor(log2(size)) + 1, or ceil(log2(size)) + 1 with ROUND_UP
        var floor = 63 - BitOperations.LeadingZeroCount((ulong)size);
        var log = RoundsLevelsUp && (size & (size - 1)) != 0 ? floor + 1 : floor;
        return log + 1;
    }

    private void ReadTiles(ReadOnlySpan<byte> value)
    {
        ExpectSize("tiles", value, 9);
        var width = BinaryPrimitives.ReadUInt32LittleEndian(value);
        var height = BinaryPrimitives.ReadUInt32LittleEndian(value[4..]);
        var mode = value[8];
        if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue)
            throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the tile size {width}x{height} is not valid."));

        var levelMode = mode & 0x0F;
        var roundingMode = mode >> 4;
        if (levelMode > (int)ExrLevelMode.RipmapLevels || roundingMode > 1)
            throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the tile mode 0x{mode:X2} is not defined."));

        TileWidth = (int)width;
        TileHeight = (int)height;
        LevelMode = (ExrLevelMode)levelMode;
        RoundsLevelsUp = roundingMode == 1;
        _hasTiles = true;
    }

    private ExrChannel[] ReadChannels(ReadOnlySpan<byte> value)
    {
        var channels = new List<ExrChannel>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var offset = 0;
        while (true)
        {
            if (offset >= value.Length)
                throw ExrFormat.Invalid("the channel list does not end with a null byte.");

            if (value[offset] == 0)
            {
                if (offset + 1 != value.Length)
                    throw ExrFormat.Invalid("the channel list has data after its end.");

                break;
            }

            var end = value[offset..].IndexOf((byte)0);
            if (end < 0)
                throw ExrFormat.Invalid("a channel name does not end with a null byte.");

            if (end > MaxNameLength)
                throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"a channel name has {end} bytes; at most {MaxNameLength} are allowed."));

            var name = Encoding.UTF8.GetString(value.Slice(offset, end));
            offset += end + 1;
            if (value.Length - offset < 16)
                throw ExrFormat.Invalid($"the description of the channel '{name}' is truncated.");

            var type = BinaryPrimitives.ReadInt32LittleEndian(value[offset..]);
            var pLinear = value[offset + 4];
            var xSampling = BinaryPrimitives.ReadInt32LittleEndian(value[(offset + 8)..]);
            var ySampling = BinaryPrimitives.ReadInt32LittleEndian(value[(offset + 12)..]);
            offset += 16;
            if (type is < 0 or > 2)
                throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the pixel type {type} of the channel '{name}' is not defined."));

            if (pLinear > 1)
                throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the pLinear field of the channel '{name}' is {pLinear}; 0 or 1 is expected."));

            if (xSampling < 1 || ySampling < 1)
                throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the sampling {xSampling}x{ySampling} of the channel '{name}' is not valid."));

            if (!names.Add(name))
                throw ExrFormat.Invalid($"the channel list has two channels named '{name}'.");

            channels.Add(new ExrChannel(name, (ExrPixelType)type, pLinear == 1, xSampling, ySampling));
        }

        // The samples of a chunk follow the order of the names (byte-wise), whatever the order of the list
        channels.Sort(static (left, right) => CompareNames(left.Name, right.Name));
        return [.. channels];
    }

    private static int CompareNames(string left, string right)
    {
        Span<byte> leftBytes = stackalloc byte[Encoding.UTF8.GetMaxByteCount(ExrFormat.LongNameMaxLength)];
        Span<byte> rightBytes = stackalloc byte[Encoding.UTF8.GetMaxByteCount(ExrFormat.LongNameMaxLength)];
        var leftLength = Encoding.UTF8.GetBytes(left, leftBytes);
        var rightLength = Encoding.UTF8.GetBytes(right, rightBytes);
        return leftBytes[..leftLength].SequenceCompareTo(rightBytes[..rightLength]);
    }

    private static ExrBox ReadBox(string name, ReadOnlySpan<byte> value)
    {
        ExpectSize(name, value, 16);
        return new ExrBox(
            BinaryPrimitives.ReadInt32LittleEndian(value),
            BinaryPrimitives.ReadInt32LittleEndian(value[4..]),
            BinaryPrimitives.ReadInt32LittleEndian(value[8..]),
            BinaryPrimitives.ReadInt32LittleEndian(value[12..]));
    }

    private static void ExpectType(string name, string type, string expected)
    {
        if (!string.Equals(type, expected, StringComparison.Ordinal))
            throw ExrFormat.Invalid($"the attribute '{name}' has the type '{type}'; '{expected}' is expected.");
    }

    private static void ExpectSize(string name, ReadOnlySpan<byte> value, int expected)
    {
        if (value.Length != expected)
            throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the attribute '{name}' has {value.Length} bytes; {expected} are expected."));
    }

    private static InvalidImageContentException MissingAttribute(string name) => ExrFormat.Invalid($"the required attribute '{name}' is missing.");
}
