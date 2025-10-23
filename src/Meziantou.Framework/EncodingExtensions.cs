namespace Meziantou.Framework;

public static class EncodingExtensions
{
    private static readonly Encoding Utf8WithoutPreambleEncodingInstance = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    extension(Encoding)
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
