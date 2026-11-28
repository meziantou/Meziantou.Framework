using System.Runtime.InteropServices;

namespace Meziantou.Framework.Yaml;

/// <summary>
/// The state of a <see cref="Utf8YamlReader"/>, to continue reading in a new reader when the input comes in several buffers.
/// </summary>
/// <remarks>
/// Get it from <see cref="Utf8YamlReader.CurrentState"/>, drop the first <see cref="Utf8YamlReader.BytesConsumed"/> bytes of
/// the input, and pass it with the remaining bytes followed by the new data to the constructor of the next reader.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public struct Utf8YamlReaderState
{
    internal Utf8YamlReader.StateData _data;

    /// <summary>Initializes a new state with the specified options.</summary>
    public Utf8YamlReaderState(Utf8YamlReaderOptions options)
    {
        Options = options;
    }

    /// <summary>Gets the options of the reader.</summary>
    public Utf8YamlReaderOptions Options { get; }
}
