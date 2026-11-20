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
/// the G.711 decoders (<c>ulaw</c>, <c>alaw</c>), and Opus (<c>opus</c>, mono and stereo, with the managed implementation
/// of the Concentus package; the audio is resampled to 48 kHz). Video: VP8 (<c>vp8</c>) with key frames only: a frame is encoded as the
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

        return codec.Codec is MediaCodec.PcmU8 or MediaCodec.PcmS16 or MediaCodec.PcmS24 or MediaCodec.PcmS32 or MediaCodec.PcmF32 or MediaCodec.Ulaw or MediaCodec.Alaw or MediaCodec.Opus;
    }

    public override MediaAudioDecoder? CreateAudioDecoder(AudioDecoderConfig config) => config.Codec switch
    {
        "opus" => config.NumberOfChannels <= 2 ? new OpusAudioDecoder(config) : null,
        _ => MediaCodecString.TryParse(config.Codec, out var codec) ? new PcmDecoder(codec.Codec, config) : null,
    };

    public override bool IsConfigSupported(AudioEncoderConfig config) => config.Codec == "opus" && config.SampleRate is >= 3000 and <= 384000 && config.NumberOfChannels is 1 or 2
        && (config.Opus?.FrameDuration ?? 20000) is 2500 or 5000 or 10000 or 20000 or 40000 or 60000 or 80000 or 100000 or 120000;

    public override MediaAudioEncoder? CreateAudioEncoder(AudioEncoderConfig config) => new OpusAudioEncoder(config);

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

    /// <summary>Resamples interleaved samples with a linear interpolation.</summary>
    private static float[] Resample(ReadOnlySpan<float> samples, int channels, int sourceRate, int targetRate)
    {
        if (sourceRate == targetRate)
            return samples.ToArray();

        var sourceFrames = samples.Length / channels;
        var targetFrames = (int)((long)sourceFrames * targetRate / sourceRate);
        var output = new float[targetFrames * channels];
        for (var frame = 0; frame < targetFrames; frame++)
        {
            var position = (double)frame * sourceRate / targetRate;
            var index = (int)position;
            var next = Math.Min(index + 1, sourceFrames - 1);
            var fraction = (float)(position - index);
            for (var channel = 0; channel < channels; channel++)
            {
                output[(frame * channels) + channel] = (samples[(index * channels) + channel] * (1 - fraction)) + (samples[(next * channels) + channel] * fraction);
            }
        }

        return output;
    }

    private static byte[] ToBytes(float[] samples)
    {
        var bytes = new byte[samples.Length * 4];
        for (var i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), samples[i]);
        }

        return bytes;
    }

    /// <summary>The identification header of an Ogg Opus stream (https://datatracker.ietf.org/doc/html/rfc7845#section-5.1): the description of the <c>ogg</c> format.</summary>
    private static byte[] CreateOpusHead(int channels, int sampleRate, int preSkip)
    {
        var head = new byte[19];
        "OpusHead"u8.CopyTo(head);
        head[8] = 1;
        head[9] = (byte)channels;
        BinaryPrimitives.WriteUInt16LittleEndian(head.AsSpan(10), (ushort)preSkip);
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(12), (uint)sampleRate);
        return head;
    }

    /// <summary>Encodes Opus at 48 kHz (the audio is resampled), with the frame duration of the configuration.</summary>
    private sealed class OpusAudioEncoder : MediaAudioEncoder
    {
        private const int OpusSampleRate = 48000;

        private readonly AudioEncoderConfig _config;
        private readonly Concentus.IOpusEncoder _encoder;
        private readonly int _channels;
        private readonly int _frameSize;
        private readonly List<float> _pending = [];
        private long? _nextTimestamp;
        private bool _hasPendingFlush;

        public OpusAudioEncoder(AudioEncoderConfig config)
        {
            Concentus.OpusCodecFactory.AttemptToUseNativeLibrary = false;
            _config = config;
            _channels = (int)config.NumberOfChannels;
            var application = config.Opus?.Application switch
            {
                OpusApplication.Voip => Concentus.Enums.OpusApplication.OPUS_APPLICATION_VOIP,
                OpusApplication.LowDelay => Concentus.Enums.OpusApplication.OPUS_APPLICATION_RESTRICTED_LOWDELAY,
                _ => Concentus.Enums.OpusApplication.OPUS_APPLICATION_AUDIO,
            };
            _encoder = Concentus.OpusCodecFactory.CreateEncoder(OpusSampleRate, _channels, application, null);
            if (config.Bitrate is { } bitrate)
                _encoder.Bitrate = (int)Math.Clamp(bitrate, 6000, 510000);

            if (config.Opus?.Complexity is { } complexity)
                _encoder.Complexity = (int)complexity;

            _encoder.UseVBR = config.BitrateMode == BitrateMode.Variable;
            _encoder.UseDTX = config.Opus?.UseDtx ?? false;
            _encoder.UseInbandFEC = config.Opus?.UseInBandFec ?? false;
            _encoder.PacketLossPercent = (int)(config.Opus?.PacketLossPercentage ?? 0);
            _encoder.SignalType = config.Opus?.Signal switch
            {
                OpusSignal.Music => Concentus.Enums.OpusSignal.OPUS_SIGNAL_MUSIC,
                OpusSignal.Voice => Concentus.Enums.OpusSignal.OPUS_SIGNAL_VOICE,
                _ => Concentus.Enums.OpusSignal.OPUS_SIGNAL_AUTO,
            };
            _frameSize = (int)((config.Opus?.FrameDuration ?? 20000) * OpusSampleRate / 1_000_000);
        }

        public override IReadOnlyList<MediaEncodedAudio> Encode(AudioData data)
        {
            if ((uint)data.SampleRate != _config.SampleRate || data.NumberOfChannels != _config.NumberOfChannels)
                throw new InvalidDataException("The audio data does not have the sample rate and the channels of the configuration");

            var samples = new byte[data.NumberOfFrames * data.NumberOfChannels * 4];
            data.CopyTo(samples, new AudioDataCopyToOptions { PlaneIndex = 0, Format = AudioSampleFormat.F32 });
            var floats = new float[samples.Length / 4];
            for (var i = 0; i < floats.Length; i++)
            {
                floats[i] = BinaryPrimitives.ReadSingleLittleEndian(samples.AsSpan(i * 4));
            }

            _nextTimestamp ??= data.Timestamp;
            _hasPendingFlush = true;
            _pending.AddRange(Resample(floats, _channels, (int)_config.SampleRate, OpusSampleRate));
            return EncodeFrames(flush: false);
        }

        public override IReadOnlyList<MediaEncodedAudio> Flush() => EncodeFrames(flush: true);

        public override void Reset()
        {
            _pending.Clear();
            _hasPendingFlush = false;
            _nextTimestamp = null;
            _encoder.ResetState();
        }

        protected override void Dispose(bool disposing)
        {
            _encoder.Dispose();
            base.Dispose(disposing);
        }

        private List<MediaEncodedAudio> EncodeFrames(bool flush)
        {
            var outputs = new List<MediaEncodedAudio>();
            var frameSamples = _frameSize * _channels;
            if (flush && _hasPendingFlush)
            {
                _hasPendingFlush = false;
                // The samples delayed by the look-ahead of the encoder are flushed: the decoder discards the pre-skip samples
                _pending.AddRange(new float[_encoder.Lookahead * _channels]);
                if (_pending.Count % frameSamples != 0)
                    _pending.AddRange(new float[frameSamples - (_pending.Count % frameSamples)]);
            }

            var packet = new byte[4000];
            var description = CreateOpusHead(_channels, (int)_config.SampleRate, _encoder.Lookahead);
            var decoderConfig = new AudioDecoderConfig { Codec = "opus", SampleRate = _config.SampleRate, NumberOfChannels = _config.NumberOfChannels, Description = description };
            var frameDuration = (long)_frameSize * 1_000_000 / OpusSampleRate;
            while (_pending.Count >= frameSamples)
            {
                var frame = _pending.GetRange(0, frameSamples).ToArray();
                _pending.RemoveRange(0, frameSamples);
                var length = _encoder.Encode(frame, _frameSize, packet, packet.Length);
                var timestamp = _nextTimestamp ?? 0;
                _nextTimestamp = timestamp + frameDuration;
                var chunk = new EncodedAudioChunk(new EncodedAudioChunkInit { Type = EncodedAudioChunkType.Key, Timestamp = timestamp, Duration = (ulong)frameDuration, Data = packet.AsSpan(0, length).ToArray() });
                outputs.Add(new MediaEncodedAudio(chunk, decoderConfig));
            }

            return outputs;
        }
    }

    /// <summary>Decodes Opus at 48 kHz: the pre-skip samples of the description (an <c>OpusHead</c>) are discarded.</summary>
    private sealed class OpusAudioDecoder : MediaAudioDecoder
    {
        private const int OpusSampleRate = 48000;

        private readonly Concentus.IOpusDecoder _decoder;
        private readonly int _channels;
        private readonly int _preSkip;
        private int _remainingSkip;

        public OpusAudioDecoder(AudioDecoderConfig config)
        {
            Concentus.OpusCodecFactory.AttemptToUseNativeLibrary = false;
            _channels = (int)config.NumberOfChannels;
            _decoder = Concentus.OpusCodecFactory.CreateDecoder(OpusSampleRate, _channels, null);
            // The description of the configurations given to the backend is a byte array
            if (config.Description is byte[] head && head.Length >= 19 && head.AsSpan(0, 8).SequenceEqual("OpusHead"u8))
                _preSkip = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(10));

            _remainingSkip = _preSkip;
        }

        public override IReadOnlyList<AudioData> Decode(EncodedAudioChunk chunk)
        {
            // The longest Opus packet lasts 120 ms
            var samples = new float[5760 * _channels];
            var frames = _decoder.Decode(chunk.Data.Span, samples, 5760, false);
            if (frames <= 0)
                throw new InvalidDataException("The Opus packet cannot be decoded");

            var skipped = Math.Min(_remainingSkip, frames);
            _remainingSkip -= skipped;
            frames -= skipped;
            if (frames == 0)
                return [];

            // The discarded pre-skip samples shift the timestamp of a stream that starts before 0 (as in Chromium)
            var output = samples.AsSpan(skipped * _channels, frames * _channels).ToArray();
            var timestamp = chunk.Timestamp < 0 ? chunk.Timestamp + ((long)skipped * 1_000_000 / OpusSampleRate) : chunk.Timestamp;
            return [new AudioData(new AudioDataInit { Format = AudioSampleFormat.F32, SampleRate = OpusSampleRate, NumberOfFrames = (uint)frames, NumberOfChannels = (uint)_channels, Timestamp = timestamp, Data = ToBytes(output) })];
        }

        public override void Reset()
        {
            _decoder.ResetState();
            _remainingSkip = _preSkip;
        }

        protected override void Dispose(bool disposing)
        {
            _decoder.Dispose();
            base.Dispose(disposing);
        }
    }
}
