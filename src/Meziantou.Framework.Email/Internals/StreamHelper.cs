namespace Meziantou.Framework.Email.Internals;

internal static class StreamHelper
{
    /// <summary>Reads a stream until its end.</summary>
    /// <exception cref="EmailResourceLimitException">The stream has more than <paramref name="maxSize"/> bytes.</exception>
    public static byte[] ReadAll(Stream stream, long maxSize)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk)) > 0)
        {
            if (buffer.Length + read > maxSize)
            {
                throw new EmailResourceLimitException($"The content is larger than the limit of {maxSize} bytes.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
