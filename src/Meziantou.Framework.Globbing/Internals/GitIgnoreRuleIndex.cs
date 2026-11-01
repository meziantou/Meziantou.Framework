namespace Meziantou.Framework.Globbing.Internals;

/// <summary>
///     Finds the last gitignore rule that matches a path without evaluating every rule. Most entries of a
///     <c>.gitignore</c> file are a name (<c>node_modules</c>, <c>bin/</c>) or an extension (<c>*.log</c>), and such
///     a rule can only match a path whose last segment has that name or that extension. Those rules are grouped by
///     name and by extension, so only the rules that can match are evaluated, still from the last one to the first.
/// </summary>
internal sealed class GitIgnoreRuleIndex
{
    private readonly Glob[] _rules;
    private readonly bool[] _directoryOnly;
    private readonly int[] _otherRules;
    private readonly Dictionary<string, int[]>.AlternateLookup<ReadOnlySpan<char>> _rulesByName;
    private readonly Dictionary<string, int[]>.AlternateLookup<ReadOnlySpan<char>> _rulesByExtension;

    public GitIgnoreRuleIndex(Glob[] rules)
    {
        _rules = rules;
        _directoryOnly = new bool[rules.Length];

        var otherRules = new List<int>();
        var rulesByName = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var rulesByExtension = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < rules.Length; i++)
        {
            var (kind, key, directoryOnly) = Classify(rules[i]);
            _directoryOnly[i] = directoryOnly;
            switch (kind)
            {
                case RuleKind.Name:
                    Add(rulesByName, key!, i);
                    break;

                case RuleKind.Extension:
                    Add(rulesByExtension, key!, i);
                    break;

                default:
                    otherRules.Add(i);
                    break;
            }
        }

        _otherRules = [.. otherRules];
        _rulesByName = ToArrays(rulesByName).GetAlternateLookup<ReadOnlySpan<char>>();
        _rulesByExtension = ToArrays(rulesByExtension).GetAlternateLookup<ReadOnlySpan<char>>();

        static void Add(Dictionary<string, List<int>> dictionary, string key, int index)
        {
            if (!dictionary.TryGetValue(key, out var list))
            {
                list = [];
                dictionary.Add(key, list);
            }

            list.Add(index);
        }

        static Dictionary<string, int[]> ToArrays(Dictionary<string, List<int>> dictionary)
        {
            var result = new Dictionary<string, int[]>(dictionary.Count, StringComparer.Ordinal);
            foreach (var (key, value) in dictionary)
            {
                result.Add(key, [.. value]);
            }

            return result;
        }
    }

    /// <summary>Returns the mode of the last rule that matches the path, or <see langword="null"/> when no rule matches it.</summary>
    public GlobMode? GetLastMatch(ReadOnlySpan<char> directory, ReadOnlySpan<char> filename, PathItemType? itemType)
    {
        // Read the path as the rules read it, so the last segment and the kind of item are the ones they see
        var pathReader = new PathReader(directory, filename, itemType);
        var isDirectory = pathReader.IsDirectory;
        pathReader.ConsumeToLastSegment();
        var name = pathReader.CurrentText;

        ReadOnlySpan<int> nameRules = _rulesByName.TryGetValue(name, out var byName) ? byName : [];
        ReadOnlySpan<int> extensionRules = [];
        var dotIndex = name.LastIndexOf('.');
        if (dotIndex >= 0 && _rulesByExtension.TryGetValue(name[(dotIndex + 1)..], out var byExtension))
        {
            extensionRules = byExtension;
        }

        ReadOnlySpan<int> otherRules = _otherRules;

        // The three lists are sorted, so merge them from the end to evaluate the rules from the last one
        var otherIndex = otherRules.Length - 1;
        var nameIndex = nameRules.Length - 1;
        var extensionIndex = extensionRules.Length - 1;
        while (true)
        {
            var ruleIndex = -1;
            var source = 0;
            if (otherIndex >= 0)
            {
                ruleIndex = otherRules[otherIndex];
                source = 1;
            }

            if (nameIndex >= 0 && nameRules[nameIndex] > ruleIndex)
            {
                ruleIndex = nameRules[nameIndex];
                source = 2;
            }

            if (extensionIndex >= 0 && extensionRules[extensionIndex] > ruleIndex)
            {
                ruleIndex = extensionRules[extensionIndex];
                source = 3;
            }

            switch (source)
            {
                case 0:
                    return null;
                case 1:
                    otherIndex--;
                    break;
                case 2:
                    nameIndex--;
                    break;
                default:
                    extensionIndex--;
                    break;
            }

            // A rule ending with a '/' only matches a directory
            if (_directoryOnly[ruleIndex] && !isDirectory)
                continue;

            var rule = _rules[ruleIndex];
            if (rule.IsMatchCore(directory, filename, itemType))
                return rule.Mode;
        }
    }

    private static (RuleKind Kind, string? Key, bool DirectoryOnly) Classify(Glob rule)
    {
        ReadOnlySpan<Segment> segments = rule._segments;

        // A gitignore entry ending with a '/' ends with a segment that only matches the directory at the end of the path
        var directoryOnly = false;
        if (segments is [.., DirectoryContentSegment directoryContent])
        {
            if (!ReferenceEquals(directoryContent, DirectoryContentSegment.DirectoryOnly))
                return (RuleKind.Other, null, false);

            directoryOnly = true;
            segments = segments[..^1];
        }

        // The last segment matches the last segment of the path as a whole
        return segments switch
        {
            [.., LiteralSegment { IgnoreCase: false } literal] => (RuleKind.Name, literal.Value, directoryOnly),
            [.., LastSegment { InnerSegment: LiteralSegment { IgnoreCase: false } literal }] => (RuleKind.Name, literal.Value, directoryOnly),
            [.., EndsWithSegment { IgnoreCase: false } endsWith] when TryGetExtension(endsWith.Value, out var extension) => (RuleKind.Extension, extension, directoryOnly),
            [.., MatchAllEndsWithSegment { IgnoreCase: false } endsWith] when TryGetExtension(endsWith.Suffix, out var extension) => (RuleKind.Extension, extension, directoryOnly),
            _ => (RuleKind.Other, null, directoryOnly),
        };

        // A name ending with ".ext" has the extension "ext" only when the suffix holds no other dot
        static bool TryGetExtension(string suffix, [NotNullWhen(true)] out string? extension)
        {
            if (suffix.Length >= 2 && suffix[0] == '.' && suffix.AsSpan(1).IndexOfAny('.', Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) < 0)
            {
                extension = suffix[1..];
                return true;
            }

            extension = null;
            return false;
        }
    }

    private enum RuleKind
    {
        Other,
        Name,
        Extension,
    }
}
