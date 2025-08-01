namespace Meziantou.Framework;

public static class EncodingExtensions
{
    private static readonly Encoding Utf8WithoutPreambleEncodingInstance = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

#pragma warning disable CA1034 // Nested types should not be visible
    extension(Encoding)
#pragma warning restore CA1034 // Nested types should not be visible
    {
        public static Encoding UTF8WithoutPreamble
        {
            get
            {
                return Utf8WithoutPreambleEncodingInstance;
            }
        }
    }
}
