using System.Diagnostics;
using System.Globalization;
using System.Text;
using SharpFuzz;

namespace Meziantou.Framework.Fuzzing;

/// <summary>
/// The entry point of a fuzz project. libfuzzer-dotnet starts the published executable with the name of the target as its
/// only argument (<c>--target_arg</c>); the other commands prepare a campaign and reproduce its failures:
/// <list type="bullet">
/// <item><description><c>&lt;target&gt;</c>: fuzzes the target under libfuzzer-dotnet;</description></item>
/// <item><description><c>&lt;target&gt; &lt;file or directory&gt;...</c>: runs the target on each input file and prints the failures (exit code 1 when one fails);</description></item>
/// <item><description><c>&lt;target&gt; --seeds &lt;directory&gt;</c>: writes the seed corpus of the target;</description></item>
/// <item><description><c>&lt;target&gt; --dictionary &lt;file&gt;</c>: writes the libFuzzer dictionary of the target (an empty file when it has none);</description></item>
/// <item><description><c>--list</c>: prints the names of the targets.</description></item>
/// </list>
/// </summary>
public static class FuzzProgram
{
    // Set by libfuzzer-dotnet in the environment of the process it fuzzes
    private const string LibFuzzerSharedMemoryVariable = "__LIBFUZZER_SHM_ID";

    /// <summary>Runs a command.</summary>
    /// <param name="args">The command line arguments.</param>
    /// <param name="getTargets">
    /// Gets the targets. When fuzzing, it is only called from the first input, once SharpFuzz has set up the memory that the
    /// instrumented code writes its coverage to: the static initializers of the targets may call the library under test.
    /// </param>
    /// <returns>The exit code.</returns>
    public static int Run(string[] args, Func<IReadOnlyList<FuzzTarget>> getTargets)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(getTargets);

        if (args is [var name] && !name.StartsWith('-', StringComparison.Ordinal) && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(LibFuzzerSharedMemoryVariable)))
        {
            FuzzTarget? fuzzedTarget = null;
            Fuzzer.LibFuzzer.Run(data =>
            {
                fuzzedTarget ??= getTargets().FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal)) ?? throw new InvalidOperationException($"Unknown target '{name}'.");
                fuzzedTarget.Action(data);
            });

            return 0;
        }

        var targets = getTargets();
        if (args is ["--list"])
        {
            foreach (var item in targets)
            {
                Console.WriteLine(item.Name);
            }

            return 0;
        }

        if (args.Length == 0)
            return Usage(targets);

        var target = targets.FirstOrDefault(item => string.Equals(item.Name, args[0], StringComparison.Ordinal));
        if (target is null)
        {
            Console.Error.WriteLine($"Unknown target '{args[0]}'.");
            return Usage(targets);
        }

        switch (args)
        {
            case [_]:
                Console.Error.WriteLine($"Run the target under libfuzzer-dotnet (--target_arg={target.Name}), or pass the input files to run.");
                return 2;

            case [_, "--seeds", var directory]:
                return WriteSeeds(target, directory);

            case [_, "--dictionary", var file]:
                // libFuzzer rejects a byte order mark
                File.WriteAllText(file, GetDictionary(target), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                return 0;

            default:
                return Replay(target, args.AsSpan(1));
        }
    }

    /// <summary>Gets the libFuzzer dictionary of a target: one quoted token per line, with the non-printable bytes escaped.</summary>
    public static string GetDictionary(FuzzTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var builder = new StringBuilder();
        foreach (var token in target.Dictionary.Distinct(StringComparer.Ordinal))
        {
            builder.Append('"');
            foreach (var b in FuzzText.Encode(token))
            {
                if (b is >= 0x20 and < 0x7F and not (byte)'"' and not (byte)'\\')
                {
                    builder.Append((char)b);
                }
                else
                {
                    builder.Append("\\x").Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }
            }

            builder.Append('"').Append('\n');
        }

        return builder.ToString();
    }

    private static int WriteSeeds(FuzzTarget target, string directory)
    {
        Directory.CreateDirectory(directory);
        var count = 0;
        foreach (var seed in target.Seeds())
        {
            var name = new string([.. seed.Name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_')]);
            if (name.Length > 80)
            {
                name = name[..80];
            }

            File.WriteAllBytes(Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"{count:D5}-{name}")), seed.Data.ToArray());
            count++;
        }

        Console.WriteLine($"{count} seed(s) written to {directory}");
        return 0;
    }

    private static int Replay(FuzzTarget target, ReadOnlySpan<string> paths)
    {
        var files = new List<string>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                files.AddRange(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
            }
            else
            {
                files.Add(path);
            }
        }

        var failures = 0;
        foreach (var file in files)
        {
            var data = File.ReadAllBytes(file);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                target.Action(data);
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"OK     {file} ({data.Length} bytes, {stopwatch.Elapsed.TotalMilliseconds:F0} ms)"));
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"FAILED {file} ({data.Length} bytes, {stopwatch.Elapsed.TotalMilliseconds:F0} ms)"));
                Console.WriteLine(ex);
            }
        }

        Console.WriteLine($"{files.Count - failures}/{files.Count} input(s) passed");
        return failures == 0 ? 0 : 1;
    }

    private static int Usage(IReadOnlyList<FuzzTarget> targets)
    {
        Console.Error.WriteLine("""
            Usage:
              <target>                         fuzz the target under libfuzzer-dotnet
              <target> <file or directory>...  run the target on input files
              <target> --seeds <directory>     write the seed corpus
              <target> --dictionary <file>     write the libFuzzer dictionary
              --list                           list the targets
            """);
        Console.Error.WriteLine("Targets: " + string.Join(", ", targets.Select(target => target.Name)));
        return 2;
    }
}
