using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.Adapters;

/// <summary>
/// The adapter from a sequential reader to the comparison model: every image returned by
/// <c>ReadPosterFrame</c>, <c>ReadFrame</c> or <c>ReadFrameInto</c> is captured through the public row API into a
/// <see cref="DecodedImageSnapshot"/>, which is compared with the manifest's full displayed frames, durations and separate
/// poster by <see cref="GoldenAssert.ImageMatches"/> — never with an eager load of the same input.
/// </summary>
/// <remarks>
/// While reading, the reader contract is checked and violations throw <see cref="GoldenAssertionException"/>: returned images
/// are single-frame still images of the canvas size without poster or animation settings (those belong to
/// <c>reader.Info</c>), <c>FramesRead</c> counts displayed frames, the end of input is reported again by later calls, and a
/// <c>ReadFrameInto</c> destination keeps its frame object. To prove that the reader's compositor state is independent of
/// the caller, every captured image is overwritten (all bytes inverted) and then disposed — or, for a reused destination,
/// overwritten before the next read — so a reader that depended on caller pixels produces frames that no longer match.
/// </remarks>
public static class ReaderSnapshots
{
    /// <summary>Reads the poster (when the header announces one) and every displayed frame as owned images.</summary>
    /// <typeparam name="TPixel">The pixel type of the reader.</typeparam>
    /// <param name="reader">The reader, positioned before the first image.</param>
    /// <param name="asynchronous">Whether to use the asynchronous reader methods.</param>
    /// <param name="includeMetadata">Whether the snapshot carries the metadata of the first returned image.</param>
    /// <param name="cancellationToken">The token passed to asynchronous calls.</param>
    /// <returns>The snapshot (animation settings from <c>reader.Info</c>).</returns>
    public static async Task<DecodedImageSnapshot> CaptureAsync<TPixel>(ImageReader<TPixel> reader, bool asynchronous, bool includeMetadata = false, CancellationToken cancellationToken = default)
        where TPixel : unmanaged
    {
        ArgumentNullException.ThrowIfNull(reader);
        var state = new CaptureState(reader.Info);
        var poster = await ReadPosterAsync(reader, asynchronous, state, cancellationToken).ConfigureAwait(false);
        var frames = new List<DecodedFrameSnapshot>();
        while (true)
        {
            var image = asynchronous ? await reader.ReadFrameAsync(cancellationToken).ConfigureAwait(false) : reader.ReadFrame();
            if (image is null)
                break;

            using (image)
            {
                state.CheckStill(image, $"frame {frames.Count.ToString(CultureInfo.InvariantCulture)}");
                state.OnImage(image, includeMetadata);
                frames.Add(new DecodedFrameSnapshot(ImageSnapshots.CaptureFrameRows(image.Frames[0]), ImageSnapshots.ToRational(image.Frames[0].Metadata.Duration)));
                Overwrite(image);
            }

            CheckFramesRead(reader, frames.Count);
        }

        await CheckEndAsync(reader, asynchronous, frames.Count, cancellationToken).ConfigureAwait(false);
        return state.CreateSnapshot(frames, poster);
    }

    /// <summary>Reads the poster (when the header announces one) as an owned image, then every displayed frame into one reused destination.</summary>
    /// <typeparam name="TPixel">The pixel type of the reader.</typeparam>
    /// <param name="reader">The reader, positioned before the first image.</param>
    /// <param name="asynchronous">Whether to use the asynchronous reader methods.</param>
    /// <param name="includeMetadata">Whether the snapshot carries the metadata of the destination after the first frame.</param>
    /// <param name="cancellationToken">The token passed to asynchronous calls.</param>
    /// <returns>The snapshot.</returns>
    public static async Task<DecodedImageSnapshot> CaptureIntoAsync<TPixel>(ImageReader<TPixel> reader, bool asynchronous, bool includeMetadata = false, CancellationToken cancellationToken = default)
        where TPixel : unmanaged
    {
        ArgumentNullException.ThrowIfNull(reader);
        var state = new CaptureState(reader.Info);
        var poster = await ReadPosterAsync(reader, asynchronous, state, cancellationToken).ConfigureAwait(false);
        var frames = new List<DecodedFrameSnapshot>();
        using var destination = new Image<TPixel>(reader.Info.Width, reader.Info.Height);
        var frame = destination.Frames[0];
        while (asynchronous ? await reader.ReadFrameIntoAsync(destination, cancellationToken).ConfigureAwait(false) : reader.ReadFrameInto(destination))
        {
            var name = $"frame {frames.Count.ToString(CultureInfo.InvariantCulture)} (ReadFrameInto)";
            state.CheckStill(destination, name);
            if (!ReferenceEquals(frame, destination.Frames[0]))
                throw new GoldenAssertionException($"{name}: ReadFrameInto replaced the destination frame instead of reusing its storage.");

            state.OnImage(destination, includeMetadata);
            frames.Add(new DecodedFrameSnapshot(ImageSnapshots.CaptureFrameRows(frame), ImageSnapshots.ToRational(frame.Metadata.Duration)));

            // The next frame must not depend on the destination content: full-canvas frames overwrite every pixel
            Overwrite(destination);
            CheckFramesRead(reader, frames.Count);
        }

        await CheckEndAsync(reader, asynchronous, frames.Count, cancellationToken).ConfigureAwait(false);
        return state.CreateSnapshot(frames, poster);
    }

