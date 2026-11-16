using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.CompressionFuzz;

/// <summary>
/// The readers and the decoders of Meziantou.Framework.Compression reject invalid data with an <see cref="InvalidDataException"/>
/// (an <see cref="Compression.ArchivePasswordException"/> for a wrong password), or a <see cref="NotSupportedException"/> for a
/// method they do not support, and never throw anything else. The encoders produce data the decoders read back unchanged.
/// </summary>
public static partial class CompressionFuzzTargets
{
    public static IReadOnlyList<FuzzTarget> All => [.. ArchiveTargets, .. BZip2Targets, .. LzmaTargets, .. PpmdTargets, .. RarTargets, .. SevenZipTargets, .. ZipTargets, .. ZipWriterTargets];
}
