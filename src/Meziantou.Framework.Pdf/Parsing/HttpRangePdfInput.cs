using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>
/// A file read on demand over HTTP with range requests (RFC 9110, 14): the bytes are downloaded in aligned blocks, kept,
/// and the adjacent missing blocks are requested together.
/// </summary>
/// <remarks>
/// A block that is not downloaded yet is downloaded synchronously when it is read (<see cref="HttpClient.Send(HttpRequestMessage, CancellationToken)"/>),
/// except while <see cref="RunDeferred"/> runs: the missing blocks are then reported by
/// <see cref="PdfInputMissingDataException"/>, so that the caller downloads them asynchronously and runs its work again.
/// </remarks>
internal sealed class HttpRangePdfInput : PdfInput
{
    public const int BlockSize = 16 * 1024;

    // The blocks a request downloads at most: the adjacent missing blocks are merged up to this length
    private const int MaxBlocksPerRequest = 256;

    private readonly HttpClient _client;
    private readonly Uri _uri;
    private readonly long _length;
    private readonly Dictionary<long, byte[]> _blocks = [];
    private readonly Lock _lock = new();
    private bool _deferMisses;

    public HttpRangePdfInput(HttpClient client, Uri uri, long length, bool allowFullDownload)
    {
        _client = client;
        _uri = uri;
        _length = length;
        AllowFullDownload = allowFullDownload;
    }

    public override long Length => _length;

    public override bool IsInMemory => false;

    /// <summary>Gets a value indicating whether the reader can read the whole file: to repair it, or to look for a header beyond the first kilobyte.</summary>
    public bool AllowFullDownload { get; }

    /// <summary>Gets the number of bytes downloaded.</summary>
    public long DownloadedBytes
    {
        get
        {
            lock (_lock)
            {
                return _blocks.Values.Sum(block => (long)block.Length);
            }
        }
    }

    /// <summary>Gets the number of requests sent.</summary>
    public int RequestCount { get; private set; }

    /// <summary>Opens the input: the first request downloads the first block and gives the length of the file (<c>Content-Range</c>); a server that ignores the range returns the whole file.</summary>
    /// <returns>The input, or the bytes of the whole file when the server does not support range requests.</returns>
    public static async Task<(HttpRangePdfInput? Input, byte[]? WholeFile)> OpenAsync(HttpClient client, Uri uri, bool allowFullDownload, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(uri, 0, BlockSize - 1);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        // The first range of an empty file is not satisfiable (RFC 9110, 15.5.17)
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && response.Content.Headers.ContentRange is { Length: 0 })
            return (null, []);

        response.EnsureSuccessStatusCode();
        if (response.StatusCode != HttpStatusCode.PartialContent || response.Content.Headers.ContentRange is not { Length: { } length, From: 0 })
        {
            // The server sends the whole file
            return (null, await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
        }

        var input = new HttpRangePdfInput(client, uri, length, allowFullDownload) { RequestCount = 1 };
        var data = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        input.Store(0, data);

        // The end of the file: the startxref keyword and the last cross-reference section are there
        await input.PrefetchAsync([(Math.Max(0, length - (4 * BlockSize)), (int)Math.Min(length, 4 * BlockSize))], cancellationToken).ConfigureAwait(false);
        return (input, null);
    }

    public override int Read(long offset, Span<byte> destination)
    {
        if (offset >= _length || destination.IsEmpty)
            return 0;

        if (destination.Length > _length - offset)
        {
            destination = destination[..(int)(_length - offset)];
        }

        EnsureBlocks(offset, destination.Length);
        var total = 0;
        lock (_lock)
        {
            while (total < destination.Length)
            {
                var position = offset + total;
                var block = _blocks[position / BlockSize];
                var start = (int)(position % BlockSize);
                var count = Math.Min(block.Length - start, destination.Length - total);
                if (count <= 0)
                    break;

                block.AsSpan(start, count).CopyTo(destination[total..]);
                total += count;
            }
        }

        return total;
    }

    /// <summary>Runs work that reads the input: the blocks it misses are downloaded asynchronously, and the work runs again, until it misses none.</summary>
    /// <exception cref="PdfResourceLimitException">The work misses blocks more times than the limit.</exception>
    public async Task<T> RunDeferred<T>(Func<T> work, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 1024; attempt++)
        {
            List<(long Offset, int Length)> missing;
            _deferMisses = true;
            try
            {
                return work();
            }
            catch (PdfInputMissingDataException ex)
            {
                missing = ex.Ranges;
            }
            finally
            {
                _deferMisses = false;
            }

            await PrefetchAsync(missing, cancellationToken).ConfigureAwait(false);
        }

