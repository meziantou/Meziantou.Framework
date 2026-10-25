using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.CodeOwners.Fuzz;

/// <summary>
/// For each <see cref="CodeOwnersDialect"/>, <c>Parse</c> and <c>TryParse</c> (string and span, with and without the error)
/// agree, and <c>Parse</c> only throws <see cref="CodeOwnersParseException"/>. An error points at a character of the input
/// that can start the construct it reports, and the lines before it parse. A valid file is made of lines: every prefix
/// that ends a line parses into a prefix of its entries, and an entry is a non-empty pattern without a line break and
/// well-formed owners. Both dialects read a file without sections or roles the same way. Writing the entries back
/// (<see cref="CodeOwnersSection.ToString"/>, <see cref="CodeOwnersEntry.ToString"/>) gives a file that
/// parses into equal entries whatever its line endings, and writing is a fixed point.
/// </summary>
public static partial class CodeOwnersFuzzTargets
{
    private const int MaxPrefixChecks = 32;
    private const int MaxEqualityChecks = 16;

    private static readonly CodeOwnersDialect[] Dialects = Enum.GetValues<CodeOwnersDialect>();

    private static readonly string[] Tokens =
    [
        "*", "**", "/", "/**/", "*.js", "docs/", "/apps/github", "[abc]", "?", "!", "#", "# comment\n", " ", "  ", "\t", "\n", "\r\n", "\r", "\\", "\\ ", "\\#", "\\[",
        "@", "@@", "@user1", "@org/team-name", "user@example.com", "a@b", "@a@b", "@@developer", "@@developers", "@@maintainer", "@@Maintainers", "@@owner", "@@owners", "@@guest",
        "[", "]", "^", "^[", "[Section]", "^[Optional Section]", "[Test][2]", "[2]", "[0]", "[-1]", "[+3]", "[ 2]", "[abc]", "[2147483648]", "] ", "[Docs] @docs-team\n",
    ];

    private static readonly string[] RealWorldFiles =
    [
        // A GitLab file: sections with default owners, optional sections, reviewer counts and roles
        "# Required for all files\n* @gitlab-org/maintainers\n\n[Documentation][2] @docs-team\ndocs/\n*.md @tech-writer docs@example.com\n/doc/api/ @@maintainers\n\n^[Frontend] @frontend\n*.vue\napp/assets/javascripts/ @fe-lead @@developer\n\n[Database]\ndb/ @dba\n/db/migrate/with\\ space/ @dba # inline comment\n",

        // The same file with CRLF line endings
        "# Required for all files\r\n* @gitlab-org/maintainers\r\n\r\n[Documentation][2] @docs-team\r\ndocs/\r\n*.md @tech-writer docs@example.com\r\n\r\n^[Frontend] @frontend\r\n*.vue\r\n",

        // A GitHub file: character classes, escapes, teams, globstars and an unowned path
        "[abc].txt @user1\n\\#file_with_pound.js @owner\n/build/logs/ @org/team-name\n**/logs @octocat\ndocs/**  docs@example.com\n*.js    @js-owner  # inline\n/apps/ @octocat\n/apps/github\n",

        // Leading whitespace, tabs and escaped spaces
        "  *.cs\t@user1\t@user2\n\t/src/My\\ Project/ @user3\nfoo\\\\bar @user4\n",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("codeowners-parse", Parse) { Seeds = GetSeeds, Dictionary = Tokens },
        new("codeowners-round-trip", RoundTrip) { Seeds = GetSeeds, Dictionary = Tokens },
    ];

    /// <summary>The input is a dialect byte, then a CODEOWNERS file.</summary>
    private static void Parse(ReadOnlySpan<byte> data)
    {
        var dialect = FuzzInput.Pick(Dialects, FuzzInput.ReadByte(ref data));
        var content = FuzzText.Decode(data);

        var (file, error) = ParseEveryWay(content, dialect);
        if (file is not null)
        {
            CheckFile(file, content, dialect);
        }
        else
        {
            CheckError(error, content, dialect);
        }

        CheckLinePrefixes(content, dialect, file, error);

        // The dialects only differ on sections and roles
        if (!content.Contains('[', StringComparison.Ordinal) && !content.Contains("@@", StringComparison.Ordinal))
        {
            var otherDialect = dialect is CodeOwnersDialect.GitHub ? CodeOwnersDialect.GitLab : CodeOwnersDialect.GitHub;
            var (otherFile, otherError) = ParseEveryWay(content, otherDialect);
            FuzzAssert.Equal(Describe(file, error), Describe(otherFile, otherError), $"{dialect} and {otherDialect} disagree on a file without sections or roles");
        }
    }

