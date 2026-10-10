namespace Meziantou.Framework.Imaging.Tests;

/// <summary>One directory entry assembled by <see cref="IcoFileBuilder"/>.</summary>
internal sealed class IcoEntrySpec
{
    /// <summary>The displayed width, used to fill the directory byte when <see cref="DeclaredWidth"/> is not set.</summary>
    public required int Width { get; init; }

    /// <summary>The displayed height, used to fill the directory byte when <see cref="DeclaredHeight"/> is not set.</summary>
    public required int Height { get; init; }

    /// <summary>The encoded payload: a DIB or a PNG.</summary>
    public required byte[] Payload { get; init; }

    /// <summary>An override of the <c>bWidth</c> byte (0 means 256).</summary>
    public byte? DeclaredWidth { get; init; }

    /// <summary>An override of the <c>bHeight</c> byte (0 means 256).</summary>
    public byte? DeclaredHeight { get; init; }

    public byte ColorCount { get; init; }

    /// <summary>The <c>bReserved</c> byte; must be 0 in a valid file.</summary>
    public byte Reserved { get; init; }

    /// <summary>The color planes of an icon entry, or the hotspot X of a cursor entry.</summary>
    public ushort PlanesOrHotspotX { get; init; } = 1;

    /// <summary>The bit count of an icon entry, or the hotspot Y of a cursor entry.</summary>
    public ushort BitCountOrHotspotY { get; init; } = 32;

    /// <summary>An override of <c>dwBytesInRes</c>, for malformed files.</summary>
    public uint? LengthOverride { get; init; }

    /// <summary>An override of <c>dwImageOffset</c>, for malformed files.</summary>
    public uint? OffsetOverride { get; init; }
}