    private static async Task<RawPixelBuffer?> ReadPosterAsync<TPixel>(ImageReader<TPixel> reader, bool asynchronous, CaptureState state, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        var image = asynchronous ? await reader.ReadPosterFrameAsync(cancellationToken).ConfigureAwait(false) : reader.ReadPosterFrame();
        if (image is null)
        {
            if (reader.Info.HasPosterFrame == true)
                throw new GoldenAssertionException("The header announces a separate poster frame but ReadPosterFrame returned null.");

            return null;
        }

        using (image)
        {
            if (reader.Info.HasPosterFrame != true)
                throw new GoldenAssertionException("ReadPosterFrame returned an image but the header does not announce a separate poster frame.");

            state.CheckStill(image, "poster");
            state.OnImage(image, includeMetadata: false);
            var pixels = ImageSnapshots.CaptureFrameRows(image.Frames[0]);
            Overwrite(image);
            return pixels;
        }
    }

    private static void CheckFramesRead<TPixel>(ImageReader<TPixel> reader, int count)
        where TPixel : unmanaged
    {
        if (reader.FramesRead != count)
            throw new GoldenAssertionException(string.Create(CultureInfo.InvariantCulture, $"FramesRead is {reader.FramesRead} after {count} displayed frames (the poster is not counted)."));
    }

    private static async Task CheckEndAsync<TPixel>(ImageReader<TPixel> reader, bool asynchronous, int count, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        // The clean end of input is sticky
        var again = asynchronous ? await reader.ReadFrameAsync(cancellationToken).ConfigureAwait(false) : reader.ReadFrame();
        if (again is not null)
        {
            again.Dispose();
            throw new GoldenAssertionException("ReadFrame returned an image after reporting the end of input.");
        }

        CheckFramesRead(reader, count);
    }

    /// <summary>Inverts every byte of the image (caller edits the reader must be independent of).</summary>
    private static void Overwrite(Image image)
    {
        image.Frames[0].ProcessPixelBytes(static pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                var row = pixels.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = (byte)~row[x];
                }
            }
        });
    }

    private sealed class CaptureState(ImageInfo info)
    {
        private int? _orientation;
        private DecodedMetadataSnapshot? _metadata;

        public void CheckStill(Image image, string name)
        {
            if (image.Size != info.Size)
                throw new GoldenAssertionException($"{name}: the image is {image.Size} but the canvas is {info.Size}.");

            if (image.Frames.Count != 1)
                throw new GoldenAssertionException(string.Create(CultureInfo.InvariantCulture, $"{name}: a reader returns single-frame images, but this one has {image.Frames.Count} frames."));

            if (image.PosterFrame is not null)
                throw new GoldenAssertionException($"{name}: a returned image must not have a poster frame.");

            if (image.Animation is not null)
                throw new GoldenAssertionException($"{name}: a returned image must not have animation settings (they belong to reader.Info).");
        }

        public void OnImage(Image image, bool includeMetadata)
        {
            _orientation ??= (int)image.Metadata.Orientation;
            if (includeMetadata && _metadata is null)
            {
                _metadata = ImageSnapshots.CaptureMetadata(image.Metadata);
            }
        }

        public DecodedImageSnapshot CreateSnapshot(List<DecodedFrameSnapshot> frames, RawPixelBuffer? poster) => new()
        {
            Frames = frames,
            Poster = poster,
            HasAnimation = info.Animation is not null,
            TotalPlays = info.Animation?.TotalPlays,
            Orientation = _orientation ?? (int)info.Metadata.Orientation,
            Metadata = _metadata,
        };
    }
}
