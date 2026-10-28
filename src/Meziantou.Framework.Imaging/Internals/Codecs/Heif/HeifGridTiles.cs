using System.Runtime.ExceptionServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The parallelism of the tiles of a grid of a file of the HEIF family: with several workers
/// (<see cref="ImageConfiguration.MaxDegreeOfParallelism"/>), up to that many consecutive tiles are decoded, or encoded, at
/// once, and the result and the errors are those of one worker.
/// </summary>
internal static class HeifGridTiles
{
    // The limits of the trackers of the tiles that workers decode, which only record the hidden pixels: the input charges them
    // in the order of the tiles, so that a limit fails as in a sequential decoding
    private static readonly ImageResourceLimits HiddenPixelsRecording = new() { MaxTotalPixels = long.MaxValue };

    /// <summary>
    /// Gets the number of workers of a grid: several workers for at least two tiles and
    /// <see cref="RowBands.MinPixelsForParallelism"/> samples, at most one per tile.
    /// </summary>
    public static int GetWorkerCount(int maxDegreeOfParallelism, Size tileSize, int tileCount)
    {
        if (maxDegreeOfParallelism <= 1 || tileCount < 2 || (long)tileSize.Width * tileSize.Height * tileCount < RowBands.MinPixelsForParallelism)
            return 1;

        return Math.Min(maxDegreeOfParallelism, tileCount);
    }

    /// <summary>Gets the number of workers that decode the tiles of a coded image: one for an image that is one item.</summary>
    public static int GetWorkerCount(ImageCodecContext context, HeifCodedImage image)
        => image.IsGrid ? GetWorkerCount(context.Configuration.MaxDegreeOfParallelism, image.TileSize, image.Tiles.Length) : 1;

    /// <summary>
    /// Decodes the tiles of a grid and hands them to <paramref name="consume"/>, on the calling thread and in the order of the
    /// tiles: <paramref name="consume"/> owns each decoded tile. <paramref name="prepare"/> runs on the calling thread, which
    /// alone uses the structure parser and the input tracker; <paramref name="decode"/> runs on the workers, with a tracker of
    /// its own whose hidden pixels are charged to the input once the turn of its tile comes. The errors are those of a
    /// sequential decoding: the error of the first tile that fails, after the tiles before it are consumed (a cancellation
    /// first). The decoded tiles that are not consumed are released.
    /// </summary>
    /// <param name="context">The decoding context.</param>
    /// <param name="image">The grid.</param>
    /// <param name="prepare">Reads what the decoding of a tile needs.</param>
    /// <param name="decode">Decodes a prepared tile, and checks it against the file.</param>
    /// <param name="release">Releases a decoded tile.</param>
    /// <param name="consume">Uses a decoded tile, and releases it.</param>
    /// <param name="sharesBatch">Whether a tile may be decoded in the batch that starts at another one; every tile when omitted.</param>
    public static void Decode<TPrepared, TDecoded>(
        ImageCodecContext context,
        HeifCodedImage image,
        Func<int, TPrepared> prepare,
        Func<TPrepared, InputResourceTracker, TDecoded> decode,
        Action<TDecoded> release,
        Action<int, TDecoded> consume,
        Func<int, int, bool>? sharesBatch = null)
    {
        var tileCount = image.Tiles.Length;
        var workers = GetWorkerCount(context, image);
        if (workers is 1)
        {
            for (var i = 0; i < tileCount; i++)
            {
                consume(i, decode(prepare(i), context.Tracker));
            }

            return;
        }

        var tiles = new TPrepared[workers];
        var decoded = new TDecoded[workers];
        var owned = new bool[workers];
        var trackers = new InputResourceTracker?[workers];
        var errors = new ExceptionDispatchInfo?[workers];
        var start = 0;
        while (start < tileCount)
        {
            // The batch ends at the first tile that cannot be prepared
            var count = 0;
            while (count < workers && start + count < tileCount && (sharesBatch is null || sharesBatch(start, start + count)))
            {
                try
                {
                    tiles[count] = prepare(start + count);
                    count++;
                }
                catch (Exception ex)
                {
                    errors[count] = ExceptionDispatchInfo.Capture(ex);
                    count++;
                    break;
                }
            }

            try
            {
                RowBands.RunParallel(count, k =>
                {
                    if (errors[k] is not null)
                        return;

                    var tracker = new InputResourceTracker(HiddenPixelsRecording);
                    trackers[k] = tracker;
                    try
                    {
                        decoded[k] = decode(tiles[k], tracker);
                        owned[k] = true;
                    }
                    catch (Exception ex)
                    {
                        errors[k] = ExceptionDispatchInfo.Capture(ex);
                    }
                });

                for (var k = 0; k < count; k++)
                {
                    if (errors[k]?.SourceException is OperationCanceledException)
                        errors[k]!.Throw();
                }

                for (var k = 0; k < count; k++)
                {
                    if (trackers[k] is { TotalPixels: > 0 and var hiddenPixels })
                    {
                        context.Tracker.ChargeHiddenPixels(hiddenPixels);
                    }

                    errors[k]?.Throw();
                    owned[k] = false;
                    consume(start + k, decoded[k]);
                }
            }
            finally
            {
                for (var k = 0; k < count; k++)
                {
                    if (owned[k])
                        release(decoded[k]);

                    owned[k] = false;
                    decoded[k] = default!;
                    tiles[k] = default!;
                    trackers[k] = null;
                    errors[k] = null;
                }
            }

            start += count;
        }
    }
}
