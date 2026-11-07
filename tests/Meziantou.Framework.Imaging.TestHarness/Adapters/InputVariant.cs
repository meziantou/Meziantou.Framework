namespace Meziantou.Framework.Imaging.TestHarness.Adapters;

/// <summary>The ways encoded data is handed to the eager <c>Identify</c>/<c>Load</c> entry points.</summary>
public enum InputVariant
{
    /// <summary>A <see cref="ReadOnlySpan{T}"/> (synchronous only).</summary>
    Span,

    /// <summary>A file path with the extension of the format.</summary>
    Path,

    /// <summary>A file path whose extension names another format (detection is content-based).</summary>
    MisleadingExtensionPath,

    /// <summary>A seekable stream positioned at 0.</summary>
    SeekableStream,

    /// <summary>A non-seekable stream.</summary>
    NonSeekableStream,

    /// <summary>A non-seekable stream returning at most one byte per read.</summary>
    ShortReadStream,

    /// <summary>A seekable stream whose data starts at a nonzero position, after unrelated bytes.</summary>
    OffsetStream,

    /// <summary>The asynchronous path overload.</summary>
    AsyncPath,

    /// <summary>The asynchronous stream overload with a seekable stream.</summary>
    AsyncStream,

    /// <summary>The asynchronous stream overload with a non-seekable stream returning three bytes per read; synchronous reads are forbidden.</summary>
    AsyncShortReadStream,
}
