namespace Meziantou.Framework;

[Obsolete("Use Meziantou.Framework.ProcessWrapper.")]
public sealed class ProcessResult
{
    internal ProcessResult(int exitCode, IReadOnlyList<ProcessOutput> output)
    {
        ExitCode = exitCode;
        Output = new ProcessOutputCollection(output);
    }

    public int ExitCode { get; }
    public ProcessOutputCollection Output { get; }
}
