using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>Registers test codecs behind the public entry points for the current asynchronous flow.</summary>
internal static class TestCodecs
{
    /// <summary>
    /// Puts <paramref name="decoder"/> in front of the built-in decoders and replaces the encoders of the formats of
    /// <paramref name="encoders"/> (the other formats keep their built-in, not-yet-implemented encoders).
    /// </summary>
    /// <returns>A scope restoring both registries.</returns>
    public static IDisposable Use(ImageCodec? decoder, params TestStreamEncoder[] encoders)
    {
        var decoders = decoder is null ? ImageCodecRegistry.Default.Codecs : [decoder, .. ImageCodecRegistry.Default.Codecs];
        var encoderCodecs = ImageEncoderRegistry.Default.Codecs.Select(codec => (ImageEncoderCodec?)encoders.FirstOrDefault(encoder => encoder.Format == codec.Format) ?? codec).ToList();
        var decoderScope = ImageCodecRegistry.Override(new ImageCodecRegistry(decoders));
        var encoderScope = ImageEncoderRegistry.Override(new ImageEncoderRegistry(encoderCodecs));
        return new Scope(decoderScope, encoderScope);
    }

    /// <summary>Registers a <see cref="TestStreamEncoder"/> for every built-in output format and the test stream decoder.</summary>
    public static IDisposable UseAll(out TestStreamCodec decoder, out TestStreamEncoder png, out TestStreamEncoder gif, out TestStreamEncoder jpeg)
    {
        decoder = new TestStreamCodec();
        png = new TestStreamEncoder(ImageFormat.Png);
        gif = new TestStreamEncoder(ImageFormat.Gif);
        jpeg = new TestStreamEncoder(ImageFormat.Jpeg);
        return Use(decoder, png, gif, jpeg);
    }

    private sealed class Scope(IDisposable first, IDisposable second) : IDisposable
    {
        public void Dispose()
        {
            second.Dispose();
            first.Dispose();
        }
    }
}
