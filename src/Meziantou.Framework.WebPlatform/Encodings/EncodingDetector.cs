using Meziantou.Framework.WebPlatform.Encodings;

namespace Meziantou.Framework.WebPlatform;

/// <summary>
/// Detects the character encoding of the HTML documents that do not declare one: the step of the encoding sniffing
/// algorithm that "may attempt to autodetect the character encoding from applying frequency analysis or other algorithms
/// to the data stream" (https://html.spec.whatwg.org/multipage/parsing.html#determining-the-character-encoding). It runs
/// after the byte order mark, the encoding of the transport layer, the <c>meta</c> elements and the encoding of the
/// container document, and before the default encoding; the confidence of its result is tentative.
/// <para>
/// Set <see cref="HtmlParserOptions.EncodingDetector"/> or <see cref="Dom.BrowsingContextOptions.EncodingDetector"/> to
/// replace the detector (<see cref="Default"/>), or to null to disable the detection.
/// </para>
/// </summary>
public abstract class EncodingDetector
{
    /// <summary>
    /// Gets the built-in detector, a port of chardetng, the detector of Firefox: it guesses the legacy encodings from the
    /// validity of the byte sequences and the frequencies of the characters of the languages that use them (Shift_JIS,
    /// EUC-JP, GBK, Big5, EUC-KR, windows-1250 to windows-1258, windows-874, ISO-8859-2, ISO-8859-4 to ISO-8859-8,
    /// ISO-8859-13, KOI8-U and IBM866).
    /// <para>
    /// As in the browsers, UTF-8 is only detected for the documents that do not come from the network: the ones whose URL
    /// is a <c>file:</c> URL, and the ones that have no URL (<see cref="HtmlParserOptions.Url"/> is not set). The other
    /// documents whose bytes are valid UTF-8 get the default encoding, so that the content of the Web does not start to
    /// rely on the detection instead of declaring its encoding. ISO-2022-JP is never detected.
    /// </para>
    /// <para>
    /// The validity of UTF-8 is checked for all the bytes; the legacy encodings are guessed from the 256 KiB that follow
    /// the first non-ASCII byte.
    /// </para>
    /// </summary>
    public static EncodingDetector Default { get; } = new BuiltInEncodingDetector();

    /// <summary>
    /// Detects the encoding of a document. It is not called for the documents that only have ASCII bytes other than the
    /// escape, shift out and shift in characters (0x1B, 0x0E and 0x0F): they are the same in all the encodings that can
    /// be detected.
    /// </summary>
    /// <param name="bytes">
    /// The bytes of the document: all of them, or, for a document that is parsed as its bytes arrive, the bytes received
    /// when the first chunk that has another byte than the ones above arrives (see <see cref="EncodingDetectionContext.IsComplete"/>).
    /// </param>
    /// <param name="context">The context of the detection.</param>
    /// <returns>
    /// A label of the encoding (https://encoding.spec.whatwg.org/#names-and-labels, e.g. <c>windows-1251</c>), or null when
    /// the encoding is not detected: the document gets the default encoding. The labels that are not supported and the
    /// ones of UTF-16BE/LE and of the replacement encoding are ignored.
    /// </returns>
    public abstract string? Detect(ReadOnlySpan<byte> bytes, EncodingDetectionContext context);

    private sealed class BuiltInEncodingDetector : EncodingDetector
    {
        // The number of bytes that are scored, from the first non-ASCII byte
        private const int MaximumScoredLength = 256 * 1024;

        public override string? Detect(ReadOnlySpan<byte> bytes, EncodingDetectionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // The ASCII bytes do not indicate an encoding
            var firstNonAscii = bytes.IndexOfAnyExceptInRange((byte)0x00, (byte)0x7F);
            if (firstNonAscii < 0)
                return null;

            // Nothing else is guessed for valid UTF-8: the other encodings do not have to score the bytes
            if (IsValidUtf8(bytes, context.IsComplete))
                return context.Url is null || context.Url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? "UTF-8" : null;

            // Each candidate encoding scores each byte: the work is bounded for the large documents
            var end = (int)Math.Min(bytes.Length, (long)firstNonAscii + MaximumScoredLength);
            var detector = new LegacyEncodingDetector();
            detector.DisqualifyUtf8();
            detector.Feed(bytes[..end], last: context.IsComplete && end == bytes.Length);
            return detector.Guess(allowUtf8: false) is { } encoding ? WebEncodingRegistry.GetName(encoding) : null;
        }

        private static bool IsValidUtf8(ReadOnlySpan<byte> bytes, bool isComplete)
        {
            if (System.Text.Unicode.Utf8.IsValid(bytes))
                return true;

            if (isComplete)
                return false;

            // The bytes may end in the middle of a character: the last lead byte starts a sequence that is valid so far
            var tailStart = bytes.Length - 1;
            while (tailStart >= 0 && bytes.Length - tailStart < 4 && bytes[tailStart] is >= 0x80 and < 0xC0)
            {
                tailStart--;
            }

            if (tailStart < 0 || bytes[tailStart] < 0xC0 || !System.Text.Unicode.Utf8.IsValid(bytes[..tailStart]))
                return false;

            Span<char> output = stackalloc char[WebDecoder.MinimumOutputLength];
            return new Utf8Decoder(fatal: true).Decode(bytes[tailStart..], output, flush: false, out _, out var written) == DecodeStatus.NeedMoreData && written == 0;
        }
    }
}

/// <summary>The context of the detection of the encoding of a document (see <see cref="EncodingDetector.Detect"/>).</summary>
public sealed class EncodingDetectionContext
{
    internal EncodingDetectionContext(string? url, bool isComplete)
    {
        Url = url;
        IsComplete = isComplete;
    }

    /// <summary>Gets the URL of the document, or null when it has none (<see cref="HtmlParserOptions.Url"/> is not set).</summary>
    public string? Url { get; }

    /// <summary>
    /// Gets whether the bytes are the whole document. It is false for a document that is parsed as its bytes arrive: the
    /// bytes may end in the middle of a character.
    /// </summary>
    public bool IsComplete { get; }
}
