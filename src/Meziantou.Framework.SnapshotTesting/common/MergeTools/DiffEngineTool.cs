using System.Diagnostics;
using Meziantou.Framework.DiffEngine;

#if MEZIANTOU_INLINE_SNAPSHOT_TESTING
using SnapshotException = Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotException;

namespace Meziantou.Framework.InlineSnapshotTesting.MergeTools;
#else
namespace Meziantou.Framework.SnapshotTesting.MergeTools;
#endif

internal sealed class DiffEngineTool(DiffTool tool) : MergeTool
{
    public override MergeToolResult? Start(string currentFilePath, string newFilePath) => Start(currentFilePath, newFilePath, waitForMerge: false);

    internal override MergeToolResult? Start(string currentFilePath, string newFilePath, bool waitForMerge)
    {
        if (!DiffTools.TryFindByName(tool, out var resolvedTool))
            return null;

        return Start(resolvedTool, currentFilePath, newFilePath, waitForMerge);
    }

    internal static MergeToolResult? Start(ResolvedTool resolvedTool, string currentFilePath, string newFilePath, bool waitForMerge)
    {
        var arguments = resolvedTool.GetArguments(newFilePath, currentFilePath);

        // Most launchers of an IDE hand the files over to the running instance and exit right away. VS Code and Cursor
        // can be asked to wait until the diff is closed; the other tools give no such guarantee.
        var waitsForMerge = false;
        if (waitForMerge && resolvedTool.Tool is DiffTool.VisualStudioCode or DiffTool.Cursor)
        {
            arguments = "--wait " + arguments;
            waitsForMerge = true;
        }

        var startInfo = CreateStartInfo(resolvedTool.ExePath, arguments);

        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process is not null)
                return new ProcessMergeToolResult(process, waitsForMerge);

            throw new SnapshotException($"Failed to launch diff tool: {resolvedTool.ExePath} {arguments}");
        }
        catch (Exception exception)
        {
            process?.Dispose();
            throw new SnapshotException($"Failed to launch diff tool: {resolvedTool.ExePath} {arguments}", exception);
        }
    }

    /// <summary>
    /// Creates the start information for a diff tool. <paramref name="arguments" /> follows the Windows command-line
    /// rules, which only protect spaces, tabs and quotes.
    /// </summary>
    /// <remarks>
    /// A batch file, such as the <c>code.cmd</c> launcher of VS Code on Windows, runs in <c>cmd.exe</c>, which gives
    /// <c>&amp;</c>, <c>|</c>, <c>^</c>, <c>%</c> and a few other characters a meaning of their own. A snapshot path
    /// such as <c>C:\src\R&amp;D\file.txt</c> would otherwise be cut at <c>&amp;</c> and the rest run as another command.
    /// So a batch file is started through <c>cmd.exe</c> explicitly, with each argument escaped for it. <c>/s</c>
    /// with the surrounding quotes makes <c>cmd.exe</c> remove exactly those quotes before it parses the command.
    /// </remarks>
    internal static ProcessStartInfo CreateStartInfo(string executablePath, string arguments)
    {
        if (!IsBatchFile(executablePath))
        {
            return new ProcessStartInfo(executablePath, arguments)
            {
                UseShellExecute = true,
            };
        }

        var commandLine = CommandLineBuilder.WindowsCmdArgument(executablePath) + " " + CommandLineBuilder.WindowsCmdArguments([.. SplitWindowsArguments(arguments)]);
        return new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /e:on /v:off /s /c \"" + commandLine.TrimEnd() + "\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
    }

    private static bool IsBatchFile(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Splits a command line into arguments using the rules of the Microsoft C runtime, which
    /// <see cref="CommandLineBuilder.WindowsQuotedArgument(string?)" /> quotes for.
    /// </summary>
    internal static List<string> SplitWindowsArguments(string arguments)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inArgument = false;
        var inQuotes = false;
        for (var i = 0; i < arguments.Length; i++)
        {
            var c = arguments[i];
            if (c is '\\')
            {
                var backslashCount = 0;
                while (i < arguments.Length && arguments[i] is '\\')
                {
                    backslashCount++;
                    i++;
                }

                inArgument = true;
                if (i < arguments.Length && arguments[i] is '"')
                {
                    // 2n backslashes and a quote are n backslashes and a delimiting quote, 2n+1 backslashes and a quote
                    // are n backslashes and a literal quote.
                    current.Append('\\', backslashCount / 2);
                    if (backslashCount % 2 is 1)
                    {
                        current.Append('"');
                        continue;
                    }
                }
                else
                {
                    current.Append('\\', backslashCount);
                }

                // Process the character that ended the backslashes on the next iteration.
                i--;
                continue;
            }

            if (c is '"')
            {
                inArgument = true;
                if (inQuotes && i + 1 < arguments.Length && arguments[i + 1] is '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (c is ' ' or '\t' && !inQuotes)
            {
                if (inArgument)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    inArgument = false;
                }

                continue;
            }

            inArgument = true;
            current.Append(c);
        }

        if (inArgument)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    public override string ToString() => tool.ToString();
}
