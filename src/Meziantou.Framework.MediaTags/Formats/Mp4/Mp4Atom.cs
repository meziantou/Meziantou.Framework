using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.MediaTags.Formats.Mp4;

internal sealed class Mp4Atom
{
    public long Position { get; set; }
    public long Size { get; set; }
    public string Type { get; set; } = "";
    public byte[]? Data { get; set; }
    public List<Mp4Atom> Children { get; } = [];

    /// <summary>
    /// Reads the atom tree from a stream, optionally recursing into container atoms.
    /// </summary>
    public static List<Mp4Atom> ReadAtoms(Stream stream, long endPosition, bool recurse = true)
    {
        var atoms = new List<Mp4Atom>();
        Span<byte> headerBuf = stackalloc byte[16]; // Reuse for both header and extended size

        while (stream.Position < endPosition)
        {
            var atomPosition = stream.Position;
            if (stream.ReadAtLeast(headerBuf[..8], 8, throwOnEndOfStream: false) < 8)
                break;

            var size = (long)BinaryPrimitives.ReadUInt32BigEndian(headerBuf);
            var type = Encoding.Latin1.GetString(headerBuf[4..8]);

            long headerSize = 8;
            if (size == 1)
            {
                // Extended size (64-bit)
                if (stream.ReadAtLeast(headerBuf[8..16], 8, throwOnEndOfStream: false) < 8)
                    break;
                size = BinaryPrimitives.ReadInt64BigEndian(headerBuf[8..16]);
                headerSize = 16;
            }
            else if (size == 0)
            {
                // Atom extends to end of file
                size = endPosition - atomPosition;
            }

            var atom = new Mp4Atom
            {
                Position = atomPosition,
                Size = size,
                Type = type,
            };

            var dataSize = size - headerSize;
            var atomEnd = atomPosition + size;

            if (recurse && IsContainerAtom(type))
            {
                // For 'meta' atom, skip 4-byte version/flags
                if (type == "meta")
                {
                    stream.Seek(4, SeekOrigin.Current);
                    atom.Children.AddRange(ReadAtoms(stream, atomEnd, recurse: true));
                }
                else
                {
                    atom.Children.AddRange(ReadAtoms(stream, atomEnd, recurse: true));
                }
            }
            else if (dataSize > 0 && dataSize <= 10 * 1024 * 1024) // Max 10MB for single atom data
            {
                atom.Data = new byte[dataSize];
                stream.ReadAtLeast(atom.Data, (int)dataSize, throwOnEndOfStream: false);
            }

            stream.Position = atomEnd;
            atoms.Add(atom);
        }

        return atoms;
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

    private static bool IsContainerAtom(string type)
    {
        return type is "moov" or "trak" or "mdia" or "minf" or "stbl" or "udta" or "meta" or "ilst"
            or ItunesAtomNames.Title or ItunesAtomNames.Artist or ItunesAtomNames.Album
            or ItunesAtomNames.AlbumArtist or ItunesAtomNames.Genre or ItunesAtomNames.Year
            or ItunesAtomNames.TrackNumber or ItunesAtomNames.DiscNumber or ItunesAtomNames.Composer
            or ItunesAtomNames.Comment or ItunesAtomNames.Lyrics or ItunesAtomNames.Copyright or ItunesAtomNames.Bpm
            or ItunesAtomNames.Compilation or ItunesAtomNames.CoverArt or ItunesAtomNames.Freeform
            or "aART";
    }
}
