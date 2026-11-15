using System.Buffers.Binary;
using System.Numerics;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The validated header of a DDS file (<c>DDS_HEADER</c>, and <c>DDS_HEADER_DXT10</c> when the FourCC is <c>DX10</c>) and
/// the layout of its surfaces.
/// </summary>
/// <remarks>
/// <para>
/// The surfaces follow the header in this order ("Programming Guide for DDS" and its pages on the layouts of textures, cube
/// maps and volume textures): for each element of the array, for each face of a cube map (+X, −X, +Y, −Y, +Z, −Z, the faces
/// a partial cube map stores), for each mipmap level from the largest, the surface of the level; a level of a volume
/// texture stores its depth slices one after the other. A level halves each dimension, rounded down, down to 1.
/// </para>
/// <para>
/// The size of a surface is computed, never read: the rows of a block-compressed format are rows of 4×4 blocks
/// (<c>max(1, (width + 3) / 4)</c> blocks), the others are <c>(width × bits + 7) / 8</c> bytes long. The
/// <c>dwPitchOrLinearSize</c> field, which writers fill unreliably (as the guide says), must agree with that in strict
/// mode when its flag is set (<c>DDSD_PITCH</c>: the length of a row; <c>DDSD_LINEARSIZE</c>: the length of the top
/// surface), and is ignored in lenient mode, as DirectXTex ignores it. The flags the guide says readers should not rely on
/// (<c>DDSD_CAPS</c>, <c>DDSD_PIXELFORMAT</c>, <c>DDSD_MIPMAPCOUNT</c>, <c>DDSCAPS_TEXTURE</c>, <c>DDSCAPS_COMPLEX</c>)
/// are not checked: <c>dwMipMapCount</c> gives the number of levels whatever the flag (0 is one level).
/// </para>
/// </remarks>
internal sealed class DdsHeader
{
    private static readonly CubeMapFace[] AllFaces = [CubeMapFace.PositiveX, CubeMapFace.NegativeX, CubeMapFace.PositiveY, CubeMapFace.NegativeY, CubeMapFace.PositiveZ, CubeMapFace.NegativeZ];

    private DdsHeader(int length, int width, int height, int depth, int mipCount, long arraySize, CubeMapFace[]? faces, DdsPixelLayout layout, int alphaMode, bool hasDx10Header, uint resourceDimension)
    {
        Length = length;
        Width = width;
        Height = height;
        Depth = depth;
        MipCount = mipCount;
        ArraySize = arraySize;
        Faces = faces;
        Layout = layout;
        AlphaMode = alphaMode;
        HasDx10Header = hasDx10Header;
        ResourceDimension = resourceDimension;
    }

