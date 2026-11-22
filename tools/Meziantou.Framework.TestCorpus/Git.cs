using System.Diagnostics;
using System.Text;

namespace Meziantou.Framework.TestCorpus;

/// <summary>Runs git in a directory.</summary>
public static class Git
{
    /// <summary>Runs git and returns its standard output.</summary>
    /// <exception cref="InvalidOperationException">git failed.</exception>
    public static string Run(string directory, params string[] arguments) => Run(directory, environment: null, input: null, arguments);

    /// <summary>Runs git with additional environment variables and a standard input, and returns its standard output.</summary>
    /// <exception cref="InvalidOperationException">git failed.</exception>
    public static string Run(string directory, IReadOnlyDictionary<string, string>? environment, string? input, params string[] arguments)
    {
        return TryRun(directory, environment, input, arguments) ?? throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed");
    }

    /// <summary>Runs git and returns its standard output, or <see langword="null"/> when it fails (its error is written to the standard error).</summary>
    public static string? TryRun(string directory, params string[] arguments) => TryRun(directory, environment: null, input: null, arguments);

    /// <summary>Runs git with additional environment variables and a standard input, and returns its standard output, or <see langword="null"/> when it fails.</summary>
    public static string? TryRun(string directory, IReadOnlyDictionary<string, string>? environment, string? input, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardInput = input is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = input is not null ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) : null,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(directory);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                startInfo.Environment[name] = value;
            }
        }

        using var process = Process.Start(startInfo)!;
        var errorTask = process.StandardError.ReadToEndAsync();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        if (input is not null)
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }

        process.WaitForExit();
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            Console.Error.Write(error);
            return null;
        }

        return output;
    }

    /// <summary>Runs git, retrying 4 times (after 2, 4, 8 and 16 seconds) when it fails, for the transient network errors.</summary>
    /// <exception cref="InvalidOperationException">git failed 5 times.</exception>
    public static string RunWithRetry(string directory, params string[] arguments)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return Run(directory, arguments);
            }
            catch (InvalidOperationException) when (attempt < 4)
            {
                var delay = TimeSpan.FromSeconds(2 << attempt);
                Console.WriteLine($"git {string.Join(' ', arguments)} failed, retrying in {delay.TotalSeconds}s");
                Thread.Sleep(delay);
            }
        }
    }
}
