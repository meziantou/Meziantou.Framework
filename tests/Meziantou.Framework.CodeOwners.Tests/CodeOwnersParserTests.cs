using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.CodeOwners.Tests;

public sealed class CodeOwnersParserTests
{
    [Fact]
    public void ParseEmptyCodeOwners()
    {
        var actual = CodeOwnersParser.Parse("").ToArray();
        actual.Should().BeEmpty();
    }

    [Fact]
    public void ParseSingleLineCodeOwners()
    {
        var actual = CodeOwnersParser.Parse("* @user1 @user2").ToArray();

        var expected = new CodeOwnersEntry[]
        {
            CodeOwnersEntry.FromUsername(0, "*", "user1", section: null),
            CodeOwnersEntry.FromUsername(0, "*", "user2", section: null),
        };

        actual.Should().Equal(expected);
    }

    [Fact]
    public void ParseSingleLineCodeOwnersWithSection()
    {
        var actual = CodeOwnersParser.Parse("[Test]\n* @user1 @user2").ToArray();

        var expected = new CodeOwnersEntry[]
        {
            CodeOwnersEntry.FromUsername(0, "*", "user1", section: new CodeOwnersSection("Test")),
            CodeOwnersEntry.FromUsername(0, "*", "user2", section: new CodeOwnersSection("Test")),
        };

        actual.Should().Equal(expected);
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

        var actual = CodeOwnersParser.Parse(Content).ToArray();

        var expected = new CodeOwnersEntry[]
        {
            CodeOwnersEntry.FromUsername(0, "*", "global-owner1", section: null),
            CodeOwnersEntry.FromUsername(0, "*", "global-owner2", section: null),
            CodeOwnersEntry.FromUsername(1, "*.js", "js-owner", section: null),
            CodeOwnersEntry.FromEmailAddress(2, "*.go", "docs@example.com", section: null),
            CodeOwnersEntry.FromUsername(3, "/build/logs/", "doctocat", section: null),
            CodeOwnersEntry.FromEmailAddress(4, "docs/*", "docs@example.com", section: null),
            CodeOwnersEntry.FromUsername(5, "apps/", "octocat", section: null),
            CodeOwnersEntry.FromUsername(6, "/docs/", "doctocat", section: null),
            CodeOwnersEntry.FromUsername(7, "/apps/", "octocat", section: null),
            CodeOwnersEntry.FromNone(8, "/apps/github", section: null),
        };

        actual.Should().Equal(expected);
    }

    [Fact]
    public void ParseLineEndingWithSpaces()
    {
        var actual = CodeOwnersParser.Parse("* @user1 @user2  ").ToArray();

        var expected = new CodeOwnersEntry[]
        {
            CodeOwnersEntry.FromUsername(0, "*", "user1", section: null),
            CodeOwnersEntry.FromUsername(0, "*", "user2", section: null),
        };

        actual.Should().Equal(expected);
    }

    [Fact]
    public void ParseTwice()
    {
        const string Content = "* @user1 @user2  ";
        var parse1 = CodeOwnersParser.Parse(Content).ToArray();
        var parse2 = CodeOwnersParser.Parse(Content).ToArray();
        parse1.Should().Equal(parse2);
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

        var actual = CodeOwnersParser.Parse(Content).ToArray();

        var expected = new CodeOwnersEntry[]
        {
            CodeOwnersEntry.FromUsername(0, "doc/", "user4", section: null),
            CodeOwnersEntry.FromUsername(1, "*", "user1", section: new CodeOwnersSection("Section")),
            CodeOwnersEntry.FromUsername(1, "*", "user2", section: new CodeOwnersSection("Section")),
            CodeOwnersEntry.FromUsername(2, "*.js", "user2", section: new CodeOwnersSection("Optional Section", 0)),
            CodeOwnersEntry.FromUsername(2, "*.js", "user3", section: new CodeOwnersSection("Optional Section", 0)),
        };

        actual.Should().Equal(expected);
    }

