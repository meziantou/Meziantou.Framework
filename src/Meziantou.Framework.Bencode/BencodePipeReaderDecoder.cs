using System.IO.Pipelines;

namespace Meziantou.Framework.Bencode;

internal static class BencodePipeReaderDecoder
{
    public static async ValueTask<BencodeValue> ParseAsync(PipeReader reader, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var bencodeReader = new BencodeReader(reader);
        await using (bencodeReader.ConfigureAwait(false))
        {
            await bencodeReader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var value = await bencodeReader.ReadValueAsync(cancellationToken).ConfigureAwait(false);
            if (await bencodeReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new FormatException("Unexpected trailing data after bencode value.");

            return value;
        }
    }
}
