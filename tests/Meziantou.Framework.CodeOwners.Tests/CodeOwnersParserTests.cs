using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.CodeOwners.Tests
{
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
                CodeOwnersEntry.FromUsername("*", "user1", section: null),
                CodeOwnersEntry.FromUsername("*", "user2", section: null),
            };

            actual.Should().Equal(expected);
        }

        [Fact]
        public void ParseSingleLineCodeOwnersWithSection()
        {
            var actual = CodeOwnersParser.Parse("[Test]\n* @user1 @user2").ToArray();

            var expected = new CodeOwnersEntry[]
            {
                CodeOwnersEntry.FromUsername("*", "user1", section: new CodeOwnersSection("Test", isOptional: false)),
                CodeOwnersEntry.FromUsername("*", "user2", section: new CodeOwnersSection("Test", isOptional: false)),
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
                                   "*.js    @js-owner\n" +
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
                                   "/docs/ @doctocat\n";

            var actual = CodeOwnersParser.Parse(Content).ToArray();

            var expected = new CodeOwnersEntry[]
            {
                CodeOwnersEntry.FromUsername("*", "global-owner1", section: null),
                CodeOwnersEntry.FromUsername("*", "global-owner2", section: null),
                CodeOwnersEntry.FromUsername("*.js", "js-owner", section: null),
                CodeOwnersEntry.FromEmailAddress("*.go", "docs@example.com", section: null),
                CodeOwnersEntry.FromUsername("/build/logs/", "doctocat", section: null),
                CodeOwnersEntry.FromEmailAddress("docs/*", "docs@example.com", section: null),
                CodeOwnersEntry.FromUsername("apps/", "octocat", section: null),
                CodeOwnersEntry.FromUsername("/docs/", "doctocat", section: null),
            };

            actual.Should().Equal(expected);
        }

        [Fact]
        public void ParseLineEndingWithSpaces()
        {
            var actual = CodeOwnersParser.Parse("* @user1 @user2  ").ToArray();

            var expected = new CodeOwnersEntry[]
            {
                CodeOwnersEntry.FromUsername("*", "user1", section: null),
                CodeOwnersEntry.FromUsername("*", "user2", section: null),
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
                CodeOwnersEntry.FromUsername("doc/", "user4", section: null),
                CodeOwnersEntry.FromUsername("*", "user1", section: new CodeOwnersSection("Section", isOptional: false)),
                CodeOwnersEntry.FromUsername("*", "user2", section: new CodeOwnersSection("Section", isOptional: false)),
                CodeOwnersEntry.FromUsername("*.js", "user2", section: new CodeOwnersSection("Optional Section", isOptional: true)),
                CodeOwnersEntry.FromUsername("*.js", "user3", section: new CodeOwnersSection("Optional Section", isOptional: true)),
            };

            actual.Should().Equal(expected);
        }
    }
}
