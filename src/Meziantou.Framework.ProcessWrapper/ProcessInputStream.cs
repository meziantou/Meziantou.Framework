namespace Meziantou.Framework;

internal abstract class ProcessInputStream
{
    internal abstract Task WriteAsync(StreamWriter standardInput, CancellationToken cancellationToken);

    internal sealed class StreamInput(Stream stream) : ProcessInputStream
    {
        internal override async Task WriteAsync(StreamWriter standardInput, CancellationToken cancellationToken)
        {
            await stream.CopyToAsync(standardInput.BaseStream, cancellationToken).ConfigureAwait(false);
        }
    }

    internal sealed class TextReaderInput(TextReader reader) : ProcessInputStream
    {
        internal override async Task WriteAsync(StreamWriter standardInput, CancellationToken cancellationToken)
        {
            var buffer = new char[4096];
            int bytesRead;
            while ((bytesRead = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await standardInput.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    internal sealed class StringInput(string text) : ProcessInputStream
    {
        internal override async Task WriteAsync(StreamWriter standardInput, CancellationToken cancellationToken)
        {
            await standardInput.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }
}
