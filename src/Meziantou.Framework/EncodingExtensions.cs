namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for <see cref="Encoding"/>.
/// </summary>
public static class EncodingExtensions
{
#pragma warning disable IDE0052 // Will be fixed in a future version of Roslyn https://github.com/dotnet/roslyn/issues/81986
    private static readonly Encoding Utf8WithoutPreambleEncodingInstance = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
#pragma warning restore IDE0052

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