    /// <summary>The input is a dialect byte, then a CODEOWNERS file. A valid file is written back and parsed again.</summary>
    private static void RoundTrip(ReadOnlySpan<byte> data)
    {
        var dialect = FuzzInput.Pick(Dialects, FuzzInput.ReadByte(ref data));
        var content = FuzzText.Decode(data);
        if (!CodeOwnersFile.TryParse(content, dialect, out var file))
            return;

        var description = Describe(file.Entries);
        var written = Write(file);
        foreach (var lineEnding in (string[])["\n", "\r\n", "\r"])
        {
            var text = written.Replace("\n", lineEnding, StringComparison.Ordinal);
            FuzzAssert.True(CodeOwnersFile.TryParse(text, dialect, out var reparsed, out var error), $"The written file does not parse ({error}): {FuzzAssert.Format(text)}");
            FuzzAssert.Equal(description, Describe(reparsed.Entries), $"The written file {FuzzAssert.Format(text)} has other entries");
            FuzzAssert.True(file.Entries.SequenceEqual(reparsed.Entries), "The entries of the written file are not equal to the parsed entries");
            FuzzAssert.Equal(written, Write(reparsed), "Writing the entries is not a fixed point");
        }
    }

    /// <summary>Parses with every overload, checks they agree, and returns the file or the error.</summary>
    private static (CodeOwnersFile? File, CodeOwnersParseError Error) ParseEveryWay(string content, CodeOwnersDialect dialect)
    {
        var isValid = CodeOwnersFile.TryParse(content, dialect, out var file, out var error);
        FuzzAssert.Equal(isValid, file is not null, "TryParse returns a file exactly when it succeeds");
        FuzzAssert.Equal(isValid, error.Kind is CodeOwnersParseErrorKind.None, $"TryParse returns an error exactly when it fails: {error}");
        if (isValid)
        {
            FuzzAssert.Equal(default, error, "A successful TryParse returns the default error");
        }

        var description = Describe(file, error);
        FuzzAssert.Equal(isValid, CodeOwnersFile.TryParse(content, dialect, out var other), "TryParse with and without the error disagree");
        FuzzAssert.Equal(description, Describe(other, error), "TryParse with and without the error disagree");

        FuzzAssert.Equal(isValid, CodeOwnersFile.TryParse(content.AsSpan(), dialect, out other, out var otherError), "TryParse(ReadOnlySpan<char>) and TryParse(string) disagree");
        FuzzAssert.Equal(description, Describe(other, otherError), "TryParse(ReadOnlySpan<char>) and TryParse(string) disagree");

        FuzzAssert.Equal(isValid, CodeOwnersFile.TryParse(content.AsSpan(), dialect, out other), "TryParse(ReadOnlySpan<char>) with and without the error disagree");
        FuzzAssert.Equal(description, Describe(other, error), "TryParse(ReadOnlySpan<char>) with and without the error disagree");

        CheckParse(isValid, description, error, () => CodeOwnersFile.Parse(content, dialect), "Parse(string)");
        CheckParse(isValid, description, error, () => CodeOwnersFile.Parse(content.AsSpan(), dialect), "Parse(ReadOnlySpan<char>)");
        return (file, error);
    }

    private static void CheckParse(bool isValid, string description, CodeOwnersParseError error, Func<CodeOwnersFile> parse, string method)
    {
        CodeOwnersFile file;
        try
        {
            file = parse();
        }
        catch (CodeOwnersParseException ex)
        {
            FuzzAssert.False(isValid, $"{method} throws, but TryParse succeeds: {ex.Message}");
            FuzzAssert.Equal(error, ex.Error, $"{method} and TryParse report different errors");
            FuzzAssert.True(ex.Message.Contains(error.ToString(), StringComparison.Ordinal), $"The message of the exception does not describe the error: {ex.Message}");
            return;
        }

        FuzzAssert.True(isValid, $"{method} succeeds, but TryParse reports {error}");
        FuzzAssert.Equal(description, Describe(file, default), $"{method} and TryParse disagree");
    }

