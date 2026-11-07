using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Assembles Windows animated cursor files byte by byte for the decoder tests, independently of the encoder: the RIFF
/// <c>ACON</c> container, the <c>anih</c> header, the optional <c>rate</c> and <c>seq </c> tables, the <c>INFO</c> list
/// and the <c>fram</c> list, with control over every count, size and chunk position so that malformed files can be built.
/// Frames are icon or cursor files, typically produced by <see cref="IcoFileBuilder"/>.
/// </summary>
internal sealed class AniFileBuilder
{
    private readonly List<byte[]> _frames = [];

    /// <summary>Gets the default display rate of a step, in jiffies (<c>iDispRate</c>).</summary>
    public uint DisplayRate { get; init; } = 10;

    /// <summary>Gets the content of the <c>rate</c> chunk, or <see langword="null"/> to leave the chunk out.</summary>
    public uint[]? Rates { get; init; }

    /// <summary>Gets the content of the <c>seq </c> chunk, or <see langword="null"/> to leave the chunk out.</summary>
    public uint[]? Sequence { get; init; }

    /// <summary>Gets the <c>INAM</c> string of the <c>INFO</c> list, or <see langword="null"/>.</summary>
    public string? Title { get; init; }

    /// <summary>Gets the <c>IART</c> string of the <c>INFO</c> list, or <see langword="null"/>.</summary>
    public string? Author { get; init; }

    /// <summary>Gets an override of the flags (by default: the icon flag, plus the sequence flag when there is a sequence).</summary>
    public uint? Flags { get; init; }

    /// <summary>Gets an override of the declared number of frames.</summary>
    public uint? FrameCount { get; init; }

    /// <summary>Gets an override of the declared number of steps (by default: the length of the sequence, else of the rates, else the number of frames).</summary>
    public uint? StepCount { get; init; }

    /// <summary>Gets an override of the <c>cbSize</c> field of the header.</summary>
    public uint? HeaderLengthField { get; init; }

    /// <summary>Gets an override of the number of bytes of the <c>anih</c> chunk.</summary>
    public int? HeaderChunkLength { get; init; }

    /// <summary>Gets an override of the RIFF size field.</summary>
    public uint? RiffSize { get; init; }

    /// <summary>Gets an override of the size field of the <c>fram</c> list.</summary>
    public uint? FrameListSize { get; init; }

    /// <summary>Gets a value indicating whether the <c>rate</c> and <c>seq </c> chunks follow the frame list instead of preceding it.</summary>
    public bool TablesAfterFrames { get; init; }

    /// <summary>Gets the number of <c>anih</c> chunks written (0 leaves the header out, 2 duplicates it).</summary>
    public int HeaderChunks { get; init; } = 1;

    /// <summary>Gets the number of <c>fram</c> lists written (0 leaves the list out, 2 duplicates it).</summary>
    public int FrameLists { get; init; } = 1;

    /// <summary>Gets raw chunks written right after the form type, before every other chunk.</summary>
    public byte[] LeadingChunks { get; init; } = [];

    /// <summary>Gets raw chunks written inside the <c>fram</c> list, before the first <c>icon</c> chunk.</summary>
    public byte[] FrameListLeadingChunks { get; init; } = [];

    /// <summary>Gets raw bytes appended after the last chunk, inside the RIFF container.</summary>
    public byte[] TrailingBytes { get; init; } = [];

    public AniFileBuilder AddFrame(byte[] iconOrCursorFile)
    {
        _frames.Add(iconOrCursorFile);
        return this;
    }

    public byte[] Build()
    {
        var content = new List<byte>();
        content.AddRange("ACON"u8);
        content.AddRange(LeadingChunks);
        if (Title is not null || Author is not null)
        {
            var info = new List<byte>();
            info.AddRange("INFO"u8);
            if (Title is not null)
            {
                info.AddRange(Chunk("INAM", [.. Encoding.Latin1.GetBytes(Title), 0]));
            }

            if (Author is not null)
            {
                info.AddRange(Chunk("IART", [.. Encoding.Latin1.GetBytes(Author), 0]));
            }

            content.AddRange(Chunk("LIST", [.. info]));
        }

        for (var i = 0; i < HeaderChunks; i++)
        {
            content.AddRange(Chunk("anih", CreateHeader()));
        }

        if (!TablesAfterFrames)
        {
            AddTables(content);
        }

        for (var i = 0; i < FrameLists; i++)
        {
            var list = new List<byte>();
            list.AddRange("fram"u8);
            list.AddRange(FrameListLeadingChunks);
            foreach (var frame in _frames)
            {
                list.AddRange(Chunk("icon", frame));
            }

            var chunk = Chunk("LIST", [.. list]);
            if (FrameListSize is { } frameListSize)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), frameListSize);
            }

            content.AddRange(chunk);
        }

        if (TablesAfterFrames)
        {
            AddTables(content);
        }

        content.AddRange(TrailingBytes);
        var file = new byte[8 + content.Count];
        "RIFF"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), RiffSize ?? (uint)content.Count);
        content.CopyTo(file, 8);
        return file;
    }

    /// <summary>Builds a chunk: the four-character code, the size, the data and, when the size is odd, a pad byte.</summary>
    public static byte[] Chunk(string fourCC, byte[] data)
    {
        var chunk = new byte[8 + data.Length + (data.Length & 1)];
        Encoding.ASCII.GetBytes(fourCC, chunk);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)data.Length);
        data.CopyTo(chunk.AsSpan(8));
        return chunk;
    }

    private static byte[] Table(uint[] values)
    {
        var data = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 4), values[i]);
        }

        return data;
    }

    private void AddTables(List<byte> content)
    {
        if (Rates is not null)
        {
            content.AddRange(Chunk("rate", Table(Rates)));
        }

        if (Sequence is not null)
        {
            content.AddRange(Chunk("seq ", Table(Sequence)));
        }
    }

    private byte[] CreateHeader()
    {
        var header = new byte[HeaderChunkLength ?? 36];
        var fields = new byte[36];
        BinaryPrimitives.WriteUInt32LittleEndian(fields, HeaderLengthField ?? 36);
        BinaryPrimitives.WriteUInt32LittleEndian(fields.AsSpan(4), FrameCount ?? (uint)_frames.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(fields.AsSpan(8), StepCount ?? (uint)(Sequence?.Length ?? Rates?.Length ?? _frames.Count));
        BinaryPrimitives.WriteUInt32LittleEndian(fields.AsSpan(28), DisplayRate);
        BinaryPrimitives.WriteUInt32LittleEndian(fields.AsSpan(32), Flags ?? (1u | (Sequence is null ? 0u : 2u)));
        fields.AsSpan(0, Math.Min(fields.Length, header.Length)).CopyTo(header);
        return header;
    }
}
