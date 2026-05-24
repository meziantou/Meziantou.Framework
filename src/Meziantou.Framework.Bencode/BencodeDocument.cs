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

    public static async ValueTask<BencodeDocument> ParseAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return Parse(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
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
