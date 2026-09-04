namespace Meziantou.Framework.CodeOwners.Tests;

public sealed class CodeOwnersParserTests
{
    private static CodeOwnersOwner User(string name) => CodeOwnersOwner.Username(name);

    private static CodeOwnersOwner Email(string address) => CodeOwnersOwner.EmailAddress(address);

    private static CodeOwnersEntry Entry(string pattern, params CodeOwnersOwner[] owners) => new(pattern, owners, section: null);

    private static CodeOwnersEntry Entry(string pattern, CodeOwnersSection section, params CodeOwnersOwner[] owners) => new(pattern, owners, section);

    [Fact]
    public void ParseEmptyCodeOwners()
    {
        var actual = CodeOwnersParser.Parse("");
        Assert.Empty(actual);
    }

    [Fact]
    public void ParseSingleLineCodeOwners()
    {
        var actual = CodeOwnersParser.Parse("* @user1 @user2");

        var expected = new[] { Entry("*", User("user1"), User("user2")) };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseSingleLineCodeOwnersWithEscapedPatternCharacters()
    {
        var actual = CodeOwnersParser.Parse("foo\\ bar\\@baz @user1");

        var expected = new[] { Entry("foo bar@baz", User("user1")) };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseSingleLineCodeOwnersWithSection()
    {
        var actual = CodeOwnersParser.Parse("[Test]\n* @user1 @user2");

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

        var actual = CodeOwnersParser.Parse(Content);

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
        var actual = CodeOwnersParser.Parse("* @user1 @user2  ");

        var expected = new[] { Entry("*", User("user1"), User("user2")) };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseTwice()
    {
        const string Content = "* @user1 @user2  ";
        var parse1 = CodeOwnersParser.Parse(Content);
        var parse2 = CodeOwnersParser.Parse(Content);
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

        var actual = CodeOwnersParser.Parse(Content);

        var expected = new[]
        {
            Entry("doc/", User("user4")),
            Entry("*", new CodeOwnersSection("Section"), User("user1"), User("user2")),
            Entry("*.js", new CodeOwnersSection("Optional Section", 0), User("user2"), User("user3")),
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

        var actual = CodeOwnersParser.Parse(Content);

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
        var actual = CodeOwnersParser.Parse("[Test][2]\n* @user1 @user2");

        var expected = new[] { Entry("*", new CodeOwnersSection("Test", 2), User("user1"), User("user2")) };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseCodeOwnersWithDefaultOwners()
    {
        var actual = CodeOwnersParser.Parse("[Test] @defaultOwner default.owner@example.com\n*");

        var section = new CodeOwnersSection("Test", 1, [User("defaultOwner"), Email("default.owner@example.com")]);
        var expected = new[] { Entry("*", section, User("defaultOwner"), Email("default.owner@example.com")) };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseCodeOwnersWithDefaultOwnersOverriden()
    {
        var actual = CodeOwnersParser.Parse("[Test] @defaultOwner default.owner@example.com\n* @user1 @user2");

        var section = new CodeOwnersSection("Test", 1, [User("defaultOwner"), Email("default.owner@example.com")]);
        var expected = new[] { Entry("*", section, User("user1"), User("user2")) };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseCodeOwnersWithRequiredReviewerCountAndDefaultOwners()
    {
        var actual = CodeOwnersParser.Parse("[Test][2] @defaultOwner default.owner@example.com\n*");

        var section = new CodeOwnersSection("Test", 2, [User("defaultOwner"), Email("default.owner@example.com")]);
        var expected = new[] { Entry("*", section, User("defaultOwner"), Email("default.owner@example.com")) };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SectionHeadingEdgeCase_OptionalOverridesRequiredReviewerCount()
    {
        var actual = CodeOwnersParser.Parse("^[Test][2]\n* @user");

        var expected = new[] { Entry("*", new CodeOwnersSection("Test", 0), User("user")) };
        Assert.Equal(expected, actual);
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

        var actual = CodeOwnersParser.Parse(Content);

        var expected = new[]
        {
            Entry("*", new CodeOwnersSection("Test1", 1), User("user")),
            Entry("*", new CodeOwnersSection("Test2", 1)),
            Entry("*", new CodeOwnersSection("Test3", 1, [User("defaultOwner2")]), User("defaultOwner2")),
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

        var actual = CodeOwnersParser.Parse(Content);

        var expected = new[]
        {
            Entry("*", new CodeOwnersSection("Test1", 1), User("user1")),
            Entry("*", new CodeOwnersSection("Test2", 1), User("user2")),
            Entry("*", new CodeOwnersSection("Test3", 1)),
            Entry("*", new CodeOwnersSection("Test4", 1)),
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

        var actual = CodeOwnersParser.Parse(Content);

        var expected = new[]
        {
            Entry("*", new CodeOwnersSection("Test1", 1)),
            Entry("*", new CodeOwnersSection("Test2", 2)),
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

        var actual = CodeOwnersParser.Parse(Content);

        var expected = new[]
        {
            Entry("*", new CodeOwnersSection("Test1", 1, [User("defaultOwner1"), User("defaultOwner2")]), User("defaultOwner1"), User("defaultOwner2")),
            Entry("*", new CodeOwnersSection("Test2", 2, [User("defaultOwner3"), User("defaultOwner4")]), User("defaultOwner3"), User("defaultOwner4")),
            Entry("*", new CodeOwnersSection("Test3", 1, [User("defaultOwner5"), User("defaultOwner6")]), User("defaultOwner5"), User("defaultOwner6")),
            Entry("*", new CodeOwnersSection("Test4", 2, [User("defaultOwner7"), User("defaultOwner8")]), User("defaultOwner7"), User("defaultOwner8")),
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

        var actual = CodeOwnersParser.Parse(Content);

        var expected = new[]
        {
            Entry("*", new CodeOwnersSection("Test1"), User("user1")),
            Entry("*", new CodeOwnersSection("Test2", 2), User("user2")),
            Entry("*", new CodeOwnersSection("Test3", 1, [User("defaultOwner")]), User("user3")),
        };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TryParseValidFile()
    {
        Assert.True(CodeOwnersParser.TryParse("[Test][2] @defaultOwner\n* @user1 docs@example.com\n", out var entries));
        Assert.HasCount(1, entries);
        Assert.HasCount(2, entries[0].Owners);
    }

    [Theory]
    [InlineData("[Backend\n*.cs @user1", CodeOwnersErrorKind.UnterminatedSectionHeader, 1, 1)]
    [InlineData("* @user1\n[Backend", CodeOwnersErrorKind.UnterminatedSectionHeader, 2, 1)]
    [InlineData("[Test][0]\n* @user1", CodeOwnersErrorKind.InvalidRequiredReviewerCount, 1, 7)]
    [InlineData("[Test][-3]\n* @user1", CodeOwnersErrorKind.InvalidRequiredReviewerCount, 1, 7)]
    [InlineData("[Test][abc]\n* @user1", CodeOwnersErrorKind.InvalidRequiredReviewerCount, 1, 7)]
    [InlineData("[Test][2\n* @user1", CodeOwnersErrorKind.UnterminatedRequiredReviewerCount, 1, 7)]
    [InlineData("* @ @user1", CodeOwnersErrorKind.EmptyOwner, 1, 3)]
    [InlineData("* @", CodeOwnersErrorKind.EmptyOwner, 1, 3)]
    [InlineData("[Test] @\n*", CodeOwnersErrorKind.EmptyOwner, 1, 8)]
    [InlineData("* justsometext", CodeOwnersErrorKind.InvalidOwner, 1, 3)]
    [InlineData("* missing.local.part@", CodeOwnersErrorKind.InvalidOwner, 1, 3)]
    [InlineData("[Test] justsometext\n*", CodeOwnersErrorKind.InvalidOwner, 1, 8)]
    public void ParseInvalidFileThrows(string content, CodeOwnersErrorKind kind, int lineNumber, int linePosition)
    {
        var exception = Assert.Throws<CodeOwnersParseException>(() => CodeOwnersParser.Parse(content));

        Assert.Equal(kind, exception.Kind);
        Assert.Equal(lineNumber, exception.LineNumber);
        Assert.Equal(linePosition, exception.LinePosition);
        Assert.False(CodeOwnersParser.TryParse(content, out var entries));
        Assert.Null(entries);
    }

    [Fact]
    public void ParseThrowsOnTheFirstError()
    {
        const string Content = "* @user1\n" +
                               "[Test][0]\n" +
                               "*.js @\n" +
                               "\n" +
                               "*.go justsometext";

        var exception = Assert.Throws<CodeOwnersParseException>(() => CodeOwnersParser.Parse(Content));

        Assert.Equal(CodeOwnersErrorKind.InvalidRequiredReviewerCount, exception.Kind);
        Assert.Equal(2, exception.LineNumber);
        Assert.Equal(7, exception.LinePosition);
    }

    [Fact]
    public void ParseReportsTheLineNumberWithCarriageReturnLineEndings()
    {
        var exception = Assert.Throws<CodeOwnersParseException>(() => CodeOwnersParser.Parse("* @user1\r*.js @\r"));

        Assert.Equal(CodeOwnersErrorKind.EmptyOwner, exception.Kind);
        Assert.Equal(2, exception.LineNumber);
        Assert.Equal(6, exception.LinePosition);
    }

    [Fact]
    public void ParseCaretNotFollowedBySectionIsAPattern()
    {
        var actual = CodeOwnersParser.Parse("^file.txt @user1");

        var expected = new[] { Entry("^file.txt", User("user1")) };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseCaretFollowedBySpaceIsAPattern()
    {
        var actual = CodeOwnersParser.Parse("^ @user1");

        var expected = new[] { Entry("^", User("user1")) };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseDefaultOwnersSeparatedFromSectionNameByATab()
    {
        var actual = CodeOwnersParser.Parse("[Test]\t@defaultOwner\n*");

        var expected = new[] { Entry("*", new CodeOwnersSection("Test", 1, [User("defaultOwner")]), User("defaultOwner")) };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseCommentAndPatternsWithCarriageReturnLineEndings()
    {
        const string Content = "# This is a comment.\r" +
                               "*.js @user1\r" +
                               "*.go @user2";

        var actual = CodeOwnersParser.Parse(Content);

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

        var actual = CodeOwnersParser.Parse(Content);

        var expected = new[] { Entry("*", new CodeOwnersSection("Other", 1, [User("defaultOwner2")]), User("defaultOwner2")) };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void EntriesFromDifferentLinesAreNotEqual()
    {
        var actual = CodeOwnersParser.Parse("* @user1\n*.js @user1");

        Assert.HasCount(2, actual);
        Assert.NotEqual(actual[0], actual[1]);
    }

    [Fact]
    public void IdenticalLinesProduceEqualEntries()
    {
        var actual = CodeOwnersParser.Parse("* @user1\n* @user1");

        Assert.HasCount(2, actual);
        Assert.Equal(actual[0], actual[1]);
    }

    [Fact]
    public void LastMatchingEntryOwnsThePath()
    {
        var actual = CodeOwnersParser.Parse("* @global1 @global2\n*.js @js-owner1 @js-owner2");

        var winner = actual.Last(entry => entry.Pattern is "*.js");
        Assert.Equal([User("js-owner1"), User("js-owner2")], winner.Owners);
    }

    [Fact]
    public void OwnerToStringRoundTrips()
    {
        var actual = CodeOwnersParser.Parse("[Test][2] @defaultOwner\n*.js @user1 docs@example.com");

        Assert.Equal("@user1", actual[0].Owners[0].ToString());
        Assert.Equal("docs@example.com", actual[0].Owners[1].ToString());
        Assert.Equal("*.js @user1 docs@example.com", actual[0].ToString());
        Assert.Equal("[Test][2] @defaultOwner", actual[0].Section?.ToString());
    }
}