    private static void CheckFile(CodeOwnersFile file, string content, CodeOwnersDialect dialect)
    {
        var lines = SplitLines(content);
        FuzzAssert.True(file.Entries.Count <= lines.Count, $"{file.Entries.Count} entries come from {lines.Count} lines");

        CodeOwnersSection? previousSection = null;
        foreach (var entry in file.Entries)
        {
            var pattern = entry.Pattern;
            FuzzAssert.True(pattern.Length > 0, "An entry has an empty pattern");
            FuzzAssert.False(pattern.AsSpan().ContainsAny('\r', '\n'), $"The pattern {FuzzAssert.Format(pattern)} spans several lines");

            // Without escapes, a pattern is text of a line
            if (!content.Contains('\\', StringComparison.Ordinal))
            {
                FuzzAssert.True(lines.Any(line => line.Contains(pattern, StringComparison.Ordinal)), $"The pattern {FuzzAssert.Format(pattern)} is not text of a line");
            }

            foreach (var owner in entry.Owners)
            {
                CheckOwner(owner, dialect);
            }

            var section = entry.Section;
            FuzzAssert.Equal(section?.IsOptional ?? false, entry.IsOptional, "IsOptional is not the one of the section");
            FuzzAssert.True(section is not null || previousSection is null, "An entry that follows a section has no section");
            if (section is not null)
            {
                FuzzAssert.True(dialect is CodeOwnersDialect.GitLab, $"The {dialect} dialect has no section, but an entry belongs to {section}");
                FuzzAssert.True(section.RequiredReviewerCount >= 1, $"The section {section} requires {section.RequiredReviewerCount} reviewers");
                FuzzAssert.Equal(!section.IsOptional, section.IsMandatory, "IsMandatory is not the opposite of IsOptional");
                FuzzAssert.Equal(section.DefaultOwners.Count > 0, section.HasDefaultOwners, "HasDefaultOwners does not reflect DefaultOwners");
                FuzzAssert.False(section.Name.AsSpan().ContainsAny(']', '\r', '\n'), $"The section name {FuzzAssert.Format(section.Name)} holds a ']' or a line break");
                foreach (var owner in section.DefaultOwners)
                {
                    CheckOwner(owner, dialect);
                }

                // An entry without owners inherits the default owners of its section
                FuzzAssert.True(entry.Owners.Count > 0 || !section.HasDefaultOwners, $"The entry {entry} has no owner, but its section {section} has default owners");
                FuzzAssert.True(section.Equals(section), "A section is not equal to itself");
            }

            previousSection = section;
            CheckEntryToString(entry, dialect);
            FuzzAssert.True(entry.Equals(entry), $"The entry {entry} is not equal to itself");
            FuzzAssert.False(entry.Equals(null), "An entry equals null");
        }

        // Equal values have equal hash codes
        var entries = file.Entries.Take(MaxEqualityChecks).ToArray();
        foreach (var left in entries)
        {
            foreach (var right in entries)
            {
                FuzzAssert.Equal(left.Equals(right), Describe([left]) == Describe([right]), $"Equals disagrees with the values of {left} and {right}");
                if (left == right)
                {
                    FuzzAssert.Equal(left.GetHashCode(), right.GetHashCode(), $"The equal entries {left} and {right} have different hash codes");
                }

                if (left.Section is not null && right.Section is not null && left.Section == right.Section)
                {
                    FuzzAssert.Equal(left.Section.GetHashCode(), right.Section.GetHashCode(), $"The equal sections {left.Section} and {right.Section} have different hash codes");
                }
            }
        }
    }

    /// <summary><see cref="CodeOwnersEntry.ToString"/>, after the header of its section, parses back into the entry.</summary>
    private static void CheckEntryToString(CodeOwnersEntry entry, CodeOwnersDialect dialect)
    {
        var line = entry.ToString();
        FuzzAssert.False(line.AsSpan().ContainsAny('\r', '\n'), $"The entry is written on several lines: {FuzzAssert.Format(line)}");
        FuzzAssert.False(line[0] is ' ' or '\t', $"The entry is written with leading whitespace: {FuzzAssert.Format(line)}");
        var text = entry.Section is { } section ? section.ToString() + "\n" + line : line;
        FuzzAssert.True(CodeOwnersFile.TryParse(text, dialect, out var reparsed, out var error), $"The written entry does not parse ({error}): {FuzzAssert.Format(text)}");
        FuzzAssert.Equal(Describe([entry]), Describe(reparsed.Entries), $"The written entry {FuzzAssert.Format(text)} is another entry");
        FuzzAssert.True(entry.Equals(reparsed.Entries[0]), $"The written entry {FuzzAssert.Format(text)} is not equal to the entry");
        FuzzAssert.Equal(line, reparsed.Entries[0].ToString(), "Writing an entry is not a fixed point");
    }

