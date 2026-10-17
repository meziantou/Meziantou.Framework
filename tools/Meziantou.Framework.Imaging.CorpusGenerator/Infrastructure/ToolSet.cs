using System.Text.RegularExpressions;

namespace Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

/// <summary>The external tools of one generator. Every generator uses FFmpeg; subclasses add their own tools.</summary>
internal abstract partial class ToolSet
{
    protected ToolSet(string ffmpeg)
    {
        Ffmpeg = ffmpeg;
        var first = Proc.RunText([ffmpeg, "-hide_banner", "-version"]).Split('\n')[0];
        FfmpegVersion = FfmpegVersionRegex().Match(first).Groups[1].Value;
    }

    /// <summary>The runtime that runs the generator, recorded in the manifest like any other tool.</summary>
    public static string RuntimeLabel { get; } = ".NET " + Environment.Version.ToString(2);

    public string Ffmpeg { get; }

    public string FfmpegVersion { get; }

    public string FfmpegLabel => "ffmpeg " + FfmpegVersion;

    /// <summary>Absolute tool paths and the names the manifest shows instead.</summary>
    public abstract IReadOnlyDictionary<string, string> DisplayNames { get; }

    public List<string> FfmpegCmd(params IEnumerable<string> args) => [Ffmpeg, "-hide_banner", "-nostdin", "-loglevel", "error", "-y", .. args];

    protected static string Require(string name) => ExecutableFinder.GetFullExecutablePath(name) ?? throw new FatalException("Missing required tool: " + name);

    [GeneratedRegex(@"ffmpeg version (\S+)")]
    private static partial Regex FfmpegVersionRegex();
}
