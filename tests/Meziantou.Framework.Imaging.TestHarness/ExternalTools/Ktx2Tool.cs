using System.Buffers.Binary;
using System.Globalization;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// The reference tools of KTX 2.0: KTX-Software (<c>ktx create</c>, <c>ktx extract</c>), astcenc (the ASTC codec of Arm) and
/// basisu (the Basis Universal codec), from the meziantou/prebuilt release pinned by the <c>Meziantou.Prebuilt</c> package.
/// </summary>
/// <remarks>
/// <c>ktx extract</c> writes PNG files of 8-bit samples for ASTC (decode_unorm8, and the sRGB operation mode) and for
/// <c>--transcode rgba8</c>; astcenc writes a KTX (version 1) file of halves with <c>-dh</c>, an RGB one when every alpha
/// is 1 (its EXR output is premultiplied by alpha). KTX-Software and basisu decode UASTC with the linear expansion of the
/// endpoints, whatever the transfer function of the file.
/// </remarks>
public sealed class Ktx2Tool
{
    private Ktx2Tool(string ktx, string astcenc, string basisu)
    {
        KtxPath = ktx;
        AstcencPath = astcenc;
        BasisuPath = basisu;
    }

    public string KtxPath { get; }

    public string AstcencPath { get; }

    public string BasisuPath { get; }

    /// <summary>Downloads the tools (cached and checksum-verified) and checks that <c>ktx</c> runs.</summary>
    /// <exception cref="ExternalToolUnavailableException">A tool does not run.</exception>
    public static async Task<Ktx2Tool> LocateAsync(CancellationToken cancellationToken)
    {
        var ktx = await PrebuiltTools.Ktx.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var astcenc = await PrebuiltTools.Astcenc.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var basisu = await PrebuiltTools.Basisu.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var version = await ProcessRunner.RunAsync(ktx, ["--version"], cancellationToken).ConfigureAwait(false);
        if (version.ExitCode != 0 || !version.StandardOutputText.Contains("v" + PrebuiltTools.Ktx.Version, StringComparison.Ordinal))
            throw new ExternalToolUnavailableException($"'{ktx}' does not run or is not version {PrebuiltTools.Ktx.Version}: {version.StandardOutputText}{version.StandardError}");

        return new Ktx2Tool(ktx, astcenc, basisu);
    }

    /// <summary>Runs <c>ktx</c>.</summary>
    /// <exception cref="InvalidOperationException">The tool failed.</exception>
    public Task RunKtxAsync(IEnumerable<string> arguments, CancellationToken cancellationToken) => RunAsync(KtxPath, arguments, cancellationToken);

    /// <summary>Runs <c>basisu</c>.</summary>
    /// <exception cref="InvalidOperationException">The tool failed.</exception>
    public Task RunBasisuAsync(IEnumerable<string> arguments, CancellationToken cancellationToken) => RunAsync(BasisuPath, arguments, cancellationToken);

    /// <summary>Decodes the ASTC blocks of a KTX 2.0 file with <c>astcenc -dh</c>: the halves of decode_float16 (RGBA, alpha 1 when astcenc omits it).</summary>
    public async Task<Half[]> DecodeAstcHalvesAsync(FullPath file, int blockWidth, int blockHeight, int width, int height, FullPath scratch, CancellationToken cancellationToken)
    {
        var blocks = scratch / "astc-blocks.raw";
        File.Delete(blocks);
        await RunKtxAsync(["extract", "--raw", file, blocks], cancellationToken).ConfigureAwait(false);
        byte[] header = [0x13, 0xAB, 0xA1, 0x5C, (byte)blockWidth, (byte)blockHeight, 1, (byte)width, (byte)(width >> 8), 0, (byte)height, (byte)(height >> 8), 0, 1, 0, 0];
        var astc = scratch / "astc-decode.astc";
        await File.WriteAllBytesAsync(astc, [.. header, .. await File.ReadAllBytesAsync(blocks, cancellationToken).ConfigureAwait(false)], cancellationToken).ConfigureAwait(false);
        var ktx = scratch / "astc-decode.ktx";
        File.Delete(ktx);
        await RunAsync(AstcencPath, ["-dh", astc, ktx, "-silent"], cancellationToken).ConfigureAwait(false);
        var data = await File.ReadAllBytesAsync(ktx, cancellationToken).ConfigureAwait(false);
        var glFormat = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(24));
        var offset = 64 + BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(60));
        var length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
        var channels = glFormat == 0x1907 ? 3 : 4;
        var count = length / (2 * channels);
        var halves = new Half[count * 4];
        for (var i = 0; i < count; i++)
        {
            for (var c = 0; c < 4; c++)
            {
                halves[(i * 4) + c] = c < channels ? BinaryPrimitives.ReadHalfLittleEndian(data.AsSpan(offset + 4 + (((i * channels) + c) * 2))) : Half.One;
            }
        }

        return halves;
    }

    /// <summary>Writes the image of a level of an ASTC 4x4 KTX (version 1) file of basisu, the lossless transcoding of UASTC blocks.</summary>
    public static byte[] ReadKtx1Image(byte[] data)
    {
        var offset = 64 + BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(60));
        var length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
        return data.AsSpan(offset + 4, length).ToArray();
    }

    private static async Task RunAsync(string tool, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var list = arguments.ToList();
        var result = await ProcessRunner.RunAsync(tool, list, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(tool)} {string.Join(' ', list)} failed ({result.ExitCode}): {result.StandardError}{result.StandardOutputText}"));
    }
}