    private static void CheckOwner(CodeOwner owner, CodeOwnersDialect dialect)
    {
        var name = owner.Name;
        FuzzAssert.True(name.Length > 0, $"An owner of type {owner.Type} has an empty name");
        FuzzAssert.False(name.AsSpan().ContainsAny(" \t\r\n"), $"The owner {FuzzAssert.Format(name)} holds a separator");
        switch (owner.Type)
        {
            case CodeOwnerType.Username:
                FuzzAssert.False(name.Contains('@', StringComparison.Ordinal), $"The username {FuzzAssert.Format(name)} holds a '@'");
                FuzzAssert.Equal("@" + name, owner.ToString(), "A username is not written with a leading '@'");
                break;

            case CodeOwnerType.EmailAddress:
                var index = name.IndexOf('@', StringComparison.Ordinal);
                FuzzAssert.True(index > 0 && index < name.Length - 1, $"The email address {FuzzAssert.Format(name)} has no local part or no domain");
                FuzzAssert.Equal(name, owner.ToString(), "An email address is not written as is");
                break;

            case CodeOwnerType.Role:
                FuzzAssert.True(dialect is CodeOwnersDialect.GitLab, $"The {dialect} dialect has no roles, but the owner is the role {FuzzAssert.Format(name)}");
                var singular = name[^1] is 's' or 'S' ? name[..^1] : name;
                FuzzAssert.True(IsAsciiCaseInsensitiveEqual(singular, "developer") || IsAsciiCaseInsensitiveEqual(singular, "maintainer") || IsAsciiCaseInsensitiveEqual(singular, "owner"), $"{FuzzAssert.Format(name)} is not a GitLab role");
                FuzzAssert.Equal("@@" + name, owner.ToString(), "A role is not written with a leading '@@'");
                break;

            default:
                FuzzAssert.Fail($"Unknown owner type {owner.Type}");
                break;
        }

        FuzzAssert.True(owner.Equals(owner), "An owner is not equal to itself");
    }

    private static void CheckError(CodeOwnersParseError error, string content, CodeOwnersDialect dialect)
    {
        FuzzAssert.True(Enum.IsDefined(error.Kind) && error.Kind is not CodeOwnersParseErrorKind.None, $"Unexpected error kind {error.Kind}");
        if (dialect is CodeOwnersDialect.GitHub)
        {
            FuzzAssert.True(error.Kind is CodeOwnersParseErrorKind.EmptyOwner or CodeOwnersParseErrorKind.InvalidOwner, $"The {dialect} dialect has no section, but reports {error}");
        }

        var lines = SplitLines(content);
        FuzzAssert.True(error.LineNumber >= 1 && error.LineNumber <= lines.Count, $"The error {error} is not on a line of the input ({lines.Count} lines)");
        var line = lines[error.LineNumber - 1];
        FuzzAssert.True(error.LinePosition >= 1 && error.LinePosition <= line.Length, $"The error {error} is not in its line {FuzzAssert.Format(line)}");
        _ = error.ToString();

        // The position is the start of the construct the error reports
        var c = line[error.LinePosition - 1];
        switch (error.Kind)
        {
            case CodeOwnersParseErrorKind.UnterminatedSectionHeader or CodeOwnersParseErrorKind.UnterminatedRequiredReviewerCount or CodeOwnersParseErrorKind.InvalidRequiredReviewerCount:
                FuzzAssert.Equal('[', c, $"The error {error} does not point at a '['");
                break;

            case CodeOwnersParseErrorKind.EmptyOwner:
                FuzzAssert.Equal('@', c, $"The error {error} does not point at a '@'");
                break;

            default:
                FuzzAssert.False(c is ' ' or '\t' or '#', $"The error {error} points at a separator");
                break;
        }
    }

