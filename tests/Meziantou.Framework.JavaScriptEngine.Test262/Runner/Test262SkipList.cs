using System.Text.RegularExpressions;

namespace Meziantou.Framework.JavaScriptEngine.Test262.Runner;

/// <summary>
/// The tests that are not run yet (test262-skip.txt): a path, a directory (ending with '/'), or a pattern where '*'
/// matches a segment and '**' any number of segments.
/// </summary>
internal sealed class Test262SkipList
{
    private readonly HashSet<string> _paths = new(StringComparer.Ordinal);
    private readonly List<string> _directories = [];
    private readonly List<Regex> _patterns = [];

    public static Test262SkipList Load(string path)
    {
        var list = new Test262SkipList();
        foreach (var (value, _) in Test262Discovery.ReadListFile(path))
        {
            if (value.Contains('*', StringComparison.Ordinal))
            {
                var pattern = "^" + Regex.Escape(value).Replace(@"\*\*", ".*", StringComparison.Ordinal).Replace(@"\*", "[^/]*", StringComparison.Ordinal) + "$";
                list._patterns.Add(new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1)));
            }
            else if (value.EndsWith('/', StringComparison.Ordinal))
            {
                list._directories.Add(value);
            }
            else
            {
                list._paths.Add(value);
            }
        }

        return list;
    }

    public bool IsSkipped(string id)
    {
        if (_paths.Contains(id))
            return true;

        foreach (var directory in _directories)
        {
            if (id.StartsWith(directory, StringComparison.Ordinal))
                return true;
        }

        foreach (var pattern in _patterns)
        {
            if (pattern.IsMatch(id))
                return true;
        }

        return false;
    }
}
