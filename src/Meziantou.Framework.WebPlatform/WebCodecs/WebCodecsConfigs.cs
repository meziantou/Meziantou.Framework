using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.WebCodecs;

/// <summary>The validation, the cloning and the comparison of the configurations of the codecs (https://w3c.github.io/webcodecs/#configurations).</summary>
internal static class WebCodecsConfigs
{
    /// <summary>The complexity of the Opus encoder when the configuration does not give it (the default of libopus on desktop platforms).</summary>
    private const uint DefaultOpusComplexity = 9;

    public static DomException CreateAbortError() => new("The codec was reset or closed", "AbortError");

    /// <summary>https://w3c.github.io/webcodecs/#valid-audiodecoderconfig</summary>
    public static bool IsValid(AudioDecoderConfig config) => IsValidCodec(config.Codec) && config.SampleRate > 0 && config.NumberOfChannels > 0 && !WebCodecsBuffers.IsDetached(config.Description);

    /// <summary>https://w3c.github.io/webcodecs/#valid-videodecoderconfig</summary>
    public static bool IsValid(VideoDecoderConfig config)
    {
        if (!IsValidCodec(config.Codec) || WebCodecsBuffers.IsDetached(config.Description))
            return false;

        if (config.CodedWidth.HasValue != config.CodedHeight.HasValue || config.CodedWidth == 0 || config.CodedHeight == 0)
            return false;

        if (config.DisplayAspectWidth.HasValue != config.DisplayAspectHeight.HasValue || config.DisplayAspectWidth == 0 || config.DisplayAspectHeight == 0)
            return false;

        return double.IsFinite(config.Rotation);
    }

    /// <summary>https://w3c.github.io/webcodecs/#valid-audioencoderconfig, with the validations of the codec registrations (Opus).</summary>
    public static bool IsValid(AudioEncoderConfig config)
    {
        if (!IsValidCodec(config.Codec) || config.SampleRate == 0 || config.NumberOfChannels == 0)
            return false;

        if (config.Bitrate is 0 or > uint.MaxValue)
            return false;

        if (config.Codec == "opus")
        {
            if (config.Opus is { } opus)
            {
                // https://w3c.github.io/webcodecs/opus_codec_registration.html#opus-encoder-config: a frame duration is a multiple of 2.5 ms up to 120 ms
                if (opus.Complexity > 10 || opus.PacketLossPercentage > 100 || opus.FrameDuration is 0 or > 120000 || opus.FrameDuration % 2500 != 0)
                    return false;
            }

            if (config.Bitrate is < 6000 or > 510000)
                return false;
        }

        return true;
    }

    /// <summary>https://w3c.github.io/webcodecs/#valid-videoencoderconfig</summary>
    public static bool IsValid(VideoEncoderConfig config)
    {
        if (!IsValidCodec(config.Codec) || config.Width == 0 || config.Height == 0)
            return false;

        if (config.DisplayWidth == 0 || config.DisplayHeight == 0)
            return false;

        if (config.Bitrate == 0)
            return false;

        return config.Framerate is null || (double.IsFinite(config.Framerate.Value) && config.Framerate.Value > 0);
    }

    /// <summary>A codec string is valid when it is not empty after the removal of the leading and trailing ASCII whitespace.</summary>
    private static bool IsValidCodec(string codec) => codec.AsSpan().Trim(" \t\n\f\r").Length > 0;

    /// <summary>https://w3c.github.io/webcodecs/#clone-config: the recognized members, and a copy of the description.</summary>
    public static AudioDecoderConfig Clone(AudioDecoderConfig config) => new()
    {
        Codec = config.Codec,
        SampleRate = config.SampleRate,
        NumberOfChannels = config.NumberOfChannels,
        Description = CopyDescription(config.Description),
    };

    public static VideoDecoderConfig Clone(VideoDecoderConfig config) => new()
    {
        Codec = config.Codec,
        Description = CopyDescription(config.Description),
        CodedWidth = config.CodedWidth,
        CodedHeight = config.CodedHeight,
        DisplayAspectWidth = config.DisplayAspectWidth,
        DisplayAspectHeight = config.DisplayAspectHeight,
        ColorSpace = config.ColorSpace is { } colorSpace ? new VideoColorSpaceInit { Primaries = colorSpace.Primaries, Transfer = colorSpace.Transfer, Matrix = colorSpace.Matrix, FullRange = colorSpace.FullRange } : null,
        HardwareAcceleration = config.HardwareAcceleration,
        OptimizeForLatency = config.OptimizeForLatency,
        Rotation = config.Rotation,
        Flip = config.Flip,
    };