    /// <summary>The lines before the first error parse, and every prefix of a valid file that ends a line parses into a prefix of its entries.</summary>
    private static void CheckLinePrefixes(string content, CodeOwnersDialect dialect, CodeOwnersFile? file, CodeOwnersParseError error)
    {
        var lineCount = 0;
        var checks = 0;
        for (var i = 0; i < content.Length && checks < MaxPrefixChecks; i++)
        {
            if (content[i] is '\r' && i + 1 < content.Length && content[i + 1] is '\n')
            {
                i++;
            }
            else if (content[i] is not ('\r' or '\n'))
            {
                continue;
            }

            lineCount++;
            if (file is null && lineCount >= error.LineNumber)
                return;

            checks++;
            var prefix = content[..(i + 1)];
            FuzzAssert.True(CodeOwnersFile.TryParse(prefix, dialect, out var prefixFile, out var prefixError), $"The first {lineCount} lines are invalid ({prefixError}), but the first error of the file is {(file is null ? error.ToString() : "none")}");
            if (file is not null)
            {
                FuzzAssert.True(prefixFile.Entries.Count <= file.Entries.Count && prefixFile.Entries.SequenceEqual(file.Entries.Take(prefixFile.Entries.Count)), $"The entries of the first {lineCount} lines are not a prefix of the entries of the file:{Environment.NewLine}{Describe(prefixFile.Entries)}");
            }
        }
    }

    /// <summary>Writes entries as a CODEOWNERS file: a section header when the section changes, then the entry.</summary>
    private static string Write(CodeOwnersFile file)
    {
        var sb = new StringBuilder();
        CodeOwnersSection? section = null;
        foreach (var entry in file.Entries)
        {
            if (entry.Section is not null && !ReferenceEquals(entry.Section, section))
            {
                sb.Append(entry.Section.ToString()).Append('\n');
                section = entry.Section;
            }

            sb.Append(entry.ToString()).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>Splits the text at CR, LF and CRLF, keeping the empty line that follows a final line break.</summary>
    private static List<string> SplitLines(string content)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] is not ('\r' or '\n'))
                continue;

            lines.Add(content[start..i]);
            if (content[i] is '\r' && i + 1 < content.Length && content[i + 1] is '\n')
            {
                i++;
            }

            start = i + 1;
        }

        lines.Add(content[start..]);
        return lines;
    }

    private static bool IsAsciiCaseInsensitiveEqual(string value, string lowercase)
    {
        if (value.Length != lowercase.Length)
            return false;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i] is >= 'A' and <= 'Z' ? (char)(value[i] + ('a' - 'A')) : value[i];
            if (c != lowercase[i])
                return false;
        }

        return true;
    }

    private static string Describe(CodeOwnersFile? file, CodeOwnersParseError error) => file is null ? "error: " + error.ToString() : Describe(file.Entries);

    private static string Describe(IEnumerable<CodeOwnersEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var entry in entries)
        {
            sb.Append(FuzzAssert.Format(entry.Pattern)).Append(" owners=").Append(Describe(entry.Owners));
            if (entry.Section is { } section)
            {
                sb.Append(CultureInfo.InvariantCulture, $" section={FuzzAssert.Format(section.Name)} optional={section.IsOptional} count={section.RequiredReviewerCount} defaults={Describe(section.DefaultOwners)}");
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static string Describe(IEnumerable<CodeOwner> owners) => "[" + string.Join(", ", owners.Select(owner => $"{owner.Type}:{FuzzAssert.Format(owner.Name)}")) + "]";

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        var index = 0;
        foreach (var content in GetFiles().Distinct(StringComparer.Ordinal))
        {
            foreach (var dialect in Dialects)
            {
                yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"{index}-{dialect}"), [(byte)dialect], content);
            }

            index++;
        }
    }

    /// <summary>Gets the files of the unit tests and real-world files.</summary>
    private static IEnumerable<string> GetFiles()
    {
        var source = FuzzResources.ReadText(typeof(CodeOwnersFuzzTargets).Assembly, "CodeOwnersParserTests.cs");
        foreach (Match match in ConcatenationRegex().Matches(source))
        {
            yield return string.Concat(StringLiteralRegex().Matches(match.Groups["parts"].Value).Select(literal => Regex.Unescape(literal.Groups["value"].Value)));
        }

        foreach (Match match in ArgumentRegex().Matches(source))
        {
            yield return Regex.Unescape(match.Groups["value"].Value);
        }

        foreach (var file in RealWorldFiles)
        {
            yield return file;
        }
    }

    [GeneratedRegex(@"Content = (?<parts>(?:""(?:[^""\\]|\\.)*""\s*\+\s*)*""(?:[^""\\]|\\.)*"");", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex ConcatenationRegex();

    [GeneratedRegex(@"""(?<value>(?:[^""\\]|\\.)*)""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex StringLiteralRegex();

    [GeneratedRegex(@"(?:Parse|InlineData)\(""(?<value>(?:[^""\\]|\\.)*)""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex ArgumentRegex();
}