        throw new PdfResourceLimitException("The document needs too many downloads to be read");
    }

    /// <summary>Downloads the blocks of ranges that are not downloaded yet.</summary>
    public async Task PrefetchAsync(IEnumerable<(long Offset, int Length)> ranges, CancellationToken cancellationToken)
    {
        foreach (var (first, last) in GetMissingRequests(ranges))
        {
            using var request = CreateRequest(_uri, first * BlockSize, Math.Min(_length, (last + 1) * BlockSize) - 1);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var data = await ReadResponseAsync(response, first, cancellationToken).ConfigureAwait(false);
            Store(first * BlockSize, data);
        }
    }

    private void EnsureBlocks(long offset, int length)
    {
        var requests = GetMissingRequests([(offset, length)]);
        if (requests.Count == 0)
            return;

        if (_deferMisses)
            throw new PdfInputMissingDataException([.. requests.Select(request => (request.First * BlockSize, (int)Math.Min((request.Last - request.First + 1) * (long)BlockSize, int.MaxValue)))]);

        foreach (var (first, last) in requests)
        {
            using var request = CreateRequest(_uri, first * BlockSize, Math.Min(_length, (last + 1) * BlockSize) - 1);
            using var response = _client.Send(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            using var content = new MemoryStream();
            using (var body = response.Content.ReadAsStream())
            {
                body.CopyTo(content);
            }

            Store(first * BlockSize, CheckResponse(response, content.ToArray(), first));
        }
    }

    /// <summary>Gets the runs of adjacent missing blocks of ranges, as (first block, last block), at most <see cref="MaxBlocksPerRequest"/> blocks each.</summary>
    private List<(long First, long Last)> GetMissingRequests(IEnumerable<(long Offset, int Length)> ranges)
    {
        var missing = new SortedSet<long>();
        lock (_lock)
        {
            foreach (var (offset, length) in ranges)
            {
                if (length <= 0 || offset >= _length)
                    continue;

                var end = Math.Min(_length, offset + length) - 1;
                for (var block = Math.Max(0, offset) / BlockSize; block <= end / BlockSize; block++)
                {
                    if (!_blocks.ContainsKey(block))
                    {
                        missing.Add(block);
                    }
                }
            }
        }

        var result = new List<(long, long)>();
        foreach (var block in missing)
        {
            if (result.Count > 0 && result[^1].Item2 == block - 1 && block - result[^1].Item1 < MaxBlocksPerRequest)
            {
                result[^1] = (result[^1].Item1, block);
            }
            else
            {
                result.Add((block, block));
            }
        }

        return result;
    }

    private async Task<byte[]> ReadResponseAsync(HttpResponseMessage response, long firstBlock, CancellationToken cancellationToken)
    {
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return CheckResponse(response, data, firstBlock);
    }

    /// <summary>Gets the bytes of a response from its first block: the range requested, or the same range of the whole file a server that ignores ranges sends.</summary>
    private byte[] CheckResponse(HttpResponseMessage response, byte[] data, long firstBlock)
    {
        RequestCount++;
        if (response.StatusCode == HttpStatusCode.PartialContent && response.Content.Headers.ContentRange is { From: { } from } && from == firstBlock * BlockSize)
            return data;

        // A server that ignores the range sends the whole file: the blocks are taken from it
        if (response.StatusCode == HttpStatusCode.OK && data.LongLength == _length)
            return data.AsSpan((int)(firstBlock * BlockSize)).ToArray();

        throw new HttpRequestException(string.Create(CultureInfo.InvariantCulture, $"The server did not return the range of the file at offset {firstBlock * BlockSize} ({(int)response.StatusCode})"));
    }

    /// <summary>Keeps the bytes of a response, which starts at a block, as blocks.</summary>
    private void Store(long offset, byte[] data)
    {
        lock (_lock)
        {
            for (var position = 0; position < data.Length; position += BlockSize)
            {
                var blockOffset = offset + position;
                var length = (int)Math.Min(BlockSize, Math.Min(data.Length - position, _length - blockOffset));
                if (length <= 0)
                    break;

                // The last block of the file is shorter; a block is complete, or the file ends in it
                if (length == BlockSize || blockOffset + length == _length)
                {
                    _blocks.TryAdd(blockOffset / BlockSize, data.AsSpan(position, length).ToArray());
                }
            }
        }
    }

    private static HttpRequestMessage CreateRequest(Uri uri, long from, long to)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Range = new RangeHeaderValue(from, to);
        return request;
    }
}
