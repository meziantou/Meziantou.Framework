using System.Buffers.Binary;
using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Mp4;

internal sealed class Mp4Atom
{
    /// <summary>The maximum number of nested container atoms a file may contain.</summary>
    /// <remarks>
    /// Reading is recursive, so an unbounded nesting depth would overflow the stack, which cannot be caught.
    /// Real files nest a handful of levels (moov/trak/mdia/minf/stbl/stsd); this limit is far above any legitimate use.
    /// </remarks>
    public const int MaxDepth = 32;

    /// <summary>The maximum number of atoms read from one file.</summary>
    /// <remarks>
    /// An empty atom costs 8 bytes in the file but a retained object here, so an unbounded count lets a small
    /// file force a disproportionate allocation.
    /// </remarks>
    public const int MaxCount = 65536;

    /// <summary>The chunk offset table of a track, in 32-bit and 64-bit form.</summary>
    public const string ChunkOffsetTable = "stco";
    public const string ChunkOffsetTable64 = "co64";

    public long Position { get; set; }
    public long Size { get; set; }

    /// <summary>Gets the size of this atom's header, which is 16 bytes for an atom using a 64-bit size.</summary>
    public long HeaderSize { get; set; }

    public string Type { get; set; } = "";
    public byte[]? Data { get; set; }
    public List<Mp4Atom> Children { get; } = [];

    /// <summary>
    /// Reads the atom tree from a stream.
    /// </summary>
    /// <param name="complete">
    /// <see langword="true"/> when every byte of the container was accounted for. A writer must not rebuild a
    /// file from an incomplete parse: the atoms that were not reached, <c>mdat</c> included, would be dropped.
    /// </param>
    public static List<Mp4Atom> ReadAtoms(Stream stream, long endPosition, out bool complete)
    {
        var atoms = new List<Mp4Atom>();
        var remainingCount = MaxCount;
        complete = ReadAtoms(stream, endPosition, depth: 0, ref remainingCount, atoms);
        return atoms;
    }

    private static bool ReadAtoms(Stream stream, long endPosition, int depth, ref int remainingCount, List<Mp4Atom> atoms)
    {
        if (depth >= MaxDepth)
            throw new InvalidDataException($"MP4 atoms are nested too deeply. The maximum supported depth is {MaxDepth}.");

        Span<byte> headerBuf = stackalloc byte[16]; // Reuse for both header and extended size

        while (stream.Position < endPosition)
        {
            if (remainingCount <= 0)
                throw new InvalidDataException($"The file declares more than {MaxCount} MP4 atoms.");

            var atomPosition = stream.Position;
            if (stream.ReadAtLeast(headerBuf[..8], 8, throwOnEndOfStream: false) < 8)
                return false;

            var size = (long)BinaryPrimitives.ReadUInt32BigEndian(headerBuf);
            var type = Encoding.Latin1.GetString(headerBuf[4..8]);

            long headerSize = 8;
            if (size == 1)
            {
                // Extended size (64-bit)
                if (stream.ReadAtLeast(headerBuf[8..16], 8, throwOnEndOfStream: false) < 8)
                    return false;

                size = BinaryPrimitives.ReadInt64BigEndian(headerBuf[8..16]);
                headerSize = 16;
            }
            else if (size == 0)
            {
                // Atom extends to end of file
                size = endPosition - atomPosition;
            }

            if (size < headerSize || atomPosition > endPosition - size)
                return false;

            remainingCount--;
            var atom = new Mp4Atom
            {
                Position = atomPosition,
                Size = size,
                HeaderSize = headerSize,
                Type = type,
            };

            var dataSize = size - headerSize;
            var atomEnd = atomPosition + size;

            if (IsContainerAtom(type))
            {
                // For 'meta' atom, skip 4-byte version/flags
                if (type == "meta")
                {
                    if (dataSize < 4)
                        return false;

                    stream.Seek(4, SeekOrigin.Current);
                }

                if (!ReadAtoms(stream, atomEnd, depth + 1, ref remainingCount, atom.Children))
                    return false;
            }
            else if (ShouldBufferData(type) && dataSize > 0 && dataSize <= StreamHelpers.MaxRecordDataSize)
            {
                atom.Data = new byte[dataSize];
                if (stream.ReadAtLeast(atom.Data, (int)dataSize, throwOnEndOfStream: false) < dataSize)
                    return false;
            }

            stream.Position = atomEnd;
            atoms.Add(atom);
        }

        return true;
    }

    /// <summary>
    /// Whether the content of an atom is needed in memory.
    /// </summary>
    /// <remarks>
    /// <c>mdat</c> holds the audio and is not a container, so buffering every non-container atom costs a full
    /// read and a large object heap allocation per file for bytes nothing looks at.
    /// </remarks>
    private static bool ShouldBufferData(string type)
    {
        return type is "data" or "mean" or "name" or "mvhd" or "mdhd" or "hdlr" or ChunkOffsetTable or ChunkOffsetTable64;
    }

    public Mp4Atom? FindChild(string type)
    {
        foreach (var child in Children)
        {
            if (child.Type == type)
                return child;
        }

        return null;
    }

    public static Mp4Atom? FindPath(List<Mp4Atom> atoms, params string[] path)
    {
        var current = atoms;
        Mp4Atom? result = null;

        foreach (var segment in path)
        {
            result = null;
            foreach (var atom in current)
            {
                if (atom.Type == segment)
                {
                    result = atom;
                    break;
                }
            }

            if (result is null)
                return null;

            current = result.Children;
        }

        return result;
    }

    /// <summary>Enumerates this atom and all of its descendants.</summary>
    public IEnumerable<Mp4Atom> DescendantsAndSelf()
    {
        yield return this;

        foreach (var child in Children)
        {
            foreach (var descendant in child.DescendantsAndSelf())
            {
                yield return descendant;
            }
        }
    }

    private static bool IsContainerAtom(string type)
    {
        return type is "moov" or "trak" or "mdia" or "minf" or "stbl" or "udta" or "meta" or "ilst"
            or ItunesAtomNames.Title or ItunesAtomNames.Artist or ItunesAtomNames.Album
            or ItunesAtomNames.AlbumArtist or ItunesAtomNames.Genre or ItunesAtomNames.Year
            or ItunesAtomNames.TrackNumber or ItunesAtomNames.DiscNumber or ItunesAtomNames.Composer
            or ItunesAtomNames.Conductor or ItunesAtomNames.Comment or ItunesAtomNames.Lyrics
            or ItunesAtomNames.Copyright or ItunesAtomNames.Bpm
            or ItunesAtomNames.Compilation or ItunesAtomNames.CoverArt or ItunesAtomNames.Freeform
            or "aART";
    }
}
