namespace Meziantou.Framework.Imaging.Internals;

/// <summary>An internal encoder registration: creates the sessions of one output format. This is not a public plug-in interface.</summary>
internal abstract class ImageEncoderCodec
{
    /// <summary>Gets the format produced by this codec.</summary>
    public abstract ImageFormat Format { get; }

    /// <summary>Creates the encoding state of one output. Called after the shared validation and before any output is created or written.</summary>
    /// <param name="options">The validated inputs.</param>
    /// <returns>The session.</returns>
    public abstract ImageEncoderSession CreateSession(ImageEncoderSessionOptions options);
}