    [Fact]
    public void ParseCodeOwnersWithPatternsWithoutMembers()
    {
        const string Content = "* @user1\n" +
                               "*.txt \n" +
                               "*.js\n" +
                               "doc/ @user2\n" +
                               "*.md #Inline comment\n" +
                               "app/\n" +
                               " ";

        var actual = CodeOwnersParser.Parse(Content).ToArray();

        var expected = new CodeOwnersEntry[]
        {
            CodeOwnersEntry.FromUsername(0, "*", "user1", null),
            CodeOwnersEntry.FromNone(1, "*.txt", null),
            CodeOwnersEntry.FromNone(2, "*.js", null),
            CodeOwnersEntry.FromUsername(3, "doc/", "user2", null),
            CodeOwnersEntry.FromNone(4, "*.md", null),
            CodeOwnersEntry.FromNone(5, "app/", null),
        };

        actual.Should().Equal(expected);
    }

    [Fact]
    public void ParseCodeOwnersWithRequiredReviewerCount()
    {
        var actual = CodeOwnersParser.Parse("[Test][2]\n* @user1 @user2").ToArray();

        var expected = new CodeOwnersEntry[]
        {
            CodeOwnersEntry.FromUsername(0, "*", "user1", section: new CodeOwnersSection("Test", 2)),
            CodeOwnersEntry.FromUsername(0, "*", "user2", section: new CodeOwnersSection("Test", 2)),
        };

        actual.Should().Equal(expected);
    }

    [Fact]
    public void ParseCodeOwnersWithDefaultOwners()
    {
        var actual = CodeOwnersParser.Parse("[Test] @defaultOwner default.owner@example.com\n*").ToArray();

        var expected = new CodeOwnersEntry[]
        {
            CodeOwnersEntry.FromUsername(0, "*", "defaultOwner", section: new CodeOwnersSection("Test", 1, ["@defaultOwner", "default.owner@example.com"])),
            CodeOwnersEntry.FromEmailAddress(0, "*", "default.owner@example.com", section: new CodeOwnersSection("Test", 1, ["@defaultOwner", "default.owner@example.com"])),
        };

        actual.Should().Equal(expected);
    }

    [Fact]
    public void ParseCodeOwnersWithDefaultOwnersOverriden()
    {
        var actual = CodeOwnersParser.Parse("[Test] @defaultOwner default.owner@example.com\n* @user1 @user2").ToArray();

        var expected = new CodeOwnersEntry[]
        {
            CodeOwnersEntry.FromUsername(0, "*", "user1", section: new CodeOwnersSection("Test", 1, ["@defaultOwner", "default.owner@example.com"])),
            CodeOwnersEntry.FromUsername(0, "*", "user2", section: new CodeOwnersSection("Test", 1, ["@defaultOwner", "default.owner@example.com"])),
        };

        actual.Should().Equal(expected);
    }

    [Fact]
    public void ParseCodeOwnersWithRequiredReviewerCountAndDefaultOwners()
    {
        var actual = CodeOwnersParser.Parse("[Test][2] @defaultOwner default.owner@example.com\n*").ToArray();

        var expected = new CodeOwnersEntry[]
        {
            CodeOwnersEntry.FromUsername(0, "*", "defaultOwner", section: new CodeOwnersSection("Test", 2, ["@defaultOwner", "default.owner@example.com"])),
            CodeOwnersEntry.FromEmailAddress(0, "*", "default.owner@example.com", section: new CodeOwnersSection("Test", 2, ["@defaultOwner", "default.owner@example.com"])),
        };

        actual.Should().Equal(expected);
    }