    public static AudioEncoderConfig Clone(AudioEncoderConfig config) => new()
    {
        Codec = config.Codec,
        SampleRate = config.SampleRate,
        NumberOfChannels = config.NumberOfChannels,
        Bitrate = config.Bitrate,
        BitrateMode = config.BitrateMode,
        Opus = config.Opus is { } opus ? new OpusEncoderConfig { Format = opus.Format, Signal = opus.Signal, Application = opus.Application, FrameDuration = opus.FrameDuration, Complexity = opus.Complexity ?? DefaultOpusComplexity, PacketLossPercentage = opus.PacketLossPercentage, UseInBandFec = opus.UseInBandFec, UseDtx = opus.UseDtx } : null,
        Aac = config.Aac is { } aac ? new AacEncoderConfig { Format = aac.Format } : null,
        Flac = config.Flac is { } flac ? new FlacEncoderConfig { BlockSize = flac.BlockSize, CompressLevel = flac.CompressLevel } : null,
    };

    public static VideoEncoderConfig Clone(VideoEncoderConfig config) => new()
    {
        Codec = config.Codec,
        Width = config.Width,
        Height = config.Height,
        DisplayWidth = config.DisplayWidth,
        DisplayHeight = config.DisplayHeight,
        Bitrate = config.Bitrate,
        Framerate = config.Framerate,
        HardwareAcceleration = config.HardwareAcceleration,
        Alpha = config.Alpha,
        ScalabilityMode = config.ScalabilityMode,
        BitrateMode = config.BitrateMode,
        LatencyMode = config.LatencyMode,
        ContentHint = config.ContentHint,
        Avc = config.Avc is { } avc ? new AvcEncoderConfig { Format = avc.Format } : null,
        Hevc = config.Hevc is { } hevc ? new HevcEncoderConfig { Format = hevc.Format } : null,
    };

    public static bool AreEqual(AudioDecoderConfig a, AudioDecoderConfig b) => a.Codec == b.Codec && a.SampleRate == b.SampleRate && a.NumberOfChannels == b.NumberOfChannels && SameBytes(a.Description, b.Description);

    public static bool AreEqual(VideoDecoderConfig a, VideoDecoderConfig b) => a.Codec == b.Codec && a.CodedWidth == b.CodedWidth && a.CodedHeight == b.CodedHeight
        && a.DisplayAspectWidth == b.DisplayAspectWidth && a.DisplayAspectHeight == b.DisplayAspectHeight && a.Rotation == b.Rotation && a.Flip == b.Flip
        && a.ColorSpace?.Primaries == b.ColorSpace?.Primaries && a.ColorSpace?.Transfer == b.ColorSpace?.Transfer && a.ColorSpace?.Matrix == b.ColorSpace?.Matrix && a.ColorSpace?.FullRange == b.ColorSpace?.FullRange
        && SameBytes(a.Description, b.Description);

    /// <summary>Runs the Check Configuration Support algorithm with the codecs of the current global object.</summary>
    public static bool CheckSupport(Func<MediaCodecBackend, bool> check) => GlobalScope.Current?.Options.MediaCodecs is { } backend && check(backend);

    /// <summary>Resolves a promise in a task of the event loop of the current global object.</summary>
    public static Task<T> Resolve<T>(T value)
    {
        if (GlobalScope.Current is not { } global)
            return Task.FromResult(value);

        var completion = new TaskCompletionSource<T>();
        global.QueueGlobalTask(() => completion.TrySetResult(value));
        return completion.Task;
    }

    private static byte[]? CopyDescription(object? description) => description is null ? null : WebCodecsBuffers.GetBytes(description).ToArray();

    private static bool SameBytes(object? a, object? b) => (a, b) switch
    {
        (null, null) => true,
        (null, _) or (_, null) => false,
        _ => WebCodecsBuffers.GetBytes(a).Span.SequenceEqual(WebCodecsBuffers.GetBytes(b).Span),
    };
}
