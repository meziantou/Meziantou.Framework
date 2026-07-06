namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for <see cref="Encoding"/>.
/// </summary>
public static class EncodingExtensions
{
    private static readonly Encoding Utf8WithoutPreambleEncodingInstance = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    extension(Encoding)
    {
        /// <summary>Gets a UTF-8 encoding without a byte order mark (BOM).</summary>
        public static Encoding UTF8WithoutPreamble
        {
            get
            {
                return Utf8WithoutPreambleEncodingInstance;
            }
        }
    }
}
