namespace Meziantou.Framework.CodeOwners.Tests;

public sealed class CodeOwnersParserTests
{
    private static CodeOwner User(string name) => CodeOwner.Username(name);

    private static CodeOwner Email(string address) => CodeOwner.EmailAddress(address);

    private static CodeOwner Role(string name) => CodeOwner.Role(name);

    private static CodeOwnersEntry Entry(string pattern, params CodeOwner[] owners) => new(pattern, owners, section: null);

    private static CodeOwnersEntry Entry(string pattern, CodeOwnersSection section, params CodeOwner[] owners) => new(pattern, owners, section);

    [Fact]
    public void ParseEmptyCodeOwners()
    {
        var actual = CodeOwnersFile.Parse("", CodeOwnersDialect.GitLab).Entries;
        Assert.Empty(actual);
    }

    [Fact]
    public void ParseSingleLineCodeOwners()
    {
        var actual = CodeOwnersFile.Parse("* @user1 @user2", CodeOwnersDialect.GitLab).Entries;

        var expected = new[] { Entry("*", User("user1"), User("user2")) };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseSingleLineCodeOwnersWithEscapedPatternCharacters()
    {
        var actual = CodeOwnersFile.Parse("foo\\ bar\\@baz @user1", CodeOwnersDialect.GitLab).Entries;

        var expected = new[] { Entry("foo bar@baz", User("user1")) };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseSingleLineCodeOwnersWithSection()
    {
        var actual = CodeOwnersFile.Parse("[Test]\n* @user1 @user2", CodeOwnersDialect.GitLab).Entries;

        var expected = new[] { Entry("*", new CodeOwnersSection("Test"), User("user1"), User("user2")) };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseCodeOwners()
    {
        const string Content = "\n" +
                               "# This is a comment.\n" +
                               "# Each line is a file pattern followed by one or more owners.\n" +
                               "\n" +
                               "# These owners will be the default owners for everything in\n" +
                               "# the repo. Unless a later match takes precedence,\n" +
                               "# @global-owner1 and @global-owner2 will be requested for\n" +
                               "# review when someone opens a pull request.\n" +
                               "*       @global-owner1 @global-owner2\n" +
                               "\n" +
                               "# Order is important; the last matching pattern takes the most\n" +
                               "# precedence. When someone opens a pull request that only\n" +
                               "# modifies JS files, only @js-owner and not the global\n" +
                               "# owner(s) will be requested for a review.\n" +
                               "*.js    @js-owner #This is an inline comment.\n" +
                               "\n" +
                               "# You can also use email addresses if you prefer. They'll be\n" +
                               "# used to look up users just like we do for commit author\n" +
                               "# emails.\n" +
                               "*.go docs@example.com\n" +
                               "\n" +
                               "# In this example, @doctocat owns any files in the build/logs\n" +
                               "# directory at the root of the repository and any of its\n" +
                               "# subdirectories.\n" +
                               "/build/logs/ @doctocat\n" +
                               "\n" +
                               "# The `docs/*` pattern will match files like\n" +
                               "# `docs/getting-started.md` but not further nested files like\n" +
                               "# `docs/build-app/troubleshooting.md`.\n" +
                               "docs/*  docs@example.com\n" +
                               "\n" +
                               "# In this example, @octocat owns any file in an apps directory\n" +
                               "# anywhere in your repository.\n" +
                               "apps/ @octocat\n" +
                               "\n" +
                               "# In this example, @doctocat owns any file in the `/docs`\n" +
                               "# directory in the root of your repository.\n" +
                               "/docs/ @doctocat\n" +
                               "\n" +
                               "# In this example, @octocat owns any file in the `/apps`\n" +
                               "# directory in the root of your repository except for the `/apps/github`\n" +
                               "# subdirectory, as its owners are left empty.\n" +
                               "/apps/ @octocat\n" +
                               "/apps/github";

        var actual = CodeOwnersFile.Parse(Content, CodeOwnersDialect.GitLab).Entries;

        var expected = new[]
        {
            Entry("*", User("global-owner1"), User("global-owner2")),
            Entry("*.js", User("js-owner")),
            Entry("*.go", Email("docs@example.com")),
            Entry("/build/logs/", User("doctocat")),
            Entry("docs/*", Email("docs@example.com")),
            Entry("apps/", User("octocat")),
            Entry("/docs/", User("doctocat")),
            Entry("/apps/", User("octocat")),
            Entry("/apps/github"),
        };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseLineEndingWithSpaces()
    {
        var actual = CodeOwnersFile.Parse("* @user1 @user2  ", CodeOwnersDialect.GitLab).Entries;

        var expected = new[] { Entry("*", User("user1"), User("user2")) };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseTwice()
    {
        const string Content = "* @user1 @user2  ";
        var parse1 = CodeOwnersFile.Parse(Content, CodeOwnersDialect.GitLab).Entries;
        var parse2 = CodeOwnersFile.Parse(Content, CodeOwnersDialect.GitLab).Entries;
        Assert.Equal(parse2, parse1);
    }

    [Fact]
    public void ParseCodeOwnersWithSections()
    {
        const string Content = "\n" +
                               "doc/ @user4 \n" +
                               "[Section]\n" +
                               "* @user1 @user2\n" +
                               "\n" +
                               "^[Optional Section]\n" +
                               "*.js @user2 @user3\n";

        var actual = CodeOwnersFile.Parse(Content, CodeOwnersDialect.GitLab).Entries;

        var expected = new[]
        {
            Entry("doc/", User("user4")),
            Entry("*", new CodeOwnersSection("Section"), User("user1"), User("user2")),
            Entry("*.js", new CodeOwnersSection("Optional Section", isOptional: true), User("user2"), User("user3")),
        };
        Assert.Equal(expected, actual);
        Assert.True(actual[2].IsOptional);
    }

    [Fact]
    public void ParseCodeOwnersWithPatternsWithoutOwners()
    {
        const string Content = "* @user1\n" +
                               "*.txt \n" +
                               "*.js\n" +
                               "doc/ @user2\n" +
                               "*.md #Inline comment\n" +
                               "app/\n" +
                               " ";

        var actual = CodeOwnersFile.Parse(Content, CodeOwnersDialect.GitLab).Entries;

        var expected = new[]
        {
            Entry("*", User("user1")),
            Entry("*.txt"),
            Entry("*.js"),
            Entry("doc/", User("user2")),
            Entry("*.md"),
            Entry("app/"),
        };
        Assert.Equal(expected, actual);
        Assert.Empty(actual[1].Owners);
    }

    [Fact]
    public void ParseCodeOwnersWithRequiredReviewerCount()
    {
        var actual = CodeOwnersFile.Parse("[Test][2]\n* @user1 @user2", CodeOwnersDialect.GitLab).Entries;

        var expected = new[] { Entry("*", new CodeOwnersSection("Test", requiredReviewerCount: 2), User("user1"), User("user2")) };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseCodeOwnersWithDefaultOwners()
    {
        var actual = CodeOwnersFile.Parse("[Test] @defaultOwner default.owner@example.com\n*", CodeOwnersDialect.GitLab).Entries;

        var section = new CodeOwnersSection("Test", requiredReviewerCount: 1, defaultOwners: [User("defaultOwner"), Email("default.owner@example.com")]);
        var expected = new[] { Entry("*", section, User("defaultOwner"), Email("default.owner@example.com")) };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseCodeOwnersWithDefaultOwnersOverriden()
    {
        var actual = CodeOwnersFile.Parse("[Test] @defaultOwner default.owner@example.com\n* @user1 @user2", CodeOwnersDialect.GitLab).Entries;

        var section = new CodeOwnersSection("Test", requiredReviewerCount: 1, defaultOwners: [User("defaultOwner"), Email("default.owner@example.com")]);
        var expected = new[] { Entry("*", section, User("user1"), User("user2")) };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseCodeOwnersWithRequiredReviewerCountAndDefaultOwners()
    {
        var actual = CodeOwnersFile.Parse("[Test][2] @defaultOwner default.owner@example.com\n*", CodeOwnersDialect.GitLab).Entries;

        var section = new CodeOwnersSection("Test", requiredReviewerCount: 2, defaultOwners: [User("defaultOwner"), Email("default.owner@example.com")]);
        var expected = new[] { Entry("*", section, User("defaultOwner"), Email("default.owner@example.com")) };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void OptionalSectionKeepsItsRequiredReviewerCount()
    {
        var actual = CodeOwnersFile.Parse("^[Test][2]\n* @user", CodeOwnersDialect.GitLab).Entries;

        var expected = new[] { Entry("*", new CodeOwnersSection("Test", isOptional: true, requiredReviewerCount: 2), User("user")) };
        Assert.Equal(expected, actual);

        var section = actual[0].Section!;
        Assert.True(section.IsOptional);
        Assert.False(section.IsMandatory);
        Assert.Equal(2, section.RequiredReviewerCount);
        Assert.True(actual[0].IsOptional);
        Assert.Equal("^[Test][2]", section.ToString());
    }

    [Fact]
    public void OptionalAndMandatorySectionsWithTheSameCountAreNotEqual()
    {
        var optional = CodeOwnersFile.Parse("^[Test][2]\n*", CodeOwnersDialect.GitLab).Entries[0].Section;
        var mandatory = CodeOwnersFile.Parse("[Test][2]\n*", CodeOwnersDialect.GitLab).Entries[0].Section;

        Assert.NotEqual(optional, mandatory);
    }

    [Fact]
    public void DefaultParseErrorReportsNoError()
    {
        Assert.Equal(CodeOwnersParseErrorKind.None, default(CodeOwnersParseError).Kind);
        Assert.Equal("no error", default(CodeOwnersParseError).ToString());

        Assert.True(CodeOwnersFile.TryParse("* @user1", CodeOwnersDialect.GitLab, out _, out var error));
        Assert.Equal(CodeOwnersParseErrorKind.None, error.Kind);
        Assert.Equal(default, error);
    }

    [Fact]
    public void SectionHeadingEdgeCase_ExtraSpaceBeforeRequiredReviewerCountShouldDiscardRestOfLine()
    {
        const string Content = "[Test1] [2]\n" +
                               "* @user\n" +
                               "\n" +
                               "[Test2] [2] @defaultOwner1\n" +
                               "*\n" +
                               "[Test3] @defaultOwner2 [2] @defaultOwner3\n" +
                               "*\n";

        var actual = CodeOwnersFile.Parse(Content, CodeOwnersDialect.GitLab).Entries;

        var expected = new[]
        {
            Entry("*", new CodeOwnersSection("Test1", requiredReviewerCount: 1), User("user")),
            Entry("*", new CodeOwnersSection("Test2", requiredReviewerCount: 1)),
            Entry("*", new CodeOwnersSection("Test3", requiredReviewerCount: 1, defaultOwners: [User("defaultOwner2")]), User("defaultOwner2")),
        };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SectionHeadingEdgeCase_HashTagBeforeRequiredRewiewerCountShouldDiscardRestOfLine()
    {
        const string Content = "[Test1]#[2]\n" +
                               "* @user1\n" +
                               "[Test2] # [2]\n" +
                               "* @user2\n" +
                               "[Test3]#[2] @defaultOwner1\n" +
                               "*\n" +
                               "[Test4] # [2] @defaultOwner2\n" +
                               "*\n";

        var actual = CodeOwnersFile.Parse(Content, CodeOwnersDialect.GitLab).Entries;

        var expected = new[]
        {
            Entry("*", new CodeOwnersSection("Test1", requiredReviewerCount: 1), User("user1")),
            Entry("*", new CodeOwnersSection("Test2", requiredReviewerCount: 1), User("user2")),
            Entry("*", new CodeOwnersSection("Test3", requiredReviewerCount: 1)),
            Entry("*", new CodeOwnersSection("Test4", requiredReviewerCount: 1)),
        };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SectionHeadingEdgeCase_DefaultOwnersShouldBePrecededBySpace()
    {
        const string Content = "[Test1]@defaultOwner1\n" +
                               "*\n" +
                               "[Test2][2]@defaultOwner2\n" +
                               "*\n";

        var actual = CodeOwnersFile.Parse(Content, CodeOwnersDialect.GitLab).Entries;

        var expected = new[]
        {
            Entry("*", new CodeOwnersSection("Test1", requiredReviewerCount: 1)),
            Entry("*", new CodeOwnersSection("Test2", requiredReviewerCount: 2)),
        };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SectionHeadingEdgeCase_IgnoreExtraSpacesBetweenDefaultOwners()
    {
        const string Content = "[Test1]  @defaultOwner1    @defaultOwner2\n" +
                               "*\n" +
                               "[Test2][2]  @defaultOwner3    @defaultOwner4\n" +
                               "*\n" +
                               "[Test3] \t @defaultOwner5  \t\t  @defaultOwner6\n" +
                               "*\n" +
                               "[Test4][2] \t @defaultOwner7  \t\t  @defaultOwner8\n" +
                               "*\n";

        var actual = CodeOwnersFile.Parse(Content, CodeOwnersDialect.GitLab).Entries;

        var expected = new[]
        {
            Entry("*", new CodeOwnersSection("Test1", requiredReviewerCount: 1, defaultOwners: [User("defaultOwner1"), User("defaultOwner2")]), User("defaultOwner1"), User("defaultOwner2")),
            Entry("*", new CodeOwnersSection("Test2", requiredReviewerCount: 2, defaultOwners: [User("defaultOwner3"), User("defaultOwner4")]), User("defaultOwner3"), User("defaultOwner4")),
            Entry("*", new CodeOwnersSection("Test3", requiredReviewerCount: 1, defaultOwners: [User("defaultOwner5"), User("defaultOwner6")]), User("defaultOwner5"), User("defaultOwner6")),
            Entry("*", new CodeOwnersSection("Test4", requiredReviewerCount: 2, defaultOwners: [User("defaultOwner7"), User("defaultOwner8")]), User("defaultOwner7"), User("defaultOwner8")),
        };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseSectionWithCRLFLineEndings()
    {
        const string Content = "\r\n" +
                               "[Test1]\r\n" +
                               "* @user1\r\n" +
                               "\r\n" +
                               "[Test2][2]\r\n" +
                               "* @user2\r\n" +
                               "\r\n" +
                               "[Test3] @defaultOwner\r\n" +
                               "* @user3\r\n" +
                               " ";

        var actual = CodeOwnersFile.Parse(Content, CodeOwnersDialect.GitLab).Entries;

        var expected = new[]
        {
            Entry("*", new CodeOwnersSection("Test1"), User("user1")),
            Entry("*", new CodeOwnersSection("Test2", requiredReviewerCount: 2), User("user2")),
            Entry("*", new CodeOwnersSection("Test3", requiredReviewerCount: 1, defaultOwners: [User("defaultOwner")]), User("user3")),
        };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TryParseValidFile()
    {
        Assert.True(CodeOwnersFile.TryParse("[Test][2] @defaultOwner\n* @user1 docs@example.com\n", CodeOwnersDialect.GitLab, out var file, out var error));
        Assert.HasCount(1, file.Entries);
        Assert.HasCount(2, file.Entries[0].Owners);
        Assert.Equal(default, error);
    }

    [Fact]
    public void ErrorAndExceptionMessageDescribeTheProblem()
    {
        Assert.False(CodeOwnersFile.TryParse("* @user1\n[Test][0]\n", CodeOwnersDialect.GitLab, out _, out var error));
        Assert.Equal("line 2, position 7: the required reviewer count is not a positive integer", error.ToString());

        var exception = Assert.Throws<CodeOwnersParseException>(() => CodeOwnersFile.Parse("* @user1\n[Test][0]\n", CodeOwnersDialect.GitLab));
        Assert.Equal("The CODEOWNERS file is invalid at line 2, position 7: the required reviewer count is not a positive integer", exception.Message);
    }

    [Theory]
    [InlineData("[Backend\n*.cs @user1", CodeOwnersParseErrorKind.UnterminatedSectionHeader, 1, 1)]
    [InlineData("* @user1\n[Backend", CodeOwnersParseErrorKind.UnterminatedSectionHeader, 2, 1)]
    [InlineData("[Test][0]\n* @user1", CodeOwnersParseErrorKind.InvalidRequiredReviewerCount, 1, 7)]
    [InlineData("[Test][-3]\n* @user1", CodeOwnersParseErrorKind.InvalidRequiredReviewerCount, 1, 7)]
    [InlineData("[Test][abc]\n* @user1", CodeOwnersParseErrorKind.InvalidRequiredReviewerCount, 1, 7)]
    [InlineData("[Test][2\n* @user1", CodeOwnersParseErrorKind.UnterminatedRequiredReviewerCount, 1, 7)]
    [InlineData("* @ @user1", CodeOwnersParseErrorKind.EmptyOwner, 1, 3)]
    [InlineData("* @", CodeOwnersParseErrorKind.EmptyOwner, 1, 3)]
    [InlineData("[Test] @\n*", CodeOwnersParseErrorKind.EmptyOwner, 1, 8)]
    [InlineData("* justsometext", CodeOwnersParseErrorKind.InvalidOwner, 1, 3)]
    [InlineData("* missing.local.part@", CodeOwnersParseErrorKind.InvalidOwner, 1, 3)]
    [InlineData("[Test] justsometext\n*", CodeOwnersParseErrorKind.InvalidOwner, 1, 8)]
    public void ParseInvalidFileThrows(string content, CodeOwnersParseErrorKind kind, int lineNumber, int linePosition)
    {
        var expected = new CodeOwnersParseError(kind, lineNumber, linePosition);

        var exception = Assert.Throws<CodeOwnersParseException>(() => CodeOwnersFile.Parse(content, CodeOwnersDialect.GitLab));
        Assert.Equal(expected, exception.Error);

        Assert.False(CodeOwnersFile.TryParse(content, CodeOwnersDialect.GitLab, out var file, out var error));
        Assert.Null(file);
        Assert.Equal(expected, error);

        Assert.False(CodeOwnersFile.TryParse(content, CodeOwnersDialect.GitLab, out file));
        Assert.Null(file);
    }

    [Fact]
    public void ParseThrowsOnTheFirstError()
    {
        const string Content = "* @user1\n" +
                               "[Test][0]\n" +
                               "*.js @\n" +
                               "\n" +
                               "*.go justsometext";

        var exception = Assert.Throws<CodeOwnersParseException>(() => CodeOwnersFile.Parse(Content, CodeOwnersDialect.GitLab));

        Assert.Equal(new CodeOwnersParseError(CodeOwnersParseErrorKind.InvalidRequiredReviewerCount, 2, 7), exception.Error);
    }

    [Fact]
    public void ParseReportsTheLineNumberWithCarriageReturnLineEndings()
    {
        var exception = Assert.Throws<CodeOwnersParseException>(() => CodeOwnersFile.Parse("* @user1\r*.js @\r", CodeOwnersDialect.GitLab));

        Assert.Equal(new CodeOwnersParseError(CodeOwnersParseErrorKind.EmptyOwner, 2, 6), exception.Error);
    }

    [Fact]
    public void ParseCaretNotFollowedBySectionIsAPattern()
    {
        var actual = CodeOwnersFile.Parse("^file.txt @user1", CodeOwnersDialect.GitLab).Entries;

        var expected = new[] { Entry("^file.txt", User("user1")) };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseCaretFollowedBySpaceIsAPattern()
    {
        var actual = CodeOwnersFile.Parse("^ @user1", CodeOwnersDialect.GitLab).Entries;

        var expected = new[] { Entry("^", User("user1")) };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseDefaultOwnersSeparatedFromSectionNameByATab()
    {
        var actual = CodeOwnersFile.Parse("[Test]\t@defaultOwner\n*", CodeOwnersDialect.GitLab).Entries;

        var expected = new[] { Entry("*", new CodeOwnersSection("Test", requiredReviewerCount: 1, defaultOwners: [User("defaultOwner")]), User("defaultOwner")) };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseCommentAndPatternsWithCarriageReturnLineEndings()
    {
        const string Content = "# This is a comment.\r" +
                               "*.js @user1\r" +
                               "*.go @user2";

        var actual = CodeOwnersFile.Parse(Content, CodeOwnersDialect.GitLab).Entries;

        var expected = new[]
        {
            Entry("*.js", User("user1")),
            Entry("*.go", User("user2")),
        };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseSectionWithDefaultOwnersAndCarriageReturnLineEndings()
    {
        const string Content = "[Test] @defaultOwner1\r" +
                               "[Other] @defaultOwner2\r" +
                               "*";

        var actual = CodeOwnersFile.Parse(Content, CodeOwnersDialect.GitLab).Entries;

        var expected = new[] { Entry("*", new CodeOwnersSection("Other", requiredReviewerCount: 1, defaultOwners: [User("defaultOwner2")]), User("defaultOwner2")) };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void EntriesFromDifferentLinesAreNotEqual()
    {
        var actual = CodeOwnersFile.Parse("* @user1\n*.js @user1", CodeOwnersDialect.GitLab).Entries;

        Assert.HasCount(2, actual);
        Assert.NotEqual(actual[0], actual[1]);
    }

    [Fact]
    public void IdenticalLinesProduceEqualEntries()
    {
        var actual = CodeOwnersFile.Parse("* @user1\n* @user1", CodeOwnersDialect.GitLab).Entries;

        Assert.HasCount(2, actual);
        Assert.Equal(actual[0], actual[1]);
    }

    [Fact]
    public void LastMatchingEntryOwnsThePath()
    {
        var actual = CodeOwnersFile.Parse("* @global1 @global2\n*.js @js-owner1 @js-owner2", CodeOwnersDialect.GitLab).Entries;

        var winner = actual.Last(entry => entry.Pattern is "*.js");
        Assert.Equal([User("js-owner1"), User("js-owner2")], winner.Owners);
    }

    [Fact]
    public void OwnerToStringRoundTrips()
    {
        var actual = CodeOwnersFile.Parse("[Test][2] @defaultOwner\n*.js @user1 docs@example.com", CodeOwnersDialect.GitLab).Entries;

        Assert.Equal("@user1", actual[0].Owners[0].ToString());
        Assert.Equal("docs@example.com", actual[0].Owners[1].ToString());
        Assert.Equal("*.js @user1 docs@example.com", actual[0].ToString());
        Assert.Equal("[Test][2] @defaultOwner", actual[0].Section?.ToString());
    }

    [Fact]
    public void ObsoleteCodeOwnersParserForwardsToCodeOwnersFile()
    {
        const string Content = "* @user1 docs@example.com";
#pragma warning disable CS0618 // Type or member is obsolete
        var parsed = CodeOwnersParser.Parse(Content);
        var invalid = Assert.Throws<CodeOwnersParseException>(() => CodeOwnersParser.Parse("[Test][0]"));
#pragma warning restore CS0618

        Assert.Equal(CodeOwnersFile.Parse(Content, CodeOwnersDialect.GitLab).Entries, parsed.Entries);
        Assert.Equal(new CodeOwnersParseError(CodeOwnersParseErrorKind.InvalidRequiredReviewerCount, 1, 7), invalid.Error);
    }

    [Fact]
    public void GitHubDialectParsesACharacterClassAsAPattern()
    {
        // '[abc].txt' is a valid gitignore-style pattern. GitLab reads the same line as a section header.
        var github = CodeOwnersFile.Parse("[abc].txt @user1", CodeOwnersDialect.GitHub).Entries;
        Assert.Equal([Entry("[abc].txt", User("user1"))], github);

        var gitlab = CodeOwnersFile.Parse("[abc].txt @user1", CodeOwnersDialect.GitLab).Entries;
        Assert.Empty(gitlab);
    }

    [Fact]
    public void GitHubDialectHasNoSections()
    {
        const string Content = "[Backend]\n" +
                               "* @user1\n";

        var github = CodeOwnersFile.Parse(Content, CodeOwnersDialect.GitHub).Entries;
        Assert.Equal([Entry("[Backend]"), Entry("*", User("user1"))], github);
        Assert.All(github, entry => Assert.Null(entry.Section));

        var gitlab = CodeOwnersFile.Parse(Content, CodeOwnersDialect.GitLab).Entries;
        Assert.Equal([Entry("*", new CodeOwnersSection("Backend"), User("user1"))], gitlab);
    }

    [Fact]
    public void GitHubDialectTreatsTheOptionalSectionMarkerAsAPatternCharacter()
    {
        const string Content = "^[Test][2]\n" +
                               "* @user1\n";

        var github = CodeOwnersFile.Parse(Content, CodeOwnersDialect.GitHub).Entries;
        Assert.Equal([Entry("^[Test][2]"), Entry("*", User("user1"))], github);

        var gitlab = CodeOwnersFile.Parse(Content, CodeOwnersDialect.GitLab).Entries;
        Assert.True(gitlab[0].IsOptional);
    }

    [Theory]
    [InlineData("[Backend\n*.cs @user1")]
    [InlineData("[Test][0]\n* @user1")]
    [InlineData("[Test][2\n* @user1")]
    public void GitHubDialectDoesNotReportSectionErrors(string content)
    {
        // These are only errors because GitLab reads the line as a section header.
        Assert.True(CodeOwnersFile.TryParse(content, CodeOwnersDialect.GitHub, out var github));
        Assert.HasCount(2, github.Entries);

        Assert.False(CodeOwnersFile.TryParse(content, CodeOwnersDialect.GitLab, out _));
    }

    [Fact]
    public void BothDialectsAgreeOnAFileWithoutSections()
    {
        const string Content = "* @user1 @user2\n" +
                               "*.js @js-owner #comment\n" +
                               "docs/* docs@example.com\n" +
                               "/apps/github\n";

        var github = CodeOwnersFile.Parse(Content, CodeOwnersDialect.GitHub).Entries;
        var gitlab = CodeOwnersFile.Parse(Content, CodeOwnersDialect.GitLab).Entries;

        Assert.Equal(github, gitlab);
    }

    [Theory]
    [InlineData("developer")]
    [InlineData("developers")]
    [InlineData("maintainer")]
    [InlineData("maintainers")]
    [InlineData("owner")]
    [InlineData("owners")]
    [InlineData("Maintainer")]
    public void GitLabDialectParsesRoles(string role)
    {
        var actual = CodeOwnersFile.Parse($"*.md @@{role}", CodeOwnersDialect.GitLab).Entries;

        Assert.Equal([Entry("*.md", Role(role))], actual);
        Assert.Equal(CodeOwnerType.Role, actual[0].Owners[0].Type);
        Assert.Equal($"@@{role}", actual[0].Owners[0].ToString());
    }

    [Fact]
    public void GitLabDialectParsesRolesAmongOtherOwners()
    {
        var actual = CodeOwnersFile.Parse("[Test] @@maintainer\n*.md @user1 @@developer docs@example.com", CodeOwnersDialect.GitLab).Entries;

        var section = new CodeOwnersSection("Test", defaultOwners: [Role("maintainer")]);
        Assert.Equal([Entry("*.md", section, User("user1"), Role("developer"), Email("docs@example.com"))], actual);
    }

    [Fact]
    public void GitLabDialectUsesRolesAsSectionDefaultOwners()
    {
        var actual = CodeOwnersFile.Parse("[Test] @@owners\n*.md", CodeOwnersDialect.GitLab).Entries;

        var section = new CodeOwnersSection("Test", defaultOwners: [Role("owners")]);
        Assert.Equal([Entry("*.md", section, Role("owners"))], actual);
        Assert.Equal("[Test] @@owners", section.ToString());
    }

    [Theory]
    // Only Developer, Maintainer and Owner are roles: everything else after '@@' identifies nobody.
    [InlineData("* @@user", CodeOwnersDialect.GitLab)]
    [InlineData("* @@guest", CodeOwnersDialect.GitLab)]
    [InlineData("* @@reporter", CodeOwnersDialect.GitLab)]
    [InlineData("[Test] @@user\n*", CodeOwnersDialect.GitLab)]
    // GitHub has no roles at all
    [InlineData("* @@maintainer", CodeOwnersDialect.GitHub)]
    [InlineData("* @@user", CodeOwnersDialect.GitHub)]
    public void UnknownRolesAreInvalidOwners(string content, CodeOwnersDialect dialect)
    {
        Assert.False(CodeOwnersFile.TryParse(content, dialect, out _, out var error));
        Assert.Equal(CodeOwnersParseErrorKind.InvalidOwner, error.Kind);
    }

    [Theory]
    [InlineData("* @us@er")]
    [InlineData("* @user@example.com")]
    public void AUsernameCannotContainAnAtSign(string content)
    {
        Assert.False(CodeOwnersFile.TryParse(content, CodeOwnersDialect.GitLab, out _, out var error));
        Assert.Equal(CodeOwnersParseErrorKind.InvalidOwner, error.Kind);
    }

    [Fact]
    public void ParsedCollectionsCannotBeMutatedByCallers()
    {
        var file = CodeOwnersFile.Parse("[Test] @owner1\n* @user1\n*.js\n", CodeOwnersDialect.GitLab);

        Assert.IsNotType<List<CodeOwnersEntry>>(file.Entries);
        Assert.IsNotType<List<CodeOwner>>(file.Entries[0].Owners);
        Assert.IsNotType<List<CodeOwner>>(file.Entries[0].Section!.DefaultOwners);

        Assert.Throws<NotSupportedException>(() => ((IList<CodeOwnersEntry>)file.Entries).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<CodeOwner>)file.Entries[0].Owners).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<CodeOwner>)file.Entries[0].Section!.DefaultOwners).Clear());
    }
}