    /// <summary>Gets the number of bytes of the magic number and the headers: 128, or 148 with a DX10 header.</summary>
    public int Length { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Gets the depth of a volume texture, 1 otherwise.</summary>
    public int Depth { get; }

    /// <summary>Gets the number of mipmap levels (at least 1).</summary>
    public int MipCount { get; }

    /// <summary>Gets the number of elements of the array (of cube maps for a cube map), 1 without a DX10 header.</summary>
    public long ArraySize { get; }

    /// <summary>Gets the faces a cube map stores, in file order, or <see langword="null"/> when the texture is not a cube map.</summary>
    public CubeMapFace[]? Faces { get; }

    public DdsPixelLayout Layout { get; }

    /// <summary>Gets the alpha mode of the DX10 header (<c>DDS_ALPHA_MODE</c>: 0 unknown, 1 straight, 3 opaque, 4 custom), 0 without one.</summary>
    public int AlphaMode { get; }

    public bool HasDx10Header { get; }

    /// <summary>Gets the <c>resourceDimension</c> of the DX10 header, or the dimension the legacy header implies.</summary>
    public uint ResourceDimension { get; }

    public bool IsVolume => ResourceDimension == DdsFormat.DimensionTexture3D;

    public bool IsCube => Faces is not null;

    /// <summary>Gets the number of faces of an element of the array: the stored faces of a cube map, otherwise 1.</summary>
    public int FaceCount => Faces?.Length ?? 1;

    public Size Size => new(Width, Height);

    /// <summary>Gets the number of bytes of the headers that a header with these first 128 bytes has.</summary>
    /// <param name="header">At least the first 128 bytes.</param>
    public static int GetLength(ReadOnlySpan<byte> header)
    {
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(header[80..]);
        var fourCC = BinaryPrimitives.ReadUInt32LittleEndian(header[84..]);
        return (flags & DdsFormat.PixelFourCC) != 0 && fourCC == DdsFormat.FourCC("DX10") ? DdsFormat.HeaderLength + DdsFormat.Dx10HeaderLength : DdsFormat.HeaderLength;
    }

    /// <summary>Reads and validates the headers.</summary>
    /// <param name="header">The <see cref="GetLength"/> bytes of the headers.</param>
    /// <param name="context">The context: its limits, and whether decoding is lenient.</param>
    /// <returns>The header.</returns>
    /// <exception cref="InvalidImageContentException">The header is malformed or inconsistent.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The header is valid but uses an unsupported feature.</exception>
    /// <exception cref="ImageResourceLimitException">The top surface exceeds a limit.</exception>
    public static DdsHeader Parse(ReadOnlySpan<byte> header, ImageCodecContext context)
    {
        if (!DdsFormat.MatchesSignature(header))
            throw DdsFormat.Invalid("The DDS magic number 'DDS ' is missing.");

        var size = ReadUInt32(header, 4);
        if (size != DdsFormat.HeaderSize)
            throw DdsFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DDS header size (dwSize) is {size}; 124 is expected."));

        var pixelFormatSize = ReadUInt32(header, 76);
        if (pixelFormatSize != DdsFormat.PixelFormatSize)
            throw DdsFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DDS pixel format size (ddspf.dwSize) is {pixelFormatSize}; 32 is expected."));

        var flags = ReadUInt32(header, 8);
        var height = ReadUInt32(header, 12);
        var width = ReadUInt32(header, 16);
        var pitchOrLinearSize = ReadUInt32(header, 20);
        var depth = ReadUInt32(header, 24);
        var mipMapCount = ReadUInt32(header, 28);
        var pixelFlags = ReadUInt32(header, 80);
        var fourCC = ReadUInt32(header, 84);
        var caps2 = ReadUInt32(header, 112);

        if (width == 0 || height == 0)
            throw DdsFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DDS surface is {width}x{height}: both dimensions must be positive."));

        var limits = context.Limits;
        if (width > int.MaxValue)
            throw new ImageResourceLimitException(ImageResourceLimitKind.Width, limits.MaxWidth, width);

        if (height > int.MaxValue)
            throw new ImageResourceLimitException(ImageResourceLimitKind.Height, limits.MaxHeight, height);

        limits.EnsureCanvasWithinLimits((int)width, (int)height);

        DdsPixelLayout layout;
        long arraySize = 1;
        CubeMapFace[]? faces = null;
        uint dimension;
        var alphaMode = 0;
        var hasDx10 = header.Length >= DdsFormat.HeaderLength + DdsFormat.Dx10HeaderLength && GetLength(header) > DdsFormat.HeaderLength;
        if (hasDx10)
        {
            var dx10 = header[DdsFormat.HeaderLength..];
            var dxgiFormat = ReadUInt32(dx10, 0);
            dimension = ReadUInt32(dx10, 4);
            var miscFlag = ReadUInt32(dx10, 8);
            arraySize = ReadUInt32(dx10, 12);
            var miscFlags2 = ReadUInt32(dx10, 16);
            if (dimension is not (DdsFormat.DimensionTexture1D or DdsFormat.DimensionTexture2D or DdsFormat.DimensionTexture3D))
                throw DdsFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DX10 header of the DDS file has the resource dimension {dimension}; 2 (1D), 3 (2D) or 4 (3D) is expected."));

            if (arraySize == 0)
                throw DdsFormat.Invalid("The DX10 header of the DDS file has an array size of 0.");

            if ((miscFlag & DdsFormat.MiscTextureCube) != 0)
            {
                if (dimension != DdsFormat.DimensionTexture2D)
                    throw DdsFormat.Invalid("The DX10 header of the DDS file declares a cube map (DDS_RESOURCE_MISC_TEXTURECUBE) that is not a 2D texture.");

                if (width != height)
                    throw DdsFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The faces of the DDS cube map are {width}x{height}: they must be square."));

                faces = AllFaces;
            }

            if (dimension == DdsFormat.DimensionTexture3D && arraySize != 1)
                throw DdsFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DX10 header of the DDS volume texture has an array size of {arraySize}; 1 is expected."));

            if (dimension == DdsFormat.DimensionTexture1D && height != 1)
                throw DdsFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DX10 header of the DDS file declares a 1D texture of height {height}; 1 is expected."));

            alphaMode = (int)(miscFlags2 & DdsFormat.AlphaModeMask);
            if (alphaMode > 4)
                throw DdsFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DX10 header of the DDS file has the alpha mode {alphaMode}; 0 to 4 are defined."));

            layout = DdsPixelLayout.FromDxgi(dxgiFormat);
        }
        else
        {
            layout = DdsPixelLayout.FromLegacy(pixelFlags, fourCC, ReadUInt32(header, 88), ReadUInt32(header, 92), ReadUInt32(header, 96), ReadUInt32(header, 100), ReadUInt32(header, 104));
            dimension = DdsFormat.DimensionTexture2D;
            if ((caps2 & DdsFormat.Caps2Volume) != 0)
            {
                if ((caps2 & DdsFormat.Caps2CubeMap) != 0)
                    throw DdsFormat.Invalid("The DDS header declares both a cube map and a volume texture (DDSCAPS2_CUBEMAP and DDSCAPS2_VOLUME).");

                dimension = DdsFormat.DimensionTexture3D;
            }
            else if ((caps2 & DdsFormat.Caps2CubeMap) != 0)
            {
                if ((caps2 & DdsFormat.Caps2CubeMapAllFaces) == 0)
                    throw DdsFormat.Invalid("The DDS cube map (DDSCAPS2_CUBEMAP) stores no face: none of the DDSCAPS2_CUBEMAP_POSITIVEX to NEGATIVEZ flags is set.");

                if (width != height)
                    throw DdsFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The faces of the DDS cube map are {width}x{height}: they must be square."));

                var stored = new List<CubeMapFace>(6);
                for (var i = 0; i < 6; i++)
                {
                    if ((caps2 & (0x400u << i)) != 0)
                        stored.Add(AllFaces[i]);
                }

                faces = [.. stored];
            }
        }

        if (alphaMode == 2)
            throw DdsFormat.Unsupported("The DX10 header of the DDS file declares premultiplied alpha (DDS_ALPHA_MODE_PREMULTIPLIED); the working pixel formats store straight alpha and the library never divides it out.", "Premultiplied alpha");

        if (dimension == DdsFormat.DimensionTexture3D)
        {
            if (depth == 0)
                throw DdsFormat.Invalid("The DDS volume texture has a depth of 0.");

            if (depth > int.MaxValue)
                throw DdsFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DDS volume texture has a depth of {depth}."));
        }
        else
        {
            depth = 1;
        }

        var largest = Math.Max(Math.Max(width, height), depth);
        var maxMips = 32 - BitOperations.LeadingZeroCount(largest);
        var mipCount = mipMapCount == 0 ? 1u : mipMapCount;
        if (mipCount > maxMips)
            throw DdsFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DDS texture declares {mipCount} mipmap levels; a texture of {width}x{height}{(dimension == DdsFormat.DimensionTexture3D ? "x" + depth.ToString(CultureInfo.InvariantCulture) : "")} has at most {maxMips}."));

        var result = new DdsHeader(header.Length, (int)width, (int)height, (int)depth, (int)mipCount, arraySize, faces, layout, alphaMode, hasDx10, dimension);
        if (!context.IsLenient)
        {
            result.CheckPitch(flags, pitchOrLinearSize);
        }

        // The size of the data of the file must be representable
        _ = result.DataLength;
        return result;
    }

    /// <summary>Gets the width of a mipmap level.</summary>
    public int GetWidth(int mip) => Math.Max(1, Width >> mip);

    /// <summary>Gets the height of a mipmap level.</summary>
    public int GetHeight(int mip) => Math.Max(1, Height >> mip);

    /// <summary>Gets the number of depth slices of a mipmap level: 1 unless the texture is a volume texture.</summary>
    public int GetDepth(int mip) => Math.Max(1, Depth >> mip);

    /// <summary>Gets the number of bytes of a row of a level: a row of pixels, or of 4×4 blocks.</summary>
    public long GetRowLength(int mip) => Layout.IsBlockCompressed
        ? ((GetWidth(mip) + 3L) / 4) * Layout.BlockLength
        : (long)GetWidth(mip) * Layout.BytesPerPixel;

    /// <summary>Gets the number of rows of a level: rows of pixels, or of 4×4 blocks.</summary>
    public int GetRowCount(int mip) => Layout.IsBlockCompressed ? (GetHeight(mip) + 3) / 4 : GetHeight(mip);

    /// <summary>Gets the number of bytes of one surface (one depth slice) of a level.</summary>
    public long GetSurfaceLength(int mip) => Multiply(GetRowLength(mip), GetRowCount(mip));

    /// <summary>Gets the number of bytes of a face (or of an element of the array): every level, every depth slice.</summary>
    public long FaceLength
    {
        get
        {
            long length = 0;
            for (var mip = 0; mip < MipCount; mip++)
            {
                length = Add(length, Multiply(GetSurfaceLength(mip), GetDepth(mip)));
            }

            return length;
        }
    }

    /// <summary>Gets the number of surfaces of the file (images of a level, a face, an element and a depth slice).</summary>
    /// <exception cref="InvalidImageContentException">The number cannot be represented.</exception>
    public long SurfaceCount
    {
        get
        {
            long perFace = 0;
            for (var mip = 0; mip < MipCount; mip++)
            {
                perFace += GetDepth(mip);
            }

            return Multiply(Multiply(perFace, FaceCount), ArraySize);
        }
    }

    /// <summary>Gets the number of bytes of the data after the headers.</summary>
    /// <exception cref="InvalidImageContentException">The length cannot be represented.</exception>
    public long DataLength
    {
        get => Multiply(Multiply(FaceLength, FaceCount), ArraySize);
    }

    /// <summary>Gets the metadata the header gives: the sRGB label of the <c>_SRGB</c> formats, the linear one of floating-point formats.</summary>
    public ImageMetadata CreateMetadata()
    {
        var metadata = new ImageMetadata { SourceFormat = ImageFormat.Dds };
        if (Layout.IsSrgb)
        {
            metadata.Cicp = new CicpColorDescription(1, 13, 0, fullRange: true);
        }
        else if (Layout.IsFloatingPoint)
        {
            metadata.TransferFunction = ColorTransferFunction.Linear;
        }

        return metadata;
    }

    /// <summary>Locates a surface.</summary>
    /// <param name="index">The index of the surface, in file order.</param>
    /// <returns>The surface and the offset of its data from the end of the headers.</returns>
    public (TextureSurface Surface, Size Size, long Offset) GetSurface(long index)
    {
        var perFace = 0L;
        for (var mip = 0; mip < MipCount; mip++)
        {
            perFace += GetDepth(mip);
        }

        var faceIndex = index / perFace;
        var withinFace = index % perFace;
        var arrayIndex = faceIndex / FaceCount;
        var face = Faces?[faceIndex % FaceCount];
        var offset = faceIndex * FaceLength;
        for (var mip = 0; mip < MipCount; mip++)
        {
            var slices = GetDepth(mip);
            if (withinFace < slices)
            {
                offset += withinFace * GetSurfaceLength(mip);
                return (new TextureSurface((int)arrayIndex, face, mip, (int)withinFace), new Size(GetWidth(mip), GetHeight(mip)), offset);
            }

            withinFace -= slices;
            offset += slices * GetSurfaceLength(mip);
        }

        throw new ArgumentOutOfRangeException(nameof(index), index, "The surface is outside the texture.");
    }

    private void CheckPitch(uint flags, uint value)
    {
        var hasPitch = (flags & DdsFormat.FlagPitch) != 0;
        var hasLinearSize = (flags & DdsFormat.FlagLinearSize) != 0;
        if (!hasPitch && !hasLinearSize)
            return;

        var rowLength = GetRowLength(0);
        var surfaceLength = GetSurfaceLength(0);
        if ((hasPitch && value == rowLength) || (hasLinearSize && value == surfaceLength))
            return;

        var field = hasPitch ? "DDSD_PITCH" : "DDSD_LINEARSIZE";
        var expected = hasPitch ? rowLength : surfaceLength;
        throw DdsFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DDS header gives a dwPitchOrLinearSize of {value} with {field}; the {Layout.Name} surface of {Width}x{Height} has {expected}. Lenient decoding ignores the field, as DirectXTex does."));
    }

    private static long Multiply(long a, long b)
    {
        try
        {
            return checked(a * b);
        }
        catch (OverflowException)
        {
            throw TooLarge();
        }
    }

    private static long Add(long a, long b)
    {
        try
        {
            return checked(a + b);
        }
        catch (OverflowException)
        {
            throw TooLarge();
        }
    }

    private static InvalidImageContentException TooLarge() => DdsFormat.Invalid("The DDS header describes more data than a file can hold.");

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
}
