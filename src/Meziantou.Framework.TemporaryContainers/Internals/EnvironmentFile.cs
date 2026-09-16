using System.Text;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>A file holding the environment variables passed to a CLI with <c>--env-file</c>. The values often are secrets, and the command line of a process can be read by every user of the machine, while the file is only readable by the current user.</summary>
internal sealed class EnvironmentFile : IDisposable
{
    private readonly HashSet<string> _names;

    private EnvironmentFile(string path, HashSet<string> names)
    {
        Path = path;
        _names = names;
    }

    public string Path { get; }

    /// <summary>Determines whether the file holds a variable. The ones it does not hold must still be passed on the command line.</summary>
    public bool Contains(string name) => _names.Contains(name);

    /// <summary>Writes the variables that can be expressed in an environment file. Returns <see langword="null"/> when none can.</summary>
    public static EnvironmentFile? Create(IEnumerable<KeyValuePair<string, string>> variables)
    {
        var content = new StringBuilder();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, value) in variables)
        {
            if (!CanBeWritten(name, value))
                continue;

            content.Append(name).Append('=').Append(value).Append('\n');
            names.Add(name);
        }

        if (names.Count == 0)
            return null;

        var path = PrivateTemporaryFile.CreatePath();
        using (var stream = PrivateTemporaryFile.Create(path))
        {
            stream.Write(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content.ToString()));
        }

        return new EnvironmentFile(path, names);
    }

    /// <summary>A file holds one variable per line, so a value that spans lines cannot be written. The runtimes also disagree on the rest of the syntax: podman removes the quotes around a value and reads a name ending with '*' as a pattern of host variables, and Apple's parser skips comments. A variable that any of them would read differently stays on the command line.</summary>
    internal static bool CanBeWritten(string name, string value)
    {
        if (name.Length == 0 || name.AsSpan().ContainsAny(" \t\r\n#=*"))
            return false;

        return !value.AsSpan().ContainsAny("\r\n#") && !value.StartsWith('"', StringComparison.Ordinal) && !value.StartsWith('\'', StringComparison.Ordinal);
    }

    /// <summary>Adds environment variables to a command line: through the file for the ones it holds, as <c>--env name=value</c> for the others.</summary>
    public static void AddArguments(List<string> args, IEnumerable<KeyValuePair<string, string>> variables, EnvironmentFile? environmentFile)
    {
        if (environmentFile is not null)
        {
            args.Add("--env-file");
            args.Add(environmentFile.Path);
        }

        foreach (var (name, value) in variables)
        {
            if (environmentFile?.Contains(name) is true)
                continue;

            args.Add("--env");
            args.Add($"{name}={value}");
        }
    }

    public void Dispose()
    {
        try
        {
            File.Delete(Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
