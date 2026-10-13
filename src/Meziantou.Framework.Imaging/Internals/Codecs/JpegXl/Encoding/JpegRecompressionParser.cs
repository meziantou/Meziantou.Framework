namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Takes a whole JPEG file from a stream and recompresses it (<see cref="JpegXlJpegRecompressor"/>): the file is read to
/// its end before anything is done with it, since what follows its last marker is part of what is stored.
/// </summary>
internal sealed class JpegRecompressionParser : ImageParser<byte[]>
{
    private readonly int _effort;
    private readonly ImageCodecContext _context;
    private byte[] _result = [];

    public JpegRecompressionParser(int effort, ImageCodecContext context)
        : base(ImageFormat.Jpeg)
    {
        _effort = effort;
        _context = context;
    }

    public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
    {
        if (!isEndOfInput)
        {
            // The end of the input is the end of the file
            consumed = 0;
            return ParseStatus.NeedMoreDataOrEnd(buffer.Length + 1);
        }

        _result = JpegXlJpegRecompressor.Recompress(buffer, _effort, _context);
        consumed = buffer.Length;
        return ParseStatus.Complete;
    }

    public override byte[] GetResult() => _result;
}
