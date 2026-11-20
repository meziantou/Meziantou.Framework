using System.Buffers.Binary;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Media;
using Meziantou.Framework.WebPlatform.WebCodecs;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The codecs of the tests (<see cref="BrowsingContextOptions.MediaCodecs"/>): deterministic software codecs, real but
/// simple. Audio: the linear PCM decoders (<c>pcm-u8</c>, <c>pcm-s16</c>, <c>pcm-s24</c>, <c>pcm-s32</c>, <c>pcm-f32</c>) and
/// the G.711 decoders (<c>ulaw</c>, <c>alaw</c>). Video: VP8 (<c>vp8</c>) with key frames only: a frame is encoded as the
/// VP8 bitstream of a lossy WebP image (Meziantou.Framework.Imaging), and a key frame is decoded as one. A delta frame of
/// another encoder cannot be decoded.
/// </summary>
internal sealed class WptMediaCodecBackend : MediaCodecBackend
{
    /// <summary>The largest width or height of the frames of the VP8 encoder.</summary>
    private const int MaxVideoDimension = 4096;

    public override bool IsConfigSupported(AudioDecoderConfig config)
    {
        if (!MediaCodecString.TryParse(config.Codec, out var codec) || config.SampleRate is < 3000 or > 768000 || config.NumberOfChannels is 0 or > 8)
            return false;

        return codec.Codec is MediaCodec.PcmU8 or MediaCodec.PcmS16 or MediaCodec.PcmS24 or MediaCodec.PcmS32 or MediaCodec.PcmF32 or MediaCodec.Ulaw or MediaCodec.Alaw;
    }

    public override MediaAudioDecoder? CreateAudioDecoder(AudioDecoderConfig config) => MediaCodecString.TryParse(config.Codec, out var codec) ? new PcmDecoder(codec.Codec, config) : null;

    public override bool IsConfigSupported(VideoDecoderConfig config) => config.Codec == "vp8" && config.HardwareAcceleration != HardwareAcceleration.PreferHardware;

    public override MediaVideoDecoder? CreateVideoDecoder(VideoDecoderConfig config) => new Vp8Decoder();

    public override bool IsConfigSupported(VideoEncoderConfig config)
    {
        if (config.Codec != "vp8" || config.HardwareAcceleration == HardwareAcceleration.PreferHardware || config.Alpha == AlphaOption.Keep)
            return false;

        if (config.Width > MaxVideoDimension || config.Height > MaxVideoDimension)
            return false;

        return config.ScalabilityMode is null or "L1T1" or "L1T2" or "L1T3";
    }

    public override MediaVideoEncoder? CreateVideoEncoder(VideoEncoderConfig config) => new Vp8Encoder(config);

    /// <summary>Decodes linear PCM (little-endian, interleaved) and G.711.</summary>
    private sealed class PcmDecoder(MediaCodec codec, AudioDecoderConfig config) : MediaAudioDecoder
    {
        private static readonly short[] UlawTable = CreateG711Table(ulaw: true);
        private static readonly short[] AlawTable = CreateG711Table(ulaw: false);

        public override IReadOnlyList<AudioData> Decode(EncodedAudioChunk chunk)
        {
            var data = chunk.Data.Span;
            var channels = (int)config.NumberOfChannels;
            var (sampleBytes, format) = codec switch
            {
                MediaCodec.PcmU8 => (1, AudioSampleFormat.U8),
                MediaCodec.PcmS16 => (2, AudioSampleFormat.S16),
                MediaCodec.PcmS24 => (3, AudioSampleFormat.S32),
                MediaCodec.PcmS32 => (4, AudioSampleFormat.S32),
                MediaCodec.PcmF32 => (4, AudioSampleFormat.F32),
                _ => (1, AudioSampleFormat.S16),
            };
            var frames = data.Length / (sampleBytes * channels);
            if (frames == 0)
                throw new InvalidDataException("The chunk has no complete frame");

            var samples = frames * channels;
            byte[] output;
            switch (codec)
            {
                case MediaCodec.PcmS24:
                    // The 24-bit samples are widened to 32 bits
                    output = new byte[samples * 4];
                    for (var i = 0; i < samples; i++)
                    {
                        var value = data[(i * 3) + 0] | (data[(i * 3) + 1] << 8) | (data[(i * 3) + 2] << 16);
                        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(i * 4), value << 8);
                    }

                    break;

                case MediaCodec.Ulaw or MediaCodec.Alaw:
                    output = new byte[samples * 2];
                    var table = codec == MediaCodec.Ulaw ? UlawTable : AlawTable;
                    for (var i = 0; i < samples; i++)
                    {
                        BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(i * 2), table[data[i]]);
                    }

