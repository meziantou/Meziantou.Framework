using System.IO.Pipelines;

namespace Meziantou.Framework.Bencode;

public sealed class BencodeDocument
{
    public BencodeDocument(BencodeValue root)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
    }

    public BencodeValue Root { get; }

    public static BencodeDocument Parse(ReadOnlySpan<byte> data)
    {
        var value = BencodeDecoder.Parse(data);
        return new BencodeDocument(value);
    }

    public static async ValueTask<BencodeDocument> ParseAsync(PipeReader reader, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var value = await BencodePipeReaderDecoder.ParseAsync(reader, cancellationToken).ConfigureAwait(false);
        return new BencodeDocument(value);
    }

    public static async ValueTask<BencodeDocument> ParseAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var reader = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            return await ParseAsync(reader, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
        }
    }

    public byte[] ToArray()
    {
        return Root.ToUtf8ByteArray(canonical: true);
    }

    public ValueTask WriteToAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        return Root.WriteToAsync(stream, canonical: true, cancellationToken);
    }
}