    [Fact]
    public void SectionHeadingEdgeCase_OptionalOverridesRequiredReviewerCount()
    {
        var actual = CodeOwnersParser.Parse("^[Test][2]\n* @user").ToArray();

        var expected = new CodeOwnersEntry[]
        {
            CodeOwnersEntry.FromUsername(0, "*", "user", section: new CodeOwnersSection("Test", 0)),
        };

        actual.Should().Equal(expected);
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

        var actual = CodeOwnersParser.Parse(Content).ToArray();

        var expected = new CodeOwnersEntry[]
        {
            CodeOwnersEntry.FromUsername(0, "*", "user", section: new CodeOwnersSection("Test1", 1)),
            CodeOwnersEntry.FromNone(1, "*", section: new CodeOwnersSection("Test2", 1)),
            CodeOwnersEntry.FromUsername(2, "*", "defaultOwner2", section: new CodeOwnersSection("Test3", 1, ["@defaultOwner2"])),
        };

        actual.Should().Equal(expected);
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

        var actual = CodeOwnersParser.Parse(Content).ToArray();

        var expected = new CodeOwnersEntry[]
        {
            CodeOwnersEntry.FromUsername(0, "*", "user1", section: new CodeOwnersSection("Test1", 1)),
            CodeOwnersEntry.FromUsername(1, "*", "user2", section: new CodeOwnersSection("Test2", 1)),
            CodeOwnersEntry.FromNone(2, "*", section: new CodeOwnersSection("Test3", 1)),
            CodeOwnersEntry.FromNone(3, "*", section: new CodeOwnersSection("Test4", 1)),
        };

        actual.Should().Equal(expected);
    }

    [Fact]
    public void SectionHeadingEdgeCase_DefaultOwnersShouldBePrecededBySpace()
    {
        const string Content = "[Test1]@defaultOwner1\n" +
                               "*\n" +
                               "[Test2][2]@defaultOwner2\n" +
                               "*\n";

        var actual = CodeOwnersParser.Parse(Content).ToArray();

        var expected = new CodeOwnersEntry[]
        {
            CodeOwnersEntry.FromNone(0, "*", section: new CodeOwnersSection("Test1", 1)),
            CodeOwnersEntry.FromNone(1, "*", section: new CodeOwnersSection("Test2", 2)),
        };

        actual.Should().Equal(expected);
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


        var actual = CodeOwnersParser.Parse(Content).ToArray();

        var expected = new CodeOwnersEntry[]
        {
            CodeOwnersEntry.FromUsername(0, "*", "defaultOwner1", section: new CodeOwnersSection("Test1", 1, ["@defaultOwner1", "@defaultOwner2"])),
            CodeOwnersEntry.FromUsername(0, "*", "defaultOwner2", section: new CodeOwnersSection("Test1", 1, ["@defaultOwner1", "@defaultOwner2"])),
            CodeOwnersEntry.FromUsername(1, "*", "defaultOwner3", section: new CodeOwnersSection("Test2", 2, ["@defaultOwner3", "@defaultOwner4"])),
            CodeOwnersEntry.FromUsername(1, "*", "defaultOwner4", section: new CodeOwnersSection("Test2", 2, ["@defaultOwner3", "@defaultOwner4"])),
            CodeOwnersEntry.FromUsername(2, "*", "defaultOwner5", section: new CodeOwnersSection("Test3", 1, ["@defaultOwner5", "@defaultOwner6"])),
            CodeOwnersEntry.FromUsername(2, "*", "defaultOwner6", section: new CodeOwnersSection("Test3", 1, ["@defaultOwner5", "@defaultOwner6"])),
            CodeOwnersEntry.FromUsername(3, "*", "defaultOwner7", section: new CodeOwnersSection("Test4", 2, ["@defaultOwner7", "@defaultOwner8"])),
            CodeOwnersEntry.FromUsername(3, "*", "defaultOwner8", section: new CodeOwnersSection("Test4", 2, ["@defaultOwner7", "@defaultOwner8"])),
        };

        actual.Should().Equal(expected);
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

        var actual = CodeOwnersParser.Parse(Content).ToArray();

        var expected = new CodeOwnersEntry[]
        {
            CodeOwnersEntry.FromUsername(0, "*", "user1", section: new CodeOwnersSection("Test1")),
            CodeOwnersEntry.FromUsername(1, "*", "user2", section: new CodeOwnersSection("Test2", 2)),
            CodeOwnersEntry.FromUsername(2, "*", "user3", section: new CodeOwnersSection("Test3", 1, ["@defaultOwner"])),
        };

        actual.Should().Equal(expected);
    }
}