                    break;

                default:
                    output = data[..(samples * sampleBytes)].ToArray();
                    break;
            }

            return [new AudioData(new AudioDataInit { Format = format, SampleRate = config.SampleRate, NumberOfFrames = (uint)frames, NumberOfChannels = config.NumberOfChannels, Timestamp = chunk.Timestamp, Data = output })];
        }

        /// <summary>The 16-bit linear values of the G.711 code words (ITU-T G.711).</summary>
        private static short[] CreateG711Table(bool ulaw)
        {
            var table = new short[256];
            for (var i = 0; i < 256; i++)
            {
                if (ulaw)
                {
                    var value = ~i & 0xFF;
                    var exponent = (value >> 4) & 0x07;
                    var mantissa = value & 0x0F;
                    var magnitude = (((mantissa << 3) + 0x84) << exponent) - 0x84;
                    table[i] = (short)((value & 0x80) != 0 ? -magnitude : magnitude);
                }
                else
                {
                    var value = i ^ 0x55;
                    var exponent = (value >> 4) & 0x07;
                    var mantissa = value & 0x0F;
                    var magnitude = exponent == 0 ? (mantissa << 4) + 8 : ((mantissa << 4) + 0x108) << (exponent - 1);
                    table[i] = (short)((value & 0x80) != 0 ? magnitude : -magnitude);
                }
            }

            return table;
        }
    }

    /// <summary>Encodes the frames as VP8 key frames (the VP8 bitstream of a lossy WebP image).</summary>
    private sealed class Vp8Encoder(VideoEncoderConfig config) : MediaVideoEncoder
    {
        private static readonly uint[] TwoLayers = [0, 1];
        private static readonly uint[] ThreeLayers = [0, 2, 1, 2];

        private long _frameCount;

        public override IReadOnlyList<MediaEncodedVideo> Encode(VideoFrame frame, VideoEncoderEncodeOptions options)
        {
            var visible = frame.VisibleRect!;
            var (width, height) = ((int)visible.Width, (int)visible.Height);
            var rgba = new byte[width * height * 4];
            frame.CopyPixelsTo(rgba, new VideoFrameCopyToOptions { Format = VideoPixelFormat.RGBA });
            var (outputWidth, outputHeight) = ((int)config.Width, (int)config.Height);
            if (outputWidth != width || outputHeight != height)
                rgba = Scale(rgba, width, height, outputWidth, outputHeight);

            // VP8 has no alpha
            for (var i = 3; i < rgba.Length; i += 4)
            {
                rgba[i] = 255;
            }

            byte[] webp;
            using (var image = Image.ImportPixelBytes<Rgba32>(rgba, outputWidth, outputHeight, outputWidth * 4))
            using (var stream = new MemoryStream())
            {
                image.Save(stream, new WebPEncoder { Compression = WebPCompression.Lossy, Quality = 90 });
                webp = stream.ToArray();
            }

            var bitstream = FindVp8Chunk(webp) ?? throw new InvalidOperationException("The WebP encoder did not produce a VP8 bitstream");
            var chunk = new EncodedVideoChunk(new EncodedVideoChunkInit { Type = EncodedVideoChunkType.Key, Timestamp = frame.Timestamp, Duration = frame.Duration, Data = bitstream });
            var layers = config.ScalabilityMode switch
            {
                "L1T2" => TwoLayers,
                "L1T3" => ThreeLayers,
                _ => null,
            };
            uint? layer = layers is null ? (config.ScalabilityMode is null ? null : 0u) : layers[_frameCount % layers.Length];
            _frameCount++;
            return [new MediaEncodedVideo(chunk, new VideoDecoderConfig { Codec = "vp8", CodedWidth = config.Width, CodedHeight = config.Height }, layer)];
        }

        /// <summary>Scales RGBA pixels with a bilinear interpolation.</summary>
        private static byte[] Scale(byte[] source, int width, int height, int outputWidth, int outputHeight)
        {
            var output = new byte[outputWidth * outputHeight * 4];
            for (var y = 0; y < outputHeight; y++)
            {
                var sourceY = Math.Clamp(((y + 0.5) * height / outputHeight) - 0.5, 0, height - 1);
                var y0 = (int)sourceY;
                var y1 = Math.Min(y0 + 1, height - 1);
                var fy = sourceY - y0;
                for (var x = 0; x < outputWidth; x++)
                {
                    var sourceX = Math.Clamp(((x + 0.5) * width / outputWidth) - 0.5, 0, width - 1);
                    var x0 = (int)sourceX;
                    var x1 = Math.Min(x0 + 1, width - 1);
                    var fx = sourceX - x0;
                    for (var c = 0; c < 4; c++)
                    {
                        var top = (source[((y0 * width) + x0) * 4 + c] * (1 - fx)) + (source[((y0 * width) + x1) * 4 + c] * fx);
                        var bottom = (source[((y1 * width) + x0) * 4 + c] * (1 - fx)) + (source[((y1 * width) + x1) * 4 + c] * fx);
                        output[((y * outputWidth) + x) * 4 + c] = (byte)Math.Clamp(Math.Round((top * (1 - fy)) + (bottom * fy), MidpointRounding.AwayFromZero), 0, 255);
                    }
                }
            }

            return output;
        }

        /// <summary>Returns the payload of the <c>VP8 </c> chunk of a WebP file.</summary>
        private static byte[]? FindVp8Chunk(byte[] webp)
        {
            for (var offset = 12; offset + 8 <= webp.Length;)
            {
                var size = BinaryPrimitives.ReadInt32LittleEndian(webp.AsSpan(offset + 4));
                if (size < 0 || offset + 8 + size > webp.Length)
                    return null;

                if (webp.AsSpan(offset, 4).SequenceEqual("VP8 "u8))
                    return webp.AsSpan(offset + 8, size).ToArray();

                offset += 8 + size + (size & 1);
            }

            return null;
        }
    }

    /// <summary>Decodes VP8 key frames as the VP8 bitstream of a lossy WebP image.</summary>
    private sealed class Vp8Decoder : MediaVideoDecoder
    {
        public override IReadOnlyList<VideoFrame> Decode(EncodedVideoChunk chunk)
        {
            var data = chunk.Data.Span;

            // https://datatracker.ietf.org/doc/html/rfc6386#section-9.1: a key frame has the start code 9d 01 2a
            if (data.Length < 10 || (data[0] & 1) != 0 || data[3] != 0x9D || data[4] != 0x01 || data[5] != 0x2A)
                throw new InvalidDataException("Only the VP8 key frames can be decoded");

            var padding = data.Length & 1;
            var webp = new byte[20 + data.Length + padding];
            "RIFF"u8.CopyTo(webp);
            BinaryPrimitives.WriteInt32LittleEndian(webp.AsSpan(4), 12 + data.Length + padding);
            "WEBPVP8 "u8.CopyTo(webp.AsSpan(8));
            BinaryPrimitives.WriteInt32LittleEndian(webp.AsSpan(16), data.Length);
            data.CopyTo(webp.AsSpan(20));
            using var image = Image.Load<Rgba32>(webp);
            var pixels = new byte[image.Width * image.Height * 4];
            image.Frames[0].CopyPixelBytesTo(pixels, image.Width * 4);
            return [new VideoFrame(pixels, new VideoFrameBufferInit { Format = VideoPixelFormat.RGBX, CodedWidth = (uint)image.Width, CodedHeight = (uint)image.Height, Timestamp = chunk.Timestamp, Duration = chunk.Duration })];
        }
    }
}
